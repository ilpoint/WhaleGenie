"""A tunnel into the virtual machine and just enough MCP to call its tools.

The MCP server in the VM listens on 127.0.0.1, which is the only place it should listen. The way in
is the SSH connection that is already open: a local port forwards to the VM's loopback, and the
calls go through it. Nothing is exposed on the network.
"""

import http.client
import json
import os
import socket
import threading

import paramiko

HOST = os.environ.get("WG_VM_HOST", "169.254.81.45")
USER = os.environ.get("WG_VM_USER", "1")
PASSWORD = os.environ.get("WG_VM_PASSWORD", "1")
REMOTE = ("127.0.0.1", int(os.environ.get("WG_VM_PORT", "3917")))
LOCAL_PORT = 13917


class Tunnel:
    """A local port whose traffic is carried to the VM's loopback over SSH."""

    def __init__(self):
        self.client = paramiko.SSHClient()
        self.client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
        self.client.connect(HOST, username=USER, password=PASSWORD, timeout=10,
                            banner_timeout=20, auth_timeout=20)
        self.transport = self.client.get_transport()
        self.server = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self.server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        self.server.bind(("127.0.0.1", LOCAL_PORT))
        self.server.listen(16)
        self.running = True
        threading.Thread(target=self._accept, daemon=True).start()

    def _accept(self):
        while self.running:
            try:
                here, _ = self.server.accept()
            except OSError:
                return
            threading.Thread(target=self._carry, args=(here,), daemon=True).start()

    def _carry(self, here):
        try:
            there = self.transport.open_channel("direct-tcpip", REMOTE, here.getpeername())
        except Exception:
            here.close()
            return

        def pump(source, target):
            try:
                while True:
                    data = source.recv(65536)
                    if not data:
                        break
                    target.sendall(data)
            except Exception:
                pass
            finally:
                try:
                    target.shutdown(socket.SHUT_WR)
                except Exception:
                    pass

        threading.Thread(target=pump, args=(here, there), daemon=True).start()
        pump(there, here)

    def close(self):
        self.running = False
        try:
            self.server.close()
        except Exception:
            pass
        self.client.close()


class Mcp:
    """The smallest client that can call a tool and read the answer."""

    def __init__(self, port=LOCAL_PORT):
        self.port = port
        self.session = None
        self.next_id = 0

    def _post(self, body, expect_reply=True):
        raw = json.dumps(body).encode("utf-8")
        headers = {
            "Content-Type": "application/json",
            "Accept": "application/json, text/event-stream",
        }
        if self.session:
            headers["Mcp-Session-Id"] = self.session

        connection = http.client.HTTPConnection("127.0.0.1", self.port, timeout=120)
        connection.request("POST", "/mcp", body=raw, headers=headers)
        answer = connection.getresponse()
        self.session = answer.getheader("Mcp-Session-Id") or self.session
        kind = answer.getheader("Content-Type") or ""
        text = answer.read().decode("utf-8", "replace")
        connection.close()

        if not expect_reply:
            return None
        if "text/event-stream" in kind:
            for line in text.splitlines():
                if line.startswith("data:"):
                    return json.loads(line[5:].strip())
            raise RuntimeError(f"no event in answer: {text[:200]}")
        return json.loads(text) if text.strip() else None

    def initialize(self):
        reply = self._post({
            "jsonrpc": "2.0", "id": 1, "method": "initialize",
            "params": {
                "protocolVersion": "2025-06-18",
                "capabilities": {},
                "clientInfo": {"name": "vm-sweep", "version": "1"},
            },
        })
        self._post({"jsonrpc": "2.0", "method": "notifications/initialized"},
                   expect_reply=False)
        return reply

    def call(self, name, arguments=None):
        self.next_id += 1
        reply = self._post({
            "jsonrpc": "2.0", "id": self.next_id, "method": "tools/call",
            "params": {"name": name, "arguments": arguments or {}},
        })
        if "error" in reply:
            return False, json.dumps(reply["error"], ensure_ascii=False)
        result = reply.get("result", {})
        parts = []
        for block in result.get("content", []):
            if block.get("type") == "text":
                parts.append(block.get("text", ""))
            elif block.get("type") == "image":
                parts.append(f"[image {block.get('mimeType')} {len(block.get('data', ''))} chars base64]")
        failed = result.get("isError") is True
        return not failed, "\n".join(parts)

    def tools(self):
        self.next_id += 1
        reply = self._post({
            "jsonrpc": "2.0", "id": self.next_id, "method": "tools/list", "params": {},
        })
        return [tool["name"] for tool in reply["result"]["tools"]]


def run(command):
    """Runs one command in the VM over the same SSH connection, and returns its output."""
    client = paramiko.SSHClient()
    client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
    client.connect(HOST, username=USER, password=PASSWORD, timeout=10,
                   banner_timeout=20, auth_timeout=20)
    _, out, err = client.exec_command(command, timeout=120)
    text = out.read().decode("utf-8", "replace")
    client.close()
    return text
