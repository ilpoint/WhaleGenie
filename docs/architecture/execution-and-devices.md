# 执行、设备与画面获取

所属：[WhaleGenie 架构](../architecture.md)。

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

识别动作区分“否定答案”和“无法完成”：没找到、颜色不符是正常结果，由后续条件读取；抓屏失败、
区域越界或参数无法求值才走 `Run.*` 与失败策略。找色和多点找色用 `missingIsFailure` 让调用方选择
未命中属于哪一种，等待与必须命中的动作则直接失败。具体字段和失败键仍以动作目录与手册为准。

找色族保留两项有意的表达：每个辅助点可以覆盖基准容差，一处也可以声明多个备选色或反色，避免用过大的
全局容差换取命中。命中排序只提供四向，因为颜色命中是单像素，面积与分数没有对应含义。取色和偏移
拾取读取打开时的定格画面，不追随实时屏幕，保证所见内容与最终写入一致。

### 5.3 控制流与变量

控制流使用块结构，循环变量在进入和离开循环时保存与恢复。子宏调用通过 `IMacroLibrary` 解析，
并切换局部变量表；形参、返回值和递归限制在调用边界检查。

变量读取按局部、全局、运行提供值和系统值解析。`VariableBag` 对共享写入提供锁和变更通知，
累计与列表修改提供原子操作。表达式系统负责解析、求值、引用分析和变量改名。写变量名的选择器只列出
当前动作实际可写的作用域，读值选择器可以列出全部可读作用域。

值字段只有三种读法：`VariableName` 读取裸变量名，`WholeValue` 对整格求值，`Text` 进行文本插值。
文本中的 `$名字` 读取变量，`$$` 表示字面 `$`，`${名字}` 消除名称边界，其他 `${...}` 按表达式求值。

三种读法在进入引擎时先读成同一个形状（`Formula`：一段文本、一个名字、一条公式，或它们首尾相接的拼接），
运行、检查、引用分析和改名都在这棵树上进行，引擎本身不再解析文本。文本变成形状只发生在 `FormulaText`
一处——`$名字`、`$$`、`${名字}`、`${...}` 的规则写在那里，别处不再各写一份。`FormulaText.Write` 把形状写回
一行，只用于日志、失败提示和仍在讲文本的界面，没有任何东西从它读回来；`Formula.Speak` 是给人读的另一种
写法，把名字讲成「名字」。文件按形状保存，`FormulaJson` 是它的结构化写法。

扫描器也只有一份：`ExpressionTokens` 把一行切成带位置的 token，严格模式供解析器使用，宽松模式供结构带
建块。两个读者对同一个字符是不是名字、数字、文字或符号的判断因此不会再分叉。

运行态因此不再携带文本：`ExecutableParameter.Written` 就是那一格的值本身，`ExecutableStep.Text(名字)`
只是它的写法。`MacroStepConverter` 是唯一应用读法的地方——它按动作目录把文件里的一行读成形状；
引擎再要判断的只有意图（这是个名字、一个值，还是一段要说给人听的文本），不再判断语法。

文件也按形状保存：一段文字仍是它自己，一个名字写成 `{"name":"count"}`，一条公式按运算与调用拆开，
行里的每一格同样。于是文件里不再有哪个字符需要转义，读它的人不必先学会一门语言才知道哪里是引用。
以文字写成的旧值仍能读入——那是 `Formula.FromLine` 的用处——所以手工改过的宏不会被格式挡住。
读不出的值不会让整份文件打不开：`FormulaText.Read` 交出一个 `FaultNode`，它记着原文与原因，
等那一步真正运行时才失败。
位置使用带 `x`、`y` 的记录，矩形使用带 `left`、`top`、`width`、`height` 的记录；记录字段统一通过
`field(记录, "名字")` 读取，不增加第二套访问语法。字段形状、单位和空值含义仍由动作目录声明。

宏可以声明自己保留的局部变量（`MacroItem.Locals`，见 `MacroSignature.Locals`）。它们在第一步之前按声明顺序
写进本次运行的局部表，用 `Set` 覆盖而不是 `SetIfAbsent` 兜底：初始值定义的是每次运行的起点，不是缺值时的替代。
被调用的宏在 `SwapLocal` 换表之后才预置自己的声明，所以初始值里的 `$名字` 读的是它自己那一层，而不是调用方的
同名局部变量。

### 5.4 并发门与观测

`InputGate` 和 `BrowserGate` 分别串行化输入与浏览器操作。两道门都只由持有门的容器块
（`control.holdInput` / `control.holdBrowser`）取，普通步骤不再按动作名自己排队；块内步骤不重复获取同一门，
宏里调用的子宏沿用这一次运行的持有状态。输入会发到哪一步仍由目录声明并沿调用图计算，供界面提示与运行观测使用。

按键与鼠标的节奏另有一条线：`InputLimit` 对每个运行给出自己的速率与时间线（`AsyncLocal` 作用域），
`MacroRunner.RunAsync` 进出该作用域，因此两个同时运行、限速不同的宏互不拖慢。`RunRequest.InputRate`
是宏自己写的上限；为 `null` 时该运行沿用程序设置。

`StepRun`、`StepAttempt` 和 `StepResult` 记录步骤与尝试结果；`RunLogStore` 向运行界面和 MCP
提供日志。运行失败使用 `Run.*`，日志事件使用 `Log.*`，MCP 拒绝码使用独立词表。
`LogRetention` 在启动时按设置份数清理 `logs/` 下一次运行或一天一个文件的失败与 MCP 日志，
`runtime.log` 与其他文件不在其中。

## 6. 设备层与画面获取

### 6.1 设备契约与实现

`Core/Devices/` 下每个设备族一个契约文件（`IInputDevice`、`IScreenDevice`、`IUiDevice`、
`IFileDevice`、`IProcessDevice`、`ISystemDevice`、`IBrowserDevice`、`IWindowDevice` 等），
`IDeviceLayer` 把它们聚合为引擎取得的唯一契约。消费者可以只依赖所需接口；`MacroRunner` 和
综合编辑器使用聚合层。

`Core/Devices/Platform/` 是 Windows 实现所在位置，第三方库的类型和异常不得泄漏到界面。
`NullDeviceLayer` 提供无设备默认实现，测试替身按设备分别组装，并共享一份有序调用记录以验证
跨设备顺序。

窗口动作、屏幕识别、UIA、后台输入和窗口触发器共用 `window` 记录：`handle`、`title`、`process`、
`class`、`how`、`caseSensitive`、`trim`、`multiline`。各调用点不再拆出另一套文本匹配字段。

CSV 统一由 `CsvHelper` 解析。文本读取优先采用 BOM；无 BOM 时先验证 UTF-8，再回退到本机代码页，
避免各动作分别猜测编码。

### 6.2 画面获取

`ScreenGrabber` 负责选路、回退和会话缓存，`OneThread` 串行执行需要专用线程的图形调用。
输出 `ScreenGrab` 使用紧密排列的 BGRA 像素，并记录实际采用的 `CaptureWay`；源矩形裁剪、
越界处理和多显示器拼接由 `Placement`、`ScreenPlacement` 等类型负责，坐标不保存在 `ScreenGrab` 中。

自动选路顺序为：

- 窗口：Graphics Capture → PrintWindow → GDI；无 Graphics Capture 会话时为 PrintWindow → GDI。
- 桌面：桌面复制 → GDI。

多显示器画面按虚拟桌面坐标拼接，源图未覆盖的区域保持黑色。窗口与显示器会话需要明确释放；
共享会话由 `SharedDevices` 持有到退出。

一次运行多久取一次画面、取到的画面能不能再读一次，同样属于"这台机器"而不是某一步：
`FramePolicy`（`Core/Devices/DeviceTypes.cs`）与 `CaptureChoice` 并列，由界面在启动和每次改动时
写入，引擎通过 `IDeviceLayer.Frames` 读取。`MacroRunner` 因此只保留一个运行级的
`_lastFrame` 时间戳与一份按"来源 + 抓法"键控的帧缓存，不按步骤记节拍；`IScreenDevice.RefreshHertz`
让设置页能显示显示器自己的赫兹数并把它当作取帧上限。
