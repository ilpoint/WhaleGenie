# 界面与主题

所属：[WhaleGenie 架构](../architecture.md)。

## 7. 界面与 FluentAvalonia

### 7.1 状态边界

| 状态 | 主要持有者 |
| --- | --- |
| 项目文档态 | `ProjectWorkspace`（Domain）：宏、触发器、路径、修订号、`Changed` 与整体替换 `Replaced`；`ProjectSaver`（App）是工程包唯一队列，`IPackageStore`/`FilePackageStore` 是它和导入导出共用的原子读写边界；`ProjectSaver` 用 `ReplaceAsync` 把打开/重载与在途写串行化 |
| 编辑态 | `EditorDocumentSet` 与 `HeadlessEditorSession`：稳定文档标识、打开的页签、父子关系、录制与编辑操作，以及每份文档「这次编辑有未保存内容」；`EditorDocumentService`（App）是桥、退出询问和工程替换共同使用的文档门面 |
| 运行期态 | `TriggerRuntime`、运行宿主和窗口字段：触发快照、浮动界面、恢复和运行显示 |
| 系统开关 | `SystemState`（App）：页面、热键与窗口读写同一个对象，触发宿主跟随它启停 |
| 纯界面态 | 窗口与控件：拖动、落点、滚动、焦点和弹出层 |

编辑器里的改动由会话记一次：页面自己知道内容变没变，会话把它们与「新增了文档、抬出了子宏」这类
没有页面承载的改动合成一个答案，窗口再把它发布到项目文档态。关闭与关闭确认问的是同一个答案，不会
各自拼一遍。自动保存只覆盖工程包：宏提交进项目之后由 `ProjectSaver` 写盘；单个宏的编辑不会自己
提交，关闭页签、离开编辑器、关闭主窗口或退出时仍问“保存 / 不保存 / 取消”。恢复快照只保存工程
包和共享变量，打开而未提交的编辑器草稿不进入恢复文件。

一次保存由 `ProjectMacroCommit` 回答：不能写的页面先被挡住、什么都还没构建，构建出的宏先问项目、
收下之后才清脏。它给出四种结局——页面挡住了、项目没收（目标已不在工程里）、未改动、已经进包；
`EditorDocumentSet` 把逐文档结局交给页面，页面只负责显示原因并保留脏状态。子宏保存只写回父宏
编辑模型，父宏保存才把整份宏提交给项目，因此工程对象不会在编辑器背后被改掉。新建子宏先开一份
草稿文档，保存成功才进入父宏的子宏列表；草稿被放弃时父宏没有留下半成品。页签身份由
`EditorDocumentSet` 的 `documentId` 回答：保存、改名、保存后重开、子宏打开都复用同一个文档；
关掉当前页签时激活左侧邻居，父宏关闭会连同它的子文档。子宏文档的 `rootSubMacros` 始终给出所属
根宏的已保存子宏名，页面在子宏页签上仍能显示这份列表。

工程替换（打开/重载/关闭全部）先经 `ProjectSession.PrepareProjectChangeAsync`：有脏文档就问一次
保存/放弃/取消，工程本身脏时再问工程保存；取消则原工程不动。MCP 在仍有打开的编辑文档时拒绝应用
草稿并要求重试。

窗口与视图按功能分目录：`Views/Pickers`（取色/区域/点位/UIA/窗口/偏差点选覆盖层与拾取宿主）、
`Views/Common`（跨窗口的画法辅助与元素测试框）、`Views/Run`（元素测试的闪烁框）、`Views/Settings`
（主题探针）与 `Views/Shell`（窗口外壳、`PackagePickerWindow` 包文件选择与通用对话框）；`ViewModels` 只剩 `Mcp` 与 `Shell`。
宏编辑器、动作表单、步骤树、条件与取值构建器、宏菜单、轮盘与界面面板都已迁入网页壳（§7.6），
原生侧只保留要抓桌面或透明置顶的拾取器、后台问话与网页宿主。命名空间仍是
`WhaleGenie.Views` 与 `WhaleGenie.ViewModels`，所以搬文件不改类型名，XAML 也只改资源字典那几条
`avares` 路径。

XAML 使用编译绑定，CommunityToolkit.Mvvm 提供源生成命令。窗口后置代码继续处理拖放、窗口生命周期
和控件交互，不以“窗口零逻辑”为目标；项目与设备等有状态服务仍由组合根提供。

### 7.2 宏编辑模型与取值

宏编辑器的界面在网页壳（§7.6）；C# 侧由 `MacroEditorViewModel`、`HeadlessEditorSession` 与
`EditorDocumentSet` 提供文档、步骤树、动作表单、条件与取值模型，`EditorDocumentService` 把它们交给
桥、退出询问和工程替换。桥只传快照与命令。`MacroStep.Detail`/`InputMark`/`MetaSummary`、`StepLists` 与 `ActionSemantics` 是步骤行、分节和字段的读法来源；页面不另写第二套。步骤 id 仍由 `StepIds` 发号，重复时在载入或合并处重发并报告。取值构建器在网页里只是文本框加变量/函数片，最终值仍由 C# 的 `Formula` 读回。

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
这样选择器的顺序与登记表只有一处。`StyleVocabularyTests` 与 `ControlsRegistryTests` 连 `App.axaml` 一起读，样式若再漂回应用层，
“挂了类却没有样式”的报告仍看得见，不需要另开白名单。

`FluentAvaloniaTheme` 没有接手 `ContextMenu` 的控件主题。库的 `ControlThemes/BasicControls` 里有
`Menu`、`MenuItem`、`MenuFlyoutPresenter` 和 `Separator`，唯独没有 `ContextMenu`，而它的模板原本由
`FluentTheme` 提供。`Themes/Controls.axaml` 的 `Styles.Resources` 里因此有一份按项目令牌写的
`ContextMenu` 模板：删掉它，每个右键菜单仍会打开、`IsOpen` 仍为真，但屏幕上不画任何东西。
不要为此重新引入 `<FluentTheme/>`，那会盖掉层 1 已经映射好的颜色。`MenuThemeTests` 核对这份主题
存在、打开后的菜单有尺寸和 `ItemsPresenter`。

### 7.4 窗口与覆盖层

普通应用窗口继承 `AppWindow`，后者继承 `FAAppWindow`。`WindowChrome.Attach(this)` 把标题栏
约定收成一处：它把标题栏交给内容，画上鲸鱼图标和窗口标题，并留出系统按钮需要的宽度。需要把内容
伸进标题栏的拾取器（`LookPickWindow`、`PickEntryWindow`）在构造函数里调用它；`ConfirmDialog` 与
`TextPromptDialog` 只继承 `AppWindow`，使用库默认标题栏。标题文字跟随 `Window.Title`，所以运行中
会改名的窗口不必自己同步。

框选、取色、点与元素拾取、窗口拾取和元素测试框等无标题栏覆盖层继续使用 Avalonia `Window`；菜单、轮盘与面板三块运行期浮层由 `WebOverlayWindow` 承载网页路由。
拾取器打开时先通过 `AppWindows.StepAside` 隐藏鲸灵窗口，再取屏幕；取图一律经过 `ScreenReady`：
它连拍到相邻两张一致为止，因为桌面复制器在收不到新帧时会继续交出隐藏前的画面，只等合成器
（`DwmFlush`）不足以保证这一点。取色器铺满整个桌面并显示这张冻结的画面，放大镜是它上面的浮层，
所以看到的就是取到的，滚轮也不会翻动后面的内容。

无头平台的窗口句柄均为 0，`AppWindow` 清理 FluentAvalonia 在此情形下重复使用的内部句柄表。
该处理只服务无头会话，真实窗口仍由 FluentAvalonia 管理。

### 7.5 设置页

设置页是网页：`webui/src/features/settings/settings-page.ts` 从 `settings.state` 读一份快照；常规卡片里的
“自动保存项目”默认开，开启后 `ProjectSaver` 在宏提交进项目、启停宏或改触发器之后把工程包写回，
关闭后仍由显式保存与退出询问负责。没有上次项目的首次启动落到
`%APPDATA%\WhaleGenie\projects\default.wgmacro`（默认项目），在第一次真正保存前不创建空文件。常规、语言、主题、热键、声音、屏幕读取、画面、
输入、驱动、存储、菜单呼出、窗口规则、轮盘菜单与轮盘动作都在页面里编辑。改动逐项经桥写回同一个
`SettingsViewModel/LocalSettings`；主题走
`theme.get/theme.set`，语言变化由 `strings.changed` 推回页面。C# 仍是唯一设置模型，页面不保存第二份
设置文件；设置项一律即时生效并立即落盘。

菜单规则、轮盘场景和编辑器窗口标题字段共用一个窗口列表：`window.list` 由
`WindowListBridge` 提供当前打开的窗口，网页的 `wg-window-list` 对话框挑一条；`window.pick` 只打开
原生 `WindowPickWindow` 覆盖层取得一个窗口。规则本身由页面编辑后经
`settings.menu.rule.*` / `settings.wheel.scene.*` 写回项目。轮盘的扇区动作在网页里选宏、备注和图标，
位置坐标与扇区几何由 C# 的 `WheelGeometry` 算好后随快照返回，保证编辑器和实时轮盘不会各画一套。
图标选择在页面内完成：`icon.names` 列出本构建认识的图标名、`icon.path` 按名取 Lucide 路径，
图片文件经 `dialog.pickImage` 选取；菜单格与设置轮盘的图标写回都走 `surface.menu.cell.icon`。

关于页同样是网页：Help 菜单进入 `about` 路由，`app.about` 交出版本可以格式化的项目地址和随包
嵌入的第三方组件清单；项目链接经 `app.openExternal` 交给系统浏览器。原生关于窗口与主题调色板已删除。

导入/导出也走网页：`project.importPlan/importApply` 与 `project.exportPlan/exportApply` 由
`PackageTransferService` 负责计划与应用，网页 `wg-transfer` 向导收集勾选与全局变量决定；它只把文件读写交给
`IPackageStore`，把单独子宏的落点交给 `IPublicLibraryStore`，不再直接调 codec。原生侧不再有选择宏或
冲突确认的窗口。工程和公共宏库是同一种 `.wgmacro` 包；公共库用 `library/entries.json`
区段区分，工程选择器遇到它会明确拒绝。


### 7.6 网页壳与桥接

> 三块运行期浮层（宏菜单、轮盘、界面面板）已全部迁到 Lit 透明浮层，原生窗口已删除；宿主、桥通道、
> 契约与验收清单见 [surfaces-migration.md](surfaces-migration.md)。
> 已落地：桥按 `Main/Menu/Wheel/Panel` 路由；`surface.menu.*`、`surface.wheel.*`、`surface.panel.*`
> 的命令、快照与页面类型已生成。

网页壳是程序的主界面：标题栏、导航与页面都画在一张网页里，由 WebView 控件承载。系统开关、触发器、
菜单、轮盘、浮层、后台问话、恢复/未保存流程以及文件与导入/导出编排都不再挂在一个原生主窗上；壳
接管 ProjectSession、NativeSurfaceHost 与 TriggerHost。设置页、运行面板、编辑器、关于与帮助都已走
网页入口；`MainViewModel` 只为原生拾取器转发项目视图，项目替换与未保存询问由
`ProjectSession`/`ShellProjectChangeGuard` 负责。窗口本体 `WebShellWindow` 只保留外框
职责——尺寸、窗口状态与拖动——其余全部是页面的事。

关闭窗口或退出时有未保存宏，原生侧不再直接弹 `ConfirmDialog`，而是通过
`editor.leaveAsked` 请网页画同一个“保存 / 不保存 / 取消”对话框，页面以 `editor.leaveAnswered`
交回结果；只有页面未 ready、重载或没有 WebView 时才回退到原生框。

导航默认落在宏列表，`#home` 只作为旧地址的别名，不保留第二个页面。活动栏底部常驻系统开关与
开关键：`system.state` 给初始快照，`system.changed` 在开关、设置快捷键或配置变化后推送；组合键由
页面按 `+` 拆行显示，但键名与“未设置”的说法由程序给出。公共宏库按“无触发的可调用宏”工作：库页
管理条目、可新建空白条目并从宏包导入；编辑器把库条目作为当前宏的子宏导入，不把工程宏当成可编辑
的第二份副本。子宏页签的“新建”只开草稿，确认保存后才进入父宏列表；旁边的“导入”从公共宏库、
当前工程或配置文件取一份，作为子宏打开。

分层是一条硬边界：

| 层 | 位置 | 职责 |
| --- | --- | --- |
| 宿主适配 | `src/WhaleGenie/Web/` | `WebShellWindow`（外框与网页装载）、`WebOverlayWindow`（透明浮层宿主）、`OverlayProbeWindow`（Debug 首帧探针）各自持有 `NativeWebView`；`WebBridge` 只做网页消息与桥之间的管道 |
| 桥协议与处理器 | `src/WhaleGenie.App/Bridge/` | `BridgeServer` 路由请求/应答/事件；全部处理器（应用、字符串、主题、窗口、项目、系统、公共宏库、编辑器、设置、运行）与端口在此；UI 只实现端口 |
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

编辑器在同一方向：`EditorDocumentSet` 持有多个 `HeadlessEditorSession`，每个会话持有
`MacroEditorViewModel` 与 `ProjectMacroCommit`；每份文档有一个稳定 `documentId`，页签、激活、
关闭和子宏归属都按它而不是按会改名的宏名来寻址。`EditorDocumentService`（App）把文档集包成桥、
退出询问和工程替换共用的门面。桥只传快照与命令：`editor.session`（open/new/subOpen/macroSetting/
activate/patch/undo/redo/deactivate/close/publicMacroAdd/subMacroCreate/subMacroCopy/subMacroImport/
subMacroDelete）、`editor.save`、`editor.saveAll`、`editor.discardAll`、`editor.unsavedDocuments`，以及
`editor.steps`（多选/复制/粘贴/分组/移动/删除）、`editor.step`（单步/参数/落点）、`editor.condition`、
`editor.trigger`、`editor.variable`、`editor.declaration`、`editor.record`、`editor.action`（page/
field/rowAdd/rowRemove/rowMove/rowCell/returns/returnsFixName/callArgument/meta/global/branch/window/
windowRule/padKey）、`editor.value`（open/command/confirm/cancel）。只读快照
`editor.sessionState` 与 `editor.sessionActions` 返回正文、页签和静态目录；静态目录（函数、变量
类型、比较符、触发种类、系统变量）只随 `editor.sessionActions` 发送一次，快照不再重复。保存回答是
`EditorSaveResponse`，逐文档给出 `saved/unchanged/blocked/conflict/missing` 与原因，只有前两种算
成功。快照的 `revision` 由 `EditorSessionBridge` 在整条编辑器线上递增，页面因此可以跨页签比较
先后；`editor.changed` 用于宿主侧关闭文档等没有对应命令的变化。`editor.step` 的 `kind` 带
`parentId`/`list`/`index` 与 `parameters`（预置字段），用来把动作放进指定分节或在加入时就写好字段。

取值走的是同一条方向：`ValueDraft`/`SlotNodes` 和恢复的 `ValuePickerViewModel` 在 C# 里持有
Token 结构、光标、撤销与检查；`editor.value` 的 open/command/confirm/cancel 只传
`EditorValueSnapshot` 和一组插入/摘除/换运算符/包裹/移动/常量输入命令。网页的
`wg-value-editor` 只画结构带、符号/变量/函数三盘；确认时 C# 通过 `StepParameterViewModel.WriteValue`
把形状写回字段，页面从不解析或重写公式文本。值字段本身是只读显示加框内「取值」按钮（数字可直接
输入），这部分也是 C# 快照驱动的。

动作表单走同一条路：`HeadlessActionSession` 包住原生的 `AddActionViewModel`，草稿的字段、行表、
条件、返回值、实参、全局声明、窗口规则与步骤设置都写在这个实例上；`editor.action` 用 `kind` 区分
page/field/rowAdd/rowRemove/rowMove/rowCell/returns/returnsFixName/callArgument/meta/global/branch/
window/windowRule/padKey，只传命令，`EditorActionSnapshotFactory` 把 `Pages/Rows/AdvancedRows/
ReturnsRows/CallArguments/Meta` 投影成快照，页面按 `rows` 的块与格排版，不按动作名或参数名推断。
确认时 `BuildStepForSave` 才把草稿变成 `MacroStep` 并插入树；取消只丢掉会话。

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
