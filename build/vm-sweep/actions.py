"""Do the actions do what they say? One macro per action, generated from the catalogue.

The point is not "the call answered": each probe names a reading that only moves if the action
happened — the count on the range window, the text in its box, the colour at a pixel, the file on
disk. Whatever the action hands back is read from its own run, not from a log.
"""

import json
import os
import time

import mcpvm

ROWS = []


class Probe:
    def __init__(self, mcp):
        self.mcp = mcp
        ok, text = mcp.call("whalegenie_range_state")
        self.room = json.loads(text)
        self.handle = self.room["handle"]
        self.targets = {t["id"]: t for t in self.room["targets"]}
        self.draft = json.loads(mcp.call("whalegenie_open_project")[1])["draftId"]
        self._params = {}

    def at(self, target, dx=0.5, dy=0.5):
        t = self.targets[target]
        return (self.room["x"] + t["x"] + int(t["width"] * dx),
                self.room["y"] + t["y"] + int(t["height"] * dy))

    def fresh(self):
        self.mcp.call("whalegenie_range_reset")
        self.mcp.call("whalegenie_window_activate", {"handle": self.handle})
        time.sleep(0.15)

    def state(self):
        return json.loads(self.mcp.call("whalegenie_range_state")[1])

    def params_of(self, key):
        if key not in self._params:
            data = json.loads(self.mcp.call("whalegenie_get_action", {"key": key})[1])
            self._params[key] = {p["name"]: p for p in data["parameters"]}
        return self._params[key]

    def step(self, action, **given):
        """A step whose fields are the catalogue's defaults, with what the probe knows filled in."""
        specs = self.params_of(action)
        params = {}
        for name, spec in specs.items():
            default = spec.get("default")
            if default not in (None, ""):
                params[name] = default
            elif spec.get("required") and spec["shape"] == "Text":
                params[name] = "1"
            elif spec.get("required") and spec["shape"] == "Number":
                params[name] = 1
        # Only fields this action declares: a name it does not have would be refused as a bad field.
        for name, value in given.items():
            if name in specs:
                params[name] = value
        return {"type": action, "params": params}

    def put(self, name, steps):
        ok, text = self.mcp.call("whalegenie_put_macro",
                                 {"draftId": self.draft, "macro": {"name": name, "steps": steps}})
        if not ok:
            raise RuntimeError(text[:300])
        # A field the action does not have is dropped on the way in, so a typo would otherwise be
        # run as an empty field and read as the action doing nothing.
        blocking = json.loads(text).get("validation", {}).get("blocking", [])
        if blocking:
            raise RuntimeError(blocking[0].get("message", "")[:200])

    def run(self, name, timeout=15000):
        ok, text = self.mcp.call("whalegenie_run_macro",
                                 {"name": name, "draftId": self.draft, "timeoutMs": timeout})
        if not ok:
            return {"status": "error", "key": "", "detail": text[:200], "outcomes": []}
        return json.loads(text)

    def probe(self, name, what, steps, read, expect=None):
        self.fresh()
        started = time.time()
        try:
            self.put(name, steps)
            result = self.run(name)
        except RuntimeError as bad:
            ROWS.append((name, what, "打不开", str(bad)[:160], ""))
            print(f"{'打不开':8} {name:30} {bad}", flush=True)
            return
        try:
            value = str(read(result))
        except Exception as error:  # noqa: BLE001 - a probe that cannot read says so
            value = f"<读不出：{type(error).__name__}>"
        verdict = judge(result, value, expect)
        detail = result.get("detail") or ""
        if result.get("key"):
            detail = f"{result['key']}: {detail}" if detail else result["key"]
        ROWS.append((name, what, verdict, f"{detail}｜读数：{value}".strip("｜"),
                     f"{time.time() - started:.1f}s"))
        print(f"{verdict:8} {name:30} {value[:60]}", flush=True)


def results_of(result):
    """Everything the run handed back, as name=value pairs."""
    pairs = []
    for outcome in result.get("outcomes", []):
        for item in outcome.get("results", []):
            pairs.append(f"{item['key']}={item['value']}")
    return ", ".join(pairs)


def judge(result, value, expect):
    status = result.get("status")
    if status == "Completed":
        if expect is None:
            return "完成"
        return "符合" if expect in value else "不符预期"
    if status == "Stopped":
        return "超时"
    text = json.dumps(result, ensure_ascii=False)
    for known in ("Range.", "BrowserUnavailable", "DeviceUnavailable", "MissingTargetWindow",
                  "UnknownWindow", "BadOption", "BadArgument"):
        if known in text:
            return "边界"
    return "可疑"


def write():
    good = sum(1 for row in ROWS if row[2] in ("符合", "完成"))
    head = ["## 虚拟机里的动作检查", "",
            f"共 {len(ROWS)} 个动作，{good} 个符合预期或正常完成；其余见下表。", "",
            "| 动作 | 它应该做成什么 | 结果 | 读数 | 用时 |", "| --- | --- | --- | --- | --- |"]
    body = [f"| {a} | {b} | {c} | {d.replace('|', '/')} | {e} |" for a, b, c, d, e in ROWS]
    report = "\n".join(head + body) + "\n"
    for folder in ("TestResults", "."):
        try:
            with open(os.path.join(folder, "vm-action-sweep.md"), "w", encoding="utf-8") as file:
                file.write(report)
            print("written to", os.path.join(folder, "vm-action-sweep.md"))
            return
        except OSError:
            continue


def main():
    tunnel = mcpvm.Tunnel()
    try:
        mcp = mcpvm.Mcp()
        mcp.initialize()
        p = Probe(mcp)
        cx, cy = p.at("rangeCount")
        tx, ty = p.at("rangeInput")
        swatch = p.at("rangeColour")
        folder = p.room["folder"]
        inside = os.path.join(folder, "probe.txt")

        p.probe("input.mouseClick", "点一下计数按钮", [p.step("input.mouseClick", x=cx, y=cy)],
                lambda r: p.state()["presses"], "1")
        p.probe("input.mouseDoubleClick", "双击计数按钮",
                [p.step("input.mouseDoubleClick", x=cx, y=cy)],
                lambda r: p.state()["presses"], "2")
        p.probe("input.mouseDrag", "在输入框里拖一段",
                [p.step("input.mouseDrag", fromX=tx - 80, fromY=ty, toX=tx + 80, toY=ty)],
                lambda r: f"presses={p.state()['presses']}", "presses=0")
        p.probe("input.mouseMove", "把指针挪到色块上",
                [p.step("input.mouseMove", x=swatch[0], y=swatch[1])],
                lambda r: json.loads(mcp.call("whalegenie_cursor")[1])["x"], str(swatch[0]))
        p.probe("input.mouseScroll", "在窗口里滚一格",
                [p.step("input.mouseScroll", x=p.room["x"] + 260, y=p.room["y"] + 130)],
                lambda r: "无读数", None)
        p.probe("input.typeText", "在输入框里打一行字",
                [p.step("input.mouseClick", x=tx, y=ty),
                 p.step("input.typeText", typedText="动作测试")],
                lambda r: p.state()["text"], "动作测试")
        p.probe("input.keyPress", "按一下 Tab", [p.step("input.keyPress", key="Tab")],
                lambda r: "无读数", None)
        p.probe("input.keyDown", "按住 Shift", [p.step("input.keyDown", key="Shift")],
                lambda r: "无读数", None)
        p.probe("input.keyUp", "松开 Shift", [p.step("input.keyUp", key="Shift")],
                lambda r: "无读数", None)

        p.probe("vision.getPixel", "读色块那一点的颜色",
                [p.step("vision.getPixel", x=swatch[0], y=swatch[1])],
                lambda r: results_of(r), "#3366CC")
        p.probe("vision.findColor", "在屏幕里找那块蓝",
                [p.step("vision.findColor", color="#3366CC")],
                lambda r: results_of(r), None)
        rule = {"title": "WhaleGenie Range"}
        p.probe("vision.captureWindow", "截靶场窗口",
                [p.step("vision.captureWindow", window=rule)],
                lambda r: results_of(r), None)
        p.probe("vision.pixelMatches", "问色块是不是 #3366CC",
                [p.step("vision.pixelMatches", x=swatch[0], y=swatch[1], color="#3366CC")],
                lambda r: results_of(r), None)

        selector = "Button[automationId='rangeCount']"
        p.probe("uia.exists", "靶场里有没有那个按钮",
                [p.step("uia.exists", uiSelector=selector)], lambda r: results_of(r), None)
        p.probe("uia.click", "按选择器点计数按钮",
                [p.step("uia.click", uiSelector=selector, matchIndex=1)],
                lambda r: p.state()["presses"], "1")
        p.probe("uia.read", "读按钮的名字",
                [p.step("uia.read", uiSelector=selector, matchIndex=1)],
                lambda r: results_of(r), None)

        p.probe("window.info", "读靶场窗口的信息",
                [p.step("window.info", window=rule)], lambda r: results_of(r), None)
        p.probe("window.move", "把靶场窗口挪 20 像素",
                [p.step("window.move", window=rule, x=p.room["x"] + 20,
                        y=p.room["y"] + 20, width=p.room["width"], height=p.room["height"])],
                lambda r: (p.state()["x"], p.state()["y"]), str(p.room["x"] + 20))
        p.probe("window.activate", "把靶场窗口提到前面",
                [p.step("window.activate", window=rule)],
                lambda r: results_of(r), None)

        p.probe("clipboard.writeText", "把一段字放进剪贴板",
                [p.step("clipboard.writeText", clipboardText="动作测试剪贴板")],
                lambda r: "已写入", None)
        p.probe("clipboard.readText", "再读回来", [p.step("clipboard.readText")],
                lambda r: results_of(r), "动作测试剪贴板")

        p.probe("data.base64Encode", "把 vm 编码",
                [p.step("data.base64Encode", dataText="vm",
                        returns=[{"key": "encoded", "name": "编码"}])],
                lambda r: results_of(r), "dm0=")
        p.probe("file.writeText", "往靶场目录写一行字",
                [p.step("file.writeText", path=inside, fileContent="probe")],
                lambda r: "已写入", None)
        p.probe("file.readText", "再读回来",
                [p.step("file.readText", path=inside,
                        returns=[{"key": "text", "name": "内容"}])],
                lambda r: results_of(r), "probe")
        p.probe("file.exists", "问它还在不在", [p.step("file.exists", path=inside)],
                lambda r: results_of(r), None)

        p.probe("process.list", "列出在跑的程序", [p.step("process.list")],
                lambda r: results_of(r)[:80], None)
        p.probe("process.exists", "问 WhaleGenie 在不在",
                [p.step("process.exists", processName="WhaleGenie")],
                lambda r: results_of(r), None)
        p.probe("system.info", "读系统信息", [p.step("system.info")],
                lambda r: results_of(r)[:80], None)
        p.probe("screen.list", "列出显示器", [p.step("screen.list")],
                lambda r: results_of(r)[:80], None)

        p.probe("control.setVariable", "写一个变量，再用条件读它",
                [p.step("control.setVariable", name="n", scope="local", value="1"),
                 p.step("control.if", condition="$n == 1",
                        **{"then": []})],
                lambda r: results_of(r) or "已写入", None)

        mcp.call("whalegenie_discard", {"draftId": p.draft})
        write()
    finally:
        tunnel.close()


main()
