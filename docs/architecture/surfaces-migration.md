# 原生浮层迁移到 Lit：轮盘、宏菜单与界面宏

所属：[架构](../architecture.md)、[界面与桥接](interface.md)。
状态：计划（2026-11）。本文只写"怎么迁、按什么顺序、怎么验收"；已完成的结构见
[ADR 0002](../adr/0002-csharp-webui-split.md)。

## 1. 范围

网页壳已接管主窗、编辑器、设置与运行面板。仍是 Avalonia 窗口的三块**运行期浮层**：

| 浮层 | 当前实现 | 规模 | 用户入口 |
| --- | --- | --- | --- |
| 宏菜单（菜单宏） | `Views/Editor/MacroMenuWindow*`（10 个文件） + `Runtime/NativeSurfaceHost.Menu.cs` | 约 1,900 + 621 行 | 菜单键、系统热键、置顶菜单 |
| 轮盘菜单 | `Views/Editor/WheelMenuWindow.cs`、`WheelCanvas.cs` + `Runtime/NativeSurfaceHost.Wheel.cs` | 526 + 332 + 465 行 | 轮盘手势、重复键、按程序/宏切换 |
| 界面宏面板 | `Views/Shell/FloatingSurfaceWindow.cs` + `Runtime/NativeSurfaceHost.Panels.cs` | 603 + 292 行 | 界面热键、分组显示、跟随前台窗口 |

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

**已完成（2026-11）**：`BridgeServer` 已按面路由，`BridgeSurfaceTests`（6 条）覆盖只发所属面、
按面缓冲、ready/应答回源、`PageReset` 仅主壳与按面合并。测试放进现有 `WhaleGenie.Tests` 而不是
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
- 右键菜单：编辑宏 / 调试运行 / 更换图标。`surface.menu.edit/debug` 经 `IMenuSurfaceActions`
  打开编辑器或运行面板；`surface.menu.icon` 调原生图标选择器并写回条目。`MenuSurfaceTests` 覆盖委托与写回。
- 结构操作已可用：`surface.menu.page.add/remove`、`surface.menu.rule.add`、`surface.menu.block`、
  `surface.menu.move`；页面在分页点旁提供「＋／×」，实测点「＋」后项目里多出一页（两个分页点）。
  `MenuSurfaceTests` 覆盖加页/删页与格子移动。
- **热键路径已切换（`--menu-overlay`，Debug）**：`NativeSurfaceHost.OpenMenu` 在拿到浮层时直接
  `Show()` 并置开态，`Hidden` 事件负责复位（含 `MacroTriggerGate`），搜索框焦点经
  `surface.menu.focus` 回报给热键判定。实测：启动走真实 `OpenMenu` 路径打开浮层，点面板后 Esc 收起。
  **键盘要先点进页面**（S0 的同一限制）。
- 原生菜单仍是默认入口。**尚未接线**：默认构建仍用原生窗口；浮层还缺“替换宏”、拖拽落位与按规则
  编辑页面的完整交互，这些补齐后翻转默认并删除 `MacroMenuWindow`。

0. **预建宿主**：壳启动时创建菜单浮层窗口与页面并隐藏；热键只 `Show()`（S0 实测 19–25 ms）。
1. C#：把 `MacroMenuWindow` 的排版数据（分节、格子、图标、问题、搜索过滤、置顶与本屏坐标）
   抽到 App 的 `MenuSurfaceModel`，原生窗口改为只读它的快照（先保行为，再删窗口）；
2. 桥：`surface.menu.*` 接入 `NativeSurfaceHost.Menu.cs` 的打开/关闭/前台订阅；
3. 页面：`features/overlay/menu` 用 Lit + 现有 `wg-*` 组件画格子、搜索、右键菜单、拖拽；
4. 原生删除：`MacroMenuWindow*`、`MenuWindowTypes.cs` 与菜单专用样式、测试；
5. 验收：菜单键打开 <100ms；搜索、置顶、拖拽、右键、按程序规则、多屏定位与原生逐项一致；
   菜单热键在游戏前台可用由真机清单覆盖。

### S3 轮盘

1. 几何与命中先迁为纯函数并加测试（扇区、半径、角度、紧邻边界）；
2. `WheelGesture`/`WheelAim` 保留在 C#，页面发 `surface.wheel.aim/commit`；
3. 页面画扇区与中心标签，动画用 CSS/Canvas；
4. 删除 `WheelMenuWindow`/`WheelCanvas` 与相关样式、测试；
5. 验收：重复键、按住不动、快速划过、切换程序场景、与旧版命中一致（几何用例对账）。

### S4 界面面板

1. 面板控制模型（`InterfaceLayouts` 已有数据）抽成 `SurfacePanelModel`；
2. 复用 S2/S3 的浮层宿主，按前台窗口与边界跟随；
3. 折叠/旋转状态留在 C#（`SurfaceViewState`），页面只显示；
4. 删除 `FloatingSurfaceWindow`；
5. 验收：跟随、折叠、界面热键、多程序多分组与原生一致。

### S5 收尾（清理债务一并结清）

- P8：随三块原生窗口删除，`Themes/Controls.axaml` 再降一档（目标 <600 行），删对应样式测试；
- P9：mock 改为由 C# 夹具生成的快照 + 仅保留交互桩，目标 <2,500 行；
- P5：`AppSettings` 实例注入（见 §6）在本阶段前完成，浮层处理器直接接收实例；
- P3/P4：把保存/打开与浮层 IO 移出界面线程，busy 走 `surface.*.busy` 事件。

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
- 清理：`Controls.axaml` <600 行、mock <2,500 行、`LocalSettings` 静态调用点归零；
- 全量：`dotnet test`、`npm run check`、`git diff --check` 全绿。

## 6. 与清理债务的关系

| 债务 | 现状 | 归属阶段 |
| --- | --- | --- |
| P5 设置注入 | 88 处静态 `LocalSettings` 调用、23 个文件 | S5 前（独立可做） |
| P8 主题 | `Controls.axaml` 1,133 行，剩余样式对应存活浮层/拾取器 | S2–S4 随窗口删除 |
| P9 mock | 约 5,800 行，无未使用导出，需夹具化 | S5 |
| P3/P4 IO/busy | 保存/打开与浮层 IO 仍在界面线程 | S5 |

## 7. 不在本轮

桌面拾取器（点/区域/窗口/元素/浏览器/取景，约 6,900 行）依赖抓屏、置顶覆盖与跨程序指针，
排在浮层之后单独评估；在那之前它们继续用原生窗口，不并入浮层宿主。
