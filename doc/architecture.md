# 架构

这份文件讲程序是怎么搭起来的：有哪几个工程、谁可以引用谁、一次操作从界面走到机器再走回来经过哪些层、每个子系统的关键类型在哪、以及哪些地方是**有意**那么做的、哪些地方是欠着的。

它不重复另外几份文件的内容：`README.md` 讲这是什么程序，`doc/manual.md` 讲用户怎么用，`doc/dev.md` 讲怎么开发与验证，`AGENTS.md` 讲改代码的约定。这里讲结构。

> 数字都是量过的（2026-10 这一版）：三个产品工程约 4.2 万行、369 个 `.cs` / `.axaml` 文件；界面层 1409 条用例、引擎层 1461 条、画面获取 34 条。

## 一、这是什么

一个 Windows 桌面宏工具：人写宏，宏去操作这台机器（键鼠、窗口、找图找色、OCR、浏览器、脚本），并且可以被热键、计划、窗口事件等触发。同一份宏还能通过本机回环上的 MCP 服务交给 AI 客户端读写与运行。

三件事决定了架构的形状：

1. **宏要在没有界面的情况下也能跑**（触发器在后台跑、MCP 无窗口跑），所以引擎不能依赖 Avalonia。
2. **宏操作的是真机器，不是假机器**，所以"怎么操作"必须能被替换成替身——否则引擎的每一条用例都要真的点一下桌面。
3. **AI 会改用户的宏**，所以写入要经过草稿、校验、预览，且每次调用要留审计。

## 二、四个工程与依赖方向

```
        WhaleGenie（界面 + 应用层 + MCP 宿主）
                 │
                 ▼
        WhaleGenie.Core（引擎 + 设备契约 + 表达式 + 录制）
                 │
                 ▼
        WhaleGenie.GraphicsCapture（D3D/DXGI 取画面，叶子）

  测试：WhaleGenie.Tests → WhaleGenie
        WhaleGenie.Core.Tests → WhaleGenie.Core
        WhaleGenie.GraphicsCapture.Tests → GraphicsCapture
```

| 工程 | 是什么 | 依赖 | 明确不依赖 |
| --- | --- | --- | --- |
| `WhaleGenie` | Avalonia 界面、应用层（包、触发器、恢复、变量桥接）、MCP 宿主与全部工具 | Core | 不反向依赖任何测试工程 |
| `WhaleGenie.Core` | 运行器、步骤与动作目录、表达式、变量、设备契约、录制翻译 | GraphicsCapture | Avalonia、ASP.NET Core、MCP、AI 库（有测试守着） |
| `WhaleGenie.GraphicsCapture` | 用 Desktop Duplication 与 Windows.Graphics.Capture 取一帧 | Vortice.Direct3D11/DXGI | Core、界面，任何与宏有关的概念 |
| `WhaleGenie.Tests` | 界面层与 MCP 的用例，跑在 Avalonia 无头会话里 | WhaleGenie | — |
| `WhaleGenie.Core.Tests` | 引擎每条动作的用例，跑在替身设备层上 | Core | — |

`WhaleGenie.GraphicsCapture` 单独成工程的理由写在它的 csproj 里：把 D3D/DXGI 挡在引擎之外，而且它不随包发原生库（Direct3D 由 Windows 提供）。它是唯一一个"不认识宏"的工程。

### 一个隐式依赖要知道

界面工程没有显式引用 `WhaleGenie.GraphicsCapture`，但 `CaptureSetup.cs`、`LocalSettings.cs` 直接用了它的 `CaptureWay`，`IDeviceLayer.cs` 的 `IScreenDevice.Desktop` 直接返回它的 `ScreenLayout`。也就是说：**取画面这个叶子库的词汇表，渗到了引擎契约和界面设置里**。这是可以接受的（枚举就几个值），但它意味着换掉这个库不是"只改一个工程"的事。

## 三、分层规则与守卫

真正的规则只有三条，其余都是推论：

1. **引擎不知道界面存在。** Core 里没有 `Avalonia`、没有 `ViewModel`、没有 `HttpClient`。它对"宿主"的全部要求写在 Core 定义的接口里，由界面侧实现：`IRunHost`（每步前后、问话）、`IMacroLibrary`（按名字取别的宏）、`IRunLooks`（试运行看一次）、`IDeviceLayer`（机器）。
2. **只有组合根选实现。** 除了 `App.axaml.cs` 与 `SharedDevices.cs`，任何文件都不许出现 `WhaleGenie.Core.Devices.Platform` 命名空间——即不许自己 `new` 一个 Windows 设备。
3. **引擎不碰 MCP 与 AI。** Core 里不许出现 `ModelContextProtocol`、`Microsoft.AspNetCore`、`Microsoft.Extensions.AI`。

前两条由 `WhaleGenie.Tests/LayeringTests.cs` 逐个文件扫描守住，第三条会直接让构建失败（Core 没有那些包）。这类守卫的价值在于：它们检查的是**依赖方向**，而依赖方向是重构里最容易悄悄坏掉的东西。

### 共享的领域模型放在哪

`WhaleGenie/Models/`（31 文件 8377 行）不是"界面模型"，而是**全域共享的领域层**：`MacroItem`、`MacroStep`、`ProjectTriggers`、`MacroCheck`、动作定义。界面、持久化、MCP、应用层同时引用它。

这是这套架构里最需要说清楚的一处：它**名义上在界面工程里，实际上是领域层**。好处是移动少、类型少一层映射；代价是 `Storage` 与 `Mcp` 想用它就必须连同 Avalonia 一起编译，"只测领域规则"这件事做不成——`MacroCheck.Inspect`（1083 行的校验方法）因此只能在无头会话里跑。

## 四、一次操作的路径

### 4.1 跑一条宏（含触发器）

```
热键/计划/窗口事件/手动点运行
  → MacroTriggerService.Fire / RunViewModel.RunAsync
  → MacroStep.ToExecutable()            每个 MacroStep 变成 ExecutableStep（有损：丢掉编辑态）
  → new MacroRunner(devices, host, …)   ← 装配在这一层手工完成（三处各写一遍，见「债务」）
  → MacroRunner.RunAsync → RunSteps → RunStep → RunAttempts → Execute
  → Dispatch*(step)                     13 个分部按动作族分派到 IDeviceLayer
  → IDeviceLayer.Xxx                    Windows 实现，真的发输入 / 读屏幕
  → StepRun/StepResult  →  RunLogStore  →  界面的步骤树、MCP 的 get_run_log
```

跨线程：触发器在钩子线程与 `Task.Run` 的线程上跑，需要问人时经 `IRunHost.Ask` 回到界面线程；界面手动运行则在界面线程上发起、运行本身在线程池上。

### 4.2 MCP 客户端写一条宏

```
HTTP POST /mcp
  → McpTolerantTool.InvokeAsync          先看参数形状，形状不对就回 BadJson
  → McpTools.Apply                       工具层，只做参数与答案
  → Dispatcher（第 1 跳）
  → McpLiveProject.ApplyAsync
  → Dispatcher（第 2 跳）
  → MainViewModel.ApplyDraft             项目状态真正改变的地方
  → ProjectTriggers / VariableCatalog.Globals / PersistentVariables
  → MarkDirty → Changed 事件
  → MainWindow.OnProjectChanged → PublishRuntimeState → TriggerRuntimeSnapshotStore
  → MacroTriggerService 下一轮轮询看到新状态
```

一次 apply 穿过 7 层、两次界面线程跳转。**这个深度是刻意的**：工具层不许碰项目，项目只许在界面线程上改，改完要让触发器知道。但它也是"改一处要读五个文件"的来源。

### 4.3 录一段操作

```
钩子线程 → InputHookRecorder（Core.Devices）
  → RecordedInput（原始事件 + 相对毫秒）
  → RecordingTranslator.Translate（去抖、合并、识别双击与拖拽）
  → RecordedAction（16 种）
  → RecordingStepBuilder.ToSteps（应用层）→ MacroStep
  → MacroEditorViewModel.AppendSteps → MarkDirty → RecoveryStore
```

录制在 Core 里只走到 `RecordedAction`：因为 `MacroStep` 属于应用层，最后一步的映射只能在外面。这不是疏忽，是"引擎不认识动作目录里那些给编辑器看的字段"的代价。

## 五、引擎（WhaleGenie.Core）

### 5.1 运行器是 14 个文件的一个类

`MacroRunner` 是 `sealed partial class`，主文件 3075 行，另有 13 个按动作族切开的分布：

| 文件 | 负责 | 行数 |
| --- | --- | ---: |
| `MacroRunner.cs` | 会话状态、`RunAsync`、单步与重试、控制流、条件求值、变量写回、日志 | 3075 |
| `MacroRunner.Input.cs` | 键鼠、手柄、窗口内坐标换算 | 1412 |
| `MacroRunner.Files.cs` | 文件与数据动作 | 1133 |
| `MacroRunner.Detection.cs` | 找图找色、模板与 YOLO 检测 | 1034 |
| `MacroRunner.Vision.cs` | 截图与像素 | 914 |
| `MacroRunner.Script.cs` | 脚本、等待原语 | 847 |
| `MacroRunner.Window.cs` | 窗口动作、`Give`（共用的参数读法） | 599 |
| `MacroRunner.Browser.cs` | 浏览器动作 | 592 |
| `MacroRunner.Ocr.cs` | 文字识别 | 424 |
| `MacroRunner.Uia.cs` | UI 自动化 | 364 |
| `MacroRunner.System.cs` | 系统动作 | 348 |
| `MacroRunner.Process.cs` | 进程动作 | 298 |
| `MacroRunner.Clipboard.cs` | 剪贴板 | 236 |
| `MacroRunner.Reading.cs` | `ReadValue` / `ReadText` / `ReadModeOf` 等取值规则 | 230 |

分部是按**动作族**切的，不是按职责切的。所以共用原语落在"谁先需要谁那里"：`Give` 在 Window，`Pause` 在 Script，`Gate` 在 Detection，`CanLook`/`SeeOnce` 在 Input。按文件名找代码会找错三次（这是已知问题，见「债务」）。

### 5.2 步骤有两个形态

- `MacroStep`（`WhaleGenie/Models/`）：编辑态。带 `ActionDefinition`、显示名、子列表、行数据、`StepMeta`。
- `ExecutableStep`（`Core/Execution/`）：运行态。只有 `Type`、`Id`、`Meta`、`Parameters`（文本/抖动/子步骤/行）。
- 转换点唯一：`WhaleGenie/Execution/MacroStepConverter.ToExecutable()`。有损，且**每次运行都重建**。

`ExecutableStep` 不带语义快照，运行时反复用 `ActionSemanticsCatalog.Find(step.Type)` 查目录。

### 5.3 动作目录：字符串就是外键

`ActionSemantics.json`（170 条，编译进 Core）声明每个动作的类别、参数名与读法、默认值、可见性、会不会发输入/看屏幕/等待、子列表、表格列、返回值。它是**唯一声明**，但执行并不由它驱动：

- 分派是 13 个分部里 217 个 `case "input.click"` 这样的字面量 switch；
- `CanLook`（试运行"能不能看"）是一份手抄的 14 项清单；
- `SeeOnce`（试运行真的看一次）是另一份 switch。

三份真相之间没有编译期约束。目前 `CanLook` 与目录里 `looksAtScreen=true` 的 17 项已经不一致（少了三条），表现为试运行说"这不是看屏幕的动作"，而目录说它会。

### 5.4 控制流与作用域

循环与分支：`control.repeat` / `forEach` / `for` / `while` / `if` / `switch` / `try`，统一经 `RunBody` + `_loops` 计数；`break`/`continue` 越过 `LeaveLoop` 判定是否还在可去的层；循环变量由 `EnterLoop`/`LoopScope` 在退出时还原。

子宏调用：`RunOtherMacro` → `ResolveCall` 解析名字/步骤/签名/返回值 → `Variables.SwapLocal` 换局部表 → 递归深度上限 16 → `SeedStart`/`ReadArguments`/`CheckResults`。参数与返回值都是**行**（`name/value/given` 与 `key/name`），列名来自 `FieldNames`。

### 5.5 失败、重试、等待

- 失败有两条通道：异常 `StepFailure` 与信号 `Signal.Failed`。两条都要在 `RunAttempts` 里处理，目前**同一段重试判断写了两遍**。
- 重试预算按步（`Meta.TimeoutMs`），退避由 `StepMeta.RetryDelayFor` 决定；剩余预算不够起一次尝试时不再重试。
- 等待统一走 `WaitForFlagAsync`，`DelayScale` 缩放**等待**但不缩放**超时**（放弃的时间不因慢放而变长）。
- 失败策略 `StepErrorAction`（重试/跳过/停止/下一轮）由 `Decide`/`Ask` 翻成 `Decision`；无人值守时 `SilentRunHost.Ask` 直接给"停止"——"没人回答就失败"这条策略藏在默认实现里。

### 5.6 并发门

`InputGate` 与 `BrowserGate`：各一个静态 `SemaphoreSlim` + `Owner` + `Changed` 事件。发输入的动作与浏览器动作不能并发——两个宏同时点鼠标，落点是谁的说不清。

### 5.7 变量与表达式

- `VariableStore` 四层：`Local > Global > Live > System`，`SwapLocal` 支撑每次子宏调用换一张局部表。
- `VariableBag`：带锁字典 + `Changed` 事件；并发运行专门开了 `TryAddNumber` / `TryUpdate` / `SetIfAbsent`。
- 表达式：`Expression.Evaluate/TryEvaluate/Parse/Render/ReferencedNames/RenameVariable`；语法树 9 种节点；`ExpressionCompiler` 编译成委托；函数库 `ExpressionFunctions.cs`（1330 行）。
- 跳过某行时的诊断（"为什么这一格没算出来"）用 `AsyncLocal<Stack<…>>` 挂到当前步的日志上。

### 5.8 观测

`IRunHost`（`BeforeStep`/`Ask`）、`StepRun`/`StepAttempt`/`StepResult`（每步每次尝试的读数）、`RunLook`（试运行看到的东西）、`RunLogStore`（运行日志）。断点不在 Core：它只是 `IRunHost.BeforeStep` 的一个实现（`RunViewModel` 里），条件用 `MacroRunner.AskCondition` 求值。

## 六、设备层

### 6.1 十二个接口 + 一个聚合

`Core/Devices/IDeviceLayer.cs`（1707 行，单文件）装下了全部契约：

`IInputDevice`、`IScreenDevice`、`IVisionDevice`、`IOcrDevice`、`IDetectorDevice`、`IUiDevice`、`IFileDevice`、`IClipboardDevice`、`IProcessDevice`、`ISystemDevice`、`IBrowserDevice`、`IWindowDevice`，加聚合 `IDeviceLayer`（1 个方法 + 20 个属性）。同一个文件里还有数据形状（`PageTarget`、`PickResult`、`ProcessDetails`）与空对象 `NullDeviceLayer`。

按接口用是对的，但聚合接口把 20 个不相干的设备塞给了每一个消费者：注入点里有 21 处拿 `IDeviceLayer`，没有一处用到 5 个以上属性。

### 6.2 平台实现

`Core/Devices/Platform/`（48 文件 14558 行）是唯一的具体实现，按原生库分组：SharpHook（输入与全局钩子）、OpenCvSharp（找图找色与 YOLO）、Sdcb PaddleOCR（文字）、FlaUI（UIA）、Playwright（浏览器）、Roslyn + NuGet + Python + AHK（脚本）、libVIIPER（虚拟键鼠）、user32（PostMessage、窗口、剪贴板、托盘）。

几个大类的规模说明了接口宽度：`PlaywrightBrowserDevice` 2075 行 / 121 成员，`ScriptDevice` 826 行，`ViiperInputDevice` 722 行，`FlaUiDevice` 963 行。

### 6.3 组合根与生命周期

`WhaleGenie/Execution/SharedDevices.cs` 持有一个 `WindowsDeviceLayer` 单例，`App.axaml.cs` 在启动时预热（虚拟键鼠、OCR 模型）、在退出时 `Release()`。`WindowsDeviceLayer` 内部 15 个 `Lazy<>`，彼此有依赖链（输入要用屏幕、UIA 借输入、脚本借进程）。

`IDeviceLayer` 不继承 `IDisposable`：`WindowsDeviceLayer.Dispose` 靠运行时 `IsValueCreated && is IDisposable` 逐个判断，钩子永不释放。

有三个"藏在接口后面的全局静态"：`Hook` 恒为 `GlobalInputHook.Shared`（进程只能装一次）、`Capture` 是 `CaptureChoice.Current`（静态可写，`CaptureSetup` 直接改）、`Notify` 读 `WindowsTray.Shared`（托盘由界面创建）。无窗口运行时，`Notify` 会抛 `DeviceUnavailableException`。

### 6.4 替身

测试里有两套：`WhaleGenie.Tests/ChecksDevices.cs`（只真实现窗口，其余回落 `NullDeviceLayer`，输入/屏幕/剪贴板/UIA 可替换）与 `WhaleGenie.Core.Tests` 里的 `FakeDeviceLayer`（**1534 行，一个类实现 15 个接口**）。后者的规模就是"接口过宽"最直接的物证：宽接口的维护成本最终落在测试上。

浏览器、脚本、检测这几族在界面测试里基本"一律拒绝"，所以它们的用例只能起真件（`BrowserActionTests` 真的开一个不显示窗口的 Edge）。

## 七、界面层

### 7.1 三个状态，三处持有

| 状态 | 谁持有 | 例子 |
| --- | --- | --- |
| 项目文档态 | `MainViewModel`（439 行） | `Macros`、`VisibleMacros`、`Triggers`、`CurrentPath`、`IsDirty`、`Revision` |
| 运行期态 | `MainWindow` 的字段 | `_triggers`（触发器服务）、`_surfaces`（浮动界面）、`_recovery` |
| 编辑态 | `MacroEditorWindow` 的字段 + `MacroEditorViewModel` 的副本 | `_documents`（子宏页）、`_lifted`（抬起中的片段）、`_interfaceLayouts` |

`MainViewModel` 不是上帝对象——它只有 2 个命令，做的是"项目文档状态"。真正的上帝对象是 `MacroEditorViewModel`（3438 行）与 `MacroEditorWindow.axaml.cs`（3104 行），而且**两者共享同一份职责**：VM 造宏、窗口拼最终对象；VM 抛 `MacroLifted`、窗口接住塞进自己的文档表。

### 7.2 MVVM 的实际边界

数据绑定这一层成立：全部 `.axaml` 用 `x:DataType` 编译绑定，98 个 `[RelayCommand]` 源生成命令，`Strings` 用索引器绑定。

流程这一层不成立：XAML 里 118 个 `Click="OnXxx"`、82 处 `.Click +=`、128 处 `FindControl<>`；`Views/*.axaml.cs` 共 13244 行，占界面层的一半。窗口自己做的事包括：创建触发器服务（`MainWindow.OnOpened`）、开录制会话、写恢复快照、调 `MacroRunner.LookOnce` 做试运行并隐藏/恢复自身。

### 7.3 通信方式

三种并存，没有统一机制：

1. `event Action<…>` 回传（`CloseRequested`、`Saved`、`MacroLifted`、`Changed`）；
2. 构造器直接持有父 VM；
3. 静态单例广播（`SharedVariables.Current.Changed`、`VariableCatalog.GlobalsChanged`、`Strings.Current.LanguageChanged`）。

没有消息总线，通信靠上面三种。依赖由容器组装：`AppServices` 是唯一的组合根，设备层、项目、草稿箱、MCP 服务与三套工具都从那里解析。子窗口与触发器服务暂时仍由窗口创建——那是阶段 4 的活。

### 7.4 文案表

`WhaleGenie/Localization/Strings.cs`（4391 行）是**手写的两份字典**（英文 + 中文），`ActionStrings.cs`（4009 行）是动作的中文文案。没有生成器；`LocalizationTests` 检查键是否齐全、手册是否同步。切换语言时 `RefreshLanguage()` 抛 `PropertyChanged("")` 让所有绑定重读。

界面层近五分之一的体量在这里。这是有意的：文案要能逐条读、逐条改、逐条对账，生成器会把"这句话该怎么说"变成"这个键在哪"。

## 八、持久化

### 8.1 包格式

`.wgmacro` 是一个 zip：`manifest.json`（版本、全局变量声明、触发器）、`README.md`、`nodes/NN-slug.json`（每个宏一份）、`assets/`（图片）。读写全在 `WhaleGenie/Storage/MacroPackage.cs`。

写入是原子的：先写 `.tmp` 再 `File.Move(..., true)`。`FormatVersion` 是 20，`Load` 要求**严格相等**，没有迁移路径——因为开发阶段不保留兼容（见「有意的取舍」）。

### 8.2 三张"表外"的表

包之外还有三处状态，理解它们才能理解"为什么同一个值在两处"：

| 存什么 | 在哪 | 谁写 |
| --- | --- | --- |
| 设置（主题、OCR 预热、驱动自启、取画面方式） | 程序目录 `settings.json`（`LocalSettings`） | 设置窗口 |
| 全局变量的**值** | `%LocalAppData%\WhaleGenie\persistent-variables.json`（`PersistentVariables`） | 运行器写入 |
| 崩溃恢复快照 | 程序目录 `recovery.json`（`RecoveryStore`，3 秒去抖） | 两个窗口各写自己那半 |

全局变量的**声明**在包里，**值**在用户目录：同一个声明在不同项目里共享同一个值，这是设计（同名即同值）。

## 九、MCP

### 9.1 两种运行形态

同一个 `McpServerHost` 被两种启动方式共用：

- **有窗口**：`McpServerViewModel` 的开关打开服务，同时用浮层上的两个开关授权"直接控制"与"运行宏"。
- **无窗口**：`whalegenie --mcp`，`App.axaml.cs` 走 `McpHeadless`，只建 `MainViewModel` 与服务，不建 `MainWindow`。

能力并不等价：无窗口模式**没有**触发器、没有恢复快照、没有托盘，而且两个权限开关没人能打开——`McpControl.Allowed` / `AllowsRun` 唯一的写方是浮层。所以无窗口时 `whalegenie_mouse_*` 与 `whalegenie_run_macro` 永久被拒。这是当前的设计，不是遗漏，但值得在文档和提示里说清（见「债务」第 11 条）。

### 9.2 工具

`McpServerHost.Tools(...)` 反射扫描三个 owner（`McpTools`、`McpControlTools`、`McpRunTools`）的 `[McpServerTool]` 方法，逐个包进 `McpTolerantTool`。新增工具类必须手写进 `owners` 数组，忘了就静默不注册（体检的 `tools.list` 会发现）。

`McpTolerantTool` 在调用真实工具**之前**看 `arguments` 的形状：写成文本的 JSON、类型不对的值，回 `BadJson` 并点名是哪个参数。`inputSchema` 一个字节都没放宽。

### 9.3 草稿

`McpDraftStore` 持有若干 `McpDraft`：每份是项目的一个深拷贝 + 打开时的 `Revision`。工具只在草稿上改；`apply` 时才写回项目，且要求项目 revision 没变。一份草稿只能 apply 一次。

### 9.4 资源

`McpResources` 提供五个页面：动作目录、动作矩阵、单个动作、`whalegenie://project`、`whalegenie://macros/{name}`。

项目与宏这两页是为"跨几十轮写一条长宏"准备的：客户端读一次就留着，每张页带 `revision` 作为缓存键。宏页**不放每一步的参数**——放了就等于把整条宏搬上资源，那一步用 `get_step`。

没有 `resources/updated` 推送：传输是无会话的 HTTP（`Stateless = true`），服务端发不出通知，所以 `initialize` 里也没有声明 `subscribe`。失效由客户端比对 revision 自己判断。

### 9.5 体检

`McpSelfCheck`（1328 行）不是服务端的一部分，而是一个**外部驱动**的脚本：用 `McpClient` 连上去，按客户端会走的路走一遍（说明、工具表、循环、补丁、拒绝、资源、门、权限），把每条结果写进报告。`whalegenie --mcp-doctor --spawn` 就是跑它。

它的定位很重要：它测的是**协议面**，不是引擎。引擎由 `Core.Tests` 的替身用例测，真实桌面由手动那一档测（见 `doc/dev.md` 的测试分层表）。

### 9.6 审计

`McpAudit.Note()` 静态写 `logs/mcp-YYYY-MM-DD.log`，只有改变状态或动了机器的调用才记；打字只记字数。调用点是各工具手写的，没有统一拦截——所以"加一个新工具要不要记审计"靠自觉。

## 十、触发器

`WhaleGenie/Execution/MacroTriggerService.cs`（1808 行）一个类管 8 种触发：热键、颜色、热字串、计划、文件、进程、窗口、空闲。`Interface` 与 `Menu` 两种不在这里，由浮动界面与菜单直接跑。

输入是全局键鼠钩子（`IDeviceLayer.Hook`）、OS 窗口事件，加三个定时器（轮询 60ms、进程 30ms、手柄 30ms）。它**不依赖任何 ViewModel**，只有 `Func<…>` 与事件，是四个外围里唯一单向依赖的。但它自己有一个竞态点：每次触发都按名字线性查找宏，快照失败时 `catch` 后返回空。

## 十一、录制

`Core/Recording/`（5 文件 824 行）：`IInputRecorder` → `RecordedInput` → `RecordingTranslator`（去抖、合并、识别双击与拖拽）→ `RecordedAction`（16 种）。阈值集中在 `RecordingOptions`（14 个），由编辑器的"录制选项"供给。

最后一步（`RecordedAction` → `MacroStep`）在应用层的 `RecordingStepBuilder`，因为 `MacroStep` 属于应用层。

## 十二、横切

### 12.1 线程

- **界面线程**：所有 ViewModel、`MainViewModel` 的项目状态、`IDeviceLayer` 的构造。MCP 的每一次读写都要 `Dispatcher.UIThread.InvokeAsync` 进来。
- **钩子线程**：全局键鼠回调、触发器判定。只做判断与派发，不碰界面。
- **线程池**：一次运行（`Task.Run`），MCP 的 Kestrel 请求，OCR 预热。
- **互斥**：`InputGate`/`BrowserGate`（同一台机器不能被两个宏同时操作）、`McpControl`（一次只跑一条宏）。

退出有一条硬规则（写在 `App.axaml.cs` 的注释里）：**不许在界面线程上同步等 MCP 停止**——MCP 请求可能正等着界面线程，两边会永久互等，留下一个无窗口进程。

### 12.2 失败与文案

失败码分两套，各自有词表：

- 运行失败：`Core/Execution/RunVocabulary.cs`，`RunKeys` 319 个常量，`LocalizationTests` 与 `FailureKeyTests` 对照手册的表。
- MCP 拒绝：`Mcp/McpErrors.cs`，稳定代号 + 一句人话 + 一句下一步建议。

两套不能混用：`Run.*` 是给宏作者分支用的，MCP 的码是给客户端分支用的。

### 12.3 日志

- 运行日志：`RunLogStore`（内存 + 落盘），界面的步骤树、MCP 的 `get_run_log` 都读它。
- 失败日志：`FailureLogFolder`，`LeaveFailureLog` 在失败时写一份快照。
- MCP 审计：`logs/mcp-<日期>.log`，一行一次调用。

### 12.4 测试分层

| 层 | 在哪 | 测什么 | 测不到什么 |
| --- | --- | --- | --- |
| 纯逻辑 | `Core.Tests` | 每条动作在替身设备层上"要求了什么"、返回形状、副作用声明 | 真的送到了系统 |
| 动作矩阵 | `Core.Tests/Bench` | 170 条动作逐个按声明合成一步跑通 | 同上 |
| 界面 | `WhaleGenie.Tests` | 参数成型、页面归属、绑定、文案完整性、无头会话里的交互 | 真窗口与真输入 |
| 协议面 | `McpSelfCheck`（经 `--mcp-doctor`） | 客户端视角的完整一轮 | 引擎细节 |
| 真实机器 | 手动 | 真输入、抓屏取色、UIA、录制、驱动级输入 | 不可回归 |

`LayeringTests` 与 `ActionContractTests` 是跨切面的守卫：前者守依赖方向，后者守"目录、引擎、手册"三者一致。

## 十三、有意的取舍

这些看起来像问题，但是选过的，改之前先读原因：

1. **包格式不向后兼容。** `FormatVersion` 严格等于 20。开发阶段没有真实用户数据，保留双读会让每个字段都长出两条读法。
2. **文案是两份手写字典，不是资源文件 + 生成器。** 要能逐条读、逐条改，并由测试对账。
3. **无窗口 MCP 没有权限开关。** 没人能授权，所以键鼠与运行工具在那一路里恒被拒绝。这是保守的默认，不是漏洞。
4. **`WhaleGenie.Core` 里放着几个只服务界面的东西**：`StepMeta.ToJson`（文件格式）、`ActionParameterSemantics` 里的编辑器事实、`MacroRunner.LookOnce`（对话框的"试一下"）。挪走的收益不抵一次大改。
5. **MCP 传输是无状态的 HTTP**，因此没有资源订阅推送、没有会话级状态。

**原先写错的**：依赖注入曾经被列在这里，写成"有意的取舍"。那是错的——.NET 项目的依赖由容器组装是常规做法，手工 `new` 省下的只是配置。阶段 2 已经把它落地：`AppServices` 是唯一的组合根，`App.axaml.cs` 与无窗口入口共用同一份注册。

## 十四、债务与建议顺序

按"会不会出事"排，不按"改起来爽不爽"排。怎么做、按什么顺序做，见 [`refactor-plan.md`](refactor-plan.md)：阶段 1 对应下面 P0，阶段 3 对应 P1，阶段 4 对应 P2；阶段 2（依赖注入）不在这张表里，因为它是结构改动，要单独占一轮全量验证。

### P0：会出错，或会丢数据

1. **保存没有 `.bak`，只有 MCP 通道有。** `McpLiveProject.SaveAsync` 会先 `File.Copy(path, path + ".bak")`，而用户按 Ctrl+S 走的 `MainViewModel.SavePackage` **不做**。同一个动作，两条路径两种安全性。修法：把备份下沉到 `MacroPackage.Save`，一处生效。
2. **校验的阻断/提示语义在两个消费者那里是相反的。** `MacroCheck.Inspect` 产出的 45 种问题只有代码没有 severity；编辑器把结果当"行上提示"（不阻止保存），`McpDraftStore.Validate` 把它们当 `blocking`（阻止 apply 整个草稿）。同一份数据两种判断，迟早出现"界面能存、AI 存不了"或反过来。修法：给 `MacroProblem` 加 severity，两处各自声明用哪一档。
3. **`CanLook`/`SeeOnce` 与动作目录已经不一致**（目录里 17 条会看屏幕，清单里只有 14 条）。表现为"试一下"按钮对某些动作说"这不是看屏幕的动作"。修法：从 `ActionSemanticsCatalog` 生成这两份清单，删掉手抄。

### P1：改的时候容易改错

4. **重试逻辑写了两遍**（`RunAttempts` 的 signal 分支与 catch 分支）。改重试语义必须同时改两处，漏一处就是"异常重试、信号不重试"这种半截行为。修法：先把两条合成一个 `Outcome`，再判。
5. **三个地方手工装配运行器**（`RunViewModel`、`MacroTriggerService`、`McpRunTools`），几乎逐行相同，只差 host 与初始参数。修法：一个 `RunFactory`。
6. **`MacroCheck.Inspect` 是单方法 1083 行**，内含几十个局部函数，45 个裸字符串 code 没有集中词表（对比 `RunKeys` 有 319 个常量）。修法：按检查族拆成小类，code 提成常量。
7. **动作分派靠 217 个 `case` 字面量**，与目录没有编译期关联。加一个动作要改三处（目录、分派、测试）。这是这套设计里最贵的一处，但改动面极大——建议先给"目录声明的动作必须被某个 case 覆盖"加一条测试，而不是马上重写分派。
8. **共用原语散落在按族切分的分部里**（`Give` 在 Window、`Pause` 在 Script、`Gate` 在 Detection）。修法：提一个 `MacroRunner.Support.cs`，或者至少在同族文件顶部写"这个文件里还有别的族在用"。
9. **两个同名 `Execution` 目录**（`WhaleGenie.Core/Execution` 与 `WhaleGenie/Execution`），命名空间都叫 `…Execution`。看栈追踪时分不清是引擎还是应用层。修法：应用层那个改名 `WhaleGenie.App`（或 `Application`），一次性机械改。

### P2：结构性的，成本高、回报慢

10. **设备接口过宽。** `IDeviceLayer` 20 个属性、`NullDeviceLayer.Refusal` 实现 17 个接口、`FakeDeviceLayer` 1534 行、`PlaywrightBrowserDevice` 2075 行——同一根因的四个投影。**已经开始按消费者收窄**：`WindowRuleWindow` 只接窗口与钩子，`ColorPickerWindow` 只接屏幕、输入与钩子。注意收窄不会让现有测试变简单（`ChecksDevices` 本来就是整个层），它买到的是"构造函数说出这个窗口要什么"。继续时让每个构造函数只接它真正用到的窄接口，聚合接口留给组合根。
11. **视图做业务。** `MainWindow`（2118 行）创建触发器服务、管恢复、管托盘；`MacroEditorWindow`（3104 行）管子宏文档、录制、键位捕获；`ActionEditorView`（2109 行）直接调 `MacroRunner.LookOnce` 做试运行。这是最大的理解成本来源，也是最难改的一处——建议顺手改（碰到哪个窗口就把哪块搬出去），不要专门开一轮。
12. **项目状态被劈成两半。** `Macros`/`Triggers` 在 `MainViewModel` 实例上，全局变量在静态 `VariableCatalog.Globals` 上。MCP 的 `McpLiveProject.Snapshot()` 因此一半从实例读、一半从静态读，无窗口与有窗口靠进程内静态隐式共享。**已经收口的是装载**：打开、导入、恢复三个入口合成 `LoadSharedVariables(globals, replace)`，并有"换一个项目后旧变量不残留"的用例。**没做的是注入**：改成注入要动 94 处访问和 `VariableCatalog` 的静态签名（`MacroCheck` 这样的静态类也在用），而这个程序一次只开一个项目——静态就是"当前项目的那一份"。要支持多项目或并行测试隔离时再谈。
13. **无窗口 MCP 的权限缺口**（第 9.1 节）。要么在 `--mcp` 上给一个显式参数（`--allow-control` / `--allow-run`），要么在服务说明与手册里把"这一路只能读"写死。现在是"技术上不可用、文档里没说"。

### 一句话的取舍

如果只做三件：**1（备份）、2（校验语义）、3（动作清单与目录对齐）**。这三件都是"现在就可能出错"的，而且都不需要动结构。P2 那些看着更壮观，但收益要几年才兑现。

## 附：文件规模地图

定位代码时先看这张表（数字是 2026-10 的）：

| 位置 | 文件 / 行数 | 一句话 |
| --- | ---: | --- |
| `WhaleGenie/Localization/` | 9 / 9061 | 两份手写文案表 + 语言刷新 |
| `WhaleGenie/Views/` | 78 / 27412 | 33 个窗口、4 个 UserControl，后置代码占一半 |
| `WhaleGenie/ViewModels/` | 46 / 18433 | 五个大 VM 占 61%，其余是行/选项载体 |
| `WhaleGenie/Models/` | 31 / 8377 | 全域共享的领域层（宏、触发器、校验） |
| `WhaleGenie/Mcp/` | 23 / 7596 | 工具、草稿、资源、体检、审计 |
| `WhaleGenie/Execution/` | 35 / 4592 | 触发器、录制翻译、运行宿主、变量桥接、包与恢复 |
| `WhaleGenie/Storage/` | 6 / 1698 | 包格式、图片资产、恢复快照 |
| `WhaleGenie.Core/Execution/` | 27 / 14000 | 运行器与 13 个分部、动作目录、键表 |
| `WhaleGenie.Core/Devices/` | 25 / 5311 | 契约 + 空对象；`Platform/` 另有 48 文件 14558 行 |
| `WhaleGenie.Core/Expressions/` | 8 / 3068 | 语法树、求值、函数库 |
| `WhaleGenie.GraphicsCapture/` | 14 / 2292 | D3D11/DXGI 取画面 |
