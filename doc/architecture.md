# WhaleGenie 架构

本文件描述当前实现的工程划分、依赖方向、主要执行链路、子系统边界和已知限制。
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
    ▼
WhaleGenie.Core
  执行引擎、表达式、变量、设备契约、Windows 设备实现、录制翻译
    │
    ▼
WhaleGenie.GraphicsCapture
  Windows 画面获取、选路、会话缓存、矩形落位
```

三个测试工程分别引用对应产品工程：`WhaleGenie.Tests`、`WhaleGenie.Core.Tests` 和
`WhaleGenie.GraphicsCapture.Tests`。产品工程不反向引用测试工程。

| 工程 | 主要职责 | 不得承担 |
| --- | --- | --- |
| `WhaleGenie` | 窗口、ViewModel、应用生命周期、项目状态、宏与触发器文件模型、`.wgmacro`、文案、MCP | 在组合根之外直接选择第三方设备实现；把界面类型传入 Core |
| `WhaleGenie.Core` | 宏运行、动作语义、变量与表达式、设备抽象、真实设备、录制事件翻译 | Avalonia、FluentAvalonia、MCP、项目文件读写 |
| `WhaleGenie.GraphicsCapture` | Graphics Capture、桌面复制、GDI、PrintWindow 及 `ScreenGrab` 输出 | 宏、动作、ViewModel 或项目状态 |

`App.axaml.cs` 与 `Runtime/SharedDevices.cs` 是界面工程引用具体 Windows 设备实现的限定例外，
分别负责应用生命周期和设备组合。其他窗口、ViewModel 与应用代码只依赖 Core 契约，
`LayeringTests` 按这一白名单检查。

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

## 4. 主要执行链路

### 4.1 运行宏

```text
手动运行 / MacroTriggerService / MCP
  → MacroStepConverter.ToExecutable
  → RunFactory.Create(RunRequest)
  → MacroRunner.RunAsync
  → RunSteps / RunStep / RunAttempts
  → 按动作族分派
  → IDeviceLayer 及 Windows 设备实现
  → StepRun / StepResult / RunLogStore
  → 运行界面或 MCP 日志
```

`MacroStep` 是编辑态，包含动作定义、子列表和编辑信息；`ExecutableStep` 是运行态，只包含执行所需的
类型、标识、元数据、参数和子步骤。转换在每次运行时重新进行，编辑态不会传入 Core。

界面发起的运行在线程池执行，需要问话或更新界面时通过宿主接口回到界面线程。触发器回调只读取
不可变快照并派发运行，不访问可变界面集合。

### 4.2 MCP 应用草稿

```text
MCP 工具
  → McpLiveProject
  → Dispatcher.UIThread
  → MainViewModel.ApplyDraft
  → 更新宏、触发器和全局变量声明
  → MarkDirty / Changed
  → 发布新的 TriggerRuntimeSnapshot
```

草稿记录打开时的项目 `Revision`。应用时若项目已变化则拒绝，避免覆盖用户在界面中的修改。
只有 `apply` 改变项目，只有 `save_project` 写入宏包。

### 4.3 录制操作

```text
全局输入钩子
  → InputHookRecorder / RecordedInput
  → RecordingTranslator
  → RecordedAction
  → RecordingStepBuilder（应用层）
  → MacroStep
  → 编辑器与 RecoveryStore
```

Core 负责原始事件到录制动作的翻译；由于 `MacroStep` 属于界面工程的文件模型，最后的步骤映射由
应用层完成。

## 5. 执行引擎

### 5.1 动作目录与分派

`Execution/ActionSemantics.json` 嵌入 Core，由 `ActionSemanticsCatalog` 读取，声明动作分类、
参数读法、值形状、单位、空值、范围、选项、拾取器、子列表、副作用、等待和返回值。

`Models/ActionDisplay.json` 只保存界面标签、提示、编辑器种类和页面归属。`ActionCatalog` 将两类
信息投影给编辑器；`ActionContractTests` 检查二者对应关系。显示层另有一个 `WrittenByTool`：这类值
由专门的窗口写入（基准色周围要对的颜色），表单不给它生成行，值本身照常保存与校验。

`MacroRunner` 是按动作族拆分的 `partial` 类。主文件维护运行状态和循环，各
`MacroRunner.<Family>.cs` 维护本分族的分派与实现，公共读值集中在 `MacroRunner.Reading.cs`。
目录声明语义，但不替代动作分派；新增动作仍需在具体处理器中实现。

试运行分为“是否支持查看”和“实际查看一次”。前者读取目录的 `LooksAtScreen`，后者由 `SeeOnce`
等执行路径完成。修改支持试运行的动作时，必须同步检查这两条路径。

### 5.2 运行、失败与等待

- `RunAttempts` 统一处理异常失败和信号失败，并执行步骤重试与失败策略。
- 步骤超时是该步骤所有尝试共用的预算；重试不重新获得完整预算。
- 等待动作使用统一步骤超时，不另建同义超时字段。无法中途取消的设备调用在返回后检查预算。
- 每次尝试清空本步结果；同步调用失败不交付部分结果。
- `IRunHost` 提供步骤前后通知和问话能力；断点由界面宿主实现，不属于 Core 的独立流程机制。

### 5.3 控制流与变量

控制流使用块结构，循环变量在进入和离开循环时保存与恢复。子宏调用通过 `IMacroLibrary` 解析，
并切换局部变量表；形参、返回值和递归限制在调用边界检查。

变量读取按局部、全局、运行提供值和系统值解析。`VariableBag` 对共享写入提供锁和变更通知，
累计与列表修改提供原子操作。表达式系统负责解析、求值、引用分析和变量改名。

### 5.4 并发门与观测

`InputGate` 和 `BrowserGate` 分别串行化输入与浏览器操作。输入需求由目录声明并沿调用图计算，
不按动作名前缀判断。持有门的容器块负责整块互斥，块内步骤不重复获取同一门。

`StepRun`、`StepAttempt` 和 `StepResult` 记录步骤与尝试结果；`RunLogStore` 向运行界面和 MCP
提供日志。运行失败使用 `Run.*`，日志事件使用 `Log.*`，MCP 拒绝码使用独立词表。

## 6. 设备层与画面获取

### 6.1 设备契约与实现

`Core/Devices/IDeviceLayer.cs` 聚合输入、屏幕、视觉、OCR、检测、UIA、文件、剪贴板、进程、系统、
浏览器、窗口、钩子、录制和脚本等契约。消费者可以只依赖所需接口；`MacroRunner` 和综合编辑器使用
聚合层。

`Core/Devices/Platform/` 是 Windows 实现所在位置，第三方库的类型和异常不得泄漏到界面。
`NullDeviceLayer` 提供无设备默认实现，测试替身按设备分别组装，并共享一份有序调用记录以验证
跨设备顺序。

### 6.2 画面获取

`ScreenGrabber` 负责选路、回退和会话缓存，`OneThread` 串行执行需要专用线程的图形调用。
输出 `ScreenGrab` 使用紧密排列的 BGRA 像素，并记录实际采用的 `CaptureWay`；源矩形裁剪、
越界处理和多显示器拼接由 `Placement`、`ScreenPlacement` 等类型负责，坐标不保存在 `ScreenGrab` 中。

自动选路顺序为：

- 窗口：Graphics Capture → PrintWindow → GDI；无 Graphics Capture 会话时为 PrintWindow → GDI。
- 桌面：桌面复制 → GDI。

多显示器画面按虚拟桌面坐标拼接，源图未覆盖的区域保持黑色。窗口与显示器会话需要明确释放；
共享会话由 `SharedDevices` 持有到退出。

## 7. 界面与 FluentAvalonia

### 7.1 状态边界

| 状态 | 主要持有者 |
| --- | --- |
| 项目文档态 | `MainViewModel`：宏、触发器、路径、修改标记和修订号 |
| 编辑态 | `MacroEditorSession` 与编辑器 ViewModel：文档页、子宏、录制和编辑操作 |
| 运行期态 | `TriggerRuntime`、运行宿主和窗口字段：触发快照、浮动界面、恢复和运行显示 |
| 纯界面态 | 窗口与控件：拖动、落点、滚动、焦点和弹出层 |

XAML 使用编译绑定，CommunityToolkit.Mvvm 提供源生成命令。窗口后置代码继续处理拖放、窗口生命周期
和控件交互，不以“窗口零逻辑”为目标；项目与设备等有状态服务仍由组合根提供。

### 7.2 主题资源

`App.axaml` 使用 `FluentAvaloniaTheme`，控件模板和密度由 FluentAvalonia 提供。项目自身维护三层资源：

| 层 | 文件 | 职责 |
| --- | --- | --- |
| 语义令牌 | `Themes/Tokens.axaml` | 深色、浅色和自定义主题的颜色、画笔、字号、间距与圆角 |
| Fluent 资源键 | `Themes/FluentKeys.axaml` | 将 FluentAvalonia 模板读取的资源键映射到项目令牌 |
| 自定义控件类 | `Themes/Controls.axaml` | 项目控件族及其状态、变体和必要模板补充 |

ColorPicker 的 Fluent 样式从 `Avalonia.Controls.ColorPicker` 包内的
`Themes/Fluent/Fluent.xaml` 引入；`Avalonia.Themes.Fluent` 不作为应用主主题。`ThemePalette` 与
`ThemeController` 负责应用和保存主题选择。新增样式时须遵守 `Controls.axaml` 文件头的登记规则，
并通过相关样式测试。

`FluentAvaloniaTheme` 没有接手 `ContextMenu` 的控件主题。库的 `ControlThemes/BasicControls` 里有
`Menu`、`MenuItem`、`MenuFlyoutPresenter` 和 `Separator`，唯独没有 `ContextMenu`，而它的模板原本由
`FluentTheme` 提供。`Themes/Controls.axaml` 的 `Styles.Resources` 里因此有一份按项目令牌写的
`ContextMenu` 模板：删掉它，每个右键菜单仍会打开、`IsOpen` 仍为真，但屏幕上不画任何东西。
不要为此重新引入 `<FluentTheme/>`，那会盖掉层 1 已经映射好的颜色。`MenuThemeTests` 核对这份主题
存在、打开后的菜单有尺寸和 `ItemsPresenter`。

### 7.3 窗口与覆盖层

普通应用窗口继承 `AppWindow`，后者继承 `FAAppWindow`。主窗、宏编辑器、动作编辑器和运行窗将内容
扩展到标题栏，`AppWindow` 负责该区域的拖动；其他普通窗口使用库提供的默认标题栏。
`WindowChrome.Attach(this)` 把这条约定收成一处：它把标题栏交给内容，画上鲸鱼图标和窗口标题，
并留出系统按钮需要的宽度。全局变量、这一步看到了什么、表达式、断点条件与设置窗口在构造函数里
调用它；标题文字跟随 `Window.Title`，所以运行中会改名的窗口（表达式构建器）不必自己同步。

框选、取色、点与元素拾取、屏幕框、菜单浮窗和浮动面板等无标题栏覆盖层继续使用 Avalonia `Window`。
拾取器打开时先通过 `AppWindows.StepAside` 隐藏鲸灵窗口，再取屏幕；取图一律经过 `ScreenReady`：
它连拍到相邻两张一致为止，因为桌面复制器在收不到新帧时会继续交出隐藏前的画面，只等合成器
（`DwmFlush`）不足以保证这一点。取色器铺满整个桌面并显示这张冻结的画面，放大镜是它上面的浮层，
所以看到的就是取到的，滚轮也不会翻动后面的内容。

无头平台的窗口句柄均为 0，`AppWindow` 清理 FluentAvalonia 在此情形下重复使用的内部句柄表。
该处理只服务无头会话，真实窗口仍由 FluentAvalonia 管理。

## 8. 持久化与项目模型

`.wgmacro` 是 ZIP 包，包含 `manifest.json`、说明、`nodes/` 下的宏 JSON 和资源目录。
`Storage/MacroPackage.cs` 负责读写，采用临时文件加替换的原子写入方式。项目包格式当前为 21，
加载时要求版本严格相等；公共宏库使用独立格式版本。

包里的图片存在 zip 的 `assets/` 条目里；打开时解出来、截图时写入的是**程序数据目录**下这个包专属的工作目录
（`%APPDATA%\WhaleGenie\projects\<宏包名>-<路径哈希>\assets`），上一次保存替换掉的包也留在同一目录的
`last.wgmacro.bak`。它们不放在包旁边：项目文件夹因此只有 `.wgmacro`，包放在只读位置时也有地方解图，
两个同名的包也不会互相覆盖。
写包时找图有两个来源：包自己旁边，以及调用方交过来的来源目录（`Save` 的 `assetFolder`）——
导出所选宏、另存到别处、从公共宏库导出都写在图片从来没去过的地方，不给这一项，`assets/x.png`
就解析不到，图会被悄悄漏掉。`AssetCollector` 对每个目录先试 `目录/assets/x.png`，再试 `目录/x.png`，
因为值可能是相对包的路径，也可能只是解包目录里的文件名。

`ProjectTriggers` 是项目级触发入口的唯一来源，触发入口、菜单页面、界面布局和禁止呼出规则属于项目。
菜单呼出键与系统总开关属于当前机器的 `LocalSettings`，切换项目不改变它们。父宏与内嵌子宏保存在
同一宏文档中；公共宏拖入项目时复制内容并固定来源版本，不随后续库更新。

包外状态分为：

| 状态 | 位置 | 说明 |
| --- | --- | --- |
| 本机设置 | 程序目录 `settings.json` | 主题、捕获、MCP 权限和启动行为等 |
| 持久化全局变量值 | `%LOCALAPPDATA%\WhaleGenie\persistent-variables.json` | 只保存声明为持久化的值；声明仍在包内 |
| 崩溃恢复 | 程序目录 `recovery.json` | `RecoveryStore` 与 `RecoveryWriter` 保存未提交编辑状态 |
| 脚本包 | 程序目录 `packages/`，必要时回退到 `%LOCALAPPDATA%\WhaleGenie\packages` | Python 与 NuGet 包的独立缓存 |

## 9. MCP

### 9.1 进程与传输

客户端始终启动 `whalegenie --mcp` 子进程，并通过 stdio 交换 JSON-RPC：

- 若窗口进程已运行，子进程通过当前用户命名管道 `whalegenie-mcp-<用户>` 转发到窗口，工具操作
  屏幕上的项目，包括未保存修改。
- 若没有窗口，子进程直接提供 stdio 服务。可以通过 `--open` 或 `--new` 指定项目；未指定时
  先尝试加载上次项目，无法加载则使用空项目。

该结构不监听网络端口，也不需要令牌。stdio 专用于协议，诊断文字只能写入 stderr。

### 9.2 权限、草稿与工具

权限只有本机设置文件一个来源：直接控制与运行宏分别由开关控制，默认关闭。服务在每次调用前读取
`McpGrants`；文件缺失、不可读或只写入一半时按未授权处理。只读项目、读取状态和读取日志不需要开启
控制权限。

`McpDraftStore` 保存项目深拷贝及修订号，`McpLiveProject` 负责界面线程桥接，`McpPreview` 提供
应用前差异。`McpTolerantTool` 统一处理参数形状错误，`McpErrors` 提供稳定拒绝码。
工具类型由 `AppServices` 注册，并由 `McpServerHost` 的发现列表纳入宿主；新增工具须同时更新两处
并验证 `tools.list`。

`McpResources` 提供动作目录、动作矩阵、项目与宏资源。当前不声明资源订阅，客户端通过修订号判断
缓存是否失效。`RunFactory` 复用普通运行路径，MCP 运行宏仍受单运行限制。

### 9.3 自检与审计

`--mcp-doctor` 使用真实 MCP 客户端驱动协议检查，覆盖发现、草稿、补丁、资源、权限和运行入口。
它验证协议面，不替代引擎测试或真实桌面验收。

`McpAudit` 将改变状态、操作机器或导出完整图像的调用写入 `logs/mcp-YYYY-MM-DD.log`。文本输入只记录
长度。新增此类工具时必须明确审计行为，失败日志与 MCP 审计按文件名前缀区分。

## 10. 触发器与录制

`MacroTriggerService` 分派热键、颜色、热字串、计划、文件、进程、窗口和空闲触发；界面与菜单由各自
浮层直接发起运行。后台服务只读取 `TriggerRuntimeSnapshot` 和 `GlobalHotkeyPlan`，并通过异常隔离与
节流日志避免重复故障刷屏。

Core 的录制层将原始输入翻译为去抖、合并后的 `RecordedAction`，阈值集中在 `RecordingOptions`。
应用层的 `RecordingStepBuilder` 将动作转换为可编辑步骤，编辑器负责保存与恢复快照。

## 11. 线程与退出

- **界面线程**：项目状态、ViewModel、窗口操作及需要界面上下文的 MCP 读写。
- **钩子线程**：全局输入回调与触发判定，只读取快照并派发，不访问控件。
- **线程池**：宏运行、MCP 会话、异步设备等待和模型预热。
- **图形专用线程**：Graphics Capture 与桌面复制等需要串行图形调用的路径。
- **互斥原语**：`InputGate`、`BrowserGate` 和 MCP 单运行控制分别保护不同资源，不合并为全局大锁。

退出时不得在界面线程同步等待 MCP 停止，因为 MCP 请求可能正在等待界面线程。窗口隐藏与进程退出
必须区分；真正的退出流程统一处理未保存内容、触发器、MCP、共享设备和虚拟输入设备。

## 12. 当前设计取舍与限制

1. **文件模型仍在界面工程。** 这避免了过早增加领域工程，但无界面包工具仍需引用 Avalonia 所在工程；
   只有出现真实调用方才考虑拆分。
2. **设备聚合接口较宽。** 综合运行器和编辑器需要多种设备，窄接口按实际使用点逐步采用；测试替身按
   设备拆分，不要求先重写整个设备契约。
3. **界面保留交互后置代码。** 拖放、焦点和窗口行为留在窗口中，不为形式上的完整 MVVM 引入消息总线或
   逐控件 ViewModel。
4. **文案采用手写字典。** 文案可逐条审阅并由测试对账，不引入生成器复制另一份定义。
5. **部分运行路径仍依赖手写分派。** 动作目录与 `SeeOnce`、MCP 工具发现和审计调用点需要同步维护，
   相关改动必须增加可失败的检查，不能依赖人工记忆。
6. **进程级共享状态仍然存在。** 全局变量、文案和部分设备入口采用静态共享；测试须恢复这些状态，
   新代码不得借此绕过组合根管理有状态服务。
7. **MCP 会话不提供资源订阅。** 客户端使用修订号主动刷新；多客户端通过各自进程与草稿隔离。
