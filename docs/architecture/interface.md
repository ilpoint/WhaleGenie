# 界面与主题

所属：[WhaleGenie 架构](../architecture.md)。

## 7. 界面与 FluentAvalonia

### 7.1 状态边界

| 状态 | 主要持有者 |
| --- | --- |
| 项目文档态 | `MainViewModel`：宏、触发器、路径、修订号和「磁盘上的包已过期」这一标记 |
| 编辑态 | `MacroEditorSession` 与编辑器 ViewModel：文档页、子宏、录制、编辑操作，以及「这次编辑有未保存内容」 |
| 运行期态 | `TriggerRuntime`、运行宿主和窗口字段：触发快照、浮动界面、恢复和运行显示 |
| 系统开关 | `SystemState`（App）：页面、热键与窗口读写同一个对象，触发宿主跟随它启停 |
| 纯界面态 | 窗口与控件：拖动、落点、滚动、焦点和弹出层 |

编辑器里的改动由会话记一次：页面自己知道内容变没变，会话把它们与「新增了文档、抬出了子宏」这类
没有页面承载的改动合成一个答案，窗口再把它发布到项目文档态。关闭、恢复草稿与关闭确认问的是同一个
答案，不会各自拼一遍。草稿本身由 `RecoveryDraft` 保管：第一次变脏立刻写、之后的改动等停顿再写、
退出时只收回自己写过的——「谁写的谁收回」这条规则因此只有一处实现，打开又关掉一个编辑器不会顺手
丢掉上一次运行留下的草稿。网页壳与编辑器都用它，差别只在一次写往磁盘上放什么：编辑器放正在写
的那个宏，壳放整个项目。

一次保存由 `EditorCommit` 回答：不能写的页面先被挡住、什么都还没构建，构建出的宏先问项目、收下之后
才清脏并收草稿。它给出三种结局——页面挡住了、项目没收、已经进包——窗口只负责把它们说出来：把挡住
的那一页翻到前面、点明项目为什么没收、或者关窗。宏交给项目后的落位与去重由 `ProjectMacroCommit`
回答：目标宏是否还在列表、抬出的子宏是否只加入一次、未改动时是否真的写回，以及新宏落在菜单还是
轮盘。页签也有同一处回答：哪个页面代表某个宏、关掉一页之后谁在前。后者要记住按下关闭按钮时的
前台，因为按钮长在页签上，按下的那一刻前台已经换成要走的那一页。

窗口与视图按功能分目录：`Views/Editor`（宏编辑窗、步骤树、变量墙、菜单与窗口规则）、
`Views/Actions`（动作对话框与参数行）、`Views/Pickers`（取值与各类拾取窗口）、`Views/Run`
（运行窗与屏幕叠层）、`Views/Variables`、`Views/Settings`、`Views/Shell`（窗口外壳、文件选择与
通用对话框）与 `Views/Common`（跨窗口的画法辅助）；`ViewModels` 的分组与之一一对应，只有 MCP 与取值窗口两处各占一边。命名空间仍是
`WhaleGenie.Views` 与 `WhaleGenie.ViewModels`，所以搬文件不改类型名，XAML 也只改资源字典那几条
`avares` 路径。

XAML 使用编译绑定，CommunityToolkit.Mvvm 提供源生成命令。窗口后置代码继续处理拖放、窗口生命周期
和控件交互，不以“窗口零逻辑”为目标；项目与设备等有状态服务仍由组合根提供。

### 7.2 步骤、条件与取值编辑器

步骤列表使用 `TreeView` 表达已有层级：一行一步，选中行原处展开，双击只作用于步骤行本身。展开的单列表块行
与体行都是子列表末端的拖放落点；折叠的块体内一行都不画，整行只给「块前 / 块后」两缝。折叠只改变子节点可见性，
不重建步骤。横向滚动保持关闭，避免摘要宽度把缩进与树线推出视口；`StepRow.IndentWidth` 统一给出 26 像素缩进，
`GuideLinesView` 按绝对纵坐标绘制连续树线，插入带的缩进取同一份 `TreeIndent`，`StepGuideTests` 检查树线的
起止位置，`StepDropTests` 检查插入带落在哪一列。

循环自己用的名字由 `Domain/Models/LoopNames.cs` 回答：检查用它判嵌套遮蔽，动作对话框在新建计数循环时用它
取一个外层没在用的计数器（`i` 被占就是 `i2`）。两处读的是同一份读法，界面不另写一套规则；落点由编辑器提供
（`MacroEditorViewModel.LoopNamesInScope`），与保存时用的是同一个落点。

每个步骤使用四字符 `StepIds` 标识，供断点、运行高亮和状态读取使用。新建、复制、粘贴、录制和 MCP
写入时发号；载入或合并发现重复时，为后到步骤重新发号，只改写其子树内按引用读取的
`step.<旧标识>`，并向用户报告变更。

条件行表与条件树读取同一份分组比较模型，写回原有表达式字符串，不增加第二种文件格式。读回只拆
同层 `&&` / `||` 和可识别的简单比较；函数、括号或否定语义无法安全还原时保留原文并切到表达式模式。
`control.if`、`control.while` 与 `control.waitUntil` 共用条件编辑器；多分支的值与分支步骤仍属于步骤模型。

所有值字段共用框内取值片，字段只读，唯一入口是取值窗口。窗口按字段声明的 `ValueForm` 分四模式，
不再有第二个变量窗口：`Text` 是一整段文字，`Run` 是文字与名字组成的序列，`Value` 是 token 树，
`Name` 是名字列表加一行新建。窗口判断一格能不能用时不再渲染成一行再读回：字段自己的读法决定结果。

`Value` 的根只有一个表达式，空位只属于容器：函数的参数槽、二元/一元的操作数槽、括号组和列表项槽。
二元和一元是一个容器 token，运算符是容器里的一枚可点按钮，不是兄弟块，缺的操作数在容器内部；
函数参数的空位保持现状。列表是 `[` 项 `]` 一个 token；括号组是一个 `GroupNode`，引擎对它透明求值，
文件写成 `{"group": …}`，界面画成一对括号包住里面那一块。根上不会并排出现两个表达式：没有槽位时，
值、名字和函数都拒绝写入并说明原因，运算符则包住选中或光标前的完整表达式。

选中一块（`SlotNode.IsSelected`）后，运算符、括号和列表都作用在它上面；没有选中时作用在光标前的
完整表达式或整个根上。拖拽只拖 token，根和 run 不是 token，空白处按下只移动光标；移动先合拢源位置，
再把块插到落点，整次移动是一条历史。删除走 `ShapeTree.Cut`：文字片和名字片移除后合拢相邻文字，
列表项直接移除，二元运算保留另一侧。光标是 `CaretPlace(Path, After)`，`CaretOffset` 从形状投影出
窗口画的接缝；可写接缝由形状收集，二元/一元内部只保留整个结构的外侧和其中的空位。历史同时保存值
和光标，撤销把两者一起放回；删除后 ViewModel 显式把光标放回删除接缝。

小输入框一次只写一个材料：数字按语言认的写法，文字加引号转义；`Run` 里打进去的就是文字本身。
结构带有焦点时，退格删除竖线前一块，Delete 删除后一块，两者都走 `Cut`；没有变量声明的名字描红边。

值是"文字里夹着名字"时，字段显示 `ValuePart` 的序列：每段文字是一只只读的 `SelectableTextBlock`，
每个名字是一块 `NamePart`（`Border.Token`），带一枚摘掉它的叉。序列只是显示，值由窗口写回（`Compose`）；
`RebuildParts` 用 `_composing` 标志挡住"碎片写回时又去重建碎片"的循环。字段本身没有文字编辑：
摘掉名字片走它自己的叉，键盘删除在取值窗口的结构带上完成，叉、退格和 Delete 都走 `Cut`。
名字本身就是它的名字，前面不带记号；变量墙也照此显示（`剪贴板 (sys.clipboard)`）。

### 7.3 主题资源

视觉契约与令牌角色的用法见 [DESIGN.md](../../DESIGN.md)；本节只记实现层的资源分层与例外。

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

`App.axaml` 只合并主题与上面三层资源，不写控件样式：控件类一律落在 `Themes/Controls.axaml`，
这样选择器的顺序与登记表只有一处。`ClassAuditTests` 连 `App.axaml` 一起读，样式若再漂回应用层，
“挂了类却没有样式”的报告仍看得见，不需要另开白名单。

`FluentAvaloniaTheme` 没有接手 `ContextMenu` 的控件主题。库的 `ControlThemes/BasicControls` 里有
`Menu`、`MenuItem`、`MenuFlyoutPresenter` 和 `Separator`，唯独没有 `ContextMenu`，而它的模板原本由
`FluentTheme` 提供。`Themes/Controls.axaml` 的 `Styles.Resources` 里因此有一份按项目令牌写的
`ContextMenu` 模板：删掉它，每个右键菜单仍会打开、`IsOpen` 仍为真，但屏幕上不画任何东西。
不要为此重新引入 `<FluentTheme/>`，那会盖掉层 1 已经映射好的颜色。`MenuThemeTests` 核对这份主题
存在、打开后的菜单有尺寸和 `ItemsPresenter`。

### 7.4 窗口与覆盖层

普通应用窗口继承 `AppWindow`，后者继承 `FAAppWindow`。`WindowChrome.Attach(this)` 把标题栏
约定收成一处：它把标题栏交给内容，画上鲸鱼图标和窗口标题，并留出系统按钮需要的宽度。剩余的
图标/宏选择、导入选择、确认/问话与全局变量冲突窗口在构造函数里调用它；标题文字跟随
`Window.Title`，所以运行中会改名的窗口不必自己同步。

框选、取色、点与元素拾取、屏幕框、菜单浮窗和浮动面板等无标题栏覆盖层继续使用 Avalonia `Window`。
拾取器打开时先通过 `AppWindows.StepAside` 隐藏鲸灵窗口，再取屏幕；取图一律经过 `ScreenReady`：
它连拍到相邻两张一致为止，因为桌面复制器在收不到新帧时会继续交出隐藏前的画面，只等合成器
（`DwmFlush`）不足以保证这一点。取色器铺满整个桌面并显示这张冻结的画面，放大镜是它上面的浮层，
所以看到的就是取到的，滚轮也不会翻动后面的内容。

无头平台的窗口句柄均为 0，`AppWindow` 清理 FluentAvalonia 在此情形下重复使用的内部句柄表。
该处理只服务无头会话，真实窗口仍由 FluentAvalonia 管理。

### 7.5 设置页

设置页是网页：`webui/src/features/settings/settings-page.ts` 从 `settings.state` 读一份快照，常规、语言、
主题、热键、声音、屏幕读取、画面、输入、驱动、存储、菜单呼出、窗口规则、轮盘菜单与轮盘动作都在
页面里编辑。改动逐项经桥写回同一个 `SettingsViewModel/LocalSettings`；主题走
`theme.get/theme.set`，语言变化由 `strings.changed` 推回页面。C# 仍是唯一设置模型，页面不保存第二份
设置文件；设置项一律即时生效并立即落盘。

菜单规则和轮盘场景的窗口规则表单也在网页：`settings.windows.list` 提供当前打开的窗口，
`settings.windows.pick` 只打开原生 `WindowPickWindow` 覆盖层取得一个窗口，规则本身由页面编辑后经
`settings.menu.rule.*` / `settings.wheel.scene.*` 写回项目。轮盘的扇区动作在网页里选宏、备注和图标，
位置坐标与扇区几何由 C# 的 `WheelGeometry` 算好后随快照返回，保证编辑器和实时轮盘不会各画一套。
图标选择仍打开 `MenuIconPickerWindow`，因为它要在内置图标、图片文件和项目资源之间做桌面选择。

关于页同样是网页：Help 菜单进入 `about` 路由，`app.about` 交出版本可以格式化的项目地址和随包
嵌入的第三方组件清单；项目链接经 `app.openExternal` 交给系统浏览器。原生关于窗口与主题调色板已删除。


### 7.6 网页壳与桥接

网页壳是界面的下一站：标题栏、导航与页面都画在一张网页里，由 WebView 控件承载。B0 已把系统
开关、触发器、菜单、轮盘、浮层、后台问话、恢复/未保存流程以及文件与导入/导出编排搬出主窗。
B1 已让壳接管 SessionHost、NativeSurfaceHost 与 TriggerHost，去掉隐藏主窗并删除 MainWindow；
MainViewModel 仍为编辑器、运行面板和原生菜单/轮盘提供无窗项目视图。设置页、运行面板、关于与帮助都已走网页入口。
窗口本体 `WebShellWindow` 只保留外框职责——尺寸、窗口状态与拖动——其余全部是页面的事。

分层是一条硬边界：

| 层 | 位置 | 职责 |
| --- | --- | --- |
| 宿主适配 | `src/WhaleGenie/Web/` | `WebShellWindow`（外框与网页装载）、`WebBridge`（程序里唯一认识 WebView 控件的类型） |
| 桥协议与处理器 | `src/WhaleGenie/Bridge/` | `BridgeServer` 路由请求/应答/事件；`app/strings/theme/window` 各处理器注册自己的方法 |
| 触发会话与原生面 | `src/WhaleGenie/Runtime/` | `TriggerHost` 持系统热键、计划与声音；`NativeSurfaceHost` 持菜单、轮盘、浮层与前台订阅；`RunQuestionHost` 持后台问话；`ProjectSession` 持恢复与未保存流程；`SystemState`（App）是页面/热键/窗口共写的开关 |
| 网页前端 | `webui/` | Lit 组件、桥客户端、词表与主题 store、mock 宿主 |

桥处理器不引用 WebView 控件类型（`WebUiBoundaryTests` 按源码检查），只与 `IBridgeChannel`
这根文本管道说话——无头测试用内存管道直调处理器，不需要浏览器。协议是三类信封：请求
`{id, method, params}`、应答 `{id, ok, result|error}`、事件 `{event, payload}`；方法名
`域.动作`，错误码是稳定词表（`NoSuchMethod`、`BadArgument`、`Internal`）。页面注册好收信口后发
`bridge.ready`，宿主在此之前缓存推送，语言或主题的早到变化不会丢。

方向只有一条：**C# 是唯一事实来源**。文档态（`ProjectWorkspace`）、系统开关、触发状态、语言与
主题都在程序侧，页面只做呈现与命令；程序侧的变化以事件推给页面（`strings.changed`、
`theme.changed`、`window.changed`、`project.changed`）。系统开关不在页面或窗口里：
`SystemState` 在 `WhaleGenie.App`，页面、热键与窗口写同一个对象，触发宿主跟随它启停触发器；
声音由宿主播，页面不自行发声。词表由 `Strings.Snapshot()` 按当前语言解析成
整包下发，页面没有回退逻辑；主题经 `ThemeController` 应用并落盘，页面用 Fluent 令牌铺色，不读
设置文件。页面文案硬编码由同一条测试拦住（见 `WebUiBoundaryTests`）。

编辑器在 C0 走同一方向：`HeadlessEditorSession` 持有 `MacroEditorViewModel` 与
`ProjectMacroCommit`，桥的 `editor.sessionOpen/State/Patch/Step/Save/Close/Actions` 只传快照与命令；
网页编辑器不保存自己的宏副本。C1 已把会话扩成完整文档、步骤树、动作表单、条件与宏设置；C2 已删除
原生编辑器、动作编辑与取值窗口，C# 编辑模型与桥仍是唯一模型。

桥推送的时序也是契约：ready 前同一事件只留最新一份；页面显式取 `project.state`、`theme.get` 或
`strings.bundle` 时清掉同域 pending；所有发送串行。页面问话在无页时立即答否，页内应答超时也
答否，删除、关闭或退出不会等一个不会到来的对话框。错误 `message` 由 `Strings` 生成，页面只
显示，不自己拼英文。页面在 pointerdown/focusin/keydown 时经 `window.focus` 报告文本框焦点；
`WebShellWindow` 结合 `IsActive` 实现 `ITriggerFocusProbe`，系统热键与宏启停键问的是活动窗口。

`webui/` 用 npm 构建（Vite + TypeScript + Lit + Fluent UI Web Components），产物是一个内联全部
脚本与样式的 `index.html`，随程序输出与发布包一起走；开发用 `npm run dev` 起带 mock 宿主的
热重载服务，程序以 `--webui-dev` 指向它。网页里的拾取器、宏运行浮层不在迁移范围——凡是要与
桌面交互、透明置顶的窗口永远留在 Avalonia 一侧（见 7.4）。
