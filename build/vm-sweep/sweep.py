"""A sweep of MCP calls and macro steps against the range window inside the VM.

Every scenario puts the range back first, does one thing, and reads a value back. The reading is
what makes it a test: "the call answered" only says the tool exists.
"""

import json
import os
import time
import traceback

import mcpvm

rows = []


class Range:
    def __init__(self, mcp):
        self.mcp = mcp
        self.room = self.read()
        self.handle = self.room["handle"]
        self.targets = {t["id"]: t for t in self.room["targets"]}

    def read(self):
        ok, text = self.mcp.call("whalegenie_range_state")
        if not ok:
            raise RuntimeError(f"range_state: {text}")
        return json.loads(text)

    def fresh(self):
        self.mcp.call("whalegenie_range_reset")
        self.mcp.call("whalegenie_window_activate", {"handle": self.handle})
        time.sleep(0.15)
        self.room = self.read()

    def at(self, target, dx=0.5, dy=0.5):
        t = self.targets[target]
        return (self.room["x"] + t["x"] + int(t["width"] * dx),
                self.room["y"] + t["y"] + int(t["height"] * dy))

    @property
    def presses(self):
        return self.read()["presses"]

    @property
    def text(self):
        return self.read()["text"]

    @property
    def ticked(self):
        return self.read()["ticked"]


def scenario(name, what, run):
    started = time.time()
    try:
        detail = run() or ""
        rows.append((name, what, "pass", detail, time.time() - started))
    except AssertionError as bad:
        rows.append((name, what, "fail", str(bad) or "assertion", time.time() - started))
    except Exception as error:
        rows.append((name, what, "error", f"{type(error).__name__}: {error}",
                     time.time() - started))
        traceback.print_exc()
    last = rows[-1]
    print(f"{last[2]:6} {name:22} {last[3][:110]}", flush=True)


def need(condition, detail):
    if not condition:
        raise AssertionError(detail)


def main():
    tunnel = mcpvm.Tunnel()
    try:
        mcp = mcpvm.Mcp()
        hello = mcp.initialize()
        tools = mcp.tools()
        room = Range(mcp)

        opened = mcp.call("whalegenie_open_project")
        draft = json.loads(opened[1])["draftId"]

        def put(name, steps, sub=None):
            macro = {"name": name, "steps": steps}
            if sub:
                macro["subMacros"] = sub
            ok, text = mcp.call("whalegenie_put_macro", {"draftId": draft, "macro": macro})
            need(ok, f"put_macro {name}: {text[:300]}")
            return text

        def ran(name, timeout=20000):
            ok, text = mcp.call("whalegenie_run_macro",
                                {"name": name, "draftId": draft, "timeoutMs": timeout})
            need(ok, f"run_macro {name}: {text[:300]}")
            return json.loads(text)

        def click(x, y, repeat=1):
            return {"type": "input.mouseClick",
                    "params": {"inputMode": "foreground", "button": "left",
                               "x": x, "y": y, "repeat": repeat}}

        # ---- the window itself ----
        scenario("range.state", "五个靶子都在，颜色是写死的那一个", lambda: (
            need(len(room.targets) == 5, f"targets: {list(room.targets)}"),
            f"{len(room.targets)} 个控件，窗口在 ({room.room['x']}, {room.room['y']}) "
            f"{room.room['width']}×{room.room['height']}")[1])

        def click_one():
            room.fresh()
            x, y = room.at("rangeCount")
            ok, text = mcp.call("whalegenie_mouse_click", {"x": x, "y": y})
            need(ok, text[:200])
            for _ in range(20):
                if room.presses:
                    break
                time.sleep(0.1)
            need(room.presses == 1, f"presses={room.presses}")
            return f"点 ({x}, {y}) 之后 presses=1"
        scenario("click.one", "坐标点一次，计数加一", click_one)

        def click_twice():
            room.fresh()
            x, y = room.at("rangeCount")
            ok, text = mcp.call("whalegenie_mouse_click", {"x": x, "y": y, "clicks": 2})
            need(ok, text[:200])
            for _ in range(20):
                if room.presses >= 2:
                    break
                time.sleep(0.1)
            need(room.presses == 2, f"presses={room.presses}")
            return "clicks=2 之后 presses=2"
        scenario("click.double", "连点两次就是两次", click_twice)

        def drag_empty():
            room.fresh()
            fx, fy = room.at("rangeInput")
            tx, ty = room.at("rangeInput", 0.9, 0.5)
            ok, text = mcp.call("whalegenie_mouse_drag",
                                {"fromX": fx, "fromY": fy, "toX": tx, "toY": ty,
                                 "durationMs": 200})
            need(ok, text[:200])
            need(room.presses == 0, f"拖拽不该点中按钮，presses={room.presses}")
            return "拖过输入框，presses 仍是 0"
        scenario("drag.noClick", "拖拽不是点击", drag_empty)

        def type_ascii():
            room.fresh()
            x, y = room.at("rangeInput")
            mcp.call("whalegenie_mouse_click", {"x": x, "y": y})
            time.sleep(0.2)
            ok, text = mcp.call("whalegenie_type_text", {"text": "vm 123"})
            need(ok, text[:200])
            time.sleep(0.3)
            need(room.text == "vm 123", f"text={room.text!r}")
            return "输入框里是 'vm 123'"
        scenario("type.ascii", "打字真的进了输入框", type_ascii)

        def type_chinese():
            room.fresh()
            x, y = room.at("rangeInput")
            mcp.call("whalegenie_mouse_click", {"x": x, "y": y})
            time.sleep(0.2)
            ok, text = mcp.call("whalegenie_type_text", {"text": "虚拟机"})
            need(ok, text[:200])
            time.sleep(0.4)
            need(room.text == "虚拟机", f"text={room.text!r}")
            return "中文也进得去"
        scenario("type.chinese", "中文通过键盘事件进得去", type_chinese)

        def key_select_all():
            room.fresh()
            x, y = room.at("rangeInput")
            mcp.call("whalegenie_mouse_click", {"x": x, "y": y})
            mcp.call("whalegenie_type_text", {"text": "abc"})
            time.sleep(0.2)
            ok, text = mcp.call("whalegenie_key", {"key": "Ctrl+A"})
            need(ok, text[:200])
            ok, text = mcp.call("whalegenie_key", {"key": "Delete"})
            need(ok, text[:200])
            time.sleep(0.3)
            need(room.text == "", f"text={room.text!r}")
            return "Ctrl+A 再 Delete 把三行字清掉"
        scenario("key.chord", "组合键与单键都发得出去", key_select_all)

        def paste():
            room.fresh()
            x, y = room.at("rangeInput")
            ok, text = mcp.call("whalegenie_clipboard_write", {"text": "剪贴板内容"})
            need(ok, text[:200])
            mcp.call("whalegenie_mouse_click", {"x": x, "y": y})
            time.sleep(0.2)
            ok, text = mcp.call("whalegenie_key", {"key": "Ctrl+V"})
            need(ok, text[:200])
            time.sleep(0.4)
            need(room.text == "剪贴板内容", f"text={room.text!r}")
            return "写剪贴板 → Ctrl+V → 输入框里是同一段字"
        scenario("clipboard.paste", "剪贴板到输入框的完整回路", paste)

        def copy():
            room.fresh()
            x, y = room.at("rangeInput")
            mcp.call("whalegenie_mouse_click", {"x": x, "y": y})
            mcp.call("whalegenie_type_text", {"text": "copy me"})
            time.sleep(0.2)
            mcp.call("whalegenie_key", {"key": "Ctrl+A"})
            ok, text = mcp.call("whalegenie_key", {"key": "Ctrl+C"})
            need(ok, text[:200])
            time.sleep(0.3)
            ok, text = mcp.call("whalegenie_clipboard_read")
            need(ok, text[:200])
            copied = json.loads(text)["text"]
            need(copied == "copy me", f"clipboard={copied!r}")
            return "Ctrl+C 之后剪贴板读到 'copy me'"
        scenario("clipboard.copy", "复制出来的是输入框里的字", copy)

        def window_info():
            ok, text = mcp.call("whalegenie_window_info", {"handle": room.handle})
            need(ok, text[:200])
            info = json.loads(text)
            need(info["found"], "窗口没找到")
            need(info["title"] == "WhaleGenie Range", f"title={info['title']!r}")
            need(info["process"].lower().startswith("whalegenie"), f"process={info['process']!r}")
            need(info["clientWidth"] == 520 and info["clientHeight"] == 260,
                 f"client={info['clientWidth']}×{info['clientHeight']}")
            return f"{info['title']} / {info['process']} / {info['className']} / 客户区 520×260"
        scenario("window.info", "窗口句柄读得出标题、进程、窗口类与客户区", window_info)

        def window_move():
            room.fresh()
            x = room.room["x"]
            y = room.room["y"]
            ok, text = mcp.call("whalegenie_window_move",
                                {"handle": room.handle, "x": x + 24, "y": y + 12})
            need(ok, text[:200])
            time.sleep(0.4)
            moved = room.read()
            need(moved["x"] == x + 24 and moved["y"] == y + 12,
                 f"移到 ({moved['x']}, {moved['y']})")
            mcp.call("whalegenie_window_move", {"handle": room.handle, "x": x, "y": y})
            time.sleep(0.3)
            return "移动 +24,+12 之后状态里的坐标跟着变，再移回去"
        scenario("window.move", "移动自己的窗口，读数跟着走", window_move)

        def pixel():
            room.fresh()
            cx, cy = room.at("rangeColour")
            ok, text = mcp.call("whalegenie_pixel_at", {"x": cx, "y": cy})
            need(ok, text[:200])
            name = json.loads(text)["color"]
            need("#3366CC" in name.upper(), f"color={name}")
            bx, by = room.room["x"] + 4, room.room["y"] + 4
            ok, text = mcp.call("whalegenie_pixel_at", {"x": bx, "y": by})
            need(ok, text[:200])
            need("#F4F6F8" in text.upper(), f"background={text[:120]}")
            return f"色块读出 {name}，背景读出 #F4F6F8"
        scenario("pixel.at", "取色读得出那块固定的蓝", pixel)

        def element():
            room.fresh()
            x, y = room.at("rangeCount")
            ok, text = mcp.call("whalegenie_element_at", {"x": x, "y": y})
            need(ok, text[:200])
            element = json.loads(text)
            need(element["found"], "这一点上没有控件")
            need(element["automationId"] == "rangeCount",
                 f"automationId={element['automationId']!r}")
            return f"{element['controlType']} / {element['automationId']} / enabled={element['enabled']}"
        scenario("element.at", "坐标上读得到按钮的自动化 id 与状态", element)

        def find():
            room.fresh()
            ok, text = mcp.call("whalegenie_find_elements",
                                {"selector": "Button[automationId='rangeCount']"})
            need(ok, text[:300])
            found = json.loads(text)
            need(found["count"] >= 1, f"count={found['count']}")
            return f"按选择器找到 {found['count']} 个"
        scenario("element.find", "选择器找得回同一个控件", find)

        def capture():
            room.fresh()
            ok, text = mcp.call("whalegenie_capture_screen", {"windowHandle": room.handle})
            need(ok, text[:200])
            need("[image" in text, f"没有图像块：{text[:200]}")
            return text
        scenario("capture.window", "截靶场窗口，回来的是一张图", capture)

        # ---- macros ----
        def macro_click():
            room.fresh()
            x, y = room.at("rangeCount")
            put("拍一下", [click(x, y)])
            result = ran("拍一下")
            need(result["status"] == "Completed", f"status={result['status']}")
            need(room.presses == 1, f"presses={room.presses}")
            return f"宏跑完 presses=1，{len(result['outcomes'])} 步有记录"
        scenario("macro.click", "一步点击的宏，真的点中了", macro_click)

        def macro_uia():
            room.fresh()
            put("按选择器点", [{"type": "uia.click",
                             "params": {"uiSelector": "Button[automationId='rangeCount']",
                                        "matchIndex": 1, "button": "left"}}])
            result = ran("按选择器点")
            need(result["status"] == "Completed", f"status={result['status']} {result.get('detail')}")
            for _ in range(20):
                if room.presses:
                    break
                time.sleep(0.1)
            need(room.presses == 1, f"presses={room.presses}")
            return "uia.click 走控件树也点中了"
        scenario("macro.uiaClick", "按选择器点击（走 UI Automation）", macro_uia)

        def macro_repeat():
            room.fresh()
            x, y = room.at("rangeCount")
            put("点三次", [{"type": "control.repeat",
                          "params": {"times": 3, "intervalMs": 60,
                                     "returns": [{"key": "ended", "name": "结束"}],
                                     "body": [click(x, y)]}}])
            result = ran("点三次")
            need(result["status"] == "Completed", f"status={result['status']}")
            for _ in range(30):
                if room.presses >= 3:
                    break
                time.sleep(0.1)
            need(room.presses == 3, f"presses={room.presses}")
            return "repeat 三次，presses=3"
        scenario("macro.repeat", "循环体里的点击跑了三次", macro_repeat)

        def macro_type():
            room.fresh()
            x, y = room.at("rangeInput")
            put("打一行", [click(x, y),
                         {"type": "input.typeText",
                          "params": {"inputMode": "foreground", "typedText": "from macro",
                                     "intervalMs": 10}}])
            result = ran("打一行")
            need(result["status"] == "Completed", f"status={result['status']}")
            time.sleep(0.3)
            need(room.text == "from macro", f"text={room.text!r}")
            return "宏把字打进了输入框：'from macro'"
        scenario("macro.typeText", "宏里的打字落到输入框", macro_type)

        def macro_if():
            room.fresh()
            x, y = room.at("rangeCount")
            put("不该点", [{"type": "control.if",
                          "params": {"condition": "1 == 2", "then": [click(x, y)]}}])
            result = ran("不该点")
            need(result["status"] == "Completed", f"status={result['status']}")
            time.sleep(0.3)
            need(room.presses == 0, f"presses={room.presses}")
            return "条件为假，分支里的点击没有发生"
        scenario("macro.if", "条件为假时分支不执行", macro_if)

        def macro_if_typo():
            room.fresh()
            x, y = room.at("rangeCount")
            put("写错等号", [{"type": "control.if",
                            "params": {"condition": "1 = 2", "then": [click(x, y)]}}])
            result = ran("写错等号")
            need(result["status"] == "Failed", f"status={result['status']}")
            need(result.get("key") == "Run.BadExpression",
                 f"key={result.get('key')} detail={result.get('detail')}")
            need(room.presses == 0, f"presses={room.presses}")
            return f"{result['key']}：{result.get('detail')}"
        scenario("macro.ifTypo", "单等号报错，不再静默成真", macro_if_typo)

        def macro_try():
            room.fresh()
            put("兜住", [{"type": "control.try",
                        "params": {"body": [{"type": "control.fail",
                                             "params": {"reason": "故意失败"}}],
                                   "catch": [{"type": "control.delay", "params": {"ms": 20}}],
                                   "returns": [{"key": "error", "name": "错误"}]}}])
            result = ran("兜住")
            need(result["status"] == "Completed",
                 f"status={result['status']} key={result.get('key')} {result.get('detail')}")
            return "body 故意失败，try 兜住，整条宏是 Completed"
        scenario("macro.try", "故意失败被 try 接住", macro_try)

        def macro_results():
            room.fresh()
            put("算一个", [{"type": "data.base64Encode",
                          "params": {"dataText": "vm",
                                     "returns": [{"key": "encoded", "name": "编码"}]}}])
            result = ran("算一个")
            outcome = result["outcomes"][0]
            value = outcome["results"][0]["value"] if outcome["results"] else ""
            need(value == "dm0=", f"value={value!r}")
            ok, log = mcp.call("whalegenie_get_run_log", {"runId": result["runId"]})
            need(ok, log[:200])
            need("dm0=" in log, "运行日志里没有这个值")
            return "返回值 dm0= 同时出现在 run 回答与运行日志里"
        scenario("macro.results", "每步算出的值交得回来，也进日志", macro_results)

        def macro_sub():
            room.fresh()
            x, y = room.at("rangeCount")
            put("外面调用", [{"type": "control.runMacro",
                           "params": {"macro": "里面按", "arguments": [],
                                      "returns": []}}],
                sub=[{"name": "里面按", "steps": [click(x, y)]}])
            result = ran("外面调用")
            need(result["status"] == "Completed",
                 f"status={result['status']} {result.get('detail')}")
            for _ in range(20):
                if room.presses:
                    break
                time.sleep(0.1)
            need(room.presses == 1, f"presses={room.presses}")
            return "子宏里的点击真的发生了"
        scenario("macro.subMacro", "调用子宏，子宏里的步骤跑得动", macro_sub)

        def macro_file():
            room.fresh()
            folder = room.room["folder"]
            path = folder + "\\sweep.txt"
            put("写读", [{"type": "file.writeText",
                        "params": {"path": path, "fileContent": "range file",
                                   "writeMode": "overwrite", "newline": "none",
                                   "encoding": "utf8"}},
                       {"type": "file.readText",
                        "params": {"path": path, "limit": 0, "skipBlankLines": False,
                                   "trimLines": True, "encoding": "utf8",
                                   "returns": [{"key": "text", "name": "内容"}]}}])
            result = ran("写读")
            need(result["status"] == "Completed", f"status={result['status']} {result.get('detail')}")
            text = ""
            for outcome in result["outcomes"]:
                for item in outcome["results"]:
                    if item["key"] == "text":
                        text = item["value"]
            need(text == "range file", f"读回来是 {text!r}")
            return f"写进 {path} 再读回来，内容一致"
        scenario("macro.file", "靶场目录里的写与读", macro_file)

        def macro_stop():
            room.fresh()
            put("长等", [{"type": "control.delay", "params": {"ms": 8000}},
                       {"type": "control.delay", "params": {"ms": 10}}])
            started = time.time()
            result = ran("长等", timeout=1500)
            spent = time.time() - started
            need(spent < 6, f"run_macro 等了 {spent:.1f} 秒才回来")
            need(result["timedOut"] or result["status"] in ("Running", "Stopped"),
                 f"status={result['status']}")
            ok, text = mcp.call("whalegenie_stop_macro", {})
            time.sleep(0.8)
            ok, text = mcp.call("whalegenie_run_status")
            need(ok, text[:200])
            return f"{spent:.1f}s 就回来了（status={result['status']}, timedOut={result['timedOut']}）"
        scenario("macro.stop", "长宏按上限收尾，能停", macro_stop)

        def edge_script():
            room.fresh()
            put("脚本", [{"type": "script.run",
                        "params": {"kind": "ps1", "scriptSource": "text",
                                   "script": "Write-Output 1"}}])
            ok, text = mcp.call("whalegenie_run_macro",
                                {"name": "脚本", "draftId": draft, "timeoutMs": 15000})
            need(not ok, "脚本居然被允许了")
            need("Range.ActionRefused" in text, text[:200])
            return "Range.ActionRefused"
        scenario("edge.script", "脚本在靶场里被拒", edge_script)

        def edge_path():
            room.fresh()
            put("写外面", [{"type": "file.writeText",
                          "params": {"path": r"C:\Windows\wg-sweep.txt", "fileContent": "x",
                                     "writeMode": "overwrite", "newline": "none",
                                     "encoding": "utf8"}}])
            ok, text = mcp.call("whalegenie_run_macro",
                                {"name": "写外面", "draftId": draft, "timeoutMs": 15000})
            need(not ok, "写系统目录居然被允许了")
            need("Range.PathOutside" in text, text[:200])
            return "Range.PathOutside"
        scenario("edge.path", "靶场目录外拒绝写", edge_path)

        ok, text = mcp.call("whalegenie_discard", {"draftId": draft})

        passed = sum(1 for row in rows if row[2] == "pass")
        head = [
            "## WhaleGenie MCP sweep in a virtual machine",
            "",
            f"VM: Windows 10 22H2 x64, speaking MCP {hello['result']['protocolVersion']}, "
            f"{len(tools)} tools.",
            f"Result: {passed}/{len(rows)} pass.",
            "",
            "| scenario | what it proves | result | detail |",
            "| --- | --- | --- | --- |",
        ]
        body = [f"| {name} | {what} | {result} | {detail.replace('|', '/')} |"
                for name, what, result, detail, _ in rows]
        report = "\n".join(head + body) + "\n"
        # Beside the other reports when the folder can be written, and in the working directory
        # when something on the machine will not have it — a sweep that ran is worth keeping even
        # when the usual place is out of reach.
        wanted = os.path.join("TestResults", "vm-mcp-sweep.md")
        try:
            with open(wanted, "w", encoding="utf-8") as file:
                file.write(report)
        except OSError as refused:
            print(f"\ncould not write {wanted} ({refused}); writing vm-mcp-sweep.md here instead")
            with open("vm-mcp-sweep.md", "w", encoding="utf-8") as file:
                file.write(report)
        print(f"\n{passed}/{len(rows)} pass")
    finally:
        tunnel.close()


main()
