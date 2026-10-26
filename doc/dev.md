# WhaleGenie 开发说明

[简体中文](dev.md)

写给改这个仓库的人：常用命令、工程怎么分块、依赖了谁、测到哪一层为止、文档分工、改一个动作的顺序、
怎么量性能、代码约定，以及提交、CI 与发布。用这个程序的人看 [manual.md](manual.md)，长期设计约束看
仓库根目录的 `AGENTS.md`，下一步看 [roadmap.md](roadmap.md)。

## 常用命令

```powershell
dotnet build WhaleGenie.slnx -c Release          # 构建（含警告检查）
dotnet test  WhaleGenie.slnx -c Release          # 全部测试
dotnet format WhaleGenie.slnx --verify-no-changes --no-restore   # 格式检查
dotnet run --project WhaleGenie                  # 启动程序
pwsh build/site.ps1                              # 拼出介绍页 _site
```

量 MCP 的易用性，不必点界面：

```powershell
dotnet run --project WhaleGenie -c Release -- --mcp-doctor --spawn --report TestResults/mcp-after.md
dotnet run --project WhaleGenie -c Release -- --mcp --port 3917     # 无窗口起服务，Ctrl+C 停
```

量动作契约，把矩阵写到指定文件（不给变量就只在测试里算，不落盘）：

```powershell
$env:ACTION_MATRIX_REPORT = "TestResults/action-matrix.md"
dotnet test WhaleGenie.Core.Tests -c Release --filter "FullyQualifiedName~ActionBench"
Remove-Item Env:ACTION_MATRIX_REPORT
```

解决方案只声明 `x64` 一个平台（`WhaleGenie.slnx`），没有 `Any CPU`：程序依赖的 OpenCV 与
Playwright 驱动都只有 64 位。Debug 与 Release 都落在 x64 上，`dotnet build` / `dotnet test`
不写 `-p:Platform` 也一样；根 `Directory.Build.props` 里的 `PlatformTarget` 让直接构建某个
工程时也不会退回 32 位。

构建把警告当成错误（`Directory.Build.props` 里的 `TreatWarningsAsErrors`），
所以"能编译过"就等于"零警告"。

## 介绍页与短片

`site/` 是 GitHub Pages 的源：`index.html` 是单页介绍，`vibe/index.html` 是短片的时间轴；截图、
图标和收款码仍从 `doc/images` 与 `WhaleGenie/Assets` 取，由 `build/site.ps1` 拼到 `_site`，不在
仓库里多放一份会过期的副本。推送到 `master` 后 `.github/workflows/pages.yml` 发布 `_site`。
第一次发布前，在仓库的 Settings → Pages 把 Source 选为 GitHub Actions。

短片 `site/assets/whalegenie-vibe.mp4` 是 1280×720、24 秒、H.264 的无声文件，可以直接替换；要按
`site/vibe/index.html` 重录时，用一份临时安装的 Playwright 驱动本机 Edge：

```powershell
npm install --prefix "$env:TEMP\whalegenie-site-tools" playwright-core@1.64.1
npx --yes playwright@1.64.1 install ffmpeg
$env:WHALEGENIE_PLAYWRIGHT = "$env:TEMP\whalegenie-site-tools\node_modules\playwright-core"
node build/record-vibe.mjs .
```

录制脚本同时重做海报与社交分享图；它只用于改介绍页，不是程序的运行依赖。机器上还要有 `ffmpeg`
和 Edge（Windows 11 自带）。

只改文档时不必跑三项全绿，但要跑 `git diff --check`，并逐个确认 Markdown 相对链接指向存在的文件；
手册的函数表、失败键表或文案有改动时，再跑对应的 `FunctionTableTests` / `LocalizationTests`。

```powershell
# 列出文档里的相对链接，逐个确认目标存在
Get-ChildItem README.md,AGENTS.md,doc\*.md | Select-String -Pattern '\]\(([^)#]+)\)' -AllMatches |
  ForEach-Object { $_.Matches } | ForEach-Object { $_.Groups[1].Value } |
  Where-Object { $_ -notmatch '^[a-z]+:' } | Sort-Object -Unique
```

## 文档分工

| 文档 | 写给谁 | 写什么 | 不写什么 |
| --- | --- | --- | --- |
| `README.md` | 刚来的人 | 项目是什么、怎么启动、文档入口 | 动作细节、工程命令、未完成计划 |
| [manual.md](manual.md) | 用程序的人 | 每个动作、字段、失败键、限制与手动操作 | 实现细节、提交与发布 |
| `doc/dev.md` | 改仓库的人 | 命令、结构、依赖、测试分层、改动作顺序、发布 | 用户教程、路线排序 |
| [roadmap.md](roadmap.md) | 改仓库的人与 agent | 未完成阶段、依赖、验收、明确不做 | 已完成清单、工期 |
| [mcp-usability.md](mcp-usability.md) | 改 MCP 的人 | 一次真实自检的指标、改前改后、遗留项 | 手写的评价（它是 `--mcp-doctor --report` 的产物） |
| `AGENTS.md` | agent | 长期目标、开发规范与设计约定 | 已完成变更、逐阶段计划 |

四类事实各有一个真源：用户看到的行为以 `manual.md` 为准；动作语义（字段读法、值形状、单位、空值含义、
是否发输入、返回形状与记录字段）以引擎侧动作目录为准，界面目录只能引用它；失败键以 `RunKeys` 与手册的
失败键表为准，由 `FailureKeyTests` 对账；未完成的工作只在 `roadmap.md` 里；改过什么在 git 里。文档之间说的
不一致就是缺陷，与本文件、代码或测试对不上时，先改真源，再把其余几份对齐。

`manual.md` 描述已经实现的行为；路线图里的阶段是目标，未完成之前不要把未来行为写进手册。

**当前阶段**：路线图里的阶段已全部完成；新的工作按 [roadmap.md](roadmap.md) 的衡量标准重新立阶段。

动作目录分两层：引擎事实在 `WhaleGenie.Core/Execution/ActionSemantics.json`，由
`ActionSemanticsCatalog` 读取，包含动作键、分类、参数读法、值形状、单位、空值含义、字段自己的范围上限、
拾取器、是否发输入、是否看屏幕、等待与超时来源、返回形状与记录字段；界面保存编辑器种类、标签、
提示与页面归属（`WhaleGenie/Models/ActionDisplay.json`，由 `ActionCatalog.cs` 投影），只留外观。图标不在
这份清单里：它们在 `WhaleGenie/Models/AppIcons.cs`，是语义名到 `LucideIconKind` 的映射；
`IconView` 包装 `Lucide.Avalonia` 的 `LucideIcon`，把旧属性名映射到 `Kind`、`Foreground` 与
`StrokeWidth`，并把布局的 `Width`、`Height` 同步到 Lucide 的 `Size`，避免旧界面的小图标按 24 像素
画布裁剪。菜单格子的 `Builtin` 引用只保存 `MenuIconCatalog` 中的稳定键，选择器再映射到常见
`LucideIconKind`；新增菜单图标要同时给两个语言加标签。其它新增图标只加语义别名或直接使用 Lucide 枚举。
一个数值字段的范围由它的单位给出（像素 −100000..100000、毫秒 0..`LongestPauseMs`、计数 0..1000000、
百分比 0..100、手柄轴 −100..100、整数 −1000000..1000000），动作另有更窄的真实上限时在语义目录里写
`Minimum` / `Maximum`。`ActionContractTests` 卡住两边动作、参数与返回结果一一对应，并禁止展示清单再写一份
引擎事实。

**时间预算**：步骤设置里的超时是所有尝试共用的一份预算，`RunAttempts` 在重试循环外建一次取消令牌，
并记录每次尝试的耗时、结局与剩余预算（`StepRun.Attempts`）。等待循环可以按令牌停止；已经开始、无法
中途取消的设备调用在返回后判定，返回时已超预算就报超时。没有专门失败键的步骤用 `Run.Timeout`，等待
图片、元素、窗口、程序、剪贴板的那几步保留各自说明“没等到什么”的键。每次尝试开始时清空这一步勾选的
结果变量；步骤没写就是空，不是上一次的答案。

## 工程结构

| 项目 | 职责 |
| --- | --- |
| `WhaleGenie.Core` | 执行引擎。变量、表达式、执行语义、动作语义目录，以及所有真正碰机器的设备（输入、屏幕、OCR、UI Automation、文件、进程）。宏包文件模型与 `.wgmacro` 读写仍在界面层的 `WhaleGenie/Storage` |
| `WhaleGenie.GraphicsCapture` | 从 Windows 拿像素：Graphics Capture、桌面复制（DXGI）、屏幕 GDI 与 PrintWindow 四种取法，以及会话缓存与选路。它不认识引擎的类型，引擎通过 `ProjectReference` 用它 |
| `WhaleGenie` | 界面。窗口、视图模型、文案、触发器监听；动作展示清单在 `Models/ActionDisplay.json`，`ActionCatalog.cs` 只把它投影成标签、编辑器种类与页面归属；图标在 `Models/AppIcons.cs` |
| `WhaleGenie.Core.Tests` | 引擎的测试，跑在替身设备层上，不碰真实桌面；动作行为、读法与输入路由在这里验。宏包往返在 `WhaleGenie.Tests` 里验，因为它读写的文件模型目前在界面层 |
| `WhaleGenie.GraphicsCapture.Tests` | 画面获取的测试：选路次序、会话缓存与复用、图片落位。真正调用 Windows 的那几条按手动用例过 |
| `WhaleGenie.Tests` | 界面的测试，跑在 Avalonia 无头平台上；参数页面归属、返回值页、文案完整性与文档对账也在这里 |

## 依赖的包

`WhaleGenie.Core` 真正依赖的就这么几个，每个都对应一类碰机器的设备；版本写在
`WhaleGenie.Core/WhaleGenie.Core.csproj` 或 `WhaleGenie.GraphicsCapture/WhaleGenie.GraphicsCapture.csproj`
里，这里不抄第二遍。只有 `libVIIPER` 不是 NuGet 包：它是别人编译好的原生库，跟着仓库走。

| 包 | 在引擎里干什么 |
| --- | --- |
| `SharpHook` | 全局键盘鼠标钩子与注入：热键 / 鼠标按键 / 滚轮这些触发器，`input.*` 的前台路径，录制，以及各种拾取。名字只出现在引擎的钩子与注入实现里：钩子交出的是引擎自己的 `IInputHook` 与 `HookKey`、`HookMouseButton`、`HookWheelAxis` |
| `libVIIPER` | 驱动级输入：把按键和指针交给虚拟键鼠对（`input.*` 的"驱动级"），以及虚拟手柄（`gamepad.*`，一只 Xbox 360）。它不是 NuGet 包，是上游 v0.9.0 的 Windows 原生库，放在 `third_party/libVIIPER/`（dll、头文件、它自己的许可说明和一份来源说明），由 `WhaleGenie.Core.csproj` 拷到 exe 旁边，发布时再随其余原生库进 `lib`；服务端在程序自己的进程里跑，没有第二个程序 |
| `OpenCvSharp5.Windows` | 找图、找色、等图这类画面动作，以及 YOLO 检测（`vision.detect` 等三步）：OpenCV 的托管 API（`Cv2`、`Mat`、`Cv2.Dnn`）和它的 Windows 原生库。检测用的模型由用户自己带，包由引擎交给 `OpenCvDetectDevice` |
| `Sdcb.SimdPaddleOCR`、`Sdcb.SimdPaddleOCR.Models.ChineseV6Tiny` | 屏幕取字（OCR）：识别库和随包带的中文模型，不联网下载 |
| `FlaUI.UIA3` | UI Automation：元素的取值、选择、勾选、展开、滚动到可见、读表格 |
| `Microsoft.Playwright` | 浏览器动作（`browser.*`）：驱动跟着包走，默认开系统自带的 Edge |
| `CsvHelper` | 带分隔符的文本文件（`file.readCsv` / `file.writeCsv`）：引号、单元格里的换行、行尾、空行这些细节归它管。自己拆字符串在这些地方都会悄悄读错 |
| `Microsoft.CodeAnalysis.CSharp`（Roslyn） | 运行 C# 脚本（`script.csharp`）：把宏里的那段 C# 编成一份 dll。它就是 `dotnet build` 用的那个编译器，引擎按运行时给出的可信平台程序集加 `packages\nuget\references.json` 里的 dll 当引用 |
| `NuGet.Protocol`、`NuGet.Packaging`、`NuGet.Frameworks`、`NuGet.Versioning`、`System.Security.Cryptography.Pkcs` | 下载 NuGet 包（`script.nuget`）：到官方 v3 源上问一个包有哪些版本、把 nupkg 取下来、按目标框架挑最贴近的一份 lib、按版本区间解析传递依赖。包本身不跟着程序分发，下到程序自己的 `packages` 目录里。`System.Security.Cryptography.Pkcs` 单列一条钉在 10.0.13：`NuGet.Packaging` 7.9.0 拖来的 8.0.1 有两条已公开的公告（CVE-2026-84008 高、CVE-2026-70162 中），而 .NET 10 的 SDK 审计会把警告当错误，还原当场失败、CI 与发版都过不去；这个程序集由 WindowsDesktop 共享框架提供，钉住只是让还原图与运行时真正加载的那一份对上，包里不多一个文件 |
| `Vortice.Direct3D11`、`Vortice.DXGI`（都由 `WhaleGenie.GraphicsCapture` 引用，引擎不直接引） | 画面获取：Graphics Capture 与桌面复制是 Direct3D 11 与 DXGI 的调用，它们只是一层托管包装，调用的是 Windows 自带的那两个库，不随包分发原生库。边界见「工程结构」 |
| `Avalonia.Controls.ColorPicker` | 界面：设置里的自定义主题用它做 HSV 色板、滑块与十六进制输入；只有界面引用，引擎不认，也不影响宏文件格式。MIT，Fluent 主题样式从包里的 `Themes/Fluent/Fluent.xaml` 引入 |
| `Lucide.Avalonia` 0.2.27 | 界面：所有动作分类、工具栏、窗口按钮与菜单图标由 `LucideIconKind` 渲染；`IconView` 只做旧属性名到 Lucide `Kind`、`Foreground`、`StrokeWidth` 的映射。包按 MIT 授权，图形来自 ISC 授权的 Lucide |
| `ModelContextProtocol.AspNetCore` | 界面：主界面 MCP 开关启动的本机 HTTP 服务器。它只监听环回地址，不设令牌；工具读写当前项目草稿与运行日志，还能看屏幕、窗口与 UI 元素，并在浮层自己的「允许直接控制」开关下发送键鼠与窗口操作，但不能运行宏。`WhaleGenie.Core` 不引用它；宿主由 `WhaleGenie/Mcp/McpServerHost.cs` 建，工具的传输与发现由 SDK 管 |

这些包各自带进来的原生库（`OpenCvSharpExtern.dll`、`uiohook.dll`、Playwright 的驱动等）
和许可原文，列在 `THIRD-PARTY-NOTICES.md` 里。

## MCP 服务器

主界面「运行调试」左边的 MCP 按钮只负责打开浮层；浮层里的 `ToggleSwitch` 才启停
`WhaleGenie/Mcp/McpServerHost.cs`：一个只绑定 `127.0.0.1` 的 ASP.NET Core HTTP 服务，路径 `/mcp`，
**没有令牌**。`ModelContextProtocol.AspNetCore` 负责 Streamable HTTP、工具与 prompts 发现；开关关闭、
程序退出都会 `StopAsync` 并丢弃草稿。版本号在「文件 / 帮助」菜单右边。

- `McpLiveProject` 是界面与 HTTP 线程之间唯一的桥：快照、apply、save 全部通过
  `Dispatcher.UIThread.InvokeAsync` 回到界面线程。`MainViewModel.Revision` 在每次 `MarkDirty` 时
  增加；草稿 `apply` 前对不上就拒绝，避免覆盖人在界面里的修改。
- `McpDraftStore` 只改内存草稿；`apply` 才替换 `MainViewModel` 的宏与全局变量，`save_project` 才
  调 `MainViewModel.SavePackage`，先复制 `.wgmacro.bak`。
- `ActionKnowledge` 把 `ActionCatalog` 压平成 MCP 可见的参数/返回值；不暴露图标或控件。动作带
  `Hidden`，参数带 `ChildKeys`，所以外部作者能看出隐藏动作与每个子步骤列表接受的键。参数还带
  `Fields`（记录内部字段名）、`Unit`、`EmptyMeans`、`Minimum` 与 `Maximum`，窗口规则字段直接取自
  `WindowRule.ToJson`，MCP 客户端不必猜记录结构、数字范围或嵌套位置。
- `McpServerViewModel.EnsureSaved` 是启动前的保存回调：`App` 把它接到
  `MainWindow.EnsureProjectSavedForMcpAsync`，后者在没有 `CurrentPath` 时弹「选择保存位置 / 先打开」，
  选择位置走现有 `SaveAsAsync`。没有窗口的测试不设回调，仍可直接启动。
- `McpPrompts`（`WhaleGenie/Mcp/McpPrompts.cs`）提供 `whalegenie_write_macro` 与
  `whalegenie_debug_macro` 两条默认提示词，并通过 `McpServerOptions.ServerInstructions` 给只显示
  服务说明的客户端一份简短版本；`McpTools.Instructions` 还给不显示 prompts 的客户端留了
  一个同名工具。`McpServerHost` 用 `WithPrompts<McpPrompts>()` 注册。两条提示词都写明了视觉与
  直接控制工具的用法：调试从「人来回传截图」变成「AI 自己看、自己点」，但运行宏仍由人按。
- `McpControlTools`（`WhaleGenie/Mcp/McpControlTools.cs`）在项目工具之外给 AI 一双眼和一双手。
  只读的一组服务器开着就能答：`whalegenie_capture_screen`（整桌面或按 `whalegenie_list_windows`
  给出的窗口句柄，默认把最长边缩到 1920，否则 4K 桌面编码成 PNG 会把多数客户端撑爆）、
  `whalegenie_pixel_at`、`whalegenie_cursor`、`whalegenie_list_windows`、`whalegenie_element_at`
  （点上的控件，带可复用的 selector）。写的一组——移动 / 点击 / 滚轮 / 拖拽 / 按键 / 和弦 / 打字 /
  激活 / 移动 / 关闭窗口——只在 `McpControl.Allowed` 为真时响应：那是浮层里「允许直接控制」自己的
  开关（`McpServerViewModel.AllowControl` 写它），默认关、会话级，服务器停止或程序退出即复位。
  输入经静态锁串行，两个客户端不会把一次和弦交错成谁都没想发送的输入；`type_text` 的回执只报字数，
  不回显文本。截图默认经 `AppWindows.StepAside` 收起鲸灵自己的窗口再取，和拾取器同一套规矩，
  只有这一句需要界面线程，走 `Dispatcher.UIThread.InvokeAsync`。
- 新工具只经组合根交出的 `IDeviceLayer`（`SharedDevices.Current`）看屏幕、发输入，和宏运行看到、
  做到的是同一台机器，界面里不出现 `WhaleGenie.Core.Devices.Platform`；`McpControlTools` 另有一个
  收 `Func<IDeviceLayer>` 的内部构造供无头测试注入假设备层。
- 退出程序时不能在 UI 线程同步等 MCP 停止：MCP 请求可能正等 UI 线程，`GetResult()` 会让
  `App.Exit` 永久卡住，留下一个无窗口进程。`App.Exit` 现在把停止放到工作线程并最多等 3 秒；
  `McpServerHost.StopAsync` 另有 2 秒 Kestrel 超时和释放。主窗口关闭只是收进通知区域，真正的退出是
  「文件 → 退出鲸灵」或托盘退出；`Program.Run` 在桌面生命周期结束后再做 `Environment.Exit(0)` 兜底。
- MCP 端口由 `LocalSettings.McpPort` 保存（默认 3917），浮层里的 `NumberBox` 只在停止时可用；启动时
  作为首选端口，被占用则回退到系统分配端口，浮层显示实际地址。
- `RunLogStore`（`WhaleGenie/Execution/RunLogStore.cs`）保留最近 20 次运行的日志与步骤结局：
  `RunViewModel` 与触发器的 `AskRunHost` 各写一份 capture；MCP 只读，不触发运行。
- 工具名以 `whalegenie_` 开头；列表、读宏、写宏、校验、apply、save、运行日志各一组，外加视觉与
  直接控制一组（见下）。没有 `run_macro` / `debug_macro` 工具。
- 发现与形状：`whalegenie_list_actions` 返回一页 `{total, offset, limit, hasMore, categories, actions}`，
  隐藏动作默认不列（它们只允许出现在指名它的子列表里）；`whalegenie_get_actions` 一次描述多个键。
  `put_macro` 收 JSON 对象、`set_globals` 收 JSON 数组，不再把 JSON 塞进字符串参数。参数里的闭集
  （鼠标键、滚轮方向、窗口部位、比较方式、提示词主题）是枚举，由 SDK 变成 schema 的 `enum`；
  参数还带 `Conditional` 与 `MissingKey`，所以「后台才需要窗口规则」不会被读成「永远必填」。
- 错误：`McpErrors` 统一给出 `{error:{code,message,hint,retryable,details}}` 并置 `isError`，工具方法返回
  `CallToolResult`；深处用 `McpFailure` 抛，`McpErrors.Run` 接住。码表：`BadArgument`、`BadJson`、
  `BadPatch`、`BadPlacement`、`DraftNotFound`、`NoSuchMacro`、`NoSuchRun`、`UnknownAction`、`AlreadyApplied`、
  `RevisionConflict`、`NoPackagePath`、`DraftHasBlockingProblems`、`DirectControlOff`、`DeviceUnavailable`、
  `DeviceFailed`、`Internal`。消息可以改写，码不能改：客户端按码分支。
- 写入与补丁：`McpDraft.Put` 落地就 `McpPatches.Settle`，给宏与子宏的每个步骤发四字符 id（父宏与子宏共用
  一套名字），因为一次运行按 `step.<id>.outcome` 报步骤，客户端写的步骤等到有人打开编辑器才补号就太晚。
  回答给 `outline`（每步 `id` / `type` / `path`），整宏只在 `includeMacro` 时回：客户端刚发过的东西不必再收一遍。
  `whalegenie_patch_macro`（`WhaleGenie/Mcp/McpPatches.cs`）在副本上按序执行七种操作：

  | 操作 | 改什么 | 怎么点名 |
  | --- | --- | --- |
  | `set` | 一个参数的值（窗口规则可整条给对象） | `stepId` + `param` + `value` |
  | `setRow` | 一张表里一行的几列 | `stepId` + `param` + `match` + `values` |
  | `addRow` | 往表里加一行 | `stepId` + `param` + `values` |
  | `removeRow` | 从表里删一行 | `stepId` + `param` + `match` |
  | `insert` | 往列表里插一步 | `parent`（列表路径）+ `index` + `step` |
  | `remove` | 删一步 | `stepId` |
  | `move` | 把一步挪到别处 | `stepId` + `parent` + `index` |

  `set` 收一个值：标量、整值表达式都行，**窗口规则**还能整条给一个对象（它在文件里本来就写成对象，
  未知字段会被拒并列出该有的）。`rect` / `point` 不同——它们在文件里是**一个值**（一条记录变量，或
  `field($rec, "name")`），给对象会被拒并说明这一点。表是
  `arguments` / `returns` / `region` / `keySequence` / `images` / `narrow` 这些行列表。行按**它自己说的话**
  点名而不是按位置，所以上面插一行不会让这次改动落到另一行上；`match` 命中多行或零行都拒绝——猜哪一行是作者
  没做的那个决定。`addRow` 要求目录声明为必填的列都在，且不与已有行重复。`insert` / `move` 的落点走
  `StepPlacement`，与编辑器同一套 `childKeys` 规则；任一条不合就整批不改。`parent` 是 `0.then`、`0.then.2.else`
  这样的列表路径，空串是顶层；子宏里的步骤仍由 `put_macro` 改。
- 校验：`McpDraftStore.Validate` 把 `MacroCheck.DeclarationFaults` 与 `Inspect` 记为 `Blocking`，把
  `DeclarationWarnings` 记为 `Hints`；`apply` 只拦 `Blocking`，`force` 可越过。每条带 `macro`、`stepId` 与
  **`code`**：`Inspect` 的每条问题在 `MacroCheck` 里就带一个与语言无关的键（`NoMacro`、`MissingField`、
  `BadOption`、`CaseOutsideSwitch`…，就是文案键 `Editor.Problem.*` 的后缀），声明类问题带
  `Declaration.<Fault>`（`Declaration.RequiredWithDefault` 等）。句子会随界面语言变，代号不会，客户端按代号分支。
- 动作目录作为资源：`whalegenie://actions`（全部动作的 key / 名字 / 分类 / 是否隐藏 / 参数名）与
  `whalegenie://actions/{key}`（一条动作的完整描述，与 `whalegenie_get_action` 同一份），在
  `WhaleGenie/Mcp/McpResources.cs`。能缓存资源的客户端读一次就留着；读不了资源的仍用工具，两条路说同一件事。
- 后台运行的句柄规则：一次等成功取回结果之后句柄才从表里拿掉（`Finish`），等待本身失败时留着——三个等待
  （`waitMacro` / `waitAll` / `waitAny`）因此是同一条规则，宏可以在失败后再用 `runStatus` 读它结束时的状态。
  `WaitOtherMacro` 原先在核结果**之前**就 `Finish`，是三个里唯一不一样的。
- 子运行的失败键：`EndingOf` 把任务抛出的异常与取消折成 `Run.Failed`（详情是异常类型名），`HandleValue` 在
  出错/取消的任务上不再读 `.Result`——那是 `AggregateException` 绕过失败键的唯一一条路。
- 后台运行的归属：一个宏自己的 `RunAsync` 结束时（`StopChildrenAsync`）把它起的、还没被等的运行停下，所以
  同步调用里起的后台运行不会留到调用方那里，句柄随局部表一起消失是设计如此，不是漏。
- 设计期：`TrackPendingResults` 按句柄名结算（`waitAny` / `waitAll` 只忘掉列表里点到的那些，别的一律还算
  欠一次等）；`CheckSiblings` 在同一层步骤里查两件事——两个 `startMacro` 共用一个句柄名
  （`DuplicateRunHandle`），两个调用把结果写进同一个名字（`DuplicateResultName`）。只比同层，因为同一个
  `switch` 的两个分支不会都跑，同名在那里是省事而不是错。
- 返回值证据（C3）：`WhaleGenie.Tests/RunnerLedger.cs` 从源码读出「动作键 → 分派方法 → 这条路径写得出的
  结果」，不是「整个引擎目录里出现过这个字面量」。读法是：只在 `Dispatch*` 里找 `case "动作键":`（别的
  switch 的 case 是词，不是动作键），取该分支里调用的方法（含同名重载、两跳以内），把这一片的字符串、
  `FieldNames` 常量与键参数默认值都算作可写键。`ReturnsPageTests` 据此断言三件事：**每条声明的结果都被
  自己那条路径写过**、**目录里的每个动作都有分支**（隐藏动作除外，`control.case` 由父步骤读）、**每条分支
  都在目录里**。失败信息逐条给 `动作 → 方法: 声明了结果 "键"`。
  这条台账第一次跑就抓到 `control.startMacro` 声明了 `run` 却没人写它；句柄的通道是 `runHandleName` 那一格，
  所以删掉了这个死结果（目录与展示目录、中文文案一起删）。
- 目录合约（C4）：`WhaleGenie/Models/ActionContract.cs` 的 `ActionContract.Check(语义, 展示)` 一次列出缺动作、
  多动作、重名（两份各自查）、缺参数、多参数、未知控件种类 `UnknownKind`、缺结果、多结果，每条 `Kind 动作.字段`。
  `ActionCatalog` 的静态构造先跑它，**全部报完再抛一句**（以前是遇到第一个就抛）。`ActionCatalog.Display`
  把展示事实按合约形状暴露出来，测试因此能在不启动窗口的情况下核对两份**出厂**文件。
- 调用审计：`WhaleGenie/Mcp/McpAudit.cs` 往 `logs/mcp-<日期>.log` 追加一行一次调用（时间、工具、一句话摘要、
  结果）。**只记改了东西或动了机器的**：`Drive` 里记 9 个动手工具（成功 `sent`、被拒带 `code`），`McpDraft` 的
  `Put` / `Delete` / `SetGlobals` 与 `McpLiveProject` 的 `ApplyAsync` / `SaveAsync` 各记一条；只读工具不记。
  `type_text` 只记字数不记内容——日志在磁盘上，正文不该在那里。浮层的「查看调用记录」打开今天的文件（还没写过
  就打开文件夹）。`builder.Logging.ClearProviders()` 保留：框架日志关着，审计是我们自己的记录。
- 列表类返回一律带 `total` 与 `hasMore`（`list_macros`、`list_actions`）；`get_run_log` 带 `totalLines` / `returned` / `truncated` / `fromLine`，从末尾
  取（出错的行在最后），不再静默截断。
- 失败日志按文件名认（`McpFailureLogs`，只认 `failure-*.log` 且必须在日志目录里）：日志目录里还有服务器
  自己的调用记录，按"最新一个 .log"取会读到审计文件。
- 无界面入口：`WhaleGenie.exe --mcp [--port N] [--open 包] [--new 文件夹]` 在 `App.Headless` 里以
  `ShutdownMode.OnExplicitShutdown` 起 Avalonia 但不建窗口（`MainViewModel` 与设备层都要那个线程），
  只做 `PersistentVariables.Initialize` 与 `CaptureSetup.Load`，不装虚拟键鼠、不预热 OCR；stdout 只写
  一行 `endpoint=…` 给人以外的程序读。参数解析在 `McpCommandLine`。
- 体检：`WhaleGenie.exe --mcp-doctor (--url … | --spawn) [--open …] [--report …] [--write] [--strict]`
  在 Avalonia 之前跑（`McpDoctor`），用 `McpSelfCheck` 量一遍并打印 markdown；`--spawn` 先复制包到临时
  目录再拉起 `--mcp --port 0`，用完杀掉。退出码：0 全过、1 有 fail（`--strict` 下 `missing` 也算）、
  2 参数或包有问题、3 连不上、4 子进程没报出地址。客户端显式不用系统代理：本机助手的环回请求被代理
  接走时，回话是 502 而不是「连接被拒」，会把人引向错的问题。
- 调用传参：`whalegenie_get_macro_signature(draftId, name)` 交出一个宏的 `arguments`（名字、类型、必填、
  默认值、说明）与 `results`，外加 `argumentColumns`（`name` / `value` / `given` 三列的名字）与一份
  `callTemplate`——模板里只列必填的形参，可选的一个都不写，因为「没写」才是用默认值的写法。形参与结果来自被调用宏的声明，动作目录里没有，所以这是客户端唯一能读到它们的地方；
  `get_action` 对 `SignatureSource = CalledMacro` 的动作只说这件事，并给出 `returns` 行的两张必需列
  （`columns`）与返回记录的字段（`fields`）。
- `McpSelfCheck`（`WhaleGenie/Mcp/McpSelfCheck.cs`）是改前改后共用的尺子：只认 `McpClient`，不抛、不断言，
  缺失的字段记 0 并写进 `Facts`；指标与检查项见 `doc/mcp-usability.md`。它容错的是尺子遇到没见过的形状，
  不是产品里的兼容分支。
- 测试：`McpServerTests` 用官方 HTTP 客户端走工具列表、schema 里的 `enum`/对象参数、完整写存流程与
  `apply` 门禁与 `patch_macro`（改一个参数、增改删一张表的行、拒绝放错的步骤且草稿不变、`includeMacro`）；
  `McpDraftTests`
  验 Revision、保存、非法落点与「写入即发步骤标识」；`McpActionKnowledgeTests` 验 `Hidden` /
  `ChildKeys` / 分页总数 / `Conditional`；`McpSelfCheckTests` 验尺子自己（闭环、六类错误、端点消失不抛）；
  `RunLogTests` 验日志；`ActionContractReportTests` 验两份目录的缺口报告；`McpControlTests` 用假设备层验直接控制门禁、工具交给设备的请求、错误码与截图降采样。
  无外网；真实桌面行为按手动过一遍为准。

## 脚本怎么跑

脚本分两条路。`script.run` 那种是机器上别的程序在跑，引擎只写一份临时脚本、把命令行交给设备层，
CSV 那套编码规则管着脚本文件怎么写。`script.run` 的「脚本来源」选文件时，那份文件就地运行：引擎
只把它的路径放进命令行，不写副本、不替换占位符、不改编码，只有「保留窗口」那一种会另写一个两行的
批处理（`call` 那份文件 + 一行标记）交给窗口。另外三个动作——`script.python`、`script.csharp` 与
`script.ahk`——是引擎自己起进程跑的，三条都走 `IScriptDevice`（`ScriptDevice`）：脚本能读宏的变量、
按四字符标识跑宏里的某一步、往运行日志写一行、交回这一步的结果，走的都是同一条**命名管道**——两边
按行传 JSON，管道名经 `WHALEGENIE_PIPE` 交给子进程，请求在走宏的那条线程上就地回答（要跑的那一步
就当场跑），stdout 与 stderr 各由一条读线程收着逐行进运行日志，超时到点连进程树一起结束。

- **Python**：临时目录里写出脚本、桥模块 `whalegenie.py` 与引导用的 `boot.py`，`python -X utf8 boot.py script.py 参数`
  起来；`WhaleGenie` 从 `builtins` 里进去，任何模块都看得见。`packages\python` 永远在 `PYTHONPATH` 上。
- **C#**：引擎用 Roslyn 把脚本与注入的一份 `__InjectedServices`（一个 `public static dynamic DynamicObject`）
  一起编成 dll，再让**程序自己的 exe 起第二个进程**执行（`WhaleGenie.exe --script-host <dll> 参数…`）。
  `Program.Main` 在 Avalonia 之前分支：宿进程不建窗口、不占通知区域，脚本里的 `Environment.Exit`、
  栈溢出与死循环都伤不到主程序，超时也能直接杀进程。`ScriptDevice` 把本次运行的包根目录经
  `WHALEGENIE_PACKAGES` 交给子进程；宿主从 `<包根>\nuget\references.json` 里认编译时用到的那些 dll
  （`NuGetStore.References`），并把包里的 `runtimes\win-x64\native` 挂到 `PATH` 上，所以测试和发布可以用各自的
  包目录而不互相污染。
- **AutoHotkey**：AutoHotkey 的 `FileOpen` 打不开命名管道（实测会卡住），所以这一条不由脚本自己连管道，
  而是由**程序自己的 exe 再起一个进程**当桥：临时目录里写两份文件，`script.ahk` 是两行开头加用户正文
  （`#ErrorStdOut` 让语法错误写成错误输出而不是弹窗，`#Include` 把桥拉进来），`whalegenie.ahk` 是预置的
  桥——五个函数加一个 `OnError` 处理器，第一次用到时用 `ComObject("WScript.Shell").Exec` 起
  `WhaleGenie.exe --script-bridge <问> <答>`，一个脚本只起一份。桥那一支读一行请求（动词、名字、是不是
  数字、值），把它翻成管道上那套 JSON，再把回答写回另一个文件、在标准输出上说一声，AHK 侧看到 `done`
  才去读答案；两边的行号差由预置桥里的 `__OFFSET__` 补回来，所以报的错用的是脚本自己的行号。
  `Run.AhkNeedsV2` 只在版本号读得出来时才判（搜索路径上的转发程序读不到版本，就照跑）。

`packages` 目录跟着 exe 走，写不进去时退到 `%LOCALAPPDATA%\WhaleGenie\packages`；`python` 与 `nuget`
各占一格，脚本动作里因此不再列包。

`script.nuget` 当前按常见托管包验收：下载目标框架最近的 `lib` 程序集与传递依赖，并准备 Windows x64 原生库。
`ref`-only、分析器、MSBuild 目标、内容文件和只带其他平台运行时的包不在支持范围内，文档与失败提示不能暗示
它们可用。确定性测试用临时包目录验证编译与子进程加载；真实联网测试由 `WHALEGENIE_REAL_NUGET=1` 开启。

## 宏文件的形状

一个宏是一份 JSON（打包后放在 `nodes/<编号>-<slug>.json`，slug 由宏名生成），顶层节点分三块：触发与节奏（`triggerMode`、`schedule`、
`watch`、`process`、`window`、`recorder` 等，对应编辑器里触发那一页）、**调用声明**、以及 `steps`。声明是
这个宏对调用者的要求，形状如下：

```json
{
  "arguments": [
    { "name": "次数", "default": "3", "required": false, "description": "要做多少遍。", "type": "Number" },
    { "name": "标签", "default": "", "required": true, "description": "写在每一项前面的文字。" }
  ],
  "returns": [
    { "name": "答案", "description": "整理完留下的数。", "type": "Number" }
  ]
}
```

类型写的是 `Text` / `Number` / `Flag` / `List` / `Record` / `Image`；没写就是 `Text`。

两边都为空就写空数组，没在调用的宏因此不会多出什么。同一个声明在代码里有两份读法：引擎读的是
`WhaleGenie.Core/Execution/MacroSignature.cs` 里的 `MacroSignature`（名字、默认值、是否必填、类型），
编辑器与设计期检查读的是 `WhaleGenie/Models/MacroCallable.cs` 里的 `MacroCallable`（多一列说明，
调用处的表单拿它当字段的提示），两者都由 `MacroItem` 读写，形参与结果各带一个 `ActionReturnType`：
调用处按它说明要的是什么，设计期按它标出明显不符的值。

记录类型的形参与结果可以带字段表（`fields: [{name, type}]`，`MacroField`）：它是**新增可选字段**，不写就是
「没说」；因为旧包读进来只是空表、不会读错，所以没有升包格式版本。`get_macro_signature` 与 `list_macros` 都带出来。

字段表有三处读它，都只在**类型是 `Record` 且至少写了一个字段名**时生效：

- 宏接口页（`MacroInterfaceViewModel` 的 `MacroParameterRow.Fields` / `MacroResultRow.Fields`）：`Record` 那行
  展开一个两列的表（名字 + 类型），增删行直接写进 `MacroParameter.Fields` / `MacroResult.Fields`。类型换走再换
  回来时**不清空**字段：那是一次改正，不是扔掉已写的东西。
- 变量墙：`VariableChoices` 把声明里的字段填进 `VariableChoice.Fields`，于是变量墙说的是「这条记录有什么」，
  而不是只有「这是一条记录」。
- 设计期检查：`MacroCheck.CheckFieldNames` 把每个值当公式读（整值一次，文本里每个 `${…}` 再一次），找到
  `field($记录, "名字")` 这种调用就对着字段表核对，对不上报 `UnknownField`，消息里带该记录声明过的字段。
  一条记录没写字段表就**不查**——对着没人写过的名字查等于猜。

字段的读法只有 `field($记录, "名字")`（`ExpressionFunctions` 的 `field`）：`$记录.名字` 不是宏语言的写法，
它会被当成一个没定义过的变量名。所以检查与变量墙都对着 `field(...)` 做，而不是对着点号做。

形参与结果的**顺序**就是声明里的顺序，也是一次调用的表单与运行调试「形参」段列它们的顺序；接口页有上移/下移
（`MoveParameterUp` / `MoveResultDown` 等），移动同时改行集合与 `DeclaredParameters`，所以画出来的和存下去的
始终是同一个顺序。第一行没有上移、最后一行没有下移，按钮据此灰掉。

「提为子宏」按 `MacroCheck.TypesOf`（与设计期检查同一份推断）给新宏的形参与结果定类型：读到的名字原来是什么
就声明成什么，什么都不知道就是 `Text`。**必填仍是必填**——那次提取写出的调用一定把每个参数都传了，声明成可选
只会让以后有人删掉那一行时变成静默的空值。新建形参的「必填」默认也统一成 `true`，与模型和文件缺省一致。

声明名是变量名：首字符是字母、数字或 `_`，其余是字母、数字、`_`、`.`；变量在值字段里总带 `$`，所以数字开头
不会和数字字面量混淆，`$123` 读的是变量。空名、读不出来的名字、形参内或结果内重名、`sys.` 与
`step.<标识>.outcome` / `.error` 这类运行保留名，都在保存与写包前拦住。形参不能
同时是必填又有默认值（`RequiredWithDefault`）。空名字的行不丢掉：`FromJson` 与恢复快照会把它带回来，编辑器
显示声明问题，包写入则拒绝有致命声明错误的宏。被调用宏开始时先把形参写入局部表，再把尚未存在的声明结果初始化为
空；形参与结果同名时保留形参值（in-out）。默认值留在声明里，不写进调用步：调用表单没填的行不写进 `arguments`，
运行时才取默认值。宏接口里默认值一行的「表达式」「变量」复用 `ExpressionBuilderWindow` 与
`VariablePickerWindow`，名单来自 `VariableChoices.For([])`（全局与系统）：默认值在调用方作用域求值，调用方的
局部变量只能手写；变量墙在没有局部变量时初始选中「全部」，否则选择器会停在一个空页上。

宏名唯一由两条入口保证，都不是「报错让人改」而是「给后到的加日期」（`MacroNames.Free`）：
`MacroPackage.Load` 在 `triggers.Attach` 之前把同一个文件里的重名宏改成 `名字 (yyyy-MM-dd)`（再撞加 ` 2`），
`MainViewModel.AddMacro` 在名字已被项目占用时做同样的事——导入包撞名走的是这一条。改名同时改该宏的
`Triggers`；`ImportPackage` 还会把包里那张触发器表里的名字一起改（`contents.Triggers.RenameMacro`），
否则后面的 `MergeMenuFrom` 会把导入的菜单项挂到留下的那个宏上。先有的那个保留原名，因为文件里的调用和
触发器都是按它写的。`ProjectMacroLibrary` 的 `TryAdd` 因此只是最后一道闸，正常路径上撞不到。

包里的清单固定叫 `manifest.json`（小写），`manifest.json` 的 `version` 是包格式版本，当前是 20。
加载器只接受这一版，旧包会被明确拒绝，而不是让旧参数静默落到默认值。P1 把 `pollMs` 并进
`intervalMs` 时升到 6；P2 把 `useRecord` / `record` 换成 `place` / `rect` 时升到 7；P3 给形参带上类型时升到 8；F3 把`runMacro`的`wait`开关拆成`runMacro`与`startMacro`时升到 9。
触发器入口从宏文档移进项目级的 `triggers.json` 时升到 11；字串入口增加 `endChars` 时升到 12；
菜单增加标题、行列格子与每项标签，界面组增加显示/隐藏快捷键时升到 13；菜单改为固定 4×6
通用/窗口规则页、图标和单一呼出键，字串结束键改为实际字符时升到 14；菜单移除自定义标题并把每条窗口
规则升级为多页时升到 15；父宏开始携带 `subMacros`、说明和公共宏来源记录时升到 16；条件动作收口为整值布尔
表达式、`try.catchWhen` 改成表达式时升到 17；菜单移除逐格标题、图标键改成完整 Lucide 枚举名并增加禁止
呼出规则时升到 18；删掉 `file.readText.storeAs`、`vision.getPixel.asHex`、识别文字的三个比较开关与
`recheckMs`，并把启动程序改交 `program` 记录时升到 19；调用步的实参从散键（`arg.<名字>` 与
`arg.<名字>.given`）改成行列表 `arguments` 时升到 20——**形状变了就必须升版**，旧包的 `arg.*` 会被读成
「这个动作没有这个字段」，所以 19 的包直接拒绝，而不是读一半。
触发入口按名称引用宏，宏改名时同一份
`ProjectTriggers` 改写所有引用，保存、恢复和 MCP 草稿都不允许悬空引用。

父宏的内嵌子宏写在同一个宏文档的 `subMacros` 数组里，只保存名称、说明、形参、返回值、互斥设置、步骤和
可选的 `origin { id, name, version }`；它们不进入主窗口的宏列表，但会由编辑器与运行库和父宏、项目宏一起
扁平化。父宏、子宏与项目宏的可调用名忽略大小写后必须唯一。公共宏库是另一个原子写入的包，位置固定为
`library/public-macros.wgmacro`，资源解压到相邻的 `public-macros.assets`；它使用独立格式版本 1。拖入时
复制可调用内容并记录来源版本，之后不跟随来源更新。

`ProjectTriggers` 是触发入口的唯一真源。`TriggerKind` 只有 `key`、`color`、`interface`、`hotstring`、
`menu`、`timer`、`file`、`process`、`window`、`idle` 十个词。编辑器把每条入口按类型写成同一份
`TriggerEntry` JSON；`MacroItem` 上的旧触发字段只作为写入前兼容投影，不参与包里的触发判断。界面宏按
窗口规则的规范化键自动分组，`InterfaceLayout` 保存目标客户区内的位置与本组显示快捷键；同一规则下的全部
宏共用一个悬浮窗，不同匹配规则各自保留一个悬浮窗。拖动只在松开后写一次布局。`MacroTriggerService` 从
触发入口分派，但一个宏同时最多只占一条运行。

菜单配置属于项目：固定为 4 列、上下各 3 行，格子为 80×80。`MenuPage` 分为最多 8 个通用页、默认窗口页和
按顺序保存的窗口规则页；同一窗口规则可拥有最多 8 个下区子页。菜单入口的 `MenuPageId`、`MenuRow`、
`MenuColumn` 与 `MenuIconReference` 指定页面、位置和图标；格子文字只来自 `MacroItem.Name`，提示同时显示
`Description`。`BlockedMenuRules` 单独保存按进程名精确匹配的禁止呼出规则，在进入 `MacroTriggerGate` 前检查。
启用项撞格、悬空页面或缺失宏会阻止保存；同一宏允许在不同格子重复出现。

普通运行态使用 `WS_EX_NOACTIVATE`。搜索框始终保留一行布局高度，关闭时只取消可见性、命中测试与焦点；搜索
按钮展开后才临时移除该样式并把焦点交给输入框，收起或失焦后恢复并把前台交还原窗口，窗口尺寸不变。方向键、
在菜单内按下其他位置会延后到本次点击完成后收起搜索，窗口失去激活时立即收起。
Enter 与 Esc 通过全局钩子处理，但置顶菜单不截取其他程序的
键盘输入。主窗口用全局鼠标坐标判断菜单外点击，置顶菜单忽略外部点击；只读取不可变屏幕边界，不访问
Avalonia 控件。菜单定位时让中间分隔条中心对齐指针的纵向位置，搜索展开后按新增高度重新对齐；工作区边缘
只做夹紧。格子右键的四项动作始终可用，长按 400 毫秒后拖动才交换格子。
右键菜单作为主菜单的受保护弹出层，打开期间不触发外部点击关闭；两个分页区各自保留第 1 页。
图标选择器使用显式 `VirtualizingStackPanel`，并把结果硬分页为每页最多 140 个图标；搜索防抖 180 毫秒，
每次只替换当前页的 20 行模板。虚拟化回收期间数据模板必须容纳短时 `null` 行，这由菜单图标选择器测试守住。

字串识别器只读全局键盘钩子送来的字面字符，最多保留 40 个字符；它处理常见热字串的结束键、大小写、词中、
`B0`（保留输入）和 `O`（舍弃结束键），命中后同步退格并重放结束键，再启动宏一次。菜单入口在项目里显式列出，
`MenuLauncher` 只保存一个键盘组合、鼠标中键或鼠标 4/5 侧键；左键、右键和滚轮不用于菜单。菜单打开时进入
`MacroTriggerGate`，点击宏后关闭并恢复前台再运行一次。
每个字串入口的 `endChars` 保存实际字符：空格就是一个空格，换行与 Tab 由 JSON 的标准转义承载，反引号也是普通字符。钩子和计时器只能读取
UI 线程发布的 `TriggerRuntimeSnapshot`；全局快捷键只读 `GlobalHotkeyPlan`，二者都不访问 Avalonia 控件或
可变 UI 集合。所有后台回调经过异常隔离，重复故障按阶段节流写入 `logs/runtime.log`。

钩子回调里凡是回答「这个键是不是人按的」，一律先问 `WhaleGenieInputGate`，它记的是这个键**回来时**叫什么：
`KeyNames.AlsoSeenAs` 里的第二名字就是为此存在的——小键盘在 NumLock 关闭时报的是 Home / End / 方向键，
只记 `NumPad7` 就会把自家的按键当成人的按键，一个跑着按键的宏会把绑到 Home 的系统开关打起来、把自己停下。
发送侧（前台注入与驱动级注入）都按 `HookKey` 记，不按名字记。

**前台按键自己拼输入事件**（`WindowsKeyStrokes`）：一个按键同时带 Windows 键码与扫描码，**扩展位**才是把两个
共用一个扫描码的键分开的东西——NumLock 的扫描码 `0x45` 就是 Pause 序列 `E1 1D 45` 里的一个字节，少了扩展位
NumLock 在 Windows 这边照样翻转指示灯，到了浏览器却报成 Pause（`0x45` 无扩展位在 Chromium 里就是 VK_PAUSE）。
所以键码、扫描码、扩展位三样都从 `WindowsKeys` 取，交给 `SendInput`；`WindowsKeys.IsExtended` 是这两条发送
路径（注入与投消息）共用的唯一一张表，不再各写一份。

调用另一个宏的步骤（`control.runMacro`）写三个部分：

```json
{
  "type": "control.runMacro",
  "params": {
    "macro": "整理",
    "arguments": [
      { "name": "次数", "value": "$n" },
      { "name": "标签", "value": "开始" }
    ],
    "returns": [ { "key": "答案", "name": "答案" } ]
  }
}
```

`macro` 是被调用宏的名字。`arguments` 一行一个形参：`name` 是被调用宏声明的名字，`value` 是传过去的值，
`given` 为 `true` 表示「这一格是空的，但确实要传空」。两者是同一套行列表，列名由目录声明（`returns` 是
`key` / `name`，`arguments` 是 `name` / `value` / `given`），界面用的是同一个行编辑器，补丁也按行改。
**没有行就是没给**：可选形参用默认值，必填报 `Run.MissingArgument`；有行但 `value` 为空且没勾 `given` 算同一件事，
所以编辑器不为没填的行写任何东西。`returns` 一行一个要取回的结果：`key` 是被调用宏那边叫这个名字，`name` 是本宏
这边落在哪个变量里。引擎按被调用宏声明的形参逐个读，并从 `returns` 的每一行取回一个值（名字没被声明过报
`Run.UndeclaredResult`），要取回什么在调用开始之前就说清楚，出错因此发生在送输入之前。步骤里带着一个被调
宏没有声明的名字报 `Run.UndeclaredArgument`，两行写同一个名字报 `Run.DuplicateArgument`，一行既填了值又勾了
「传空值」报 `Run.GivenWithValue`——三者在被调宏开始跑之前。

形参名属于被调用宏，目录里没有：目录只声明「这个动作的参数来自被调用宏的声明」（`SignatureSource`），
`Control.runMacro.arguments` 的列也只是 `name` / `value` / `given` 这一套骨架。真正读名字的三处——编辑器参数页
（`AddActionViewModel.FillCallArguments`）、`whalegenie_get_macro_signature`、设计期检查——都从被调用宏的声明取，
把这几处收成一个投影函数是 M5 剩下的最后一件事。结果变量在每次
尝试开始时先清空；同步调用失败或后台等待失败时一个结果都不写。F3 会把同步调用与后台启动拆成
`control.runMacro` / `control.startMacro`；后台句柄是记录，结果只在 `waitMacro` / `waitAll` / `waitAny`
成功后写，`waitAll` / `waitAny` 失败时失败的那次运行的句柄仍留在表里，可以再用 `runStatus` 查看或
`waitMacro` 逐个等待。顶层的运行调试与触发器没有调用者，`MacroRunner.StartSignature` / `StartArguments` 把同一份
声明种进运行自己的表：调试窗口提供 `MacroStartArgument`，触发器不提供、默认值照用；两者都与调用共用
`ReadArgument` 和 `MacroValues.Fits`。必填缺失在 `RunAsync` 进入步骤前以 `Run.MissingArgument` 结束并写失败日志。

## 改一个动作的固定顺序

按这个顺序改，能保证目录、引擎、文案、测试与手册说的是同一件事：

1. **先查旧名字与旧形状**。在目录、`MacroRunner`、`MacroCheck`、手册与测试里搜字段名、动作键、记录字段和
   同义概念；已有一个名字能表达，就不要再造一个近义词。
2. **改语义目录**。动作键、分类、参数名、值形状、单位、空值含义、读法、拾取器、默认值、必填与它的失败键、选项与它的回退、是否发键鼠、等待与
   超时来源、形参与结果的来源（`SignatureSource`）、返回形状与记录字段都写进 `ActionSemantics.json`；
   界面展示用的标签、提示、编辑器种类、页面归属与图标不参与引擎判断。调用另一个宏时，形参与结果来自
   被调用宏的声明；这份来源是目录事实，界面与引擎不得再按 `control.runMacro` / `control.startMacro`
   动作键自行判断。
3. **改引擎分派**。`MacroRunner` 只按目录取参数与事实；不按 `type` 前缀推断读法或是否发输入；数字的范围也由目录声明，读值入口按同一个范围报 `Run.OutOfRange`。位置/矩形按
   `Point` / `Rect` 收记录，两种写法只用目录里的字段说；记录字段一律走 `FieldNames`。一个动作的分派与
   实现都写进它家族的那个 `MacroRunner.<Family>.cs`。
4. **补失败键**。新键同时进 `RunKeys` 与手册的「可以分支的失败原因」表；清单由常量表生成对账；
   日志行用 `Log.*`，不要借用 `Run.*`。
5. **补文案**。`Strings` 的英文与中文、`ActionStrings` 的中文同时加；标签说字段是什么，提示第二句说什么时候
   要改它，数字写范围、单位与默认值。整值读的字段在标签前带 `ƒ`，只做替换的字段由提示第一句写明。
6. **补页面与返回值**。参数落在基础页还是高级页由 `ActionParameter.Advanced` 决定；能交出的结果进返回值页，
   记录结果还要把字段表写进目录；`ActionPageTests`、`ReturnsPageTests` 与 `LocalizationTests` 同时更新。
7. **补引擎用例**。至少一条用例走到新动作；再加契约断言：字段按声明的读法/形状/单位/空值读、会发输入的动作
   被标真、返回键与记录字段和返回值页一致、失败键在台账里、像素坐标能负、失败/重试后结果为空。
8. **删旧形状**。被取代的参数、值、开关、文案键、测试与手册段落同一版删掉；不写双读，不留迁移分支。
   改字段形状或记录字段时升包格式版本并拒绝旧包。
9. **手动项**。移动鼠标、抓屏取色、注入按键、UIA / 页面拾取、录制、窗口布局这些真实桌面行为，按
   `AGENTS.md` 规范 5 手动过一遍，结论写进提交信息；无头能覆盖的不要留到手动。
10. **收尾**。更新手册、依赖表与 `THIRD-PARTY-NOTICES.md`（若新增依赖），从 `roadmap.md` 删掉已完成的阶段，
    跑三项全绿。

## 测试分层

| 层 | 项目 | 验证内容 |
| --- | --- | --- |
| 纯逻辑 | `WhaleGenie.Core.Tests` | 引擎每一步、变量与表达式；跑在替身设备层上，断言的是"引擎要求了什么"，不是"桌面上发生了什么"。后续的字段读法、是否发输入与返回形状的契约也在这里验 |
| 动作矩阵 | `WhaleGenie.Core.Tests/Bench` | 目录声明的 176 个动作逐个按声明合成一步、在替身设备层上跑一遍，核四件事：结局、返回值形状、副作用声明与设备账本是否一致、有没有参数给不出值。它只量不改，缺的东西显式写进报告，不静默跳过 |
| 画面获取 | `WhaleGenie.GraphicsCapture.Tests` | 选路次序、会话缓存与复用、图片落位、矩形越界；真正调用 Windows 取画面那几条按手动用例过 |
| 界面 | `WhaleGenie.Tests` | 参数成型、页面归属、返回值页、触发器比对、卡片显示、文案完整性与文档对账，宏包往返，无头会话里的界面逻辑与 Avalonia 输入（`Ui.Run` 是入口），以及拾取器与录制器交来替身设备层后的情形 |
| 真的驱动别的程序 | `BrowserTabTests`、`ScriptBridgeTests`、`ScriptHostRunTests` | 开一个不显示窗口的 Edge，跑浏览器动作；真的起一个解释器跑一段脚本，看它读值、写值、跑一步与交回结果走的是不是那条管道（机器上没有 Python 时跳过）；`WHALEGENIE_REAL_NUGET=1` 时额外下载真实包并验证编译和运行 |
| 手动 | — | 真实输入、抓屏取色、UIA 拾取、页面拾取、录制、窗口布局，以及驱动级输入：真机装一次 usbip-win2 驱动，勾上/不勾「启动时先把虚拟键鼠装上机器」各跑一次运行，键鼠四种动作（点击、打字、拖拽、滚轮）与手柄各来一次，退出程序后看设备管理器里虚拟设备是不是没了；三种屏幕拾取器（框选区域、取色放大镜、元素拾取）各进出一次：打开时鲸灵自己的窗口（含问话的那个对话框）都不在屏幕上，拾取器一开就拿到焦点、Esc 与回车与右键当场有效，拾完窗口按原状回来——其中一次在添加动作窗口里拾，看它自己也回来（它是编辑器的属主窗口，收起时跟着编辑器一起走）；框选器打开时屏幕是原样、指针看得见（底图定格在那一刻），一拖出框才框外压暗、框内照原样，框选之后截一张，图片里没有鲸灵自己的窗口；识别结果与偏移拾取窗口的标题栏都能拖着走（标题栏拖动是手动那一档，无头会话里只验到接没接线）；YOLO 检测的三步各跑一次（自备一份公开的 yolov8n.onnx 与类名表）：检测物体一次看框落在东西上、等待检测到物体在一段限时里等到与等到超时各一次、点击检测到的物体看落点在框中心加上偏移，并记下一次检测的耗时；脚本动作的三种窗口模式各跑一次（窗口留着/关着/不显示）、批处理、PowerShell 与 AutoHotkey 各一次，AutoHotkey 那一条还要在一台没装它的机器上看一次报错；Python 脚本与安装 PIP 包各一次（装的包能 import），C# 脚本与下载 NuGet 包各一次（装的包能编译进脚本），断网时看两个依赖动作各报什么；表达式编辑器里把变量、函数与树上已有的一支拖到位置上（拖到空位＝填入、拖到已有的一段＝换掉、拖自己的一支进自己的后代＝不理会、拖到窗口外＝取消，落点要跟着亮；1080×720 下四段分区与「翻译结果」一行排得下、滚动正常，函数卡片按分类 dock 只剩一组、颜色六个组两两不同、悬停气泡出得来，深嵌套公式的那句人话读起来顺） |

界面用例走的是 Avalonia 自己的输入管线，不是系统里的鼠标与按键：真的注入按键、真的抓屏取色这类事情在
无头会话里验不了，只能按手动用例过。因此**能在无头会话里跑到的部分要有用例**，只留人眼能判的那一档给
手动。

手动项在提交信息里按环境（Windows 版本、DPI、显示器数量、Edge/Python/AutoHotkey 版本）、步骤与观察结果
三项记录；只写“已手动过”不算结论。环境不同就重跑受影响的那一项，不要把一次通过当成所有机器都通过。

变量持久化保存在当前用户的 `%LOCALAPPDATA%\WhaleGenie\persistent-variables.json`，只保存勾选后的全局变量名和值；
宏包仍只保存变量声明。全局声明由 `GlobalVariablesWindow` 和 `control.setVariable` 共用
`VariableDefinitionDraft`：动作窗口保存时提交该步骤的全局定义，管理中心整窗保存时才替换
`VariableCatalog.Globals`。默认值走 `VariableValues`，与动作整值字段使用同一表达式语法，并在
`MacroVariables.Seed` 中按声明顺序计算；当前全局值只在用户改过时写回，避免覆盖同时运行的写入。
管理中心按当前草稿重新生成表达式变量名单，只列系统变量和当前行之前的全局声明；编辑字段不会立即重算
搜索筛选，避免正在编辑的行因改名或切换持久化而消失。无效保存会清空筛选、选中首个问题行并聚焦对应字段。
关闭、Esc 与取消共用未保存确认；删除也先确认，但两者都只改变草稿，真正提交仍只由保存完成。
`VariableRename` 用表达式/插值扫描器同步直接字段与引用，字符串、记录字段和脚本文本不猜改。运行调试页只显示值。
`VariableBag.Changed` 与
`VariableCatalog.GlobalsChanged` 都必须让已经打开的变量中心与运行页刷新；共享值写入仍由通知合并，退出前刷新。

拖放编排还按一项真实桌面用例验收：从四个 Dock 各拖一次到空列表、行前、行后和单列表块内；确认高亮插入带
写明落点、拒绝落点、动作窗口拖动与缩放、取消不插入、保存后落点正确；再验证保存按钮不关闭编辑器、
连续保存不重复添加宏；最后把同一公共宏同版本拖两次、改出一版后拖一次，
确认复用与新建副本的页签和来源版本；重启程序后确认公共宏库仍可列出。无头测试只验事件与结果，不替代这一项。

每加一个动作至少有一条引擎用例走到它；用户可见文案由 `LocalizationTests` 卡住两边齐全。

## 契约测试

目录是唯一真源，所以下面这些事实都要有对账用例；新增事实时先加断言，再改实现：

| 契约 | 用例 | 断言什么 |
| --- | --- | --- |
| 字段读法 | `ExpressionFieldTests` + `ActionContractTests` + `ValueReadTests` | 每个参数声明为整值读、只替换或写变量名，拾取器种类也对得上；引擎用 `ReadValue` / `ReadText` 按声明读，设计期检查与 `VariableText` 同一口径；`${名字}` 只读到右花括号，括号里不是名字时按表达式算；每个可执行键都有分派与至少一条引擎用例 |
| 值形状/单位/空值 | `ActionContractTests` + `ValueReadTests` | 每个参数声明一种形状、一个单位与一种空值含义；引擎与设计期按同一处读；`Pixels` 允许负数，`Milliseconds` 从 0 起；没有 `pollMs` 这类同义字段 |
| 坐标范围 | `ActionContractTests` + `ActionPageTests` | `x` / `y` / `left` / `top` / `startX` / `endX` 允许负数；`width` / `height` 至少 1；像素字段不继承时长的 46 小时上限 |
| 参数约束 | `MacroRunnerTests` + `ActionContractTests` + `MacroCheckTests` | 数字超出目录的 `MinimumValue` / `MaximumValue` 报 `Run.OutOfRange`（只算 `Shape = Number`）；必填且没有默认、没有空值含义的字段空着报它自己的 `MissingKey`（默认 `Run.MissingField`，路径 / 程序 / 句柄保留原键；`Conditional` 的字段不在此列）；选项不是目录里的词报 `OptionKey`（默认 `Run.BadOption`，`AllowsUnknownOption` 的窗口匹配方式与 `tabHow` 保留回退）；设计期用同一规则标出 `MissingField` / `BadOption` |
| 位置与矩形 | `ActionContractTests` + `ActionPageTests` + `ActionDialogTests` | 每个位置字段声明 `Point`、每个矩形字段声明 `Rect`；目录里没有 `useRecord` / `record` 这类开关与第二格；记录格与数字格互斥，框选写数字时清空记录格；记录进数字格报 `Run.RecordWhereNumber` |
| 记录字段 | `DeviceActionTests` + `ActionContractTests` | 引擎写出的记录字段与目录 `Fields` 一致；每个字段名都在 `FieldNames` 里；手册的记录字段表与目录一致 |
| 类型检查 | `MacroCheckTests` + `MacroRunnerTests` | 记录进数字格、文本进位置格、记录进只收文本的字段、调用实参与形参声明不符、常量实参与默认值明显不符，设计期标出；运行时实参报 `Run.BadArgumentType`、结果报 `Run.BadResultType`；空值对所有声明类型合法；窗口字段收记录不算错 |
| 行列表 | `ActionContractTests` + `MacroRunnerTests` + `MacroCheckTests` | 每个 `Rows` 字段在目录里声明列与角色（`returns` / `region` / `narrow` / `images` / `keySequence`）；运行与设计期都拒绝没有的列与缺掉的必填列，报 `Run.BadRowColumn` |
| 声明契约 | `MacroSignatureTests` + `MacroCallTests` + `MacroRunnerTests` + `MacroCheckTests` | 空名、读不出来的名字、形参 `.given` 后缀、重名、运行保留名、必填+默认值都在保存/写包/调用前拦住；空声明经文件与恢复快照保留；宏自己的声明问题由 `MacroCheck.DeclarationProblems` 列在编辑器步骤列表上方，致命项阻断保存，结果未写与默认值类型只提醒 |
| 声明可见 | `VariableWallTests` + `VariableFieldTests` + `MacroCallTests` + `RunDebugTests` | 形参/结果进入变量墙、`$` 建议与运行窗口局部变量；Record 形参能被位置/矩形字段识别；宏内读声明结果不报未定义 |
| 直接运行 | `MacroRunnerTests` + `RunDebugTests` | `StartSignature` / `StartArguments` 与调用共用 `ReadArgument`；可选默认值生效、必填缺失在第一步前报 `Run.MissingArgument`、显式空值合法、声明结果初始为空；调试窗口的必填行拦住运行按钮；触发器直接运行使用默认值 |
| 结果交付 | `MacroRunnerTests` + `MacroCheckTests` | `waitAll` 失败不消耗已成功句柄；`waitAny` 失败保留失败句柄；`cancelMacro` 不结算结果，取消后读结果仍标 `ResultBeforeWait` |
| 是否发输入 | `ActionContractTests` | 每个会调用 `Input(step)`、回退成真实鼠标点击或按快捷键的动作在目录里标真；`MacroCheck` 与 `InputGate` 查同一处，并沿宏调用图看可达步骤 |
| 返回形状 | `ReturnsPageTests` + `ActionContractTests` + `MacroRunnerTests` | 返回值页的每一行、引擎写出的键与记录字段一致；未知结果键与重复写入名在运行前报 `Run.BadResult`，值形状不符报 `Run.BadResultType`，空值表示这次没有交出 |
| 失败键 | `FailureKeyTests` + `MacroCheckTests` + `MacroRunnerTests` | `RunKeys` 与手册表一致；用户自定义键只接受 `User.<合法变量名>`；日志键、状态键不与失败键混用 |
| 页面归属 | `ActionPageTests` | 一个参数只属于一页，基础页为空时不显示 |
| 文案 | `LocalizationTests` | 中英文键齐全，参数与选项都有中文 |
| 字段名 | `FieldNameTests` | 同一字段名只有一种 `Kind`、一套选项与一套标签；记录字段也走 `FieldNames`；例外要写进白名单与理由 |
| 步骤落点 | `StepPlacementTests` + `MacroCheckTests` + `MacroEditorWorkspaceTests` | 新增、拖动、粘贴、组合、提取共用目录里的 `ChildKeys` 与 `Hidden`；`cases` 只收 Case，Case 不能离开多分支；拒绝不产生部分改动或撤销项，非法载入树在步骤行报错 |
| 循环与结果 | `MacroRunnerTests` | 嵌套循环的 item/index/counter 恢复外层；四种循环的 `ended`；失败/重试后结果为空 |
| 等按键 | `DeviceActionTests` | `input.waitKey` 只认钩子送来的匹配的键；`event` 选按下/松开；`swallow` 只拦结束等待的那个键；键名留空等任何键；超时按步骤超时 |
| 互斥与调用图 | `MacroRunnerTests` + `DeviceActionTests` + `MacroCheckTests` | 子宏自己的互斥声明生效；调用图分析带环检测；持浏览器门时启动浏览器后台运行被拒；`control.holdInput` / `control.holdBrowser` 的块内步骤不再取门，嵌套只记层数；`control.runStatus` 不等待取状态并刷新句柄；同一宏的后台运行有上限 |
| 表达式函数 | `FunctionTableTests` | 函数表与手册「表达式函数一览」一一对应 |

新增行时先加断言，再改实现。

这些测试尽量遍历目录或扫描源码，不手写第二份动作清单；白名单只用于已经说明理由的例外（如一个字段名只在
两处有不同选项，而选项由动作给出）。契约测试红一次要能说明是目录、引擎、手册还是测试哪一边落后，而不是只报一个总数。

**保留同名**（同 `Kind`、`Shape`、读法；选项或标签随动作变化，理由写在这里）

| 名字 | 保留理由 |
| --- | --- |
| `anchorMode` | 识别方式只有屏幕与窗口两档，是基础设置第一行；窗口规则紧随其后且只在窗口档显示 |
| `body` | 都是子步骤列表；标签按容器叫“试运行 / 接住 / 收尾 / 分支”，名字不必跟着容器换 |
| `button` | 手柄按键与鼠标按键共用“哪个键”这个词，但选项表由动作给出 |
| `color` | 都是颜色；标签统一为“颜色” |
| `confidence` | 都是匹配可信度；标签统一为“置信度（%）” |
| `dataText` | 都是要编码、解码或摘要的文本；标签说明这一次拿它做什么 |
| `encoding` | 都是文本编码；读取脚本时另多一档自动 |
| `elementState` | 都是页面上元素的可见或存在状态 |
| `folder` | 都是文件夹路径，都是文本框；标签说明它是工作目录、解包位置还是下载位置 |
| `force` | 都是“仍然继续”；标签说明继续的是什么 |
| `from` | 都是来源路径；标签说明复制、移动还是解压 |
| `height`、`width` | 都是长度；标签说明方向 |
| `holdMs` | 都是按住多久 |
| `inputMode` | 都是键鼠输入方式；相对移动只提供前台与驱动级，不提供后台 |
| `intervalMs` | 都是间隔；标签统一为“间隔”，提示说明隔开的是什么；原来的 `pollMs` 已并到这里 |
| `items` | 都是初始项或要遍历的项 |
| `matchMode` | 都是文字比较的五档词，改名后只剩这一个用途 |
| `maxRows` | 都是最多读多少行 |
| `message` | 都是给人看或问的一句话，都是多行文本框 |
| `name` | 都是要写入或使用的变量名；标签说明它是计数器、列表还是变量 |
| `orderBy` | 都是“有多处命中时选哪一处”，随机只出现在适用处 |
| `path` | 都是文件或文件夹路径；标签说明用途 |
| `points` | 都是颜色点列表 |
| `queryText` | 都是要在屏幕上找的文字 |
| `recurse` | 都是“连里面一起”；标签说明删子项还是列子文件夹 |
| `repeat` | 都是重复次数 |
| `separator` | 都是分隔符；读取时另多一档自动 |
| `skipBlankLines` | 都是“空行不算”；标签说明读取时跳过还是写入时留下 |
| `to` | 都是目标路径；标签说明复制、移动还是压缩 |
| `trim` | 比较前去掉两端的空格；只在「等于 / 不等于」出现，默认开。读文件时的整行或整格去空格另叫 `trimLines` / `trimCells` |
| `value` | 剩下的都是要存或要算的一个值，同为表达式控件；标签说明它在动作里的角色 |
| `writeMode` | 都是覆盖还是追加；两种写入的选项文案各说各的对象 |
| `x`、`y` | 都是坐标；标签说明是左上角还是单点。字面量写法仍用这两格，记录写法走 `place` / `rect` |

整套通常一分钟上下：界面那套最长，约一分钟；引擎十二秒上下，与它同时跑；画面获取那一套不到一秒。界面那套串
行（无头会话只有一条界面线程），单条中位数三毫秒，等待系统通知的只有文件监听三条。这几个数在本机这么量：
跑一次 `dotnet test WhaleGenie.slnx -c Release`，看每个工程那一行末尾的持续时间；换机器以后重量一遍。

测试失败就是失败。出现偶发超时或整套红时，先复现、隔离并开一条修复项，不能把“偶尔十分钟或整套失败”当成
无头会话的常态，也不能以“用例本身仍全部通过”为由放行；修好之前这一项不作为发布依据。

文案是两份手写的表（`Strings` 的英文与中文、`ActionStrings` 的中文），
`WhaleGenie.Tests` 里的本地化用例会检查键是否两边齐全、动作的每个参数和每个下拉选项是否有中文。

换语言时，XAML 的 `[Key]` 绑定由 `Strings` 的变更通知自动刷新；代码里算出来的文案由
`ILanguageAware.RefreshLanguage()` 重新读，打开着的窗口由 `LanguageRefresh` 统一走一遍。新增一个
把文案缓存成字段的视图模型，要覆盖 `RefreshLanguage()` 把这些字段重新算出来，或改成按需读的
计算属性；不要把会跟着语言变的文字存成只读字段。

第一次启动、或 `settings.json` 里没有可读的语言时，界面跟随 Windows 显示语言
（`CultureInfo.CurrentUICulture`）：`zh` 开头的系统用中文，其余用英文；设置里选过以后以存储值为准。

## 代码约定

- 格式与风格写在 `.editorconfig`，CI 会跑 `dotnet format --verify-no-changes` 卡住不合规的提交。
- 每个项目共用的构建属性写在 `Directory.Build.props`，单个项目只留自己特有的部分。
- 注释写"为什么"，不写"做了什么"；用户能看到的文字一律走文案表，不写死在代码或 XAML 里。
- 模态窗口的答案由窗口自己留着，不从 `ShowDialog` 的返回等：屏幕拾取器在出场的整段时间里把鲸灵自己的窗口
  全部收起来，问话的那个对话框也在内，而隐藏一个模态窗口会让 Avalonia 立刻以空值结束那一层模态循环——
  对话框还在屏幕上、步骤还没写完，等循环的人就已经走下去了，随后保存的步骤无处可去。`ActionEditorWindow`
  因此把答案放在 `Answer` 里，打开它的地方等这一份；新增"会收起鲸灵窗口、又要把结果交回去"的窗口照这个来。
- 动作窗口的「运行」与字段旁的测试同拾取器一套规矩：`AppWindows.StepAside` 收起全部窗口、`ScreenComposed.Wait()`
  等桌面重画，再读屏幕或发输入，结束时按原状回来。只最小化本窗口不够——属主编辑器还在它后面挡着要被试的那块画面。
- 界面只认引擎交出的接口：屏幕、UIA、窗口表、录制器、全局钩子、驱动级输入与浏览器都由组合根
  （`WhaleGenie/Execution/SharedDevices.cs` 与 `App.axaml.cs`）交来，窗口、视图模型与拾取器里不出现
  `WhaleGenie.Core.Devices.Platform`——`LayeringTests` 扫源码卡住这一条，替身设备层也就够驱动它们。
- 字段名是文件格式：概念在前、角色在后；同一个名字只能有一种 `Kind`、一套选项与一套标签。新增参数前先搜
  同义名，宁可改旧名，也不再加一个近义词。记录里的字段名同样是文件格式，统一走 `FieldNames`。
- 目录声明字段读法、值形状、单位、空值含义、是否发输入、返回形状与记录字段；引擎与设计期检查不得各判一次，
  更不得按 `type` 前缀推断。位置/矩形字段按 `Point` / `Rect` 收记录，两种写法互斥，由界面保证只填一种。
- 写名字的字段只列这个动作真正会写的名字：有 `scope` 的列本宏与共享，没有的只列本宏；不能让用户选了共享
  名字却只得到局部影子。
- 每一步开始时清空它勾选的结果变量；步骤没写就是空，不是上一次的答案。失败、重试、循环都不带旧值。
  同步调用失败不带回半截结果，`waitMacro` / `waitAll` 失败时一个结果都不写：两边规则一致。
  循环体每一轮开始时把体里各步骤的结局先置回 `skipped`，这一轮没跑到的步骤就是当轮的 skipped。
- 全局变量是一份 `SharedVariables.Current` 交给所有运行，普通写入最后写入者胜，`control.incrementGlobal`
  在同一把锁里读写；浏览器步骤另取 `BrowserGate`，与 `InputGate` 分开；脚本的「会发键鼠」只作声明，
  整段脚本不取输入门。
- 后台运行是同一套 `MacroRunner` 的另一条运行：局部变量独立、全局表共享，父运行的取消令牌传给子运行；
  `control.waitMacro` 取回调用处勾选的结果，父运行结束时还没等的子运行由 `StopChildrenAsync` 停下。
  句柄是记录，等完或取消后改写成结束时的样子；后台运行的嵌套类型在 `MacroRunner.Children.cs`。
- `MacroRunner` 按动作家族拆成 partial：主文件放状态、运行循环与按 `Category` 分派的入口，每个
  `MacroRunner.<Family>.cs` 里放**该家族的分派与实现**（`Dispatch<Family>` 的 switch 只认这个家族的
  动作）；跨家族共用的读值与数字读法集中在 `MacroRunner.Reading.cs`。新增动作先进对应家族文件：
  目录加行、家族分派加 case、实现写在旁边；只有跨家族共用的部分才回主文件或 Reading。
- `Run.*` 是用户可分支的失败键，日志键用 `Log.*`；新增失败键同时进手册表与对账测试。
- 行列表的列名、区域四格等跨文件重复的键用常量类，不散落字符串字面量。

## 性能怎么量

抓屏与识别的单位成本量一次记在 [performance.md](performance.md)（另有
[英文版](performance.en.md)），改画面获取或匹配算法之后重测一遍与表里比。量法写在那一篇的开头：
临时建一个引用 `WhaleGenie.Core` 的小程序，直接用引擎自己的设备，每项先热身、再取若干次的中位数，
量完把小程序删掉。数字说的是"看一次"的成本，谁读屏幕、什么时候读见
[manual.md](manual.md) 的「画面从哪来、怎么取」。


## 提交与 CI

`master` 是主线，`.github/workflows/ci.yml` 在推送与 PR 上跑格式检查、构建和全部测试，
失败即红。三个测试工程在 CI 上一个一个跑（`-m:1`）：它们同时跑会抢那几个核，按墙钟说话的用例
就会替别的工程花掉的 CPU 时间买单。失败的运行保留 7 天测试报告，成功的运行不保存报告，免得
构建产物把 Actions 存储配额占满。

本地用 `act` 跑同一套步骤。`windows-latest` 指到本机执行，`DOTNET_INSTALL_DIR` 给 setup-dotnet
一个不用管理员权限的目录；`ACT=true` 让工件上传在本地跳过，因为 act 自带的工件服务不接受
`upload-artifact@v6`：

```powershell
act -W .github/workflows/ci.yml -j build-and-test -P windows-latest=-self-hosted `
  --env ACT=true --env DOTNET_INSTALL_DIR="$env:LOCALAPPDATA\act-dotnet"
```

## 发布

发布走 `.github/workflows/release.yml`：**打一个 `v` 开头的标签就是发版**。

```powershell
git tag v0.01
git push origin v0.01
```

标签推上去之后，工作流会打两个包——一个要机器上有 .NET 桌面运行时与 ASP.NET Core 运行时，一个
自己带着运行环境——自动建好 Release 并把两个包挂上去。标签名同时决定了压缩包的名字和 exe 里的
版本号，所以只有一处要改；想在网页上发也行：Actions → Release → Run workflow，填一个版本号。

建 Release 需要写权限。仓库若把 Actions 的默认权限设成了只读，要在
Settings → Actions → General → Workflow permissions 里放开一次。

两种包差别只在"要不要自己带运行环境"，其余完全一样：

| 包 | 自带运行环境 | 体积（x64 打包后 / 解压后） | 什么时候用 |
| --- | --- | --- | --- |
| `WhaleGenie-<版本>-win-x64.zip` | 否，要装 .NET 10 桌面运行时与 ASP.NET Core 运行时 | 116 MB / 299 MB | 体积最小；已经装过运行环境 |
| `WhaleGenie-<版本>-win-x64-standalone.zip` | 是 | 191 MB / 474 MB | 换机器、给别人，什么都不用装 |

体积是本地这么量出来的：跑一次 `build/package.ps1`，`Expand-Archive` 解开两个 zip，再用
`Get-ChildItem -Recurse -File | Measure-Object Length -Sum` 把文件长度加起来（前者是压缩后的，
后者是解压后的）。换机器或换版本都要重新量。

解压后占地方的主要是浏览器驱动（`.playwright`，约 100 MB，其中 node 一份就占 89 MB）和
`lib` 里的原生库（OpenCV 的 `OpenCvSharpExtern.dll` 约 77 MB、ffmpeg 约 31 MB、Skia 约
12 MB）。驱动必须跟 exe 一起走，所以两个包都带着。这个驱动里只放 Windows 那一份 node
（根 `Directory.Build.props` 里的 `PlaywrightPlatform`）：驱动原本给五个平台各带一份，别的
平台的 node 在 Windows 上永远不会被执行，抄进去只是让每个包多出四百多兆。

便携包是单文件发布（`PublishSingleFile`）：托管的程序集都收进 `WhaleGenie.exe`，所以包根
没有 `WhaleGenie.dll`、`WhaleGenie.Core.dll`、`deps.json`、`runtimeconfig.json`，也没有
按语言分的资源文件夹。原生库没有一起塞进 exe：Windows 与这些库自己都按文件名找它们，程序
启动时把 `lib` 交给 Windows 的查找路径（`Program.HookLibraryFolder`）；单文件把原生库解压到
系统临时目录既慢，`DriverInput` 这类按路径判断"库在不在"的地方也会失准。项目里的
`MoveNativeLibrariesIntoLib` 在 `dotnet publish` 之后把原生库收进 `lib`，所以直接
`dotnet publish -r win-x64` 也得到同样的形状；打包脚本再补上手册、许可与驱动检查。

两种包解开后形状一样，只有一个 `WhaleGenie` 文件夹，exe 就在最外层：

```
WhaleGenie\
    WhaleGenie.exe              ← 双击它；托管的程序集都在这个文件里
    playwright.ps1              ← 装 Chromium / Firefox / WebKit 内核时用
    .playwright\                ← 浏览器驱动，约 100 MB，必须和 exe 一起走
    LICENSE.txt                 ← 程序自己的许可，AGPL-3.0 原文
    NOTICE                      ← 项目名与版权署名
    THIRD-PARTY-NOTICES.md      ← 用到的别人的东西
    libVIIPER-LICENSES.txt      ← 虚拟 USB 库自己的许可说明（它的 dll 在 lib 里）
    doc\manual.md               ← 使用说明，帮助菜单的「快速上手」打开的就是这一份
    lib\                        ← 原生库：OpenCV、ffmpeg、Skia、HarfBuzz、uiohook、libVIIPER
```

驱动级输入那个 `libVIIPER.dll` 也在 `lib` 里：它是原生库，跟着其余原生库一起搬过去，程序
启动时把 `lib` 交给 Windows 的查找路径（`Program.HookLibraryFolder`），按名字就能找到它。
开发目录里它就在 exe 旁边，两种形状都认（`DriverInput.LibraryPresent`）。包根那份
`libVIIPER-LICENSES.txt` 是打包脚本从 `third_party/libVIIPER/licenses.txt` 抄过来的。

程序按 AGPL-3.0 授权：条款原文随包里的 `LICENSE.txt` 发，署名在 `NOTICE` 里，两份都放在包根；对应源码就是
本仓库里这个标签。`LICENSE.txt` 是许可原文，一字未改——GitHub 是靠文件内容认协议的，在它前后加项目名或
版权行会让主页显示成"Other"，所以这两份的行尾转换在 `.gitattributes` 里关掉了，仓库里存什么字节，取出来
就是什么字节。

体积里不含调试符号：发布时 `DebugType` 关掉托管 pdb，项目里的 `DropPublishSymbols` 再把
SkiaSharp 与 HarfBuzzSharp 当作内容文件抄进来的原生 pdb 从发布清单里摘掉（Skia 一份就 80 MB）。
不做裁剪（`PublishTrimmed`）：Avalonia 的 XAML、UIA 和 OCR 都靠反射找类型，裁了就得一个动作
一个动作地验，不值当；体积几乎全在原生库上（OpenCV 的 `OpenCvSharpExtern.dll` 约 77 MB、
ffmpeg 约 31 MB，加上 OCR 模型、Skia、HarfBuzz）。运行 C# 脚本用的 Roslyn 与 NuGet 客户端
也在 exe 里，解压后一共约 19 MB；编译器的语言提示只留程序会显示的英文与简体中文两份
（`SatelliteResourceLanguages`），其余语言的卫星资源不进包。

本地想打同样的包，跑打包脚本就行——工作流里跑的也是它：

```powershell
pwsh build/package.ps1 -Tag v0.01
```

产物落在 `dist\`（已忽略）：`WhaleGenie-<标签>-<架构>.zip` 和
`WhaleGenie-<标签>-<架构>-standalone.zip`。

只出 64 位（`win-x64`）：找图/等图/点图依赖的 OpenCV 和浏览器动作用到的 Playwright 驱动
都只有 64 位一份，32 位的包装出来也是坏的，所以不再出。面向用户的说明写在 `.github/release-notes.md`，
发版时原样作为 Release 说明贴出去。
