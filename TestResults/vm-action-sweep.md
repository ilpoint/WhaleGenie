## 虚拟机里的动作检查

共 30 个动作，30 个符合预期或正常完成；其余见下表。

| 动作 | 它应该做成什么 | 结果 | 读数 | 用时 |
| --- | --- | --- | --- | --- |
| input.mouseClick | 点一下计数按钮 | 符合 | 读数：1 | 0.2s |
| input.mouseDoubleClick | 双击计数按钮 | 符合 | 读数：2 | 0.2s |
| input.mouseDrag | 在输入框里拖一段 | 符合 | 读数：presses=0 | 0.5s |
| input.mouseMove | 把指针挪到色块上 | 符合 | 读数：446 | 0.1s |
| input.mouseScroll | 在窗口里滚一格 | 完成 | 读数：无读数 | 0.2s |
| input.typeText | 在输入框里打一行字 | 符合 | 读数：动作测试 | 0.3s |
| input.keyPress | 按一下 Tab | 完成 | 读数：无读数 | 0.2s |
| input.keyDown | 按住 Shift | 完成 | 读数：无读数 | 0.1s |
| input.keyUp | 松开 Shift | 完成 | 读数：无读数 | 0.1s |
| vision.getPixel | 读色块那一点的颜色 | 符合 | 读数：color=#3366CC, number=13395507 | 0.1s |
| vision.findColor | 在屏幕里找那块蓝 | 完成 | 读数：found=true, match={"x":366,"y":94,"left":366,"top":94,"width":1,"height":1,"score":1,"box":"366,94,1,1","text":"","class":-1,"label":""}, window=, method=desktopDuplication | 0.1s |
| vision.captureWindow | 截靶场窗口 | 完成 | 读数：image=<image 520x260>, window=WhaleGenie Range, method=graphicsCapture, place={"x":306,"y":176,"left":46,"top":46,"width":520,"height":260,"box":"46,46,520,260"}, path= | 0.1s |
| vision.pixelMatches | 问色块是不是 #3366CC | 完成 | 读数：matched=true | 0.2s |
| uia.exists | 靶场里有没有那个按钮 | 完成 | 读数：exists=true | 0.2s |
| uia.click | 按选择器点计数按钮 | 符合 | 读数：1 | 0.3s |
| uia.read | 读按钮的名字 | 完成 | 读数：element={"enabled":true,"visible":true,"value":"rangeCount","name":"rangeCount","automationId":"rangeCount","controlType":"Button","x":156,"y":112,"left":86,"top":94,"width":140,"height":36,"box":"86,94,140,36"} | 0.1s |
| window.info | 读靶场窗口的信息 | 完成 | 读数： | 0.1s |
| window.move | 把靶场窗口挪 20 像素 | 符合 | 读数：(66, 66) | 0.2s |
| window.activate | 把靶场窗口提到前面 | 完成 | 读数： | 0.1s |
| clipboard.writeText | 把一段字放进剪贴板 | 完成 | 读数：已写入 | 0.1s |
| clipboard.readText | 再读回来 | 符合 | 读数：text=动作测试剪贴板 | 0.1s |
| data.base64Encode | 把 vm 编码 | 符合 | 读数：encoded=dm0= | 0.1s |
| file.writeText | 往靶场目录写一行字 | 完成 | 读数：已写入 | 0.1s |
| file.readText | 再读回来 | 符合 | 读数：text=probe | 0.1s |
| file.exists | 问它还在不在 | 完成 | 读数：exists=true | 0.1s |
| process.list | 列出在跑的程序 | 完成 | 读数：processes=AggregatorHost, ApplicationFrameHost, chfs, chfsgui, ChsIME, cmd, conh | 0.1s |
| process.exists | 问 WhaleGenie 在不在 | 完成 | 读数：running=true | 0.1s |
| system.info | 读系统信息 | 完成 | 读数：value=1 | 0.1s |
| screen.list | 列出显示器 | 完成 | 读数：displays={"x":640,"y":512,"left":0,"top":0,"width":1280,"height":1024,"right":12 | 0.1s |
| control.setVariable | 写一个变量，再用条件读它 | 完成 | 读数：已写入 | 0.1s |

## 两处观察

- `window` 这类字段要的是**获取窗口得到的规则记录**（`{"title":"…"}`），只写标题文本会被拒
  （`Run.BadWindowRule`）。这是设计如此（`WindowRuleOf` 的注释写明不再读旧的标题写法），但对第一次
  写宏的客户端是个坑：报错文字虽然说了“不是规则”，没说“要写成记录”。
- `vision.findColor` 的 `anchorMode=screen` 在靶场里仍然搜整块桌面，命中点落在靶场窗口之外也照样返回。
  靶场目前的边界管的是点击、窗口动作与文件写入，屏幕**读**不在其中。
