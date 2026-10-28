# WhaleGenie（鲸灵）

[![Windows 10 2004+ / 11 (x64)](https://img.shields.io/badge/Windows-10%202004%2B%20%7C%2011%20(x64)-0078D4?logo=windows&logoColor=white)](https://www.microsoft.com/windows)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)](https://github.com/dotnet/runtime)
[![SharpHook](https://img.shields.io/badge/SharpHook-%E5%85%A8%E5%B1%80%E9%92%A9%E5%AD%90%E4%B8%8E%E6%B3%A8%E5%85%A5-1F6FEB)](https://github.com/TolikPylypchuk/SharpHook)
[![libVIIPER](https://img.shields.io/badge/libVIIPER-%E9%A9%B1%E5%8A%A8%E7%BA%A7%E8%99%9A%E6%8B%9F%20USB-1F6FEB)](https://github.com/Alia5/VIIPER)
[![OpenCvSharp5](https://img.shields.io/badge/OpenCvSharp5-%E6%89%BE%E5%9B%BE%E6%89%BE%E8%89%B2%E4%B8%8E%E6%A3%80%E6%B5%8B-1F6FEB)](https://github.com/shimat/opencvsharp)
[![SimdPaddleOCR](https://img.shields.io/badge/SimdPaddleOCR-%E5%B1%8F%E5%B9%95%E5%8F%96%E5%AD%97-1F6FEB)](https://github.com/sdcb/SimdPaddleOCR)
[![FlaUI](https://img.shields.io/badge/FlaUI-UI%20Automation-1F6FEB)](https://github.com/FlaUI/FlaUI)
[![Microsoft.Playwright](https://img.shields.io/badge/Microsoft.Playwright-%E6%B5%8F%E8%A7%88%E5%99%A8%E5%8A%A8%E4%BD%9C-1F6FEB)](https://github.com/microsoft/playwright-dotnet)
[![CsvHelper](https://img.shields.io/badge/CsvHelper-CSV%20%E8%AF%BB%E5%86%99-1F6FEB)](https://github.com/JoshClose/CsvHelper)
[![AGPL-3.0](https://img.shields.io/badge/Licence-AGPL--3.0-blue)](LICENSE.txt)

Windows 通用宏与自动化工具。将键盘、鼠标、手柄、屏幕与窗口操作编排为宏，由热键、鼠标按键、
滚轮、像素颜色、窗口或进程出现触发；同一宏还可以拥有窗口悬浮按钮、键盘字串和
Quicker 风格菜单入口。界面基于 Avalonia，执行引擎与界面分离。

> [!WARNING]
> 0.1.1 把宏包格式从 18 升到 19。0.1.0 及更早版本创建的 `.wgmacro` 不能直接打开，
> 也不会自动迁移。升级前请保留旧包备份，并在 0.1.1 中重新建立项目。

## 界面

### 主界面

宏按用途分布在「按键宏」「界面宏」「字串宏」「菜单宏」和「其他宏」页签。搜索会过滤当前页；
每张卡片可以直接启停、编辑或删除。顶部提供本机 MCP、运行调试和全局设置入口。

<p align="center">
  <img src="doc/images/main-window.png" alt="鲸灵主界面" width="360">
</p>

### 宏编辑器

编辑器分三列：左侧从基础动作、流程控制、子宏和公共宏中取动作；中间按实际执行顺序显示可折叠
步骤树；右侧管理当前宏的变量与设置。循环、多分支和尝试/收尾会展开成有明确块首与块尾的嵌套
结构，子步骤直接在主列表里拖动和组织。

<p align="center">
  <img src="doc/images/macro-editor.png" alt="鲸灵宏编辑器" width="1200">
</p>

### 动作编辑

动作窗口只编辑已经选中的动作，不再重复动作选择器。基础设置、返回值和其它设置分页显示；块的
子步骤回到主列表编辑，窗口只保留这一块自己的参数、条件和策略。

<p align="center">
  <img src="doc/images/action-editor.png" alt="鲸灵动作编辑窗口" width="760">
</p>

## 能做什么

| 能力 | 说明 |
| --- | --- |
| 输入与触发 | 键盘、鼠标、手柄的按键、移动与滚轮；前台、后台消息与驱动级虚拟 USB 三种输入方式；一个宏可同时拥有按键、颜色、窗口悬浮、字串、菜单和原有定时/文件/程序/窗口/空闲入口 |
| 画面识别 | 找图（四种算法）、找色与颜色占比、多点找色、画面静止、屏幕取字、取色；内置 YOLO 模型检测、等待与点击；识别成功后点击图片或文字 |
| 窗口与元素 | 按标题、进程名或窗口类名识别窗口；移动、缩放、置顶、显示或隐藏，读取窗口与客户区矩形；UI Automation 的取值、选择、勾选、展开与表格读取 |
| 浏览器 | 打开页面、点击元素、填写表单、读取文字与属性、截图、切换与关闭标签页；默认使用系统自带 Edge |
| 文件与数据 | 读写文本、CSV 与 JSON；剪贴板（含图片）；文件夹与进程；命令行与脚本 |
| 流程与调试 | 循环、分支、尝试与收尾、子宏调用；断点、单步、识别结果、步骤结局与运行日志 |
| 录制与拾取 | 录制键鼠操作并生成步骤；图片、窗口、元素、颜色、区域、按键、手柄、变量八种拾取器 |
| 编辑与保存 | 四类动作库拖放编排，父宏可携带子宏文档，本机公共宏库按版本复制；容器块可折叠；`.wgmacro` 连同图片打包；未保存内容有崩溃恢复快照 |

## 需要什么

- Windows 10 2004（内部版本 19041）或 Windows 11，64 位。
- 界面提供中文与英文；首次启动跟随 Windows 显示语言，可在设置中切换。
- 框架依赖包需要 .NET 10 桌面运行时与 ASP.NET Core 运行时（后者供本机 MCP 服务使用）；
  自带运行时包无需预装。

Windows 10 2004 是 Graphics Capture 的最低要求。更早的系统会退回其他画面获取方式
（见[使用说明](doc/manual.md)中的「画面从哪来、怎么取」），程序仍可运行。

从 [Releases](https://github.com/ilpoint/WhaleGenie/releases) 下载。解压后为 `WhaleGenie` 文件夹，
`WhaleGenie.exe` 位于最外层，双击运行。

## 文档

| 文档 | 内容 |
| --- | --- |
| [doc/manual.md](doc/manual.md) | 使用程序：宏包格式、拾取器、落点与落手、返回值、运行与调试、画面获取、CSV 与程序文件位置 |
| [doc/dev.md](doc/dev.md) | 修改仓库：命令、工程结构、依赖、测试分层、性能测量、代码约定、提交与发布 |
| [doc/architecture.md](doc/architecture.md) | 整体结构：五个工程与依赖方向、一次操作的路径、各子系统、有意的取舍、已知债务与建议顺序 |
| [doc/refactor-plan.md](doc/refactor-plan.md) | 清债计划：五个阶段、每阶段改什么与怎么验收 |
| [doc/editor-mvvm-plan.md](doc/editor-mvvm-plan.md) | 编辑器 MVVM 化：状态归位分五步，每步可独立停下 |
| [doc/roadmap.md](doc/roadmap.md) | 后续计划：未完成阶段、可并行的零散项、明确不做的范围 |
| [doc/performance.md](doc/performance.md)（[English](doc/performance.en.md)） | 单次画面读取的单位成本与测量方法 |
| [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) | 第三方组件与图标出处 |
| [项目介绍页](https://ilpoint.github.io/WhaleGenie/) | 功能概览、界面预览与 24 秒介绍短片 |

## 打赏支持

WhaleGenie（鲸灵）是完全免费的开源项目。如果你觉得它对你有帮助，欢迎请🐋吃白饭🍚。

<table>
  <tr>
    <td align="center"><img src="WhaleGenie/Assets/DonateWeChat.png" alt="微信打赏码" width="220"></td>
    <td align="center"><img src="WhaleGenie/Assets/DonateAlipay.png" alt="支付宝打赏码" width="220"></td>
  </tr>
  <tr>
    <td align="center">微信</td>
    <td align="center">支付宝</td>
  </tr>
</table>

## 许可与致谢

程序按 AGPL-3.0 授权，条款原文见 `LICENSE.txt`，版权归 ilpoint 所有，署名见 `NOTICE`。两份文件
随发布包分发，对应源码为本仓库。图标为 Emojiall 的像素风鲸鱼 emoji，按 CC BY 4.0 使用，已缩放并修改。
