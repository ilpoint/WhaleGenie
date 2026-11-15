# WhaleGenie 架构

本文件描述当前实现的工程划分、依赖方向、入口与组合根，并索引执行、界面、状态和运行期的详细设计。
开发规范见 [AGENTS.md](../AGENTS.md)，构建与验证方法见 [dev.md](dev.md)，未完成工作见
[roadmap.md](roadmap.md)。本文不记录文件行数、测试数量或已经完成的重构历史。

## 1. 架构目标

WhaleGenie 需要在有窗口和无窗口两种形态下运行同一套宏能力，并允许 MCP 客户端读写项目、
查看状态和受控操作机器。因此采用以下基本结构：

1. 执行引擎不依赖界面，触发器、调试运行和 MCP 可以共用。
2. 机器操作通过设备契约隔离，使引擎测试不必访问真实桌面。
3. 宏的编辑态与运行态分离，界面负责文件模型和交互，Core 只接收可执行步骤。
4. MCP 写入先进入草稿，经过校验和版本检查后应用到项目；权限与审计位于界面工程。

## 2. 工程与依赖方向

```text
WhaleGenie
  界面、应用服务、文件模型、包读写、触发器、MCP
    │
    ├──────────────────────────┐
    ▼                          ▼
WhaleGenie.Platform.Windows  WhaleGenie.Core
  SharpHook、FlaUI、OpenCV、     执行引擎、表达式、变量、
  Playwright、Roslyn、NuGet、    设备契约、录制翻译
  VIIPER 等 Windows 适配器          │
    │                              ▼
    └──────────────────► WhaleGenie.GraphicsCapture
                            Windows 画面获取、选路、会话缓存、矩形落位
```

三个测试工程分别以对应产品工程为主：`WhaleGenie.Tests`、`WhaleGenie.Core.Tests` 和
`WhaleGenie.GraphicsCapture.Tests`。`Core.Tests` 目前还引用 `Platform.Windows`，因为其中
一部分用例通过引擎驱动真实适配器；独立适配器测试工程只在出现需要隔离的用例时再拆。产品工程
不反向引用测试工程。

| 工程 | 主要职责 | 不得承担 |
| --- | --- | --- |
| `WhaleGenie` | 窗口、ViewModel、应用生命周期、项目状态、宏与触发器文件模型、`.wgmacro`、文案、MCP | 在组合根之外直接选择第三方设备实现；把界面类型传入 Core |
| `WhaleGenie.Core` | 宏运行、动作语义、变量与表达式、设备契约、录制事件翻译 | Avalonia、FluentAvalonia、MCP、项目文件读写、`WhaleGenie.Platform.Windows` |
| `WhaleGenie.Platform.Windows` | SharpHook、FlaUI、OpenCV、PaddleOCR、Playwright、Roslyn/NuGet、VIIPER 等 Windows 适配器；批处理宿主 | 宏、动作、ViewModel、项目状态或界面类型 |
| `WhaleGenie.GraphicsCapture` | Graphics Capture、桌面复制、GDI、PrintWindow 及 `ScreenGrab` 输出 | 宏、动作、ViewModel 或项目状态 |

`App.axaml.cs`、`Program.cs` 与 `Runtime/SharedDevices.cs` 是界面工程引用具体 Windows 实现
的限定例外：分别负责应用生命周期、脚本宿主分派和设备组合。其他窗口、ViewModel 与应用代码只
依赖 Core 契约，`LayeringTests` 按这一白名单检查；Core 对适配器工程的引用由编译关系直接排除。

宏文件模型位于 `WhaleGenie/Models/`，包读写位于 `WhaleGenie/Storage/`，文案位于
`WhaleGenie/Localization/`。这些类型虽与界面工程一起编译，但模型本身不应读取控件状态；所需的
界面信息通过参数或委托注入。其代价是独立的包读写工具仍需引用界面工程，是否拆分由真实调用方决定。

`WhaleGenie` 通过 Core 的传递引用使用 `CaptureWay` 等画面获取类型，Core 的屏幕契约也公开
`ScreenLayout` 等类型。因此 GraphicsCapture 是依赖叶子，但其公开数据形状属于跨层契约，修改时需同时
检查 Core 与界面。

## 3. 入口、组合根与生命周期

| 类型 | 职责 |
| --- | --- |
| `Program` | 进程入口；分派普通界面、脚本宿主、脚本桥、`--mcp` 和 `--mcp-doctor`，并处理单实例 |
| `App.axaml.cs` | 应用启动、窗口或无窗口模式、预热和退出收尾 |
| `AppServices` | 对象图组合根；注册项目、设备层、触发器、草稿、MCP 服务与工具 |
| `Runtime/SharedDevices.cs` | 持有进程共享的 `WindowsDeviceLayer`，负责预热与最终释放 |
| `Runtime/RunFactory.cs` | 调试、触发器和 MCP 共用的 `MacroRunner` 装配入口 |
| `Runtime/TriggerRuntime.cs` | 持有触发器服务及不可变运行快照 |

窗口、ViewModel 和工具通过构造函数取得有状态依赖。子窗口可以直接创建，因为它们通常携带一次性
编辑参数；进程级服务不得在窗口内部自行实例化。

设备层内部按需要延迟创建输入、OCR、浏览器和脚本等设备。共享会话在退出时统一释放；录制器等单次
任务对象在使用结束后释放。全局钩子属于进程资源，不能由单次运行单独释放。

## 详细设计

| 文档 | 内容 |
| --- | --- |
| [architecture/execution-and-devices.md](architecture/execution-and-devices.md) | 运行、草稿与录制链路；执行引擎、动作目录、变量与并发；设备契约和画面获取 |
| [architecture/interface.md](architecture/interface.md) | 状态边界、步骤与条件编辑器、主题资源、窗口覆盖层和设置窗口 |
| [architecture/state-and-mcp.md](architecture/state-and-mcp.md) | 宏包与项目模型、包外状态、MCP 传输、权限、草稿、读写契约和审计 |
| [architecture/triggers-and-threading.md](architecture/triggers-and-threading.md) | 触发器与录制、轮盘手势、线程模型、退出流程、当前设计取舍与限制 |
