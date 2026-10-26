## WhaleGenie MCP sweep in a virtual machine

VM: Windows 10 22H2 x64, speaking MCP 2025-06-18, 48 tools.
Result: 28/28 pass.

| scenario | what it proves | result | detail |
| --- | --- | --- | --- |
| range.state | 五个靶子都在，颜色是写死的那一个 | pass | 5 个控件，窗口在 (208, 208) 520×260 |
| click.one | 坐标点一次，计数加一 | pass | 点 (318, 274) 之后 presses=1 |
| click.double | 连点两次就是两次 | pass | clicks=2 之后 presses=2 |
| drag.noClick | 拖拽不是点击 | pass | 拖过输入框，presses 仍是 0 |
| type.ascii | 打字真的进了输入框 | pass | 输入框里是 'vm 123' |
| type.chinese | 中文通过键盘事件进得去 | pass | 中文也进得去 |
| key.chord | 组合键与单键都发得出去 | pass | Ctrl+A 再 Delete 把三行字清掉 |
| clipboard.paste | 剪贴板到输入框的完整回路 | pass | 写剪贴板 → Ctrl+V → 输入框里是同一段字 |
| clipboard.copy | 复制出来的是输入框里的字 | pass | Ctrl+C 之后剪贴板读到 'copy me' |
| window.info | 窗口句柄读得出标题、进程、窗口类与客户区 | pass | WhaleGenie Range / WhaleGenie / Avalonia-046a905e-8cc5-40f8-8677-c3b4e32e07bc / 客户区 520×260 |
| window.move | 移动自己的窗口，读数跟着走 | pass | 移动 +24,+12 之后状态里的坐标跟着变，再移回去 |
| pixel.at | 取色读得出那块固定的蓝 | pass | 色块读出 #3366CC，背景读出 #F4F6F8 |
| element.at | 坐标上读得到按钮的自动化 id 与状态 | pass | Button / rangeCount / enabled=True |
| element.find | 选择器找得回同一个控件 | pass | 按选择器找到 1 个 |
| capture.window | 截靶场窗口，回来的是一张图 | pass | [image image/png 16168 chars base64]
{"window":525430,"x":208,"y":208,"width":520,"height":260,"way":"GraphicsCapture","scaledFrom":null} |
| macro.click | 一步点击的宏，真的点中了 | pass | 宏跑完 presses=1，1 步有记录 |
| macro.uiaClick | 按选择器点击（走 UI Automation） | pass | uia.click 走控件树也点中了 |
| macro.repeat | 循环体里的点击跑了三次 | pass | repeat 三次，presses=3 |
| macro.typeText | 宏里的打字落到输入框 | pass | 宏把字打进了输入框：'from macro' |
| macro.if | 条件为假时分支不执行 | pass | 条件为假，分支里的点击没有发生 |
| macro.ifTypo | 单等号报错，不再静默成真 | pass | Run.BadExpression："=" on its own; write "==" to compare |
| macro.try | 故意失败被 try 接住 | pass | body 故意失败，try 兜住，整条宏是 Completed |
| macro.results | 每步算出的值交得回来，也进日志 | pass | 返回值 dm0= 同时出现在 run 回答与运行日志里 |
| macro.subMacro | 调用子宏，子宏里的步骤跑得动 | pass | 子宏里的点击真的发生了 |
| macro.file | 靶场目录里的写与读 | pass | 写进 C:\Users\1\AppData\Local\Temp\whalegenie-range\sweep.txt 再读回来，内容一致 |
| macro.stop | 长宏按上限收尾，能停 | pass | 1.6s 就回来了（status=Stopped, timedOut=False） |
| edge.script | 脚本在靶场里被拒 | pass | Range.ActionRefused |
| edge.path | 靶场目录外拒绝写 | pass | Range.PathOutside |
