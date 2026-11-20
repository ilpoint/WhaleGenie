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
参数值形态、控件形状、单位、空值、范围、选项、拾取器、子列表、副作用、等待和返回值。

`Models/ActionDisplay.json` 只保存形状决定不了的展示事实：编辑器种类（默认由值形态与拾取器推导，
覆盖项只写例外）、选项文案、启用条件、路径意图、页面归属与预览；字段以语义目录里的参数名为键，
所以名称只出现一次。动作文字按同一套键放在 `ActionEnglish`（英文）与 `ActionStrings`（中文）里，
`ActionCatalog` 在读取时把它们合起来投影给编辑器；`ActionContractTests` 检查覆盖项不指向不存在的
动作或参数，`LocalizationTests` 检查每个字段与结果都有英文。显示层另有一个 `WrittenByTool`：这类值
由专门的窗口写入（基准色周围要对的颜色），表单不给它生成行，值本身照常保存与校验。

动作族已经从 `MacroRunner` 的 `partial` 文件迁成独立的类。`ClipboardActions`、`ProcessActions`、
`SystemActions`、`UiaActions`、`FileActions` 与 `DataActions`、`WindowActions`、`KeyboardActions`、
`GamepadActions`、`PointerActions`、`BrowserActions`、`OcrActions`、`DetectionActions`、
`CaptureActions`、`ColourActions`、`ImageActions` 与脚本三件（`ScriptActions`、`ScriptLanguages`、
`ScriptFiles`）都在族自己的文件里：它们共用 `RunActionHandler`，处理器表是静态的，每个处理器只接收
`IRunContext`（读字段、交付结果、写日志、取得设备与运行观测），因此这些族可以脱离运行器直接测试。
带块的动作（`browser.dialog` 这类）通过 `IRunContext.RunBlock` 运行它包住的步骤，门与循环仍由运行器
掌管；读屏的动作通过 `IRunContext.Reads`（`IScreenReads`）取用区域、读法与观察，条件判断与试看走同一份；
取一张图、读一个像素通过 `IRunContext.Shots`（`IScreenShots`）取用，截图与像素读法因此只有一处；
脚本通过 `IRunContext.RunStepById` 按标识运行一步，脚本里停止或返回的意愿由 `BeginScript` /
`EndScript` 一并交回宏本身。
`UiaActions` 连选择器解析、锚点与收窄条件一起带走，一个元素因此在族内只有一种搜索方式；文件族按读写
格式分成 `FileActions`（文本、单个文件、文件夹、列表与变量快照）、`FileJsonActions` 与
`FileCsvActions`，各自带走自己的形状规则。按键分成“按完就结束”的键盘族与按住不放的四个步骤：
后者属于运行状态，与运行结束时统一放松的代码放在一起，仍留在 `MacroRunner`。
运行器现在只装解释器本身：步骤循环、尝试与失败规则、门与循环、调用栈、条件与变量，
以及公共读值（`MacroRunner.Reading.cs`）和试看试问（`MacroRunner.Trials.cs`）。目录声明语义，
但不替代动作分派；新增动作仍需在具体处理器中实现。

被所有族共用的六层已经各自成为独立的类，而不是继续挂在运行器上：`StepResults` 交付结果
（`Give`、`Store`、`GiveMatch`、`GiveNothing`、`GiveEvery` 与点记录），`WindowFacts` 认窗口
（规则解析、`Lookup`、`Locate`、锚点与后台投递的目标），`ScreenPoints` 认点（屏幕/窗口坐标、
记录自带的基准、拖拽两端、漂移与输入路由），`ScreenSight` 看屏（搜索区域、按机器保管的回合与画面
取图、试看标记），`ScreenReads` 读屏（区域读数、识别修正、命中计数），`ReferencePictures` 认参考图
（变量里的图、磁盘上的图与按写入时间做的缓存）。运行器持有这六个对象并把接口转发给它们；
动作族通过 `IRunPoints`、`IScreenReads` 与 `IScreenShots` 取用，不必知道这些规则住在哪。

同一份读法被多个族用到时抽成独立类型而不是继续留在运行器里：`RunLimits` 是搜索与表格的上限，
`TableColumns` 给文件、浏览器与元素三处的表格列起名，`HitRecord` 是命中记录的字段表，
`Templates` 填 `{{name}}`，`TextRule` 读文本比较的方式与开关，`HitOrder` 把一步写的“怎么数”
读成一份排序（哪一档、离哪个点、分数占多重），`HitSort` 用同一套比较器把一列命中排好并让同分同距
落到阅读顺序，`SearchHits` 只从排好的那一列里取第几个。图像、颜色、检测与文字四类搜索因此不会
各数各的。

等待集中在 `Pacing`：暂停、按间隔重问一个条件、等一个会出现的值。动作族直接用它，不必经由运行器。

动作窗口的「运行」走的是 `StepTrial.TryAsync`：它拿一个 `MacroRunner` 把这一步跑一遍，与整次运行
同一套代码——该点的点、该按的按，没有“只看不做”的分支，这一步自己的超时也照运行的算法管着它。
只查看一次是另外两条路：拾取器瞄准时的一次识别，与识别结果窗口的「再看一次」。它们由 `CanLook`
（读目录的 `LooksAtScreen`）判断能不能看，由 `LookOnce` 经 `SeeOnce` 的预览表完成。修改看屏幕的
动作时，必须同步检查运行这条路与预览表这条路。

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

命中排序分成两件事，是这一节的重点：

- **扫描方向属于搜索**（大漠 `FindPic` 的 `dir` 就是这个位置）。四向与“离某个点最近”由设备按
  `VisionQuery.Walk` / `VisionQuery.Near` 自己走查：模板匹配本来就有整张分数图，按指定方向扫一遍、
  命中即抹掉它覆盖的那一块，一次 O(n) 就能给出“该方向第 k 个”。`Limit` 因此是“按这个走查要几个”，
  不是“最像的几个”。若改成“先按分数取 k 个再排序”，默认档会退化成“最像的那个”，而把 k 放大到
  `RunLimits.Matches` 又会让默认档从一次 `minMaxLoc` 变成两百次。
- **事后排序只做合并**（对应大漠的 `SortPosDistance`）。多块区域、多张参考图各自按同一走查取 k 个，
  合并后用 `HitSort` 再排一遍；综合评分、距离与四向都在这里，同分同距一律按阅读顺序兜底。
  综合评分把分数与面积各按本次命中的最大值归一化后加权，权重由 `scoreWeight` 说；找图的每个命中框
  都是参考图本身的宽高，面积项恒定，所以它等价于按分数排，也就不给权重格。
- 检测与文字把整份候选交给引擎（检测的 `DetectionQuery.Limit` 一直是 `RunLimits.Matches`，文字
  则是模型一次读出的全部），所以它们的排序全部在引擎里做；颜色族本轮仍只有四向，文字族只按识别
  顺序数，两者的分数与面积留到下一轮再并进综合评分。

### 5.3 控制流与变量

控制流使用块结构，循环变量是**借用名字**：进入循环时记下这个名字原来的值和声明，离开时放回去。于是 `item`、计数器、`sys.loopIndex` 只在循环体里指得对——被它遮蔽的声明在循环结束
后回到循环前的值，它自己起的名留在表里，所以“循环结束后还能拿到最后一个 item”仍然成立。子宏调用通过
`IMacroLibrary` 解析，并把局部变量表压栈；形参、返回值和递归限制在调用边界检查，返回时弹栈，读取路径
一次只抓一帧，不会把调用者和被调者的值拼在一起。

一个作用域是**一张按名字索引的表**：名字就是键，值按名字存，声明类型是这个键的元数据（`VariableBag`）。
没有第二套身份。两个声明不能共用一个名字，这在运行前的 `MacroSignature.Problems` 里拒绝
（`Run.BadDeclaration`）；两个步骤输出写同一个名字是普通覆盖。改名就是把声明和每一条引用都写成新名字，
没有需要同步的绑定表。声明类型是写入契约：
`Number`/`Bool`/`List`/`Record`/`Date`/`Image` 只收同类值（能读成该类型的文本也算），`Text` 收任何值，
空值始终放行；不符的写入在发生的那一步失败（`Run.VariableTypeMismatch`）。运行期自己写出的名字有上限：
五万条提醒一次，二十万条以 `Run.VariableTableFull` 失败。表达式系统负责解析、求值、引用分析和变量改名。写变量名的选择器只列出
当前动作实际可写的作用域，读值选择器可以列出全部可读作用域。

值字段在动作目录里只声明一件事：它接受什么形状。`ValueForm` 有四种——`Text` 纯文字不许有名字、
`Run` 文字里夹着名字、`Value` 一整个值（字面量、名字、调用或运算）、`Name` 一个裸变量名。引擎、
设计期检查、取值窗口和页面都按这一份声明走，不另写一套；字段读一行时怎么切，也由它决定。

编辑面上的格子是只读的：值从「取值」窗口以 `ExpressionNode` 交回，框里显示的是 `Formula.Speak`
把形状讲给人听的一行。名字说成「名字」，文字与名字各是一片，没有要敲的记号、没有要转义的字符。
引擎的行表直接对形状求值，不把值渲染成一行再解析回来。`FormulaJson` 是文件里的结构化写法。

运行态因此不再携带文本：`ExecutableParameter.Written` 就是那一格的值本身，`ExecutableStep.Text(名字)`
只是它的写法，字段在读法上要做什么由动作目录声明。`MacroStepConverter` 不再读任何东西：编辑态保存的形状
原样交给运行态；引擎再要判断的只有意图（这是个名字、一个值，还是一段要说给人听的文本），不再判断语法。

文件也按形状保存：一段文字仍是它自己，一个名字写成 `{"name":"count"}`，一条公式按运算与调用拆开，
行里的每一格同样。名字就是键，文件里没有第二套身份要维护，读它的人看到的就是运行时用的那个名字。
于是文件里不再有哪个字符需要转义，读它的人不必先学会一门语言才知道哪里是引用。
位置使用带 `x`、`y` 的记录，矩形使用带 `left`、`top`、`width`、`height` 的记录；记录字段统一通过
`field(记录, "名字")` 读取，不增加第二套访问语法。字段形状、单位和空值含义仍由动作目录声明。

宏可以声明自己保留的局部变量（`MacroItem.Locals`，见 `MacroSignature.Locals`）。它们在第一步之前按声明顺序
写进本次运行的局部表，用 `Set` 覆盖而不是 `SetIfAbsent` 兜底：初始值定义的是每次运行的起点，不是缺值时的替代。
被调用的宏在 `VariableStore.PushLocal` 换表之后才预置自己的声明，所以初始值里的名字读的是它自己那一层，而不是调用方的同名局部变量。

### 5.4 并发门与观测

`SharedDoors.Input` 与 `SharedDoors.Browser` 是同一个 `SharedDoor` 的两个实例，分别串行化输入与
浏览器操作。两道门都只由持有门的容器块（`control.holdInput` / `control.holdBrowser`）取，普通步骤不再
按动作名自己排队；块内步骤不重复获取同一门，宏里调用的子宏沿用这一次运行的持有状态。输入会发到哪一步
仍由目录声明并沿调用图计算，供界面提示与运行观测使用。

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

`src/WhaleGenie.Platform.Windows/` 是 Windows 实现所在位置，第三方库的类型和异常不得泄漏到界面；引擎只通过 `src/WhaleGenie.Core/Devices/` 的契约看到它们。
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

要读什么、有哪几种抓法、桌面由哪些显示器拼成，这些形状属于引擎：`CaptureWay`、`ScreenLayout`、
`ScreenDisplay`、`ScreenRect` 与 `ScreenCaptureRequest` 都在 `WhaleGenie.Core.Devices`。本工程只实现
它们——显示器枚举在 `Displays`，四种抓法各自的像素来源在自己的文件里——因此引擎与界面读同一套矩形
与选路词汇，而 DXGI、Direct3D 与 GDI 的调用不外泄。

自动选路顺序为：

- 窗口：Graphics Capture → PrintWindow → GDI；无 Graphics Capture 会话时为 PrintWindow → GDI。
- 桌面：桌面复制 → GDI。

多显示器画面按虚拟桌面坐标拼接，源图未覆盖的区域保持黑色。窗口与显示器会话需要明确释放；
共享会话由 `SharedDevices` 持有到退出。

多久看一次屏幕、看到的画面能不能再读一次，同样属于“这台机器”而不是某一步或某一次运行：
`FrameShelf`（`Core/Devices/Frames.cs`）与 `CaptureChoice` 并列，由界面在启动和每次改动时写入，
引擎通过 `IDeviceLayer.Frames` 读取。它同时保管下一次可以看屏的时刻、以及每个来源最近一张画面，
所以这两样都是全程序共用的：同时运行的宏轮流占取帧回合，后来的宏读前一个宏刚取到的那张，
而不是各自按上限取一张。`ScreenSight` 只在取画面时用这份保管，节拍与画面都不进 `RunState`；
`IScreenDevice.RefreshHertz` 让设置页能显示显示器自己的赫兹数并把它当作每秒捕获的上限。
