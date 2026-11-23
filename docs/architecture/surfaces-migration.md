# 原生浮层迁移到 Lit：轮盘、宏菜单与界面宏

所属：[架构](../architecture.md)、[界面与桥接](interface.md)。
状态：计划（2026-11）。本文只写"怎么迁、按什么顺序、怎么验收"；已完成的结构见
[ADR 0002](../adr/0002-csharp-webui-split.md)。

## 1. 范围

网页壳已接管主窗、编辑器、设置与运行面板。仍是 Avalonia 窗口的三块**运行期浮层**：

| 浮层 | 当前实现 | 规模 | 用户入口 |
| --- | --- | --- | --- |
| ~~宏菜单（菜单宏）~~ **已迁移** | Lit 浮层（`Web/WebOverlayWindow` + `features/overlay/menu`）；原生窗口已删 | — | 菜单键、系统热键、置顶菜单 |
| ~~轮盘菜单~~ **已迁移** | Lit 浮层（`WheelOverlayHost` + `features/overlay/wheel`）；原生窗口已删 | — | 轮盘手势、重复键、按程序/宏切换 |
| ~~界面宏面板~~ **已迁移** | Lit 浮层（每组一个 `PanelOverlayHost`）；原生窗口已删 | — | 界面热键、分组显示、跟随前台窗口 |

桌面拾取器（`Views/Pickers`，约 6,900 行）不在本轮范围：它们必须抓屏、取色或叠加在别的窗口上，
迁移顺序排在浮层之后（见 §7）。

## 2. 目标结构

### 2.1 一个可复用的透明浮层宿主

新增 `Web/WebOverlayWindow`：无边框、置顶、不抢任务栏、尺寸由页面报告；内容是一块
`NativeWebView`，按 `path` 加载同一份网页产物的路由（`#/overlay/menu`、`#/overlay/wheel`、
`#/overlay/panel`）。

公开 API 只有一个入口：`NativeWebView.Background`（设为透明画刷）。适配器把它交给 WebView2 控制器
的默认背景（`ICoreWebView2Controller2.SetDefaultBackgroundColor`），Windows 上的 HWND 透明由包内
`MakeHwndTransparent` 完成——**后者是 internal，不直接调用**。窗口侧仍需 Avalonia 的透明窗口
（`TransparencyLevelHint` + 透明 `Window.Background`）。

S0 探针（`--overlay-probe`，Debug）已验证这条路径可以编译，真机结论记录在 §3.1。

原生侧只保留三件事：**窗口定位**（光标/前台窗口/多显示器与 DPI）、**命中与焦点策略**
（菜单需要键盘焦点做搜索；轮盘需要不抢焦点、只读原始鼠标增量）、**显示/隐藏时机**
（程序切前台、全屏、UAC 提权、屏幕锁定）。其余像素、命中、动画、滚动与文本都在页面。

**S0 结论（决定 S2/S3 的做法）**：

1. **键盘必须由页面回传。** `NativeWebView` 是独立的 HWND，点进页面后键盘全被它拿走，Avalonia 窗口的
   `KeyDown`（含隧道）收不到——第一次实测 F1/F3 因此"没有效果"。探针改为页面 `keydown` 里
   `window.chrome.webview.postMessage('probe:key:' + key)`，宿主 `WebMessageReceived` 接住再执行，
   F1（全屏，日志 `bounds=0,0,2560,1440`）、F3（重载，热帧 8 ms）、Esc（关闭）全部验证通过。
   **菜单**因此用页面自己的输入框与快捷键；**轮盘**不需要键盘，宿主把 WebView 设为不可聚焦即可。
2. **冷启首帧约 0.6 秒**，远超 50 ms。
所以浮层不能"热键按下才创建"：宿主窗口与页面必须在程序启动时就建好并隐藏（预建预热），
热键只做 `Show()`，实测 9–25 ms；页面内容变化走命令，不再重新装载（热帧 8 ms 是上限参考）。
这两条写入 §3 的 S2/S3 步骤与 §4 风险表。

### 2.2 桥的多通道

现在 `BridgeServer` 只有一个 `_channel`（`BridgeServer.cs:59`，`Attach` 在 90 行）。
主壳与浮层是两个 WebView，需要按"面"路由：

- `Attach(channel)` 仍是主壳；新增 `Attach(BridgeSurface, channel)`，面是 `Main`/`Menu`/`Wheel`/`Panel`；
- `PushAsync(..., surface)` 与 `DropPending(..., surface)` 按面过滤；未 ready 的面只缓冲自己的事件；
- `bridge.ready` 与请求应答回到发话的那一面；`PageReset` 只在主壳换页时触发（浮层重载不等于
  壳的对话框被丢弃）；
- `IBridgeHandler` 不变；处理器照旧调用 `PushAsync`，默认面是 `Main`。

**已完成（2026-11）**：`BridgeServer` 已按面路由，`BridgeSurfaceTests`（9 条）覆盖只发所属面、
按面缓冲、ready/应答回源、`PageReset` 仅主壳、按面合并与 `PushAllAsync` 的全表面广播。语言与主题
这类所有页面共享的事实走广播，状态快照仍按面推。测试放进现有 `WhaleGenie.Tests` 而不是
新建工程：桥的既有测试与夹具都在那里，再开一个工程只会复制夹具。

### 2.3 契约：快照 + 命令，不传控件

每个浮层一组 `surface.*` 方法与 `WhaleGenie.Contracts` 记录，规则与其他命令一致：
快照是可渲染的只读形状；命令带 `kind` 与参数；结果回变化的切片并带 `revision`。

**菜单**（`surface.menu.*`）：

- `open` / `close` / `pin` / `move` / `search` / `activate`（页面点击/回车）；
- 快照：`SurfaceMenuSnapshot(Sections, Cells, Bounds, Pinned, SearchText, Revision)`，
  cell 带 `kind`（宏/分组/空位/更多）、`macroName`、`icon`、`enabled`、`problem`；
- 拖拽落位走 `surface.menu.drop(from, to)`，复用 `ProjectTriggers.MenuPages` 的既有规则。

**轮盘**（`surface.wheel.*`）：

- `open` / `close` / `aim(x, y)` / `commit` / `cancel`；
- 快照：`SurfaceWheelSnapshot(Sectors, Centre, Radius, SceneName, Revision)`，
  sector 带 `index`、`macroName`、`label`、`icon`、`enabled`；
- 手势状态机仍在 C#（`WheelGesture`/`WheelAim`），页面只发坐标与选择；
  命中判定由 C# 的 `WheelCanvas` 几何迁为纯函数（`SurfaceWheelGeometry`），页面用同一份扇区。

**界面面板**（`surface.panel.*`）：

- `show` / `hide` / `toggleGroup` / `state`；
- 快照：`SurfacePanelSnapshot(GroupKey, Target, Width, Height, Controls, Revision)`，
  control 带 `kind`（按钮/标签/滑块/开关）、`label`、`action`、`enabled`；
- 位置由 C# 计算（`WindowPlacement`），页面只按给定尺寸排版。

## 3. 阶段

### S0 能力验证（1–2 天，先做）

真机验证透明浮层的可行性，用临时样例窗口 + 静态页，不接桥：

1. 透明 WebView2 在 Win10/Win11 上背景是否真透明（不是白底/黑底）；
2. 置顶浮层能否盖住全屏游戏（独占全屏可能失败，需记录"仅无边框窗口"）；
3. 100%/125%/150%/200% DPI 与双屏负坐标下窗口定位；
4. 打开延迟：原生窗口 vs 网页首帧，目标 <50ms 可感知延迟；
5. 键盘焦点：菜单搜索可输入；轮盘不夺焦点时仍能收到鼠标增量（原始输入或钩子转发）。

**放弃条件**：若 1、2、4 任一不达标，菜单/轮盘保留原生，只迁界面面板（它不要求像素级即时）。
这个结论写回本文，再进入 S1。

#### S0.1 探针怎么用

```powershell
dotnet run --project src/WhaleGenie -- --overlay-probe
```

只建一个透明置顶窗口与一块 `NativeWebView`，不建项目、不碰设备、不接桥。键位：`F1` 全屏、
`F2` 置顶开关、`F3` 重新装载（测热帧）、`Esc` 关闭；控制台打印"首帧完成"的毫秒数。

逐项记录（打勾或写数值），任一项失败就按 §3 的放弃条件处理：

| 检查 | 怎么看 | 结果（2026-11，本机 Win11 / 1366×768） |
| --- | --- | --- |
| 页面背景透明 | 卡片外能看见后面程序 | **通过**：卡片外的浏览器页面直接可见，无白底/黑底 |
| 置顶盖住普通程序 | 让浏览器在前，浮层仍在上面 | **通过** |
| 冷启首帧 | 首次 `Navigate` 到 `NavigationCompleted` | **607 / 685 ms** |
| 热帧 | 同一 WebView 再次装载 | **8 ms** |
| 已建窗口显示 | `Hide()` + `Show()` | **19–25 ms** |
| 置顶盖住全屏游戏 | 无边框全屏 / 独占全屏各一次 | **通过（用户真机实测）**：透明浮层在游戏全屏上可见 |
| DPI 125%/150%/200% | 拖动与全屏后位置、字号 | 待真机 |
| 双屏（含负坐标） | 副屏与 `F1` 全屏落点 | 待真机 |
| 键盘焦点 | 点页面后键入能被页面收到 | **通过**：但宿主窗口收不到键（见下） |

### S1 桥多通道 + 契约

- `BridgeServer` 按面路由（§2.2），主壳单面先行，行为不变；
- 新增 `WhaleGenie.Bridge.Tests`（ADR 0002 已承诺）：内存管道直调处理器，覆盖多面路由、
  缓冲、错误码；
- 落 `surface.menu/wheel/panel` 契记录与方法名，生成 TypeScript；
- 验收：`ContractGenerationTests` 无漂移；新测试覆盖"只发给所属面"。

### S2 宏菜单（先做，使用频率最高）

**已完成（2026-11）**：

- 契约 `SurfaceMenuSnapshot/Page/Cell`、`surface.menu.state` 与 `surface.menu.activate` 已生成到页面；
- App 的 `MenuSurface` 把 `ProjectTriggers.BuildMenuLayout` 投影成线上形状（图标只留名称），并可按
  `entryId` 运行宏；`MenuSurfaceTests` 覆盖格子、禁用宏与默认上下文页；
- 页面 `features/overlay/menu-overlay.ts`（`wg-overlay-menu`）：4×3 格子、分页、搜索、点击运行；
  `main.ts` 见到 `#overlay/menu` 时只挂这一个组件，不画壳；
- 浮层宿主 `WebOverlayWindow`（透明、置顶、不抢焦点）已建：加载同一份产物，按 `BridgeSurface.Menu`
  附着；`MenuOverlayHost` 管“建一次、光标旁显示、隐藏”，`--menu-overlay`（Debug）在壳旁打开，
  实测真实桥与真实项目数据可画出菜单面板，位置跟随鼠标。
- `surface.menu.close`：页面按 Esc 收起浮层；`surface.menu.activate` 先收起再运行宏。
  `MenuSurfaceBridgeTests` 覆盖这两条。
- 置顶钉住：页面右上角有钉住开关，状态经 `surface.menu.pin` 写到宿主。
- 收起时机：`MenuOverlayPolicy` 规定“可见 + 未钉住 + 前台是别的窗口”才收起，`MenuOverlayHost`
  订阅设备层的前台变化并执行。策略有单测（5 组）；前台事件的真实手感仍需真机确认。
- 拖动：页面标题按下时发 `surface.menu.drag`，宿主用 `WM_NCLBUTTONDOWN/HTCAPTION` 起系统移动
  （网页自己的指针事件出不了窗口边界，这条必须由系统移动循环做）。
- 右键菜单：编辑宏 / 调试运行 / 更换图标 / 替换宏 / 为当前程序加规则 / 屏蔽此程序；
  格子可拖到任意页的任意格（`surface.menu.move`）。`surface.menu.edit/debug` 经 `IMenuSurfaceActions`
  打开编辑器或运行面板；`surface.menu.icon` 调原生图标选择器并写回条目。`MenuSurfaceTests` 覆盖委托与写回。
- 结构操作已可用：`surface.menu.page.add/remove`、`surface.menu.rule.add`、`surface.menu.block`、
  `surface.menu.move`；页面在分页点旁提供「＋／×」，实测点「＋」后项目里多出一页（两个分页点）。
  `MenuSurfaceTests` 覆盖加页/删页与格子移动。
- **热键路径已切换（`--menu-overlay`，Debug）**：`NativeSurfaceHost.OpenMenu` 在拿到浮层时直接
  `Show()` 并置开态，`Hidden` 事件负责复位（含 `MacroTriggerGate`），搜索框焦点经
  `surface.menu.focus` 回报给热键判定。实测：启动走真实 `OpenMenu` 路径打开浮层，点面板后 Esc 收起。
  **键盘要先点进页面**（S0 的同一限制）。
- **原生菜单已删除**：默认构建就是浮层（`ShellHostFactory` 总是创建 `MenuOverlayHost`）；
  `MacroMenuWindow*`（11 个文件）、`MenuWindowTypes.cs` 与 `MenuWindowTests`（8 个文件）全部移除，
  `NativeSurfaceHost.Menu.cs` 从 621 行收成 148 行，只保留开关状态。实测删除后 `--menu-overlay`
  照常打开菜单。

**S2 已完成**：预建宿主、`surface.menu.*` 桥、Lit 页面（搜索/分页/置顶/右键/拖拽/替换）、
原生窗口与测试全部删除。剩余验收（真机清单）：菜单键打开 <100ms、多屏定位、游戏前台热键、
DPI 125/150/200% 下的落点。

### S4 界面面板（已开始）

- 契约 `SurfacePanelSnapshot/Item` 与 `surface.panel.state`；App 的 `PanelSurface` 用
  `InterfaceMemberResolver` 只投影可显示的成员（停用/缺宏不上面板）。
- 页面 `features/overlay/panel` 画标题与成员（横向/纵向、停用态），`#overlay/panel` 路由。
- 宿主已改为**每组一个** `PanelOverlayHost`（实现 `IPanelOverlay`）：尺寸由 `PanelGeometry` 按成员数/
  折叠/排列算，位置按目标客户区原点 + `InterfaceLayout` 偏移并夹在客户区内，`PlaceAbove` 把窗口排到
  目标窗口上方，拖动走系统移动循环并回报位置；帧经 `surface.panel.changed` 推送。
- **生产已切换**：`NativeSurfaceHost.Panels` 改用 `IPanelOverlay` 与 `PanelGeometry`（尺寸、客户区
  偏移、夹取、`PlaceAbove` z 序、拖动回报 `StoreLayout`）；`FloatingSurfaceWindow`(603) 与
  `NativeSurfaceHost.Panels.cs`(292) 的原生实现、`InterfaceSurfaceTests` 一并删除。真机验收未做。
- 成员点击：`surface.panel.run` 按名字查找并只在宏启用时运行；页面只对可显示成员发命令。
- 折叠与排列：`surface.panel.view` 把状态记在 App 的 `PanelSurface`（按组键），页面标题栏两个按钮
  切换；`Snapshot` 优先读记忆值，切换生产入口时原生窗口那份 `_surfaceViews` 可以删掉。
- **生产已切换**（见上）：原生窗口与窗口类测试已删除。

### S3 轮盘

**已完成（第一步）**：`WheelGeometry` 早已是纯函数并有 `WheelMenuTests.Gesture/Drawing` 覆盖；新增
`surface.wheel.state`（`SurfaceWheelSnapshot/Slot`）与 App 的 `WheelSurface` 投影，页面
`features/overlay/wheel` 用生成的 SVG 画扇区（Lit 模板里的嵌套 SVG 会落进 HTML 命名空间，
必须整段 `unsafeSVG`）。`WheelSurfaceTests` 覆盖“有宏的格子出现在正确环/位置”与空轮盘。
浮层宿主 `WheelOverlayHost` 已建（`BridgeSurface.Wheel`，全屏透明框），`--wheel-overlay`（Debug）
实测能用真实项目数据画出扇区。布局帧 `surface.wheel.changed` 走完整快照；瞄准帧改走轻量
`surface.wheel.aim`（按 Latest 合并），页面只更新动态层，`IWheelOverlay.SetAim` 负责推送。
**手势已切换**：`NativeSurfaceHost.Wheel` 改为驱动 `WheelOverlayHost`（定位到指针、瞄准帧、滚动换
场景、超时、关闭），命中测试改用 `WheelGeometry` + 已解析布局；原生 `WheelMenuWindow.cs`(526)、
`WheelCanvas.cs`(332) 与窗口类测试已删除，`WheelGesture` 用例保留为 `WheelGestureTests`。
**预热已接**：壳启动时两个浮层都在屏幕外 `Show` 一次并把页面加载完再 `Hide`，第一次呼出只剩
`Show()`（S0 实测 9 ms）。真机手势验收仍未做。

**剩余验收**（真机）：重复键、按住不动、快速划过、切换程序场景、与旧版命中一致（几何用例已对账）。

### 回归修复（2026-11-24，按原生观感）

用户真机反馈后核对原生实现，修了三处形状与两处浏览器痕迹：

- **菜单**：窗口回到原生 `350×667`（4×80 格、40 标题、34 搜索区、44 分隔、28 底栏）；标题栏
  鲸鱼/程序名/设置/搜索/钉住/关闭，空格子画 `SquarePlus`、宏格画 44px Lucide 或图片；搜索框
  默认不可见、不可聚焦，点放大镜才通过 `surface.menu.focus` 激活窗口得到键盘，Esc 关闭并把前台
  还给原程序。页面不再默认 `Activate/FocusPage`。
- **图标**：新增 `icon.path` 桥（`IconBridge`，`LucideIconExtensions.GetGeometryData`），页面
  `wg-icon` 按名字取路径并缓存；`F1 ` 分段前缀在页面拼回一条 SVG 路径。菜单、面板、轮盘的图标
  同源，不再有"首字"占位。
- **面板**：页面改成与 `PanelGeometry` 同一组数（86 宽、标题 20、成员 80×24、间距 2、最多 6 行），
  标题与两个 16×16 图标按钮不再被裁；折叠窗口用 `SetWindowRgn` 裁到 20 高条带。
- **轮盘**：快照改为"C# 给圈 + 位置 + tone + 中心文案 + 指针偏移"，页面画完整扇区网格（内 8、
  外 8/宽 16、第三圈按容量），空格也可见；瞄准格高亮、中心显示关闭/悬停名/场景、引导线按指针
  实线/虚线。窗口是 `2×直径` 的透明方形、点击穿透，命中测试改用实际 `WheelOptions.Diameter` 与
  有效圈数（以前固定 420）。
- **透明**：不再用 WebView2 内部互操作（`GeneratedComInterface` 反射调用会 `E_INVALIDARG`）；
  改为适配器创建后再写一次 `NativeWebView.Background = Transparent`，白方块消失（轮盘实测）。
- **右键**：`main.ts` 全局阻止 `contextmenu` 默认，菜单格子的自定义右键菜单不受影响；实测不再
  出现 Edge 的"返回/刷新/另存为/打印/检查"。

**菜单交互（Quicker 式）**：呼出与点击不激活窗口；未置顶时全局鼠标钩子把菜单外的点击当作"离开"并收起
（同进程窗口上点击也算，不依赖前台切换），置顶时只由 Esc、右上角关闭或再次按呼出键收起；搜索仍只在
点放大镜后临时取得键盘。空格子左键或右键添加宏，右键第二项"添加并设置图标"在选完宏后直接打开原生
图标选择器（Lucide 或图片）；左侧底栏按钮为当前前台程序建立进程名相等的场景规则，右侧为禁止在此程序
呼出；上半区/下半区的加号各自加分页，滚轮悬在哪个半区就翻哪组页。

**原生选择器迁移（2026-11-24）**：MenuMacroPickerWindow、MenuIconPickerWindow 删除，宏选择改为
空格右键的二级列表（surface.menu.macros + surface.menu.cell.assign），图标选择器由页面内
wg-icon-picker 承担（icon.names + 懒取 icon.path + 图片对话框），菜单格与设置轮盘共用；
图标写回走 surface.menu.cell.icon，图片对话框经 surface.menu.image 在挂起自动关闭的状态下打开。
置顶状态由宿主持有并在打开/切换时推 surface.menu.pinned，设置里可选择是否跨打开记忆；外边点击
由宿主自己的 Hook.MouseDown（派发到 UI 线程）加按钮轮询兜底，置顶时不响应。右键层移出
.panel 的 overflow:hidden 并按窗口四边夹取，二级宏列表位置同源。

**轮盘补缺（2026-11-24）**：`WheelOverlayHost` 现在使用覆盖当前屏幕的全屏透明框，按原生规则只夹取
可见圆圆心（轮盘画布外的透明区可出屏，引导线不再被方形裁断），
勾选“指针移到中心”时先把指针移到实际圆心再显示（不再只限夹取情形）；窗口保持隐藏加载新帧，
页面经 `surface.wheel.ready` 回报后再显示，未回报则重发 opening，约 1.6 秒仍无回报就放弃该次打开，
关闭时推 `surface.wheel.closed` 清空页面，避免旧位置闪一帧；
瞄准帧只推 `surface.wheel.aim`，指示线独立于扇区始终跟指针；命中测试改用窗口回报的实际圆心。字号、Gloss、HoverBorder、ShowGuide、
HideLabelWithIcon、FollowTheme 进入快照由页面实际绘制；每次打开推 `surface.wheel.opened`，
页面按 PopAnimation 做一次缩放淡入，时长由 `settings.wheel.update/popAnimationMs` 的滑块传入。
设置页增加“轮盘”导航，外观开关、预览和动作管理沿用同一几何；格子右键不再出现针对当前程序的规则/屏蔽项，这两项只留在底栏。

已自动验证：`WheelSurfaceTests` 9 条（圈数/宽外圈/第三圈/tone/中心文案/画布/aim 偏移/外观开关/Opening）、
`WheelPlacementTests` 3 条（边缘夹取与关闭夹取）、
`MenuSurfaceTests` 格子描述、`npm run check`。已手工看：菜单尺寸与图标、搜索默认隐藏/点开可
输入/Esc 收起、右键无浏览器菜单、轮盘完整网格与透明窗口。真机剩余：面板折叠/滚动、轮盘手势与
引导线、多屏与 DPI。

### S4 界面面板

**S4 已完成**：投影 `PanelSurface`、几何 `PanelGeometry`、每组宿主 `PanelOverlayHost`、页面
`features/overlay/panel`；原生窗口删除。**剩余验收**（真机）：跟随、折叠、界面热键、多程序多分组。

### S5 收尾（清理债务一并结清）

- P8 **已完成**：`Themes/Controls.axaml` 由 1133 行拆成 436 + 291 + 275 三份（Styles.Resources 留原位，
  后两份经 `StyleInclude` 按原顺序接上），每份都在 600 行以内；
- P9：mock 改为由 C# 夹具生成的快照 + 仅保留交互桩，目标 <2,500 行；
- P5：`AppSettings` 实例注入（见 §6）在本阶段前完成，浮层处理器直接接收实例；
- P3/P4：保存/打开已移出界面线程（UI 线程准备计划 → 后台写/读包 → UI 线程收尾）；浮层本身没有
  长 IO，busy 事件暂不需要。

## 4. 风险与对策

| 风险 | 影响 | 对策 |
| --- | --- | --- |
| 透明 WebView2 在部分驱动/远程桌面上失效 | 浮层白底 | S0 门禁；失败则菜单/轮盘不迁 |
| 独占全屏游戏盖不住置顶窗口 | 游戏场景不可用 | S0 实测；必要时对全屏程序回退原生 |
| 浮层首帧慢（实测 607ms） | 热键按下才建就不可用 | 预建隐藏宿主 + 预热页面；热键只 Show（19–25ms） |
| 焦点竞争（菜单搜索 vs 游戏按键） | 按键丢失 | 菜单显式请求焦点；关闭即还原前台；快捷键由页面回传（S0 已验证） |
| 多通道推送交错 | 快照串面 | 按面队列 + 每面 revision；Bridge.Tests 覆盖 |
| 拖拽与右键在 WebView 内不跟手 | 操作困难 | 用页面指针事件 + 拖动时低延迟推送；必要时 JS 内本地预览 |

## 5. 验收

- 契约：`ContractGenerationTests`、`WhaleGenie.Bridge.Tests`、新 `surface.*` 对账用例；
- 行为：每个浮层的真机清单（打开延迟、搜索、拖拽、右键、置顶、多屏、DPI、游戏前台）；
- 结构：界面工程不再存在三块原生窗口；`LayeringTests` 通过；
- 清理：样式表已拆成 436/340/324 三份；`LocalSettings` 静态调用点已归零；mock 从 5,550 降到 4,902，
  其中编辑器 mock 承载 e2e 行为，目标按 roadmap 重定；
- 全量：`dotnet test`、`npm run check`、`git diff --check` 全绿。

## 6. 与清理债务的关系

| 债务 | 现状 | 归属阶段 |
| --- | --- | --- |
| P5 设置注入 | 已完成：`AppSettings` 实例注入，静态门面只剩测试使用 | 完成 |
| P8 主题 | 已完成：`Controls.axaml` 1133 → 436 + 340 + 324 | 完成 |
| P9 mock | 4,902 行（词表已改由 `Strings` 生成）；目标重定为“先外置纯数据，再评估 <3,500” | 后续 |
| P3/P4 IO | 已完成保存与打开：MCP 保存、窗口保存/另存为、页面打开/重载都在后台读写包 | 完成 |

## 7. 不在本轮

桌面拾取器（点/区域/窗口/元素/浏览器/取景，约 6,900 行）依赖抓屏、置顶覆盖与跨程序指针，
排在浮层之后单独评估；在那之前它们继续用原生窗口，不并入浮层宿主。