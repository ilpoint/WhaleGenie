# 界面与主题

所属：[WhaleGenie 架构](../architecture.md)。

## 7. 界面与 FluentAvalonia

### 7.1 状态边界

| 状态 | 主要持有者 |
| --- | --- |
| 项目文档态 | `MainViewModel`：宏、触发器、路径、修订号和「磁盘上的包已过期」这一标记 |
| 编辑态 | `MacroEditorSession` 与编辑器 ViewModel：文档页、子宏、录制、编辑操作，以及「这次编辑有未保存内容」 |
| 运行期态 | `TriggerRuntime`、运行宿主和窗口字段：触发快照、浮动界面、恢复和运行显示 |
| 纯界面态 | 窗口与控件：拖动、落点、滚动、焦点和弹出层 |

编辑器里的改动由会话记一次：页面自己知道内容变没变，会话把它们与「新增了文档、抬出了子宏」这类
没有页面承载的改动合成一个答案，窗口再把它发布到项目文档态。关闭、恢复草稿与关闭确认问的是同一个
答案，不会各自拼一遍。

XAML 使用编译绑定，CommunityToolkit.Mvvm 提供源生成命令。窗口后置代码继续处理拖放、窗口生命周期
和控件交互，不以“窗口零逻辑”为目标；项目与设备等有状态服务仍由组合根提供。

### 7.2 步骤、条件与取值编辑器

步骤列表使用 `TreeView` 表达已有层级：一行一步，选中行原处展开，双击只作用于步骤行本身。块行和
体行都是子列表末端的拖放落点，折叠只改变子节点可见性，不重建步骤。横向滚动保持关闭，避免摘要宽度
把缩进与树线推出视口；`StepRow.IndentWidth` 统一给出 26 像素缩进，`GuideLinesView` 按绝对纵坐标
绘制连续树线，`StepGuideTests` 检查各列的起止位置。

每个步骤使用四字符 `StepIds` 标识，供断点、运行高亮和状态读取使用。新建、复制、粘贴、录制和 MCP
写入时发号；载入或合并发现重复时，为后到步骤重新发号，只改写其子树内按引用读取的
`step.<旧标识>`，并向用户报告变更。

条件行表与条件树读取同一份分组比较模型，写回原有表达式字符串，不增加第二种文件格式。读回只拆
同层 `&&` / `||` 和可识别的简单比较；函数、括号或否定语义无法安全还原时保留原文并切到表达式模式。
`control.if`、`control.while` 与 `control.waitUntil` 共用条件编辑器；多分支的值与分支步骤仍属于步骤模型。

所有值字段共用框内取值片，不再分别放置变量和表达式按钮。取值窗口把一格值当作形状编辑（`ValueDraft`），
光标落在形状的槽位上，认不出的字符留作文字，而不是要求输入始终能构成语法树；编辑面是形状序列，
不另设整条公式输入框。窗口判断一格能不能用时不渲染成一行再读回——字段自己的读法决定结果——公式
字段声明的 `ValueForm` 决定一行读成文字、拼接、整值还是名字，窗口不另写一套。窗口内拖拽使用指针事件，悬停由
`SlotNode.IsHovered` 维护，避免 Avalonia 伪类沿祖先传播造成多层高亮。

小输入框一次只写一个材料：数字按语言认的写法，文字加引号转义。光标只落在块边界，插入不会劈开一块；
拖拽不会把一条读得通的式子拖成读不通；空公式可以确认，用来清空一格；没有变量声明的名字描红边。

一格值按其形状编辑。值是"文字里夹着名字"时，它是 `ValuePart` 的序列：每段文字是一只 `InlineTextBox`
（按自己那只字体量宽），每个名字是一块 `NamePart`（`Border.Token`），带一枚摘掉它的叉。序列就是编辑面，
整格的值由它写回（`Compose`），所以值只有一份；`RebuildParts` 只在文字从别处来时重建，并且用一个
`_composing` 标志挡住"碎片写回时又去重建碎片"的循环。中间没有哪个字符表示"变量从这里开始"：
敲进去的 `$` 就是一个美元符号。名字只从墙上选进来（`InsertName`），它落在人刚才写字的地方，
必要时把那段文字切开；在一段开头的退格、末端的删除会越过边界，摘掉旁边的名字并把两边接起来。

整格是一条公式的字段不进这套：公式不是一句话，它仍是一只框。只有值里真的出现名字时才切成碎片，
一格纯文字仍是普通的框。名字本身就是它的名字，前面不带记号——读它的那个记号属于一行算式，不属于
名字，变量墙也照此显示（`剪贴板 (sys.clipboard)`）。

### 7.3 主题资源

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

### 7.4 窗口与覆盖层

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

### 7.5 设置窗口

`Views/SettingsWindow.axaml` 顶上三个 `Button`（`Classes="Tab Root"`，与主窗的停靠页同一族）标出
"系统、触发器、外观"三组，`Classes.selected` 跟着 `SettingsViewModel.IsXxxRoot`；下面是一个
`FANavigationView`，左侧只列当前组的页面，右侧是共享一个 `ScrollViewer` 的页面面板。每个设置项是
一个 `FASettingsExpanderItem`：左边是标题与说明（`Classes` 仍是 `FieldLabel` 与 `Hint`，与其它窗口
一致），右边 `Footer` 放控件。分组用 `FASettingsExpander`，`IsExpanded` 默认展开。

页面只有一处来源：`SettingsViewModel.SelectedPage`。组由它推导（`SelectedRoot` 与 `RootOf`），不是
另存一份状态，所以点顶部的组就是"落到该组第一页"，两者不会各说各的。导航项的 `Tag` 就是页面枚举名，
`SelectionChanged` 据此写枚举；`SelectedPage` 变化时窗口再按组切换导航项的 `IsVisible` 与 `IsEnabled`，
并把 `SelectedItem` 指向对应项——只折叠、不重建 `MenuItems`，否则选择会留在已被移出的项上。
设置项一律即时生效并立即落盘，打开窗口本身不写文件。
