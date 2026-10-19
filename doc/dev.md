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
| `AGENTS.md` | agent | 长期目标、开发规范与设计约定 | 已完成变更、逐阶段计划 |

四类事实各有一个真源：用户看到的行为以 `manual.md` 为准；动作语义（字段读法、值形状、单位、空值含义、
是否发输入、返回形状与记录字段）以引擎侧动作目录为准，界面目录只能引用它；失败键以 `RunKeys` 与手册的
失败键表为准，由 `FailureKeyTests` 对账；未完成的工作只在 `roadmap.md` 里；改过什么在 git 里。文档之间说的
不一致就是缺陷，与本文件、代码或测试对不上时，先改真源，再把其余几份对齐。

`manual.md` 描述已经实现的行为；路线图里的阶段是目标，未完成之前不要把未来行为写进手册。

**当前阶段**：路线图里的阶段已全部完成；新的工作按 [roadmap.md](roadmap.md) 的衡量标准重新立阶段。

动作目录分两层：引擎事实在 `WhaleGenie.Core/Execution/ActionSemantics.json`，由
`ActionSemanticsCatalog` 读取，包含动作键、分类、参数读法、值形状、单位、空值含义、字段自己的范围上限、
拾取器、是否发输入、是否条件、是否看屏幕、等待与超时来源、返回形状与记录字段；界面保存编辑器种类、标签、
提示与页面归属（`WhaleGenie/Models/ActionDisplay.json`，由 `ActionCatalog.cs` 投影），只留外观。图标不在
这份清单里：它们在 `WhaleGenie/Models/AppIcons.cs`，来自 `third_party/andrsrxn-icons`（MIT），由
`IconView` 画；分类栏与工具栏按 24 格，菜单和窗口按钮用 `FitToBounds` 填自己的方框。
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
| `ModelContextProtocol.AspNetCore` | 界面：主界面 MCP 开关启动的本机 HTTP 服务器。它只监听环回地址，不设令牌；工具读写当前项目草稿与运行日志，不能运行宏。`WhaleGenie.Core` 不引用它；宿主由 `WhaleGenie/Mcp/McpServerHost.cs` 建，工具的传输与发现由 SDK 管 |

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
- `ActionKnowledge` 把 `ActionCatalog` 压平成 MCP 可见的参数/返回值；不暴露图标或控件。
- `McpServerViewModel.EnsureSaved` 是启动前的保存回调：`App` 把它接到
  `MainWindow.EnsureProjectSavedForMcpAsync`，后者在没有 `CurrentPath` 时弹「选择保存位置 / 先打开」，
  选择位置走现有 `SaveAsAsync`。没有窗口的测试不设回调，仍可直接启动。
- `McpPrompts`（`WhaleGenie/Mcp/McpPrompts.cs`）提供 `whalegenie_write_macro` 与
  `whalegenie_debug_macro` 两条默认提示词，并通过 `McpServerOptions.ServerInstructions` 给只显示
  服务说明的客户端一份简短版本；`McpTools.Instructions` 还给不显示 prompts 的客户端留了
  一个同名工具。`McpServerHost` 用 `WithPrompts<McpPrompts>()` 注册。
- 退出程序时不能在 UI 线程同步等 MCP 停止：MCP 请求可能正等 UI 线程，`GetResult()` 会让
  `App.Exit` 永久卡住，留下一个无窗口进程。`App.Exit` 现在把停止放到工作线程并最多等 3 秒；
  `McpServerHost.StopAsync` 另有 2 秒 Kestrel 超时和释放。主窗口关闭只是收进通知区域，真正的退出是
  「文件 → 退出鲸灵」或托盘退出；`Program.Run` 在桌面生命周期结束后再做 `Environment.Exit(0)` 兜底。
- MCP 端口由 `LocalSettings.McpPort` 保存（默认 3917），浮层里的 `NumberBox` 只在停止时可用；启动时
  作为首选端口，被占用则回退到系统分配端口，浮层显示实际地址。
- `RunLogStore`（`WhaleGenie/Execution/RunLogStore.cs`）保留最近 20 次运行的日志与步骤结局：
  `RunViewModel` 与触发器的 `AskRunHost` 各写一份 capture；MCP 只读，不触发运行。
- 工具名以 `whalegenie_` 开头；列表、读宏、写宏、校验、apply、save、运行日志各一组。没有
  `run_macro` / `debug_macro` 工具。
- 测试：`McpServerTests` 用官方 HTTP 客户端走工具列表与完整写存流程；`McpDraftTests` 验 Revision
  与保存；`RunLogTests` 验日志；无外网。

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
  栈溢出与死循环都伤不到主程序，超时也能直接杀进程。宿主从 `packages\nuget\references.json` 里认
  编译时用到的那些 dll（`NuGetStore.References`），并把包里的 `runtimes\win-x64\native` 挂到 `PATH` 上。
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

声明名是变量名：首字符字母或 `_`，其余字母、数字、`_`、`.`；空名、读不出来的名字、形参名以 `.given` 结尾、
形参内或结果内重名、`sys.` 与 `step.<标识>.outcome` / `.error` 这类运行保留名，都在保存与写包前拦住。形参不能
同时是必填又有默认值（`RequiredWithDefault`）。空名字的行不丢掉：`FromJson` 与恢复快照会把它带回来，编辑器
显示声明问题，包写入则拒绝有致命声明错误的宏。被调用宏开始时先把形参写入局部表，再把尚未存在的声明结果初始化为
空；形参与结果同名时保留形参值（in-out）。默认值留在声明里，不写进调用步：调用表单留空就不写 `arg.<名字>`，
运行时才取默认值。宏接口里默认值一行的「表达式」「变量」复用 `ExpressionBuilderWindow` 与
`VariablePickerWindow`，名单来自 `VariableChoices.For([])`（全局与系统）：默认值在调用方作用域求值，调用方的
局部变量只能手写；变量墙在没有局部变量时初始选中「全部」，否则选择器会停在一个空页上。

包里的清单固定叫 `manifest.json`（小写），`manifest.json` 的 `version` 是包格式版本，当前是 10。
加载器只接受这一版，旧包会被明确拒绝，而不是让旧参数静默落到默认值。P1 把 `pollMs` 并进
`intervalMs` 时升到 6；P2 把 `useRecord` / `record` 换成 `place` / `rect` 时升到 7；P3 给形参带上类型时升到 8；F3 把`runMacro`的`wait`开关拆成`runMacro`与`startMacro`时升到 9。

调用另一个宏的步骤（`control.runMacro`）写三个部分：

```json
{
  "type": "control.runMacro",
  "params": {
    "macro": "整理",
    "arg.次数": "$n",
    "arg.标签": "开始",
    "returns": [ { "key": "答案", "name": "答案" } ]
  }
}
```

`macro` 是被调用宏的名字；每一个形参各占一格，名字是 `arg.<形参名>`——带前缀是因为形参名是被调用宏
自己的，不加前缀就会和 `macro`、`returns` 这些调用自己的键撞上。`arg.<名字>` 存在但为空、且没有
`arg.<名字>.given` 为 `true` 时算“没给”：可选形参用默认值，必填报 `Run.MissingArgument`；只有 `.given`
把空字段变成显式空串。`returns` 一行一个要取回的结果：`key` 是被调用宏那边叫这个名字，`name` 是本宏
这边落在哪个变量里。引擎按被调用宏声明的形参逐个读，并从 `returns` 的每一行取回一个值（名字没被声明过报
`Run.UndeclaredResult`），要取回什么在调用开始之前就说清楚，出错因此发生在送输入之前。结果变量在每次
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
   超时来源、返回形状与记录字段都写进 `ActionSemantics.json`；界面展示用的标签、提示、编辑器种类、页面归属
   与图标不参与引擎判断。
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
| 画面获取 | `WhaleGenie.GraphicsCapture.Tests` | 选路次序、会话缓存与复用、图片落位、矩形越界；真正调用 Windows 取画面那几条按手动用例过 |
| 界面 | `WhaleGenie.Tests` | 参数成型、页面归属、返回值页、触发器比对、卡片显示、文案完整性与文档对账，宏包往返，无头会话里的界面逻辑与 Avalonia 输入（`Ui.Run` 是入口），以及拾取器与录制器交来替身设备层后的情形 |
| 真的驱动别的程序 | `BrowserTabTests`、`ScriptBridgeTests` | 开一个不显示窗口的 Edge，跑浏览器动作；真的起一个解释器跑一段脚本，看它读值、写值、跑一步与交回结果走的是不是那条管道（机器上没有 Python 时跳过） |
| 手动 | — | 真实输入、抓屏取色、UIA 拾取、页面拾取、录制、窗口布局，以及驱动级输入：真机装一次 usbip-win2 驱动，勾上/不勾「启动时先把虚拟键鼠装上机器」各跑一次运行，键鼠四种动作（点击、打字、拖拽、滚轮）与手柄各来一次，退出程序后看设备管理器里虚拟设备是不是没了；三种屏幕拾取器（框选区域、取色放大镜、元素拾取）各进出一次：打开时鲸灵自己的窗口（含问话的那个对话框）都不在屏幕上，拾取器一开就拿到焦点、Esc 与回车与右键当场有效，拾完窗口按原状回来——其中一次在添加动作窗口里拾，看它自己也回来（它是编辑器的属主窗口，收起时跟着编辑器一起走）；框选器打开时屏幕是原样、指针看得见（底图定格在那一刻），一拖出框才框外压暗、框内照原样，框选之后截一张，图片里没有鲸灵自己的窗口；识别结果与偏移拾取窗口的标题栏都能拖着走（标题栏拖动是手动那一档，无头会话里只验到接没接线）；YOLO 检测的三步各跑一次（自备一份公开的 yolov8n.onnx 与类名表）：检测物体一次看框落在东西上、等待检测到物体在一段限时里等到与等到超时各一次、点击检测到的物体看落点在框中心加上偏移，并记下一次检测的耗时；脚本动作的三种窗口模式各跑一次（窗口留着/关着/不显示）、批处理、PowerShell 与 AutoHotkey 各一次，AutoHotkey 那一条还要在一台没装它的机器上看一次报错；Python 脚本与安装 PIP 包各一次（装的包能 import），C# 脚本与下载 NuGet 包各一次（装的包能编译进脚本），断网时看两个依赖动作各报什么；表达式编辑器里把变量、函数与树上已有的一支拖到位置上（拖到空位＝填入、拖到已有的一段＝换掉、拖自己的一支进自己的后代＝不理会、拖到窗口外＝取消，落点要跟着亮；1080×720 下四段分区与「翻译结果」一行排得下、滚动正常，函数卡片按分类 dock 只剩一组、颜色六个组两两不同、悬停气泡出得来，深嵌套公式的那句人话读起来顺） |

界面用例走的是 Avalonia 自己的输入管线，不是系统里的鼠标与按键：真的注入按键、真的抓屏取色这类事情在
无头会话里验不了，只能按手动用例过。因此**能在无头会话里跑到的部分要有用例**，只留人眼能判的那一档给
手动。

手动项在提交信息里按环境（Windows 版本、DPI、显示器数量、Edge/Python/AutoHotkey 版本）、步骤与观察结果
三项记录；只写“已手动过”不算结论。环境不同就重跑受影响的那一项，不要把一次通过当成所有机器都通过。

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
| 返回形状 | `ReturnsPageTests` + `ActionContractTests` | 返回值页的每一行、引擎写出的键与记录字段一致 |
| 失败键 | `FailureKeyTests` | `RunKeys` 与手册表一致；日志键、状态键不与失败键混用 |
| 页面归属 | `ActionPageTests` | 一个参数只属于一页，基础页为空时不显示 |
| 文案 | `LocalizationTests` | 中英文键齐全，参数与选项都有中文 |
| 字段名 | `FieldNameTests` | 同一字段名只有一种 `Kind`、一套选项与一套标签；记录字段也走 `FieldNames`；例外要写进白名单与理由 |
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
就会替别的工程花掉的 CPU 时间买单。测试报告作为构建产物保留。

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
