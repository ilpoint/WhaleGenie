# 自动化测试

所属：[WhaleGenie 开发说明](../dev.md)。

## 测试分层与契约

| 层 | 位置 | 验证范围 |
| --- | --- | --- |
| 引擎逻辑 | `WhaleGenie.Core.Tests` | 动作行为、变量、表达式、设备请求、返回形状和输入路由 |
| 动作矩阵 | `WhaleGenie.Core.Tests/Bench` | 按目录合成动作并核对结局、返回值、副作用声明和设备记录 |
| 画面获取 | `WhaleGenie.GraphicsCapture.Tests` | 选路、缓存、复用、矩形越界与图片落位 |
| 文档与运行期服务 | `WhaleGenie.Domain.Tests` | 包往返、设计期检查、变量、触发器、运行日志与单实例；不开窗口 |
| 协议与工具 | `WhaleGenie.Mcp.Tests` | 工具清单、拒绝码、审计、命令行与容器生命周期；不经窗口 |
| 编辑器 | `WhaleGenie.Editor.Tests` | 宏编辑窗、步骤树、变量墙、窗口规则与菜单的无头交互 |
| 动作表单 | `WhaleGenie.Actions.Tests` | 动作对话框、页面归属、返回值与拾取器 |
| 运行与其余界面 | `WhaleGenie.Tests` | 运行与调试、MCP、设置、界面文案与文档对账 |
| 外部程序 | `BrowserTabTests`、`ScriptBridgeTests`、`ScriptHostRunTests` | 无头 Edge、真实脚本宿主和命名管道桥 |
| 真实桌面 | 手动验收 | 系统输入、真实窗口、抓屏取色、UIA、录制和驱动级输入 |
| 界面观感 | agent 自省通道（Debug）连运行中的程序读树、点、截图
契约测试应遍历目录或扫描源码，避免手写第二份动作清单。新增目录事实时先增加可失败的断言，
再修改实现。主要对账关系如下：

| 契约 | 主要用例 |
| --- | --- |
| 字段值形态、控件形状、单位、空值、范围、位置与矩形 | `ExpressionFieldTests`、`ActionContractTests`、`ActionPageTests` |
| 参数约束、行列表、类型检查和设计期诊断 | `MacroRunnerTests`、`MacroCheckTests`、`ActionContractTests` |
| 宏声明、直接运行、调用参数和结果交付 | `MacroSignatureTests`、`MacroCallTests`、`RunDebugTests` |
| 输入副作用、互斥门和宏调用图 | `ActionContractTests`、`DeviceActionTests`、`MacroRunnerTests` |
| 输入限速（程序与每宏）、启用/停用键 | `InputLimitTests`、`LocalSettingsTests`、`RuntimeHookSafetyTests`、`MacroEditorChromeTests` |
| 返回页、引擎返回键和记录字段 | `ReturnsPageTests`、`ActionContractTests`、`DeviceActionTests` |
| 失败键、函数表和中英文文案 | `FailureKeyTests`、`FunctionTableTests`、`LocalizationTests` |
| 字段命名与步骤落点 | `FieldNameTests`、`StepPlacementTests`、`MacroEditorWorkspaceTests` |
| 依赖方向与主题资源 | `LayeringTests`、`ControlsRegistryTests`、`StyleVocabularyTests`、`TemplatePartTests` |
| 中键菜单宏回归、按住/移动/松开手势、方向命中、第三圈几何、场景覆盖来源、图标绘制、选项往返、设置页与轮盘预览 | `WheelMenuTests` |

### 界面与无头测试注意事项

- 主题资源查询须携带 `ActualThemeVariant`。属性需要跟随主题时使用 `DynamicResource` 或
  `ThemeBrush.Assign`；`ThemeBrush.Of` 只提供快照。
- 验证样式时读取模板中实际接收颜色或状态的部件，不能只读控件静息属性。需要按名称命中模板部件时，
  使用 `TemplatePartTests` 验证部件存在。
- 普通窗口继承 `AppWindow`。无头平台的 `HWND` 为 0，`AppWindow` 已处理 FluentAvalonia 的句柄表；
  标题栏的实际外观仍需在真机检查。覆盖层不强制继承 `AppWindow`。
- 带内容的窗口在构造函数里调用 `WindowChrome.Attach(this)`，不要自己搭标题栏：它负责把标题栏交给
  内容、画鲸鱼与标题、留出系统按钮的宽度。漏掉它的窗口会退回库的默认标题栏——没有鲸鱼，也拖不动；
  `WindowChromeTests` 核对这一点。
- `DataTemplate` 内不使用 `x:Name` 作为实例定位手段。按类名查找元素时同时限定具体控件类型，
  避免继承关系导致多个匹配。
- 修改样式类时同时添加类并移除与其重复的本地属性；样式不设置由布局决定的宽、高或对齐。
- 列表行的高度写下限（`MinHeight`），不写定高。步骤行是两行字，名称与徽标还会换行；`Height` 定死之后
  多出来的那一行压到卡片边线下面，看起来只是"字被边框挡住"，而行本身没有任何报错。
  `MacroEditorTests` 核对行高不小于卡片高度。
- 一行里并排的东西各占一列。同一个 `Grid` 单元格里放两个元素，它们会叠在一起而不是左右排开，
  而且叠上去的那个往往有底色：运行调试的步骤缩略图就这样盖住过「第 1 次 20 毫秒」。
  `RunLookTests` 用两个元素的矩形是否相交来核对这一点。
- 提示只挂在说到它的那个小部件上。整张卡片挂一条 ID 提示，等于指针落在任何一处都弹同一句、
  挡住正文；`MacroEditorTests` 核对卡片上没有提示、ID 徽标上有。
- 一个参数模板里画着好几支（单行、数字、多行、变量…），只有一支显示，其余仍会生成控件。
  在测试里按 `DataContext` 去找会命中多枚，`IsVisible` 也仍是 `true`（局部值不看祖先）；每一支
  都带着同一枚 `ValueChip`，所以按按钮文字找更不可靠。要按位置找，或直接断言模型上的标志与行集合。
- 由工具写入的值在显示数据里标 `WrittenByTool`（不是语义里的 `Hidden`：契约测试禁止显示层重复
  引擎字段名），`BuildRows` 据此不给它生成行。值本身照常保存、校验，并在写入它的那扇窗里编辑。
- 控件模板的归属要查清楚。`FluentAvaloniaTheme` 覆盖大部分 Avalonia 控件，但 `ContextMenu` 没有：
  删掉 `Themes/Controls.axaml` 里那份模板，所有右键菜单都会打开却什么都不画，而无头用例不会发现。
  菜单类改动在无头里能验证的是主题存在、菜单有尺寸与 `ItemsPresenter`（`MenuThemeTests`）；
  外观与落点仍需真机检查。
- 读屏幕的图必须经过 `ScreenReady`，不要直接 `screen.Capture`：桌面复制器会把隐藏前的画面继续交出来，
  `DwmFlush` 并不能保证新帧已经到了。拾取器、框选后的成图和单步试看都走这一条。
- `vision.capture` 的矩形四个数（x/y/width/height）压在一行里，每格是
  `NumberCellViewModel`（短名 + 自带取值片的框），不再各自是一个参数对象：测试里按参数
  `DataContext` 找数字框会落空，要看 `ParameterRowViewModel.IsQuad` 与 `QuadCells`，或者按可见的
  数字框找。格与参数之间的同步在 `ParameterRowViewModel.Cell` 里，数字字段要连 `NumberValue`
  一起写，只写 `Text` 会让框里有字、步骤却是空的。OCR 的区域列表形状见下一条。
- 拾取窗口的检查用 `WindowRuleWindow.Picking` 换掉桌面那一步，不真的让位与指窗口；拾取器本身
  （`WindowPickWindow`）用 `ProbeAt`/`Confirm` 驱动，和元素拾取器一样。
- 拾取器的确认键要从 `IInputHook` 读，不能只靠窗口自己的 `KeyDown`：拾取器开在最上面，但 Windows
  不保证把前台交给它，交不出去时 F1/Esc 就落到别的窗口上了。`RegionPickerWindow` 与 `WindowPickWindow`
  都同时挂了钩子与窗口按键，`Finish` 只做一次。
- `WindowPickWindow` 的鼠标事件不设 `Suppress`：拾取时还要点别的窗口、点任务栏启动应用。取走只由 F1
  决定，左键最多把框挪过去。窗口让位期间，`WindowRuleWindow.OnLocalKeyDown` 要跳过 Esc——焦点可能还在
  那个藏起来的窗口里，否则一下 Esc 会把对话框本身关掉。
- `ocr.recognize` 的矩形是一张 `region` 列表（与找色、找字同一个参数），不再有 `x`/`y`/`width`/`height`/`rect`。
  核心测试用 `Region((x, y, w, h))` 或 `RegionFrom("record")`，界面测试看 `StepParameterViewModel.Regions`
  与 `ApplyRegion`（它往列表里加一行）。`vision.capture` 仍是四个数，两边的界面结构不同是有意的。

- 「还要像什么样」(`narrow`) 现在两族共用一张行表，但问的事不同，由目录声明：
  `ActionSemantics.NarrowOn` 取 `Page` 或 `Element`，`ActionParameter.NarrowFamily` 读它，
  `NarrowWay.For(family)` 给出该族的选项（网页六种：里面 / 含有文字 / 不含 / 有元素 / 没有元素 /
  看得见；桌面元素四种：看得见 / 能用 / 名字含 / 名字不含）。空着或写了不认识的族会抛异常，
  `Every_narrow_list_says_which_family_it_asks_about` 守着这一条。网页行上的「从页面选取」
  「看看效果」两颗按钮由 `NarrowRowViewModel.OnPage` 决定画不画。
- 桌面的窄条件读成 `UiCondition`（`UiConditionHow` + 值），从步骤的 `narrow` 行读；
  `FlaUiDevice.Matches` 逐条比，`Search` 在有窄条件时走「先按选择器缩，再逐项比」那条路。
  不认识的 `how` 报 `Run.ElementBadCondition`，这一条同时登记在[手册](../manual/17-failures.md)的失败键表里。
- 动作名：`uia.*` 说「界面」、`browser.*` 说「网页」；`No_two_actions_are_called_the_same_thing`
  直接读 `Strings.Chinese` / `Strings.English` 两张表来查重，不要改全局语言——测试并行跑，
  换语言会改到别人正在读的表。

- 无头用例报告 `You've caused dispatcher loop` 时，先找前一个启动模态窗口的用例，不要改被随机点中的
  断言。`ShowDialog` 打开的调度循环必须在本用例内完成：`RunJobs` 让循环启动，关闭窗口后 `await`
  返回的任务。`WindowStackingTests` 曾漏掉这两个步骤，把循环留给后续无关用例；新增模态用例照此写。
- 已知共享状态抖动：`MacroEditorWorkspaceTests.Setting_a_variable_can_create_a_persistent_global_in_the_same_save`、
  `GlobalVariablesTests.Closing_the_manager_without_save_changes_nothing`、
  `SharedStateTests.A_change_made_between_two_checks_is_not_there_for_the_second_one`、
  `LanguageRefreshTests.The_global_variable_manager_relabels_its_buttons_filters_and_types` 与
  `RunLookTests.A_run_that_keeps_looking_does_not_hold_every_picture_it_ever_took`
  涉及进程级状态；遇到时比较单跑与全量结果并检查 `[RestoreSharedState]`，不要仅凭重跑放行。
  最后一条在目录收敛那轮的全量里出现过一次：单跑通过、整类 6/6、二次全量全绿，
  且改动本身是纯枚举与字段名改名，与该用例无因果关系。

- 外部程序类用例同样可能在全量里抖动一次：`ScriptHostRunTests.A_script_runs_with_a_package_from_the_devices_folder`
  要起真实脚本宿主并往程序目录放一份包程序集。它在「只读值输入框推广到其余视图」那轮的全量里失败过一次：
  单跑 1/1 通过、二次全量全绿，且该轮改动只有 XAML 视图与界面断言，
  与脚本宿主、命名管道桥和包程序集无因果关系。遇到时按上面的口径比较单跑与全量结果，
  不要仅凭重跑放行，也不要改动被随机点中的断言。

- 修改类名、资源键或选择器后，检索 XAML、代码和测试中的旧引用，并运行全量测试。
- 会弹出模态确认的窗口必须在原用例中完成回答和关闭，可使用 `Ui.CloseAndAnswer` 或
  `Ui.AnswerUnsavedWork`，避免遗留调度器循环。
- 等待条件使用事件、`TaskCompletionSource` 或 `Ui.Until`，不使用固定睡眠推断异步操作已完成。
- 修改全局状态的测试使用 `[RestoreSharedState]` 并在界面线程还原；共享变量值由相关用例自行清理。
