# WhaleGenie（鲸灵）

[![Windows 10 2004+ / 11 (x64)](https://img.shields.io/badge/Windows-10%202004%2B%20%7C%2011%20(x64)-0078D4?logo=windows&logoColor=white)](https://www.microsoft.com/windows)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)](https://github.com/dotnet/runtime)
[![SharpHook](https://img.shields.io/badge/SharpHook-%E5%85%A8%E5%B1%80%E9%92%A9%E5%AD%90%E4%B8%8E%E6%B3%A8%E5%85%A5-1F6FEB)](https://github.com/TolikPylypchuk/SharpHook)
[![libVIIPER](https://img.shields.io/badge/libVIIPER-%E9%A9%B1%E5%8A%A8%E7%BA%A7%E8%99%9A%E6%8B%9F%20USB-1F6FEB)](https://github.com/Alia5/VIIPER)
[![OpenCvSharp5](https://img.shields.io/badge/OpenCvSharp5-%E6%89%BE%E5%9B%BE%E6%89%BE%E8%89%B2%E4%B8%8E%E6%A3%80%E6%B5%8B-1F6FEB)](https://github.com/shimat/opencvsharp)
[![PaddleOCR](https://img.shields.io/badge/PaddleOCR-%E5%B1%8F%E5%B9%95%E5%8F%96%E5%AD%97-1F6FEB)](https://github.com/sdcb/PaddleSharp)
[![FlaUI](https://img.shields.io/badge/FlaUI-UI%20Automation-1F6FEB)](https://github.com/FlaUI/FlaUI)
[![Microsoft.Playwright](https://img.shields.io/badge/Microsoft.Playwright-%E6%B5%8F%E8%A7%88%E5%99%A8%E5%8A%A8%E4%BD%9C-1F6FEB)](https://github.com/microsoft/playwright-dotnet)
[![CsvHelper](https://img.shields.io/badge/CsvHelper-CSV%20%E8%AF%BB%E5%86%99-1F6FEB)](https://github.com/JoshClose/CsvHelper)
[![AGPL-3.0](https://img.shields.io/badge/Licence-AGPL--3.0-blue)](LICENSE.txt)

Windows 上的通用宏 / 自动化工具：把键盘、鼠标、手柄、屏幕与窗口上的操作编成一步步的宏，用热键、鼠标按键、
滚轮、屏幕上某个像素的颜色、某个窗口或进程的出现触发它。界面用 Avalonia 写，执行引擎与界面分离。

## 能做什么

| | |
| --- | --- |
| 输入与触发 | 键盘、鼠标与手柄的按键、移动和滚轮；前台、后台消息与驱动级虚拟 USB 设备三种送法；热键、鼠标按键、滚轮、像素颜色、窗口或进程出现等触发器 |
| 画面识别 | 找图（四种算法）、找色与占比、多点找色、等画面静止、屏幕取字、取色，用自己带的 YOLO 模型找物体（检测 / 等待 / 点击），以及"认得出才下手"的点击图片与点击文字 |
| 窗口与元素 | 按标题、进程名或窗口类名认窗口；移动、缩放、置顶、开关与读窗口和客户区矩形；UI Automation 的取值、选择、勾选、展开、读表格 |
| 浏览器 | 打开页面、点元素、填表、取文字与属性、截图、切换与关闭标签页；默认用系统自带的 Edge |
| 文件与数据 | 读写文本、CSV 与 JSON，剪贴板（含图片），文件夹与进程，命令行与脚本 |
| 流程与调试 | 循环、分支、尝试与收尾、子宏调用；断点、单步、识别结果窗口、每一步的结局与运行日志 |
| 录制与拾取 | 录下键鼠操作直接生成步骤；图片、窗口、元素、颜色、区域、按键、手柄、变量八种拾取器 |
| 编辑与保存 | 一个动作一行、容器块可折叠；`.wgmacro` 连图片一起打包带走；未保存的内容有崩溃恢复快照 |

## 需要什么

- Windows 10 2004（内部版本 19041）/ 11，64 位。
- 框架依赖的那个包要机器上有 .NET 10 桌面运行时；自带运行环境的那一个什么都不用装。

2004 是 Graphics Capture 这条抓屏方式的下限：更早的 Windows 上没有它，「自动」会退回另外几条路
（见[使用说明](doc/manual.md)里的「画面从哪来、怎么取」），程序本身照常运行。

到 [Releases](https://github.com/ilpoint/WhaleGenie/releases) 下载，解压即用：里面是一个 `WhaleGenie`
文件夹，`WhaleGenie.exe` 就在最外层，双击它。

## 文档

| 文档 | 给谁看 |
| --- | --- |
| [doc/manual.md](doc/manual.md) | 用这个程序的人：宏包格式、拾取器、落点与落手、返回值、运行与调试、画面从哪来怎么取、CSV、程序自己的文件放哪儿 |
| [doc/dev.md](doc/dev.md) | 改这个仓库的人：命令、工程结构、依赖的包、测试分层、怎么量性能、代码约定、提交与发布 |
| [doc/roadmap.md](doc/roadmap.md) | 想知道接下来做什么的人：还没做的阶段、可与阶段并行的零散项、明确不做的范围 |
| [doc/performance.md](doc/performance.md)（[English](doc/performance.en.md)） | 一次"看屏幕"的单位成本与量法 |
| [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) | 用到的别人的东西与图标出处 |

## 许可与致谢

「DeepSeek 帮忙写的小助手，所以是鲸灵啦。」

程序本身按 AGPL-3.0 授权，条款原文见 `LICENSE.txt`，版权归 ilpoint 所有（署名写在 `NOTICE` 里），两份都
随发布包一起发；对应源码就是本仓库。图标是 Emojiall 的像素风鲸鱼 emoji，按 CC BY 4.0 使用，已缩放修改。
