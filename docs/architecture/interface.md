# 界面与主题

所属：[WhaleGenie 架构](../architecture.md)。

## 7. 界面与 FluentAvalonia

### 7.1 状态边界

| 状态 | 主要持有者 |
| --- | --- |
| 项目文档态 | `ProjectWorkspace`（App）：宏、触发器、路径、修订号和「磁盘上的包已过期」这一标记；`MainViewModel` 只做窗口侧转发 |
| 编辑态 | `HeadlessEditorWorkspace` 与 `HeadlessEditorSession`：打开的文档、子宏、录制、编辑操作，以及「这次编辑有未保存内容」 |
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

窗口与视图按功能分目录：`Views/Editor`（菜单图标/宏选择器等原生拾取器与
`ProjectMacroCommit`）、`Views/Pickers`（浏览器与窗口拾取覆盖层）、`Views/Variables`（字段标签列与
全局变量冲突提示）、`Views/Settings`（主题探针）、`Views/Shell`（窗口外壳、文件选择与通用对话框）与
`Views/Common`（跨窗口的画法辅助）；`ViewModels` 的分组与之一一对应。宏编辑器、动作表单、步骤树、
条件与取值构建器、宏菜单、轮盘与界面面板都已迁入网页壳（§7.6），原生侧只保留要抓桌面或透明置顶的
拾取器。命名空间仍是
`WhaleGenie.Views` 与 `WhaleGenie.ViewModels`，所以搬文件不改类型名，XAML 也只改资源字典那几条
`avares` 路径。

XAML 使用编译绑定，CommunityToolkit.Mvvm 提供源生成命令。窗口后置代码继续处理拖放、窗口生命周期
和控件交互，不以“窗口零逻辑”为目标；项目与设备等有状态服务仍由组合根提供。

### 7.2 宏编辑模型与取值

宏编辑器的界面在网页壳（§7.6）；C# 侧由 `MacroEditorViewModel`、`HeadlessEditorSession` 与`HeadlessEditorWorkspace` 提供文档、步骤树、动作表单、条件与取值模型，桥只传快照与命令。`MacroStep.Detail`/`InputMark`/`MetaSummary`、`StepLists` 与 `ActionSemantics` 是步骤行、分节和字段的读法来源；页面不另写第二套。步骤 id 仍由 `StepIds` 发号，重复时在载入或合并处重发并报告。取值构建器在网页里只是文本框加变量/函数片，最终值仍由 C# 的 `Formula` 读回。

### 7.3 主题资源

视觉契约与令牌角色的用法见 [DESIGN.md](../../DESIGN.md)；本节只记实现层的资源分层与例外。

`App.axaml` 使用 `FluentAvaloniaTheme`，控件模板和密度由 FluentAvalonia 提供。项目自身维护三层资源：

| 层 | 文件 | 职责 |
| --- | --- | --- |
| 语义令牌 | `Themes/Tokens.axaml` | 深色、浅色和自定义主题的颜色、画笔、字号、间距与圆角 |
| Fluent 资源键 | `Themes/FluentKeys.axaml` | 将 FluentAvalonia 模板读取的资源键映射到项目令牌 |
| 自定义控件类 | `Themes/Controls.axaml` | 项目控件族及其状态、变体和必要模板补充 |

原生只剩 `FluentAvaloniaTheme` 一套主题；`Avalonia.Themes.Fluent` 不作为应用主主题。`ThemePalette` 与
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

> 三块运行期浮层（宏菜单、轮盘、界面面板）已全部迁到 Lit 透明浮层，原生窗口已删除；宿主、桥通道、
> 契约与验收清单见 [surfaces-migration.md](surfaces-migration.md)。
> 已落地：桥按 `Main/Menu/Wheel/Panel` 路由；`surface.menu.*`、`surface.wheel.*`、`surface.panel.*`
> 的命令、快照与页面类型已生成。

网页壳是界面的下一站：标题栏、导航与页面都画在一张网页里，由 WebView 控件承载。B0 已把系统
开关、触发器、菜单、轮盘、浮层、后台问话、恢复/未保存流程以及文件与导入/导出编排搬出主窗。
B1 已让壳接管 SessionHost、NativeSurfaceHost 与 TriggerHost，去掉隐藏主窗并删除 MainWindow。
设置页、运行面板、编辑器、关于与帮助都已走网页入口；`MainViewModel` 只为原生拾取器转发项目视图。
窗口本体 `WebShellWindow` 只保留外框职责——尺寸、窗口状态与拖动——其余全部是页面的事。

分层是一条硬边界：

| 层 | 位置 | 职责 |
| --- | --- | --- |
| 宿主适配 | `src/WhaleGenie/Web/` | `WebShellWindow`（外框与网页装载）、`WebBridge`（程序里唯一认识 WebView 控件的类型） |
| 桥协议与处理器 | `src/WhaleGenie.App/Bridge/` | `BridgeServer` 路由请求/应答/事件；全部处理器（应用、字符串、主题、窗口、项目、编辑器、设置、运行）与端口在此；UI 只实现端口 |
| 触发会话与原生面 | `src/WhaleGenie/Runtime/` | `TriggerHost` 持系统热键、计划与声音；`NativeSurfaceHost` 持菜单、轮盘、浮层与前台订阅；`RunQuestionHost` 持后台问话；`ProjectSession` 持恢复与未保存流程；`SystemState`（App）是页面/热键/窗口共写的开关 |
| 网页前端 | `webui/` | Lit 组件、桥客户端、词表与主题 store、mock 宿主 |

桥处理器不引用 WebView 控件类型（`WebUiBoundaryTests` 按源码检查），只与 `IBridgeChannel`
这根文本管道说话——无头测试用内存管道直调处理器，不需要浏览器。协议是三类信封：请求
`{id, method, params}`、应答 `{id, ok, result|error}`、事件 `{event, payload}`；方法名
`域.动作`，错误码是稳定词表（`NoSuchMethod`、`BadArgument`、`Internal`）。载荷的形状写在
`WhaleGenie.Contracts`，由 `tools/WhaleGenie.ContractsGen` 生成页面的
`generated/types.ts` 与 `generated/methods.ts`；页面不手写线缆类型，`client.call` 的方法名是
`BridgeMethod` 联合，拼错在编译期就被拦住。状态类事件在发送前按事件名合并（新值覆盖尚未发出的
旧值），流式事件用 `BridgePushMode.All` 保序全部送出；队列由一把锁和一个泵串行处理。页面注册好
收信口后发 `bridge.ready`，宿主在此之前缓存推送，语言或主题的早到变化不会丢。

方向只有一条：**C# 是唯一事实来源**。文档态（`ProjectWorkspace`）、系统开关、触发状态、语言与
主题都在程序侧，页面只做呈现与命令；程序侧的变化以事件推给页面（`strings.changed`、
`theme.changed`、`window.changed`、`project.changed`）。系统开关不在页面或窗口里：
`SystemState` 在 `WhaleGenie.App`，页面、热键与窗口写同一个对象，触发宿主跟随它启停触发器；
声音由宿主播，页面不自行发声。词表由 `Strings.Snapshot()` 按当前语言解析成
整包下发，页面没有回退逻辑；主题经 `ThemeController` 应用并落盘，页面用 Fluent 令牌铺色，不读
设置文件。页面文案硬编码由同一条测试拦住（见 `WebUiBoundaryTests`）。

编辑器在同一方向：`HeadlessEditorWorkspace` 持有多个 `HeadlessEditorSession`，每个会话持有
`MacroEditorViewModel` 与 `ProjectMacroCommit`；桥的 `editor.sessionOpen/State/Patch/Step/Save/Close/
Actions` 只传快照与命令。快照现在同时给出步骤的分节（`sections`，每个子列表带本地化标题）、摘要、
问题、输入去向与步骤设置摘要，动作目录的类别/小分组/顺序，变量组的显示名与持久化，以及每份文档的
页签脏/问题/子宏标记；页面不再按动作名或参数自己拼这些事实。`editor.sessionStep` 接受
`list`（子列表名）与 `parameters`（预置字段），用来把动作放进指定分节或在加入时就写好字段。

取值走的是同一条方向：`ValueDraft`/`SlotNodes` 和恢复的 `ValuePickerViewModel` 在 C# 里持有
Token 结构、光标、撤销与检查；`editor.valueOpen/State/Command/Confirm/Cancel` 只传
`EditorValueSnapshot` 和一组插入/摘除/换运算符/包裹/移动/常量输入命令。网页的
`wg-value-editor` 只画结构带、符号/变量/函数三盘；确认时 C# 通过 `StepParameterViewModel.WriteValue`
把形状写回字段，页面从不解析或重写公式文本。值字段本身是只读显示加框内「取值」按钮（数字可直接
输入），这部分也是 C# 快照驱动的。

动作表单走同一条路：`HeadlessActionSession` 包住原生的 `AddActionViewModel`，草稿的字段、行表、
条件、返回值、实参、全局声明、窗口规则与步骤设置都写在这个实例上；`editor.actionPage/Field/Row*/
Returns/CallArgument/Meta/Global/Window*` 只传命令，`EditorActionSnapshotFactory` 把 `Pages/Rows/
AdvancedRows/ReturnsRows/CallArguments/Meta` 投影成快照，页面按 `rows` 的块与格排版，不按动作名或
参数名推断。确认时 `BuildStepForSave` 才把草稿变成 `MacroStep` 并插入树；取消只丢掉会话。

桌面拾取由 `IEditorPickerHost` 承接：会话的 `PreparePick` 从字段、动作目录和当前窗口算出请求，
`EditorPickerHost` 打开恢复的颜色/区域/点位/UIA 覆盖层或现存的 `WindowPickWindow`/`BrowserPicker`，
`ApplyPick` 把结果写回同一个 `StepParameterViewModel`。窗口规则表单、元素候选、键位和浏览器选择器
文本都在网页；拾取器打开前仍由 `AppWindows.StepAside` 让开鲸灵窗口。

动作表单的图片字段由 `ActionImagePreviews` 把文件读成 `data:` 预览，页面只显示缩略图和不可读
原因；区域/颜色点/键位序列通过索引把结果写回对应行。窗口规则对话框复用 `wg-window-rule`，
`editor.actionWindowPreview` 返回“现在会认到”的窗口列表。偏差点选恢复原生 `LookPickWindow`，
`PrepareLook` 在窗口让开后用 `MacroRunner.LookOnce` 取景，`ApplyLook` 写偏移或颜色点。
虚拟键盘由 C# 的 `EditorKeyPad` 生成完整键表（含 `KeyNames.Names` 未覆盖项的兜底），
热字串字符键盘由 `EditorCharacterPad` 生成，结束键由 `HotstringEndKeys` 快照渲染；树的分节
引导线仍由 `step-tree.ts` 绘制

网页侧的页面状态在 `features/editor/state/`，规格见 [dev/webui.md](../dev/webui.md)。
`editor-store.ts` 只做组合根：`editor-core` 持有快照、监听、错误与通用请求助手，
session/action/steps/value/triggers/declarations/variables/recording/ui 各是一个命令族 service；
`wg-editor` 及其子组件只渲染并把编辑命令交回。订阅与 window 监听由 `shared/lit/` 的 controller
绑定到元素生命周期。右侧是变量中心与宏设置上下双面板；步骤参数在独立编辑窗里；C# 编辑模型与桥
仍是唯一模型。mock 宿主的编辑器域拆在 `mock-editor*.ts`，与真桥同形，供 `e2e/editor.spec.ts` 驱动。

桥推送的时序也是契约：ready 前同一事件只留最新一份；页面显式取 `project.state`、`theme.get` 或
`strings.bundle` 时清掉同域 pending；所有发送串行。页面问话在无页时立即答否，页内应答超时也
答否，删除、关闭或退出不会等一个不会到来的对话框。错误 `message` 由 `Strings` 生成，页面只
显示，不自己拼英文。页面在 pointerdown/focusin/keydown 时经 `window.focus` 报告文本框焦点；
`WebShellWindow` 结合 `IsActive` 实现 `ITriggerFocusProbe`，系统热键与宏启停键问的是活动窗口。

`webui/` 用 npm 构建（Vite + TypeScript + Lit + Fluent UI Web Components），产物是一个内联全部
脚本与样式的 `index.html`，随程序输出与发布包一起走；开发用 `npm run dev` 起带 mock 宿主的
热重载服务，程序以 `--webui-dev` 指向它。需要与桌面交互、透明置顶的拾取器留在
Avalonia 一侧（见 7.4）；运行浮层已由网页承载，网页只负责按钮、规则表单和结果呈现。
