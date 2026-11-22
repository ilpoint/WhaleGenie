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
  窗口、ViewModel、应用生命周期
    │
    └──► WhaleGenie.App
           触发器、运行编排、录制翻译、
           项目服务、MCP 宿主与设备控制工具
             │
    ├──► WhaleGenie.Mcp ────────────┐
    │      协议、草稿、补丁、资源    │
    ├──► WhaleGenie.Domain ─────────┤
    │      宏文档、工程、包读写      │
    ├──► WhaleGenie.Localization ───┤
    │      文案表                    │
    ├──► WhaleGenie.Platform.Windows ─┐
    │      SharpHook、FlaUI、OpenCV、    │
    │      Playwright、Roslyn、NuGet、   ├──► WhaleGenie.Core
    │      VIIPER 等 Windows 适配器      │      执行引擎、表达式、变量、
    ├──► WhaleGenie.GraphicsCapture ───┤      设备契约、录制翻译、
    │      Graphics Capture、桌面复制、  │      画面选路与屏幕矩形
    │      GDI、PrintWindow、会话缓存    │
    └───────────────────────────────────┘
```

工程目录（`src/`、`tests/`）只决定文件位置：工程名、程序集名、根命名空间与 `avares://` 地址都不
随目录变化，所以搬动工程是一次路径与声明的调整，不是一次改名。`tools/` 放构建期工具，不随程序发布。

`WhaleGenie.Contracts` 是桥的载荷形状（请求、应答、事件与端口），只依赖 BCL。它的
`[BridgeContract]` 记录与 `BridgeMethods.All` 由 `tools/WhaleGenie.ContractsGen` 写成
`webui/src/shared/bridge/generated/types.ts` 与 `generated/methods.ts`，页面不手写线缆类型，
`client.call` 的方法名由编译器检查；`ContractGenerationTests` 在没有重新生成时失败。编辑模型正迁往
`WhaleGenie.Editing`（见 [ADR 0002](adr/0002-csharp-webui-split.md)）；取值选择器、形状树与变量墙已迁入，
动作表单与主编辑会话仍待迁。它只引用 Contracts、Domain、Localization 与 Core，不得依赖 Avalonia。

`WhaleGenie.App` 引用 Mcp、Domain、Localization、Contracts、Core 与 Platform.Windows，不引用
Avalonia；界面工程引用 `WhaleGenie.App`、`WhaleGenie.Contracts` 与 `WhaleGenie.Editing`，没有任何产品
工程反向引用它。`WhaleGenie.Editing` 引用 Contracts、Domain、Localization 与 Core。
Mcp 与 Domain 都引用 Localization 和 Core；Contracts 不引用任何产品工程；Localization 只引用 Core；
Platform 与 GraphicsCapture 都引用 Core。

八个测试工程按被测对象划分：`WhaleGenie.Editor.Tests`（宏编辑窗、步骤树、变量墙、窗口规则与菜单）、
`WhaleGenie.Actions.Tests`（动作对话框、页面归属、返回值与拾取器）、`WhaleGenie.Tests`（运行与调试、
MCP、设置、界面文案与文档对账）、
`WhaleGenie.Domain.Tests`（宏文档、包格式、设计期检查、变量、触发器与运行期服务，不开窗口）、
`WhaleGenie.Mcp.Tests`（协议、工具、拒绝码与容器，不经窗口）、`WhaleGenie.Core.Tests`（引擎与
动作语义，只用替身设备层）、`WhaleGenie.Platform.Tests`（通过引擎驱动真实 Windows 适配器）和
`WhaleGenie.GraphicsCapture.Tests`。`Core.Tests` 不引用 `Platform.Windows`：引擎测试运行时不加载
OpenCV、Playwright、FlaUI 等原生依赖，这条边界由工程引用直接保证。`Domain.Tests` 与
`Mcp.Tests` 不引用界面工程，前者的分层用例会拦住一次误加的 Avalonia 引用。

三个窗口套件各自一份无头会话：会话属于运行它的程序集，借用别人的会在错误的线程上拆除，所以
`Ui`、`TestApp` 这些夹具以链接方式编进每个套件（一份源码，每个套件一份副本），套件之间因此可以
并行跑；界面相关的三个套件合计 1,611 条用例，串行 6 分半，分片后全量 2 分钟。

两个支撑库不含测试：`WhaleGenie.TestSupport` 给引擎侧两套件提供替身设备层与样本文件，
`WhaleGenie.App.TestSupport` 给应用侧两套件提供建步骤的 `Fields` 与假桌面 `ChecksDevices`。产品工程
不反向引用测试工程。

| 工程 | 主要职责 | 不得承担 |
| --- | --- | --- |
| `WhaleGenie` | 窗口、ViewModel、应用生命周期、窗口化组合根、依赖窗口/对话框的桥处理器与 WebView 适配 | 在组合根之外直接选择第三方设备实现；把界面类型传入 Core/Domain/Mcp/App |
| `WhaleGenie.App` | 触发器、运行编排、录制翻译、项目服务、系统开关（`SystemState`）、MCP 宿主与设备控制/运行工具、桥路由/推送队列与无 UI 依赖的处理器（应用信息、字符串、MCP、库、窗口按钮、主题、壳门、页面对话框、项目）、窗口化与无窗口共用的组合根 | Avalonia 与任何窗口类型；把应用状态放进界面线程 |
| `WhaleGenie.Contracts` | 桥的请求/应答/事件记录、方法与端口；只依赖 BCL，页面类型由它生成 | Avalonia、Domain/Core 类型、设备或文件读写 |
| `WhaleGenie.Editing` | 编辑模型与无头会话（迁移中；键位表、字符面板、`ViewModelBase`、取值选择器、形状树、变量墙、动作页/返回/时长/热字串选项与 `HeadlessValueSession` 已迁入）；`net10.0-windows10.0.19041`，引用 Contracts、Domain、Localization 与 Core | Avalonia、窗口、WebView、项目文档读写 |
| `WhaleGenie.Domain` | 宏文档模型、工程文档、设计期检查、包读写、图片文件与尺寸 | Avalonia、窗口、ViewModel、MCP、设备控制 |
| `WhaleGenie.Mcp` | 协议宿主、草稿、补丁、预览、资源与项目编辑工具 | Avalonia、Views、Runtime、设备控制、运行桥 |
| `WhaleGenie.Core` | 宏运行、动作语义、变量与表达式、设备契约、键词汇表、录制事件翻译；`net10.0`，是仓库里唯一可打包的工程 | Avalonia、FluentAvalonia、MCP、项目文件读写、`WhaleGenie.Platform.Windows`；除 BCL 与 `CsvHelper` 之外的任何引用 |
| `WhaleGenie.Platform.Windows` | SharpHook、FlaUI、OpenCV、PaddleOCR、Playwright、Roslyn/NuGet、VIIPER 等 Windows 适配器；批处理宿主 | 宏、动作、ViewModel、项目状态或界面类型 |
| `WhaleGenie.Localization` | 界面字、动作名、表达式与日志措辞的文案表 | Avalonia、窗口、项目状态、设备或文件读写 |
| `WhaleGenie.GraphicsCapture` | Graphics Capture、桌面复制、GDI、PrintWindow 的 Windows 实现与会话缓存；引用 Core 并实现它的画面契约 | 宏、动作、ViewModel 或项目状态；定义跨层共用的形状（它们在 Core.Devices） |

`App.axaml.cs`、`Program.cs` 与 `Runtime/SharedDevices.cs` 是引用具体 Windows 实现的限定例外：
分别负责应用生命周期、脚本宿主分派和设备组合。其他窗口、ViewModel 与应用代码只依赖 Core 契约，
`LayeringTests` 按这一白名单检查窗口与应用两层；Core 对适配器工程的引用由编译关系直接排除。

`WhaleGenie.Localization` 只放文案表；`LocalSettings.cs`（读写本机设置）与主题选择的值类型随
应用层走，因为运行、触发器和无窗口服务都要读它们。`LanguageRefresh.cs`（驱动 Avalonia 绑定刷新）
留在界面工程，因为它依赖窗口。

`WhaleGenie.Mcp` 不引用界面工程。直接控制、运行宏和读运行日志这三类工具留在
`src/WhaleGenie.App/Mcp/`，由 `AppServices` 作为 tool owner 注入 `McpServerHost`；协议与草稿本身
因此可以脱离窗口编译和测试。
`--mcp` 在 Avalonia 之前分派：窗口化运行由界面工程注册 Avalonia 的调度器与窗口让位，无窗口运行
注册直接调度器与“没有窗口可让”的实现，因此无界面服务不启动界面框架。`WhaleGenie.Domain` 同样不
引用 Avalonia：位图转换在 `Assets/BitmapImages`，崩溃快照写入在 `Storage/RecoveryWriter`，两者都
留在界面工程。

宏文件模型与工程文档状态位于 `src/WhaleGenie.Domain/Models/`（`ProjectWorkspace` 持有宏、触发入口、
共享变量、包路径与修订号），包读写位于 `src/WhaleGenie.Domain/Storage/`，文案位于
`src/WhaleGenie.Localization/`。这些类型不与界面工程一起编译，模型本身也不读取控件状态；
`MainViewModel` 只做转发。

画面获取的形状与键的词汇表都由 Core 定义——`CaptureWay`、`ScreenLayout`、`ScreenRect`、
`ScreenCaptureRequest` 与 `KeyNames`——GraphicsCapture 与各适配器只负责把它们变成像素和事件：
DXGI、Direct3D、GDI 与 SharpHook 的调用留在各自工程。引擎与界面因此共用一套矩形、选路与按键说法，
读到这些形状时也不加载原生依赖。两个工程反过来引用 Core，改动这些形状要同时检查 Core、界面与实现
三处。公共面分哪几档、基线怎么维护见 [architecture/core-api.md](architecture/core-api.md)。

## 3. 入口、组合根与生命周期

| 类型 | 职责 |
| --- | --- |
| `Program` | 进程入口；分派普通界面、脚本宿主、脚本桥、`--mcp` 和 `--mcp-doctor`，并处理单实例 |
| `App.axaml.cs` | 应用启动、窗口或无窗口模式、预热和退出收尾 |
| `AppServices` | 应用层组合根；注册项目、设备层、触发器、草稿、MCP 服务与工具，窗口与无窗口两种形态共用 |
| `WindowedServices` | 窗口化组合根；在应用层图上补窗口与主 ViewModel |
| `Runtime/SharedDevices.cs` | 持有进程共享的 `WindowsDeviceLayer`，负责预热与最终释放 |
| `Runtime/RunFactory.cs` | 调试、触发器和 MCP 共用的 `MacroRunner` 装配入口 |
| `Runtime/SystemState.cs` | 全程序唯一的系统开关；页面、热键与窗口写同一个对象，触发宿主跟随它启停触发器 |
| `Runtime/TriggerRuntime.cs` | 持有触发器服务、共享设备层及不可变运行快照 |

窗口、ViewModel 和工具通过构造函数取得有状态依赖。子窗口可以直接创建，因为它们通常携带一次性
编辑参数；进程级服务不得在窗口内部自行实例化。

设备层内部按需要延迟创建输入、OCR、浏览器和脚本等设备。共享会话在退出时统一释放；录制器等单次
任务对象在使用结束后释放。全局钩子属于进程资源，不能由单次运行单独释放。

## 详细设计

| 文档 | 内容 |
| --- | --- |
| [architecture/execution-and-devices.md](architecture/execution-and-devices.md) | 运行、草稿与录制链路；执行引擎、动作目录、变量与并发；设备契约和画面获取 |
| [architecture/core-api.md](architecture/core-api.md) | 引擎的公共面分档、公共 API 基线的维护、打包与 XML 文档债务 |
| [architecture/interface.md](architecture/interface.md) | 状态边界、步骤与条件编辑器、主题资源、窗口覆盖层、网页设置页与关于页 |
| [architecture/editor-parity.md](architecture/editor-parity.md) | 旧动作表单/步骤树功能与网页消费点的对账表 |
| [architecture/native-surfaces.md](architecture/native-surfaces.md) | 永久保留的原生窗口、契约与退役清单 |
| [architecture/state-and-mcp.md](architecture/state-and-mcp.md) | 宏包与项目模型、包外状态、MCP 传输、权限、草稿、读写契约和审计 |
| [architecture/triggers-and-threading.md](architecture/triggers-and-threading.md) | 触发器与录制、轮盘手势、线程模型、退出流程、当前设计取舍与限制 |
