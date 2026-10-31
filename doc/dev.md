# WhaleGenie 开发说明

[简体中文](dev.md)

写给改这个仓库的人：常用命令、工程怎么分块、依赖了谁、测到哪一层为止、文档分工、改一个动作的顺序、
怎么量性能、代码约定，以及提交、CI 与发布。用这个程序的人看 [manual.md](manual.md)，长期设计约束看
仓库根目录的 `AGENTS.md`，下一步看 [roadmap.md](roadmap.md)。

**这份文件回答"怎么做"**（动作、纪律、坑）；"外观怎么定"在 `WhaleGenie/Themes/Controls.axaml` 开头的
137 行规则与登记表，加上 [architecture.md](architecture.md) 的「7.5 外观：三层与两条守卫」。
**一条道理只在一处成文，另一处只放一句引用 + 链接**——同一句话在两份里会各自按语境漂移，读的人就不知道该信哪份。

## 常用命令

```powershell
dotnet build WhaleGenie.slnx -c Release          # 构建（含警告检查）
dotnet test  WhaleGenie.slnx -c Release          # 全部测试
dotnet format WhaleGenie.slnx --verify-no-changes --no-restore   # 格式检查
dotnet run --project WhaleGenie                  # 启动程序
pwsh build/site.ps1                              # 拼出介绍页 _site
```

看得见界面，但不必点它——**要出图时**，把下面这段存成仓库**外**任意目录里的 `shot.cs`（.NET 10 的文件式程序，
不建工程、不进仓库），把 `#:project` 换成你本机的仓库路径，`dotnet run shot.cs` 即可：

```csharp
#:property TargetFramework=net10.0-windows10.0.19041
#:property JsonSerializerIsReflectionEnabledByDefault=true
#:package Avalonia.Headless@12.1.2
#:project C:/path/to/WhaleGenie/WhaleGenie.csproj

using Avalonia;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using WhaleGenie.Views;

// 与 Program.BuildAvaloniaApp 对齐：真画笔（无头默认的假画笔截不出帧）+ 同一套字体。
// 少了 WithInterFont，文字度量会和真机不同（中文多行会挤到一起）。
// 少了上面第二行的 JsonSerializerIsReflectionEnabledByDefault，文件式程序默认关掉了反射式 JSON，
// `ActionCatalog` 的静态构造会抛——凡是打开含动作的窗口（宏编辑器、动作编辑器）都会炸在这儿。
AppBuilder.Configure<WhaleGenie.App>()
    .UseSkia()
    .WithInterFont()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions
    {
        UseHeadlessDrawing = false,
        ShouldRenderOnUIThread = true,
    })
    .SetupWithoutStarting();

Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
var window = new MainWindow();          // 换别的窗就换这一行
window.Show();
Dispatcher.UIThread.RunJobs();          // 先跑完布局，否则截到的是空帧

var frame = window.CaptureRenderedFrame()
    ?? throw new InvalidOperationException("没有渲染出画面");
using var stream = File.Create(Path.Combine(Path.GetTempPath(), "whalegenie-ui.png"));
frame.Save(stream, PngBitmapEncoderOptions.Default);
```

**主窗记得先装 DataContext**：`MainWindow` 自己不带 ViewModel，运行时是容器给的（`AppServices.cs:38`）。
空着它就得到一张所有绑定都失效的图——页签没有选中态、空态文案不出现、列表是空的，看着像功能丢了。
换别的窗同理：先看它构造完之后 `DataContext` 有没有东西。

```csharp
var window = new MainWindow { DataContext = new MainViewModel(_ => new McpServerViewModel()) };
```

**这张图只给人看，不当证据用**：没有基线、没有逐字节比对，也不进仓库。能复核的是测试——它们读的是模板里
被涂色的那个部件（`LayerOneTests`、`ThemedDialogTests`、`ListBoxStyleTests`），不是像素。

**能拍到哪些窗，要先知道**：`Views/` 里大部分窗口有公开构造函数，可以直接 `new` 出来截；但
`MacroChooseWindow`、`MenuIconPickerWindow`、`MenuMacroPickerWindow`、`PublicMacroLibraryWindow`、
`GlobalConflictWindow`、`ElementPickerWindow`、`PointPickerWindow`、`VariableFieldPickerWindow`、
`ScreenFrameWindow` 要么构造函数是 `internal`，要么只有 `static ShowAsync(owner, …)`（还会等一个答案，
截到的是一张无内容的窗）。这几扇由用例覆盖，不要为了出图去把它们改成公开。

量 MCP 的易用性，不必点界面：

```powershell
dotnet run --project WhaleGenie -c Release -- --mcp-doctor --report TestResults/mcp-after.md   # 自己拉起子进程量一遍；报告是本机快照，不进仓库
dotnet run --project WhaleGenie -c Release -- --mcp     # 走标准输入输出；Ctrl+C 或客户端关管道就停（权限来自 MCP 浮层里的两个开关，写在 settings.json；命令行上没有权限参数）
```

**换一台机器验证真实输入**：把 Release 输出拷过去，用**交互式**会话跑，别用 SSH 直接跑。Windows 的 SSH 登录
落在会话 0，那里没有桌面——窗口建得出来但没人看得见，`SendInput` 也到不了，输入类动作会静默地什么都不做。

这一档以前有一个 `--range` 靶场窗口拿读数核对（按了几下、输入框里是什么、色块什么颜色），后来整组删掉了：
产品里不该留一个谁都用不到的窗口，测试里那份也只剩"只测自己"的夹具——没人排期跑它，留着会让人以为这一层
验过了。现在**用现成的窗口过**：开一个记事本点一下看光标在不在，打一行字看字有没有出来，框选一块截图看
图片对不对。读数没那么精确，但这件事本来就是人眼判的。

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

**跑测试不要和 `dotnet build` 混着用 `--no-build`**：整个解决方案构建写到 `bin/x64/<配置>`，而
`dotnet test <工程> --no-build` 可能去 `bin/<配置>` 找，那里是上一次的二进制。症状很像真的坏了——刚加的
中文文案在测试里"不存在"、刚加的结果没有标签。跑测试要么带构建（`dotnet test <工程> -c Release`），要么
两边都写上 `-p:Platform=x64`。

构建把警告当成错误（`Directory.Build.props` 里的 `TreatWarningsAsErrors`），
所以"能编译过"就等于"零警告"。

**有几条用例只在整套跑里偶发**，单独跑都过：`GlobalVariablesTests` 里与关闭/删除确认有关的三条、
`LanguageRefreshTests.The_macro_interface_relabels_its_kinds`。表现是耗时从几十毫秒涨到几秒然后红，
报出来的却像功能坏了。判断办法：**单独跑一次**，过了就当偶发，别去改产品代码；同一轮里出现两条以上
才值得停下来查。三条确认用例里的固定 `Task.Delay(20)` 已经换成 `Ui.Until(条件, 原因)`；语言刷新那条
没有固定等待，成因未明，它的失败消息现在会带上两边的标签与当时的语言。

能改全局状态的用例类都带 `[RestoreSharedState]`：它把**语言、设置文件路径、全局变量目录**在每条用例
前后各存一次、还原一次，所以一条用例改了世界，下一条不会替它背锅。两件事写在这里免得再踩：

- 还原必须在**界面线程**上做（`Ui.Run`）。`Strings.Language` 的 setter 会通知正在显示的窗口换文案，
  窗口只归它自己的线程碰——从测试线程改它会抛 "The calling thread cannot access this object"。
- `SharedVariables` 的值包里存的是 `Core` 的内部类型 `Value`，测试工程看不见，所以没纳入还原；
  需要它的用例自己 `Clear()`。

**下次它再红，从这里接着查**（交接）：

1. 先看消息。语言那条现在会印出两个标签与**当时的语言**：若 `language=Chinese` 而两个标签相同，
   说明中文表缺键（`Strings.Get` 会退回英文）；若 `language=English`，说明语言没切到中文——
   那就是 `InterfaceLanguage.InChinese` 或更早的某条用例改了它（还原器已经把这类泄漏挡住，
   真出现就说明泄漏发生在**两次用例之间**，即某条用例的异步尾巴）。
2. 再跑一次同样的筛选（`--filter` 那一条类），看是否复现；不复现就记下当时的**耗时**——
   偶发那几次都是几十毫秒涨到几秒，像是调度器上有一件没做完的事。
3. 还查不动就加一条临时探针：在 `Ui.RunAsync` 里包一层计时，把超过 200ms 的 dispatch 与它的类名打出来。
   这条不做进仓库，只在本地跑一轮，用来看"是谁占着界面线程"。

**已经查到的一条真机制**：`InvalidProgramException: You've caused dispatcher loop`（栈里是
`Dispatcher.ResetForUnitTests`）。意思是**上一条用例的模态还开着**：`ShowDialog` 会在这次 dispatch 内部
再起一个调度器循环，用例不回答它就走人，循环就留在那儿，下一条用例一 dispatch 就炸——炸在谁头上是随机的，
所以才表现为"换了三条不同的受害者"。

规矩由此而来：**用例关掉一个可能发问的窗口时，必须把它弹出的对话框回答掉**。编辑器窗在"有改动"时关闭会问
"要不要保存"。怎么答、答给谁看，见「测试分层」里的「关掉会发问的窗口：就地作答，不要走开」——那一条是唯一的一份。
识别特征记住这一句就够：报错在 `Dispatcher.ResetForUnitTests`，不在断言上。

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
| [architecture.md](architecture.md) | 改结构的人 | 四个工程与依赖方向、主链路、各子系统、有意的取舍与债务 | 操作步骤、逐字段语义 |
| [roadmap.md](roadmap.md) | 改仓库的人与 agent | 未完成阶段、依赖、验收、明确不做 | 已完成清单、工期 |
| [mcp-usability.md](mcp-usability.md) | 改 MCP 的人 | 一次真实自检的指标、改前改后、遗留项 | 手写的评价（它是 `--mcp-doctor --report` 的产物） |
| [performance.md](performance.md)（[English](performance.en.md)） | 改画面获取的人 | 单次读取、识别、检测的单位成本与量法 | 怎么用、怎么改 |
| `AGENTS.md` | agent | 长期目标、开发规范、必须守住的设计约定与实现约束 | 已完成变更、逐阶段计划、用户可见语义的细节（在手册） |

每个问题只有一个真源：

- 用户看到的行为 → `manual.md`；
- 写代码的规范与设计约定 → `AGENTS.md`；
- 动作语义（字段读法、值形状、单位、空值含义、是否发输入、返回形状与记录字段）→ 引擎侧动作目录，
  界面目录只能引用它；
- 失败键 → `RunKeys` 与手册的失败键表，由 `FailureKeyTests` 对账；
- 未完成的工作 → `roadmap.md`；改过什么 → git。

不一致就是缺陷：先改真源，再把其余几份对齐。文档里的数字要当场量——量不了就不写具体值，写上一个数看着准，
改了几轮之后没人发现它是错的。

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
| `WhaleGenie.Core` | 执行引擎。变量、表达式、执行语义、动作语义目录，以及所有真正碰机器的设备（输入、屏幕、OCR、UI Automation、文件、进程） |
| `WhaleGenie.GraphicsCapture` | 从 Windows 拿像素：Graphics Capture、桌面复制（DXGI）、屏幕 GDI 与 PrintWindow 四种取法，以及会话缓存与选路。它不认识引擎的类型，引擎通过 `ProjectReference` 用它 |
| `WhaleGenie` | 界面与 MCP 宿主。窗口、视图模型、触发器、托盘；宏的文件模型与文案也在这一工程里——宏、步骤、触发器、变量声明在 `Models/`，`.wgmacro` 的读写与两份文案表在 `Storage/`、`Localization/`。动作展示清单是 `Models/ActionDisplay.json`，`ActionCatalog.cs` 只把它投影成标签、编辑器种类与页面归属；图标在 `ViewModels/AppIcons.cs`（语义名到 Lucide 枚举），菜单图标键在 `ViewModels/MenuIconCatalog.cs` |
| `WhaleGenie.Core.Tests` | 引擎的测试，跑在替身设备层上，不碰真实桌面；动作行为、读法与输入路由在这里验。宏包往返在 `WhaleGenie.Tests` 里验——那条路要经过项目与编辑器，`MacroPackage` 那一层自己不带测试工程 |
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
| `ModelContextProtocol` | 界面：MCP 服务器本身。它没有端口、不监听任何东西，说的是客户端启动这个进程后给的那条标准输入输出；工具读写当前项目草稿与运行日志，还能看屏幕、窗口与 UI 元素，在「允许直接控制」开着时发送键鼠与窗口操作，在「允许 AI 运行宏」开着时一次运行一条宏（读回每一步的结果）；两档权限来自 `settings.json`，服务每次调用前读回来。`WhaleGenie.Core` 不引用它；宿主由 `WhaleGenie/Mcp/McpServerHost.cs` 建，传输与发现由 SDK 管 |

这些包各自带进来的原生库（`OpenCvSharpExtern.dll`、`uiohook.dll`、Playwright 的驱动等）
和许可原文，列在 `THIRD-PARTY-NOTICES.md` 里。

## MCP 服务器

主界面「运行调试」左边的 MCP 按钮打开一个浮层，里面只有两样东西：两个权限开关，和一段**客户端配置**。客户端
启动的是一个**说 stdio 的子进程**：它先敲窗口的门（命名管道 `whalegenie-mcp-<用户>`），敲开就把两股流原样
转给窗口，工具于是跑在窗口进程里、改的就是屏幕上那份项目；敲不开（没有窗口）才自己起一份项目，走和以前
一样的老路。不监听端口，因此也没有令牌。
协议面（资源、体检、审计的定位）见 `doc/architecture.md` 第九章，指标与检查项见 `doc/mcp-usability.md`；
这里只写改它时要守的规矩。

**四组工具，两档权限**　项目读写、看屏幕、直接控制（键鼠、窗口、写项目与存图片）、运行宏。后两组各由一档
开关把守，默认关：`apply` / `save_project` / `put_image` 与键鼠同一档——能改这台机器才算能改这台机器上的文件。
开关是浮层写的 `settings.json` 字段，服务端**每次调用前**读回来（`McpGrants` → `McpControl`），所以改完
下一次调用就生效、不用重启；命令行上没有第二份答案，文件里没开就是只读。加新工具先想清楚它属于哪一组——
忘了加守卫，它就成了无授权也能动的工具。

**桥与草稿**　`McpLiveProject` 是界面与 MCP 会话之间唯一的桥：快照、apply、save 全部经
`Dispatcher.UIThread.InvokeAsync` 回界面线程。`MainViewModel.Revision` 每次 `MarkDirty` 增加，草稿 `apply`
前对不上就拒绝。`McpDraftStore` 只改内存草稿：`apply` 才替换项目的宏与全局变量，`save_project` 才写包
（先复制 `.wgmacro.bak`），一份草稿只能 apply 一次。一次会话最多 8 张：满了先丢**已 apply** 的最旧一张并在
`open_project` 的 `dropped` 里说出来；一张都没 apply 就报 `TooManyDrafts`，不替客户端扔掉没落地的活。`whalegenie_preview_apply` 用 `McpPreview` 在 apply
之前说出哪些宏会来、会走、会变。

**一个窗口，一个程序**　`SingleInstance` 用命名互斥占住"这一份"，第二次启动发一个命名事件让窗口回来然后
自己退出；`--mcp` 与 `--mcp-doctor` 不参与，客户端要几个服务端就起几个。**窗口真的到前台**要靠两件事配合：
第二次启动在退出前把"可以抢前台"的权利交出去（`AllowSetForegroundWindow`），并等窗口回一个"已经回来了"
的事件才结束——它是持有前台的进程，先退就等于没交；窗口这边（`WindowFront`）除了 `SetForegroundWindow`，
还会临时 attach 到占着前台的那个线程上，最后一招是翻一下 Topmost。少任何一半，看到的就是任务栏图标闪一下、
窗口没上来。上次打开的包记在 `settings.json` 的 `LastProject`，启动时能读到就打开——窗口与无窗口两侧都用它，
所以客户端不给 `--open` 时看到的是这个人上次用的那份，而不是空项目。

**跑一条宏**　`run_macro` 默认等到跑完（`timeoutMs` 到了就取消整次运行并回 `Timeout`）。`wait=false` 时立刻回
`status: "Started"` 与运行编号，真正的结束仍然走同一段收尾（写日志、释放"一次只跑一条"的占用）；所以
不等待不等于放开并发，第二条照样 `RunBusy`。

**写入与补丁**　`put_macro` 整宏替换；`patch_macro` 在副本上按序执行七种操作，任一条不合就整批不改。
每条操作可以带 `macro` 指定写哪一份文档：`main`（默认）、`sub:名字`，或 `sub:a/sub:b` 这种按层往下的路径——
和 outline 每行印的标签是同一个字符串：

| 操作 | 改什么 | 怎么点名 |
| --- | --- | --- |
| `set` | 一个参数的值（窗口规则可整条给对象） | `stepId` + `param` + `value` |
| `setRow` | 一张表里一行的几列 | `stepId` + `param` + `match` + `values` |
| `addRow` | 往表里加一行 | `stepId` + `param` + `values` |
| `removeRow` | 从表里删一行 | `stepId` + `param` + `match` |
| `insert` | 往列表里插一步 | `parent`（列表路径）+ `index` + `step` |
| `remove` | 删一步 | `stepId` |
| `move` | 把一步挪到别处 | `stepId` + `parent` + `index` |

行按**它自己说的话**点名而不是按位置，所以上面插一行不会让这次改动落到另一行上；`match` 命中多行或零行都
拒绝——猜哪一行是作者没做的那个决定。`rect` / `point` 在文件里是**一个值**（记录变量或
`field($rec, "name")`），给对象会被拒并说明这一点。落点走 `StepPlacement`，与编辑器同一套 `childKeys` 规则。

**校验**　`McpDraftStore.Validate` 把 `MacroCheck` 的声明错误与 `Inspect` 记为 `Blocking`，`apply` 只拦
`Blocking`，`force` 可越过。每条带与语言无关的 `code`：句子会随界面语言变，代号不会，客户端按代号分支。

**错误码**　`McpErrors` 统一给 `{error:{code,message,hint,retryable,details}}` 并置 `isError`；消息可以改写，
码不能改。码表：`BadArgument`、`BadJson`、`BadPatch`、`BadPlacement`、`DraftNotFound`、`NoSuchMacro`、
`NoSuchRun`、`UnknownAction`、`AlreadyApplied`、`RevisionConflict`、`NoPackagePath`、
`DraftHasBlockingProblems`、`DirectControlOff`、`DeviceUnavailable`、`DeviceFailed`、`Internal`。

**给客户端看的形状**　`ActionKnowledge` 把动作目录压平成 MCP 可见的参数与返回值：参数带 `Fields`、`Unit`、
`EmptyMeans`、`Minimum`、`Maximum` 与 `Conditional`，窗口规则字段直接取自 `WindowRule.ToJson`，客户端不必猜
记录结构。`McpPrompts` 给两条默认提示词（写宏、调试），并用 `ServerInstructions` 给只显示服务说明的客户端
一份简短版本。列表类返回一律带 `total` 与 `hasMore`；`get_run_log` 从末尾取并报 `truncated`，不静默截断。

**审计**　`McpAudit` 往 `logs/mcp-<日期>.log` 一行一次调用，**只记改了东西、动了机器、或者拿走一整张图的**；
`type_text` 只记字数不记内容。失败日志按文件名认（`failure-*.log`），不能取"最新一个 .log"——那个可能是审计文件。

**无窗口与体检**　`--mcp [--open 包] [--new 文件夹]` 起服务（权限不在这条命令行上，见上一段的两个开关），
stdout 是协议本身，一行别的都不能写（`McpHeadless` 只说一句到 stderr）；`--mcp-doctor [--report …]
[--strict]` 自己拉起一个子进程、接上它的管道量一遍，退出码 0 全过 / 1 有 fail / 2 参数或包有问题 / 3 说不上话
/ 4 子进程起不来。体检会先读 `project_status` 里的两档权限再验：关着就期待被拒，开着就期待放行——对着把开关打开
的机器跑，它不会把「本来就该放行」报成 fail。两者都只是入口，不得借此让核心依赖 MCP。

**改它的固定四步**　改 → 量（动作矩阵与体检各跑一次）→ 比（两份报告对着看）→ 提交。量不出差别就不要说
"更顺了"；矩阵里多出来的缺夹具与不可达也不算通过。

**测试**　`McpServerTests`（工具列表、schema 里的 `enum` 与对象参数、完整写存流程、apply 门禁、补丁）、
`McpDraftTests`（Revision、非法落点、写入即发步骤标识）、`McpActionKnowledgeTests`、`McpSelfCheckTests`
（尺子自己）、`McpControlTests`（假设备层验门禁、请求与错误码）。无外网。

**退出**　不能在 UI 线程上同步等 MCP 停止：请求可能正等 UI 线程，两边永久互等，留下一个无窗口进程。

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

一个宏是一份 JSON（打包后放在 `nodes/<编号>-<slug>.json`，slug 由宏名生成），顶层分三块：触发与节奏
（`triggerMode`、`schedule`、`watch`、`process`、`window`、`recorder` 等，对应编辑器的触发页）、**调用声明**、
`steps`。声明是这个宏对调用者的要求：

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

类型写 `Text` / `Number` / `Flag` / `List` / `Record` / `Image`，没写就是 `Text`；形参与结果都为空就写空数组。
同一份声明有两份读法：引擎读 `WhaleGenie.Core/Execution/MacroSignature.cs` 的 `MacroSignature`（名字、默认
值、是否必填、类型），编辑器与设计期读 `WhaleGenie/Models/MacroCallable.cs` 的 `MacroCallable`（多一列
说明）。两者都由 `MacroItem` 读写，形参与结果各带一个 `ActionReturnType`。

记录类型的形参与结果可以带字段表（`fields: [{name, type}]`，`MacroField`）——它是**可选**字段，不写就是
"没说"，所以没有升包格式版本。三处读它，都只在类型是 `Record` 且至少写了一个字段名时生效：宏接口页（增删
行直接写进 `Fields`，类型换走再换回来不清空）、变量墙（`VariableChoices` 把字段填进 `VariableChoice.Fields`）、
设计期检查（`MacroCheck.CheckFieldNames` 对着 `field($记录, "名字")` 核对，对不上报 `UnknownField`；没写字段表
就不查——对着没人写过的名字查等于猜）。

字段的读法只有 `field($记录, "名字")`，`$记录.名字` 会被当成一个没定义过的变量名。形参与结果的**顺序**就是
声明里的顺序，接口页的上移/下移同时改行集合与 `DeclaredParameters`。**必填仍是必填**：「提为子宏」写出的调用
一定把每个参数都传了，声明成可选只会让以后有人删掉那一行时变成静默的空值。声明名是变量名（首字符字母、数字
或 `_`，其余加 `.`），空名、读不出来的名字、重名、`sys.` 与 `step.<标识>.*` 这类保留名、必填加默认值
（`RequiredWithDefault`）都在保存与写包前拦住。

宏名唯一不靠"报错让人改"，而是"给后到的加日期"（`MacroNames.Free`）：`MacroPackage.Load` 在
`triggers.Attach` 之前把同一文件里的重名宏改成 `名字 (yyyy-MM-dd)`（再撞加 ` 2`），
`MainViewModel.AddMacro` 对项目里已占用的名字做同样的事——导入包撞名走这一条。改名同时改该宏的 `Triggers`
与导入包里那张触发器表，否则后面的 `MergeMenuFrom` 会把菜单项挂到留下的那个宏上。

**包清单**固定叫 `manifest.json`（小写），`version` 是包格式版本，**当前 20**。加载器只接受这一版，旧包被明确
拒绝，而不是让旧参数静默落到默认值；**形状变了就升版并拒绝旧包**，历史在 git 里。触发入口按名称引用宏，
改名时同一份 `ProjectTriggers` 改写所有引用，保存、恢复与 MCP 草稿都不允许悬空引用。

**子宏**写在同一个文档的 `subMacros` 数组里，只保存名称、说明、形参、返回值、互斥设置、步骤与可选的
`origin { id, name, version }`；它们不进主窗口的宏列表，但编辑器与运行库会与父宏、项目宏一起扁平化。父宏、
子宏与项目宏的可调用名忽略大小写后必须唯一。公共宏库是另一个原子写入的包，位置固定
`library/public-macros.wgmacro`，独立格式版本 1；拖入时复制可调用内容并记录来源版本，之后不跟随来源更新。

**导入与导出**　只有一种文件，就是一个包：导出是"把选中的那几条写成一个包"，导入是"从这个包里挑几条"。
要动的是三张表的裁剪与合并，格式一个字都没加——`Storage/MacroBundle.cs` 管裁剪（选中宏的触发入口、界面布局、
它们引用的菜单页，以及这些宏真正读到的全局变量），`Storage/MacroImport.cs` 管落地与改名。`MacroTreeSelection`
（ViewModels）是"勾一个宏就连它的子宏一起带上"这条规则本身，导出与导入两个方向共用；勾选决定落点：有顶层宏进
项目，只勾子宏进公共宏库。全局变量同名时逐个问一次（重命名 / 用本项目的 / 覆盖），重命名只改随文件带进来的宏，
走 `VariableRename`，脚本正文不跟着改名所以要单独报出来。

**步骤标识撞了怎么办**　一个宏连同它的子宏里，标识唯一：`StepIds.Settle` 遇到重复就给后到的那一步发新
标识，并把**那一步自己的**条件与表达式里的 `step.<旧>` 一起改写。只改这一步的子树：粘贴进来的一整块是
一起搬的，原来那块的名字不能被改走。改写只碰引擎按"引用"读的字段（`AcceptsVariables` + `WholeValue`），
按字面读的字段原样留着（一个打字步骤打出 `step.abcd.outcome` 这几个字，就是这几个字）。编辑器在步骤列表
上方说明改了哪几个、把宏标脏；MCP 的 `put_macro` / `patch_macro` 答案里带 `renamed: [{from,to}]`。

**触发器**　`ProjectTriggers` 是触发入口的唯一真源，`TriggerKind` 十种：`key`、`color`、`interface`、
`hotstring`、`menu`、`timer`、`file`、`process`、`window`、`idle`。编辑器把每条入口写成同一份 `TriggerEntry`
JSON；`MacroItem` 上的旧触发字段只作为写入前的兼容投影，不参与包里的判断。界面宏按窗口规则的规范化键自动
分组，同一规则下的全部宏共用一个悬浮窗。`MacroTriggerService` 从触发入口分派，一个宏同时最多只占一条运行。

**菜单**属于项目：固定 4 列、上下各 3 行，格子 80×80；`MenuPage` 分为最多 8 个通用页、默认窗口页和按顺序
保存的窗口规则页，同一规则最多 8 个下区子页。格子文字只来自 `MacroItem.Name`，提示同时显示 `Description`；
`BlockedMenuRules` 单独保存按进程名精确匹配的禁止呼出规则，在进入 `MacroTriggerGate` 前检查。启用项撞格、
悬空页面或缺失宏会阻止保存；同一宏允许在不同格子重复出现。

**字串**只读全局键盘钩子送来的字面字符，最多保留 40 个；处理常见热字串的结束键、大小写、词中、`B0`（保留
输入）与 `O`（舍弃结束键），命中后同步退格并重放结束键，再启动宏一次。`endChars` 保存实际字符：空格就是
一个空格，换行与 Tab 由 JSON 标准转义承载。

钩子与计时器只能读 UI 线程发布的 `TriggerRuntimeSnapshot`；全局快捷键只读 `GlobalHotkeyPlan`，二者都不访问
Avalonia 控件或可变 UI 集合。所有后台回调经过异常隔离，重复故障按阶段节流写入 `logs/runtime.log`。

**前台按键自己拼输入事件**（`WindowsKeyStrokes`）：一个按键同时带键码、扫描码与扩展位，**扩展位**才是把共用
扫描码的键分开的东西（NumLock 的 `0x45` 就是 Pause 序列里的一个字节）。三样都从 `WindowsKeys` 取，
`WindowsKeys.IsExtended` 是注入与投消息两条路径共用的唯一一张表。

钩子回调里凡是回答"这个键是不是人按的"，一律先问 `WhaleGenieInputGate`，它记的是这个键**回来时**叫什么
（`KeyNames.AlsoSeenAs` 的第二名字就是为此存在）——只认 `NumPad7` 会把自家按的键当成人的键。

调用另一个宏（`control.runMacro`）写三段：

```json
{
  "type": "control.runMacro",
  "params": {
    "macro": "整理",
    "arguments": [ { "name": "次数", "value": "$n" }, { "name": "标签", "value": "开始" } ],
    "returns": [ { "key": "答案", "name": "答案" } ]
  }
}
```

`arguments` 一行一个形参（列 `name` / `value` / `given`，`given` 表示"这一格是空的，但确实要传空"），
`returns` 一行一个结果（列 `key` / `name`：那边叫什么、这边落在哪个变量）。**没有行就是没给**——可选形参用
默认值；有行但 `value` 空且没勾 `given` 算同一件事。五种拒绝都发生在被调宏开始跑之前：
`Run.MissingArgument`、`Run.UndeclaredArgument`、`Run.UndeclaredResult`、`Run.DuplicateArgument`、
`Run.GivenWithValue`。`control.runMacro` 是唯一的调用动作：同步、必等、共用步骤超时。顶层同一份声明由
`MacroRunner.StartSignature` / `StartArguments` 种进运行自己的表；调试窗口提供起始形参，触发器不提供、默认值
照用。

**包外的状态**　变量持久化保存在当前用户的 `%LOCALAPPDATA%\WhaleGenie\persistent-variables.json`，只保存勾选后的全局变量名和值；
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
| 动作矩阵 | `WhaleGenie.Core.Tests/Bench` | 目录声明的每个动作（隐藏的除外）逐个按声明合成一步、在替身设备层上跑一遍，核四件事：结局、返回值形状、副作用声明与设备账本是否一致、有没有参数给不出值。它只量不改，缺的东西显式写进报告，不静默跳过 |
| 画面获取 | `WhaleGenie.GraphicsCapture.Tests` | 选路次序、会话缓存与复用、图片落位、矩形越界；真正调用 Windows 取画面那几条按手动用例过 |
| 界面 | `WhaleGenie.Tests` | 参数成型、页面归属、返回值页、触发器比对、卡片显示、文案完整性与文档对账，宏包往返，无头会话里的界面逻辑与 Avalonia 输入（`Ui.Run` 是入口），以及拾取器与录制器交来替身设备层后的情形 |
| 真的驱动别的程序 | `BrowserTabTests`、`ScriptBridgeTests`、`ScriptHostRunTests` | 开一个不显示窗口的 Edge，跑浏览器动作；真的起一个解释器跑一段脚本，看它读值、写值、跑一步与交回结果走的是不是那条管道（机器上没有 Python 时跳过）；`WHALEGENIE_REAL_NUGET=1` 时额外下载真实包并验证编译和运行 |
| 手动 | — | 真实输入、抓屏取色、UIA 与页面拾取、录制、窗口布局、驱动级输入——清单见下面的「手动项」，无头会话里验不了这些 |

### 界面外观：无头里验得了"是不是那个令牌"，验不了"好不好看"

窗口画出来的颜色可以在无头会话里读：把伪类直接置上（`((IPseudoClasses)control.Classes).Set(":pointerover", true)`），
再读模板里真正被涂色的那个部件，就能断言某个状态的颜色是不是令牌里的那个值。`LayerOneTests`（层 1 的
Fluent 键是否真的生效）与 `ThemedDialogTests`（层 3 的类是否真的落在窗口上）都是这么验的——一次性的人眼
观察会过期，用例不会。

读哪个部件有讲究，见「静默失效」第 7 条：读控件自身的属性会给出"确定但不成立"的结论。

**取主题画笔必须带 `ActualThemeVariant`**：`Application.Current.TryFindResource(key, out _)` 不带 variant 时
查不到 `ThemeDictionaries` 里的键，**返回 `null` 而不报错**。这正是"窗口透明"那个线上 bug 的根因——
它绿着上线，因为测试里也走了同一条取不到的路，而每条调用都有一层兜底把 `null` 盖住了。
**遇到"这里取不到值"，先怀疑取值路径，别先下"测试验不了"的结论**——
"检查做不到"常常只是"这个写法做不到"（同族：`Unused()` 只扫 `Views`、grep 只认行内写法）。

剩下验不了的只有"好不好看"：配色偏没偏、层次够不够仍要人看一次。`--theme-probe`（只在本机 Debug 有效）
那个窗口就是为此留的，评审完连同分支一起删掉。

**一个控件由"外面一层 + 模板里一层"组成时，先说清底与边框归谁。** 自动补全就是这样：`AutoCompleteBox`
里有一支 `TextBox`，真正画格子的是里层。度量写在外层（高 30、内边距）、底与边框留给里层，里层就会按自己的
最小高度排下来，比 30 高两个像素，上下两条边被裁掉——界面上是一个只有左右括号的输入框，底色还被早先那条
`Background=Transparent` 清成了窗口背景。**没有任何一个颜色是错的，画笔检查一条都不会红**，所以只有
"量两个盒子的关系"的用例看得见（`FieldStyleTests`）。

发现它的办法不是盯着图看，是量：

- 放大看局部（最近邻 4～8 倍）能分清"边框被裁"和"边框颜色不对"；
- 判颜色要读像素，不靠眼睛：同一处按钮的"蓝边"往往只是文字次像素反锯齿的彩边（`487297`、`81C0E5`
  这类颜色只出现在字附近）；把边框所在的窄条取出来统计颜色，正常应当只有 `BorderControl` 的 `#555555`
  与格子底 `SurfaceField` 的 `#2A2A2A`；
- 竖直扫一条线穿过输入框，能一眼看出上边框、底色、下边框是不是都在——缺口就是被裁掉了。

**窗口是 `AppWindow`，无头里它得先把库的句柄表清掉。** 所有窗口都从 `views:AppWindow` 派生（`FAAppWindow`
的子类，标题栏归库）。`FAAppWindow` 的构造按 `HWND` 给 `Win32WindowManager` 建静态表，而无头平台给每个窗口
的句柄都是 `0`——第二个窗口就抛 `ArgumentException … Key: HWND`。`AppWindow` 里那句"句柄为 0 就清表"就是
为此；真机不执行。**标题栏的样子只能在真机上看**：无头会话里库不接线，截图拍不到它。

**`DataTemplate` 里不要写 `Name`。** 模板里的 `x:Name` 注册进的是**同一个名字域**，不是每个实例一份：
第一次实例化没事，第二次就抛 `Control with the name 'KeyField' already registered`。症状是运行到时
才炸、栈里指着模板而不是指着那一行，而且**只有第二次打开才出现**——第一次手工点一遍是绿的。
要按元素找，用 `Classes` 加 `DataContext` 匹配（`ActionDialogTests` 找 `KeyField` 就是这么改的）；
真需要名字，用 `x:Name` 之外的定位方式，或者把名字挪到模板外的宿主元素上。

**按类名找元素时，必须同时限定具体类型，不用基类。** 写成 `OfType<CheckBox>()` / `is CheckBox`，
不写成 `OfType<Button>()` + `Classes.Contains("Field")`。

原因：`CheckBox : ToggleButton : Button`。凡是按基类捞的定位，**加上一个新类就可能从"唯一"变"多个"**
——而报错是 `Sequence contains more than one matching element`，发生在离改动很远的一扇窗里。
`Button.Field` 与 `CheckBox.Field` 的选择器各自独立、互不干扰，**只有"按类名 + 基类"的找法分不开**。

实例：给勾选项挂上 `Field` 那天，`OfType<Button>().Single(Classes.Contains("Field"))` 从唯一变两个，
三条热键用例当场红；同类的四处没红，但同样脆弱，一起排掉了（`is not ToggleButton`）。

这是"同一个词两种载体"的第四种形状（前三种是 `Cell`、`VariableList`、`MenuCell`）：
前三次是**不同控件撞名**，这一次是**同一族里有继承关系**——最隐蔽，因为选择器那一侧完全正常。

**在 `Ui.Run` 的 dispatch 里再包一层，会让发现阶段卡死。** 试过把"收尾检查"包在 dispatch 回调外面
（哪怕只是一个空的 `try/finally`）：`dotnet test` 停在 `Discovering: WhaleGenie.Tests`，进程 0% CPU，
既没有输出也不超时。二分过一次，别再试同一条路——要加挂机点，加在测试体里，不要动 dispatch 的形状。

**改类名、加类名之后一律跑全量。** 本窗那几十条用例只能证明"这一窗没坏"，证明不了"别处没坏"；
而撞名的本质就是"别处会坏"。四次撞名里有三次都是先跑本窗（绿）、后跑全量才发现红——
这两条要一起有：前一条防"加了新成员就炸"，后一条防"炸了没发现"。

### 关掉会发问的窗口：就地作答，不要走开

`ConfirmDialog` 是模态的：它在当前 dispatch 里再起一个调度器循环。用例关掉一扇会在关闭时发问的窗
（`MacroEditorWindow`、`GlobalVariablesWindow` 在改动过时，`MainWindow` 在项目没保存时）却不回答，循环就
留在调度器上——**下一条用例**一 dispatch 就抛 `InvalidProgramException: You've caused dispatcher loop`，
受害者与肇事者不是同一条，单条耗时还会涨到 5～11 秒。所以关窗一律走这两个入口：

```csharp
Ui.CloseAndAnswer(window);      // 关窗，并把这一窗自己弹出的问话答掉
Ui.AnswerUnsavedWork(window);   // 只答掉已经弹出来的（例如「这段录音是空的」这类提示）
```

问话的主人是**发问的那扇窗**，所以答的是它自己的 `OwnedWindows`：管理器窗发的问不在外层编辑器窗的
`OwnedWindows` 里，`Closing_the_manager_without_save_changes_nothing` 最早就是扫错了地方（断言全绿，因为
它压根没扫到）。

想在检查里直接给出答案的，用注入点比事后答题清楚：`MainWindow.AskToSaveProject = () => Task.FromResult(ConfirmChoice.Secondary)`
（丢弃＝窗真的关掉，恢复快照也跟着收走）；`GlobalVariablesWindow` 的保存与丢弃两条路见 `GlobalVariablesTests`。

判据是"关窗前真改过东西"：`MacroEditorViewModel` 几乎对每个不在白名单里的属性变化都 `MarkDirty()`，
`AddStep` 也算（`HasSteps` 不在白名单里），所以"只是加了一步再关窗"一样会问；`GlobalVariablesViewModel.HasChanges`
看的是每行的改动与名字序列，删回原样才算干净。

**不要用固定睡眠等一件会自己发生的事**。两种都踩过：`Thread.Sleep` 造两次点击之间的间隔
（`Double_clicking_a_step_inside_a_block_edits_that_step`），以及 `Task.Delay` 等一个定时器
（`Moving_a_surface_is_saved_once_after_the_drag_stops` 等浮窗拖拽结束的 120 毫秒静默期）。前者机器一忙
就被拉过双击窗口、两次点击变成两次单击；后者机器一忙就睡过了那条定时器还没到。做法：间隔只为把已排的活
跑完，泵一轮队列就够；等一件事就等那件事本身（事件 `TaskCompletionSource` 或 `Ui.Until`），超时再失败。

### 手动项

无头会话走的是 Avalonia 自己的输入管线，不是系统里的鼠标与键盘：真的注入按键、抓屏取色这类事情在那里验不了，
只能人过一遍。所以**能在无头里跑到的部分要有用例**，只留人眼能判的给手动。下列各项每项按环境（Windows 版本、
DPI、显示器数量、Edge/Python/AutoHotkey 版本）、步骤与观察结果三项记进提交信息；只写"已手动过"不算结论。

- **浅色主题下这三处小字能不能读**（三级灰落完之后最淡的三处，**只有真机能判**）：
  ① `VariableWall` 芯片上的字（三级灰压 `SurfaceField`）；② `DelayScaleWindow` 那行附注（压 `SurfacePanel`）；
  ③ **`RunWindow` 的时间戳**（11px + 三级灰 + 压 `SurfacePanel`）——**这条最容易糊：小字、最淡的灰、面板底**。
  判据是"**读得出来**"，不是"看着还行"。
- **两套主题各看一次这两处的对比度**（**只有真机能判**：令牌名对不对读代码能判，浅灰底上黑字够不够清楚判不了）：
  ① 设置窗 `PageTab.selected` 的**选中页签**（底 `AppBorder`、字 `TextPrimary`）；
  ② 主窗 `BottomButton` 的**底部按钮**（底 `AppControl`、字 `TextPrimary`）。
  看的是"**字与底**"：暗色主题下底深灰、字近白；浅色主题下底浅灰、字近黑，两套都要能读。
  这两处原来是硬编码白字，是**只在暗色主题下正确**的潜伏 bug，换令牌之后必须两套都过一眼。
- **二维码扫码**：关于页两张码（微信 / 支付宝）**各用手机扫一次**，能读出内容即过。
  它们垫的是硬编码白底——扫码器要白，这个白**不随主题变**，是外部约束不是配色。
- **驱动级输入**：真机装一次 usbip-win2；勾上 / 不勾「启动时先把虚拟键鼠装上机器」各跑一次；点击、打字、
  拖拽、滚轮与手柄各一次；退出程序后看设备管理器里虚拟设备是不是没了。
- **真实输入**（用现成的窗口，不用专门的靶子）：记事本里点一下看光标落没落对、打一行字看字有没有出来、
  拖选一段看选中的是不是那一段；抓屏取色在一个已知颜色的地方取一次，看取到的色值对不对。
- **五个拾取器**（框选区域、取色、点的拾取、元素拾取、窗口规则）各进出一次：打开时鲸灵自己的窗口（含问话
  的那个对话框）都不在屏幕上；一开就拿到焦点，Esc、回车与右键当场有效；拾完窗口按原状回来。其中一次在
  添加动作窗口里拾，看它自己也回来（它是编辑器的属主窗口，收起时跟着一起走）。
- **框选器**：打开时屏幕是原样、指针看得见（底图定格在那一刻）；一拖出框才框外压暗、框内照原样；框选之后
  截一张，图片里没有鲸灵自己的窗口。
- **能拖的窗口**：识别结果与偏移拾取窗口的标题栏都能拖着走（标题栏拖动只在手动这一档，无头里只验接线）。
- **YOLO 三步**（自备一份公开的 yolov8n.onnx 与类名表）：检测一次看框落在东西上；等待检测在限时内等到与
  等到超时各一次；点击检测到的物体看落点在框中心加偏移，并记一次检测耗时。
- **脚本**：三种窗口模式（留着 / 关着 / 不显示）、批处理、PowerShell、AutoHotkey 各一次；AutoHotkey 还要
  在一台没装它的机器上看一次报错；Python 脚本与安装 PIP 包各一次（装的包能 import）；C# 脚本与下载 NuGet
  包各一次（装的包能编进脚本）；断网时看两个依赖动作各报什么。
- **表达式编辑器**：把变量、函数与树上已有的一支拖到位置上（拖到空位＝填入、拖到已有的一段＝换掉、拖自己
  的一支进自己的后代＝不理会、拖到窗口外＝取消，落点要跟着亮）；1080×720 下四段分区与「翻译结果」一行排得
  下、滚动正常；函数卡片按分类 dock 只剩一组、颜色六个组两两不同、悬停气泡出得来；深嵌套公式的那句人话
  读起来顺。
- **拖放编排**：从四个 Dock 各拖一次到空列表、行前、行后与单列表块内；高亮插入带写明落点、拒绝落点、动作
  窗口拖动与缩放、取消不插入、保存后落点正确；保存按钮不关闭编辑器、连续保存不重复添加宏；同一公共宏同版本
  拖两次、改出一版后拖一次，确认复用与新建副本的页签和来源版本；重启程序后公共宏库仍可列出。

环境不同就重跑受影响的那一项，不要把一次通过当成所有机器都通过。

每加一个动作至少有一条引擎用例走到它；用户可见文案由 `LocalizationTests` 卡住两边齐全。

## 结案判据：查不到就等于"不是残留"

读到一处"不该出现的颜色 / 尺寸 / 文案"，第一步永远是**在代码里搜那个值**。**搜不到，就结案。**

- 搜得到 → 它是有出处的，改它或判它都行；
- **搜不到 → 它是渲染出来的混色**（alpha 叠加、抗锯齿、两层背景叠加），**不是"没走令牌的字面值"**。

实见：截图里两处可疑颜色 `#D4F2D4`（淡绿字）与 `#863333`（暗红条），在 `Views/`、`Themes/`、全部 `.cs` 里
**一次都搜不到**，参照令牌（`Success #7FC97F`、`Danger #F07A84`、`AccentFill #C8401C`）也不等于它们——
**所以它们是混色，结案**。

**别花第二轮去反推"它叠的是哪支色、alpha 多少"**：反推的成本远大于"知道它是混色"的收益，
而且反推出来的结论**没法用**——下一张截图的角度一换，混色就变了。

## 静默失效：四种形状与三条纪律

**一条规则必须能失败，否则它只是描述。** 下面每一条都能 grep、能数、能红；加新规则时先问一句"它怎么判不过"。
**判不过之后还要有一个动作**——只说"注意确认"的规则永远判不出过不过，因为它没有失败时的下一步。
把"确认属性存在"写成"找不到就把这一格改成由谁承担"，规则才闭得上。

**本节编号只在本节内有效。** `doc/dev.md` 里不止一组编号列表（「改一个动作的固定顺序」也有一组），
所以跨节引用一律写**节名 + 条目名**（`静默失效 · 形状 4`、`静默失效 · 纪律 1`），不要只写"第 4 条"。
这一节的条目都是"不报错、不告警、只在屏幕上错"的那一类——编译器、测试和界面都不会告诉你。

四种形状：

1. **字节对齐只解一种。** 主题包的 DLL 里资源键是 UTF-16 奇偶混排，只按一种对齐解码会整批漏键，漏掉的恰好是
   "看起来不存在"的那批，于是得出"这个机制不可用"的**反向**结论。扫这类 DLL 要 shift=0/1 各解一次取并集。
2. **只统计一种写法。** 只 grep 属性形式 `FontSize="x"`、漏掉 `<Setter Property="FontSize" Value="x"/>`，
   基线就偏低，"没达标"其实是量错了。任何属性都要确认两种写法都覆盖。
3. **运行时才加的类名。** `Classes.Add("MenuCell")` 加的类在 XAML 里零出现，静态阅读看不见。判一个类名的死活
   要逐类单独 grep，并搜代码里的字符串形式。
4. **颜色挂错载体。** `Foreground` 只对继承自 `TextElement` 的元素有效；`Path`/`Ellipse` 吃 `Fill`，图标吃
   `Stroke`。属性名对、语法合法、就是不生效。
   **同族实例（类名版）：** 同一个类名可以落在**不同载体**上——`VariableList` 在 `VariableWall` 是 `ScrollViewer`、
   在 `GlobalVariablesWindow` 是 `ListBox`。所以按类名找元素、数元素、改元素时，**必须同时限定载体类型**；
   只按类名找会既数错又改错，而错法取决于哪一处先被读到。

四条纪律：

5. **删 `DynamicResource` 的键之前先把引用改完。** 缺失的键不抛错，只静默回落到默认前景——暗色下图标可能暗到
   看不见。顺序永远是"先改引用、后删键"，删完用全局搜索确认零残留，不要靠编译通过来判断。
6. **往样式里写一个资源键之前，先 grep 它存在。** 命不中只有两种可能：代码该补，或者文档该删；**两者都是真问题，
   而"跑起来都对"会把它藏住**（透明这类缺失即静默正确的最容易藏）。样式文件里出现的键名同样要逐个核。
   **同族实例（选择器版）：** 某处写过 `ListBox.X > ListBoxItem`（`>` 只配**直接子代**），而模板在中间插了一层
   ——**选择器配不到元素，样式静默不生效**，症状与"键不存在"完全一样。所以键存在之后还要问第二句：
   **这个选择器配得到元素吗**（拿实际元素去对，别只看声明写得对不对）。
7. **验外观要读模板里被涂色的那个部件，不是控件自身的属性。** 控件自身的 `Background` 在任何伪类下都只有静息值；
   按钮的状态在模板的 `ContentPresenter` 上、输入框的描边在 `Border#PART_BorderElement` 上。读错观察点会给出
   "确定但不成立"的结论——它比"没验"更危险，因为结果看起来完整，人不会再查第二遍。
   **同族实例（结论版）：** 本轮有人从"有 11 个颜色值跨 9 个窗口"推出"这套界面有语法高亮"——**数字真、结论假**；
   查过之后发现没有任何按词法着色的代码，那 11 个值是动作类别色与状态文字。所以**结论也要带动作**：
   写"我查过 X"而不是"X 是"；判据是**把这句话交给别人，他能不能重做一遍那个动作**。

   **再进一步："有据"不构成"答对"**——见本节第 19 条。

8. **删或改一个类名之前，先 grep 那个名字本身。** 不是只 grep 这批的主类——测试里常有
   `Classes.Contains("X")` 这种按类名找元素的写法，改了标记没改查找，症状是"找不到元素"而不是"样式不对"。
   代价实见：删掉一个从没定义过的修饰类，让两条用例报 `Sequence contains no matching element`。
   **名字被用作"要不要检查"的标记时，改名等于关掉检查**：`if (!text.Contains("X")) continue;`
   这样的写法在名字消失后对每个文件都跳过，测试全绿而覆盖率归零。所以标记型检查必须再断言
   **"命中的文件数大于零"**，否则标记一失配，检查静默变成空跑。

9. **批量替换按整串匹配，会漏掉带修饰的组合写法。** `Classes="Key Cell"` 与
   `Classes="Key Small Cell"` 是同一件事（`Cell` 是修饰），按整串替换只命中前者；后者留着旧类名，
   而新那一族里没有它——**症状不是"类找不到"，是"一部分元素掉回默认值"**：虚拟键盘那 11 个键
   掉回 `Width=32`，`PrintScreen` 只有 30 宽却要装 60.4 的字，靠"同一列宽度不一致"才发现。
   **替换要拿"类属性里的某一段"作匹配**（`Classes="[^"]*\bKey\b[^"]*"`），不要匹配整串；
   改完 `grep -c` 数两边的处数对不对——"改了 19 处"和"应该改 30 处"的差额才是这条规则在说的东西。

   **同一族里长的名字先换。** 把 `App*` 换成语义令牌时，`AppAccent` 会先把 `AppAccentSoft`
   咬成 `AccentSurface` + 尾巴 `Soft`，得到半截名字——**替换的匹配范围决定了它是替换还是误伤**。
   所以按名字长度倒序处理（`AppAccentSoft` 先于 `AppAccent`），换完再 `grep` 一遍旧前缀确认归零。
10. **读标记的用例钉令牌，不钉值。** 断言形如"这一处是红的"，写成
   `Assert.Contains("{DynamicResource Danger}", …)`，**不要写成 `#E06C75`**。
   理由：**值会变，语义不变**。钉值时用例红在"值变了"，钉令牌时红在"语义没了"——后者才是它想守的。
   实见：`#E06C75 → Danger` 那天，`RunBreakpointTests` 与 `VariableWarningTests` 两条同时红，
   而它们要守的东西一点没变。
   （再进一步是"钉它引了某个语义令牌，不钉具体令牌名"——令牌名也会改，`Problem → Warning` 就是。
   那一层先不做，遇到再说。）
11. **注释与代码不一致时，不要默认代码是对的。** 默认假设是：**注释写下的是当时的意图，
代码是当时的实现**，两边都要看。`MainWindow` 那条 `Chrome.Icon.Active` 旁边写着
*"No Active state: there is no server in this window to be on or off."*，而样式一直在——
**注释说明了"这个状态不存在"，代码却还留着它**。若默认"代码是事实"，就会反过来把注释删掉，
把一条死样式当成"确实存在的状态"留下来。
（这与我们一贯"代码是事实"的习惯相反，所以值得单独记；它只在**注释与代码讲的是同一件事、
却互相矛盾**时有意义。）
12. **加类之后必须删掉那行赋值，否则等于没加。** C# 里创建的控件，`new TextBlock { FontSize = 13 }`
   设的是**本地值**，本地值压过样式——**屏幕上没变化，代码里那行还在骗人**。
   所以"换类"这个动作是两半：**加 `Classes`** + **删 `FontSize=` / `Foreground=` / `Margin=`**。
   实见于 `VariableFieldPickerWindow`：换 6 处类、删 9 行赋值；验收标准就是"这个 `.cs` 里
   `FontSize =` / `Margin =` / `Foreground =` 一处都没有"。
   它与"删样式前先看它还带了什么"是一对：**一个防漏删，一个防漏加。**

13. **`Brush(key, fallback)` 里的 fallback 是裸值温床。** 它**平时不生效**（资源在就不走它），
   **换主题也不跟着变**，却让人以为"这里的颜色已经处理过"——**不起作用，但让人以为起作用**，
   比写死在 `Foreground=` 上更坏（那个至少还在起作用）。
   所以扫裸值不能只 grep `"#RRGGBB"`：**参数位置上的 `Color.FromRgb` / `Color.FromArgb`
   要单独看**。这一条已做成报告扫描（`ClassAuditTests` 的第五条），按"直接设色 / 回退参数"分开列。

14. **换类之后，删掉那些"和类表达同一件事"的属性。** `Opacity = 0.62` 配 `Caption`（三级灰）
   是**把事情说了两遍**：叠加之后比设计意图更暗。与"一色两用"同族——那次是两种含义共用一个值，
   这次是两种手段共用一个效果。同类还有 `FontWeight`、`TextWrapping`、`TextTrimming`。

15. **裸值永远报两个数：债务型 / 判定为字面。** 只报一个数，要么看起来"还没做完"，
   要么掩盖"还有多少真的没处理"。判定为字面的那类**不是欠账**——主题色样（描述别的主题长什么样）、
   二维码白底（扫码器要白）、半透明覆盖层（要透出底下的画面）、画布底（图还没画上去时的那块）、
   用户数据的配色（`palette` 的键）——**它们"必须字面"，换成令牌就是错的**。
   指标写错会把"判定"误读成"欠账"：下一次有人来"清理"它们，把画面盖住、把标签字变白看不见。

16. **向别人描述影响面，别推断"对方会注意到什么"，报可数的东西。** 写交付说明时按"影响窗口数 /
    影响元素数 / 涉及文件数"排，不按"我觉得用户会不会注意到"排。
    **注意力是回忆，窗口数是数得出来的**——注意力是读者的事，覆盖面是写的人能负责的事。
    而且**"没变"也要写**：读者看到某处没变，第一反应是"是不是漏了"；写下来他才知道那是判定，不是疏忽。
    实见：第一版交付说明按"注意到程度"排，漏掉了影响面最大的两条（18 个窗的标题栏按钮、
    17 个窗的次按钮高度）——因为它们在最早那几批，而写说明时手边只有最近的提交。
    **越早做的东西越容易被当成"本来就这样"。**

17. **XML 注释只能放在元素之间，不能放在属性之间。** 把 `<!-- … -->` 插进属性列表
   （`<Border Width="12"\n<!-- … -->\nBorderBrush="…">`）会让 Avalonia 报
   `AVLN1001 / 元素语法错误`，**而错误信息不会告诉你"是注释放错了"**——它只说 XML 解析失败。
   实见于给拾取器那五处加"判定为字面"的注释时，一次编译直接炸。
   **加注释要加在元素上方或元素之间，不能加在元素自己的属性中间。**

18. **"改结构能消掉 N 处"这个判断，要先验证那 N 处是同一件事。** 否则消掉的只是颜色，不是问题。
   实见：`RunWindow` 那 9 个自绘色看着像"第 1/2/3 次尝试各一个色调"（序数着色），
   一查是**六件不同的事**——谁接手输入、谁接手浏览器、注释、问你、速度徽标、时间戳。
   分清之后每处都落到了该在的地方（灰阶 / 运行位置 / `Info` / 新语义），
   **这比"改成序号把颜色删掉"好：删掉之后"它们各自是什么"这个问题还在。**

19. **"有据"不构成"答对"——它证明我查过，不证明我查对了题。**

    最危险的错不是数错，是**数了一个不相干的东西**：它干净、可复现、有出处，于是没人再问它和题目有没有关系。
    **处数要数，档数要问。**

    - **处数**（这个值还有多少处、分散在哪些文件）——**数出来的**，因为它是事实，`grep -c` 就够；
    - **档数**（这一族该分几档）——**问出来的**，因为它取决于"要区分什么"：问"它在说位置还是说状态"、
      "它属于哪一级灰"。

    把档数当处数去数，就会数成"**有几种写法**"，而写法不是档位：8 个灰值数出"8 档"、实际 **3 级**；
    运行位置族 4 套写法、实际 **2 件事**。**"报可数的东西"只对处数成立**——对档数，可数的是"几种写法"，
    而那正是答错题的样子。
    判据：拿到一个数先问**"它回答的是我问的那个问题吗"**。

    **动作必须是可执行的检查，否则不算动作。** "我想了想"不是动作——它既不能被别人重做，也不能被自己重做。

    > **能写成命令的判断 → 自查**（`grep` 一个值还在不在、数一个类有没有消费者、跑一次扫描、跑一次全量测试）；
    > **写不成命令的判断 → 必须由别人看**（"这句注释读起来像不像在推卸""这个措辞会不会让人误解"
    > "这个档位名会不会诱导下一个人排档"）。两类都不能省，但**不能混**：把"写不成命令的"当自查，等于没查。

    实证：本轮自己抓到的三处（`.picked/.drop` 能否同现、缺变体清单十三条里六条判错、位置指认错）背后都有命令可跑；
    被对方抓到的三处（`#8FB3D9` 主客颠倒、`#C8A165` 一色四义、S/M/L 排档）全是"读起来对不对"——没有命令可跑。
    **所以"抓到"与"仔细"无关，与"有没有命令"有关。**

20. **判据要先验证它描述的对象存在，否则它就是一张通行证。** 一条正确的判据套在一个**不存在的类别**上，
   会变成一个"**合法地跳过检查**"的理由——**这比没有判据更危险**：没有判据时人还会去看，有了判据就不看了。
   实见：有人定了"主题预览里的色值天生是字面值、不进令牌"（原则对），但主题预览**早已改成数据驱动**
   （`ThemePalette`），axaml 里**根本没有字面色样**。于是"数据类 = 主题色样 6 处"这个分类从头就是空的，
   而那一批值里有 **4 处是真债务**（"运行中"徽标的底/边/字、一个状态点）。
   **查要在"前提"上查，不在"结论"上查**——结论错了往往看得出来，前提错了看不出来，因为它是你往下走的那一步。

21. **引用先例前，先确认那个先例有消费者。** 引用一个先例等于给结论加一份担保；**担保是假的，结论就没人复核了**。
   实见：写"与 `TextBlock.FieldLabel.Strong` 同一套判断"时，那个类**是个空类**——声明了、从没人用。
   先例要能被点开看，否则它只是"听起来有依据"。

22. **清单是快照，代码是现状。** 拿清单落地前逐条 grep 一次"它说的那个东西还在不在"，不在的标掉、别照落；
   **别去"维护"清单**——维护会让它变成第二份代码，而第二份代码一定会漂。**标注比维护可持续。**
   实见：清单写"芯片行 `Padding="4,2"` 缺变体"，实际它还在，只是写成了 `<Setter Property="Padding" Value="4,2" />`
   ——**grep 要覆盖四种写法**：行内属性（`Padding="4,2"`）、`Setter`（`Value="4,2"`）、`Classes` 里的类名、
   代码里（`Classes.Add`/初始化器）。**只查一种会得出"它已不在"的错误结论，而那个结论会被写进文档**——
   标错的作废记录比不标更糟：它让人以为东西已经不在，于是不会去看。

23. **"加类"这个动作有前置检查，它先红给你看，别绕过它。** 库里加一个类会惊动
   `ControlsRegistryTests`（自己画 hover，或进登记表）；写了没登记、也没画 hover 的类会在这里先红。
   **先失败、后补是正确的顺序**——它们是"加类"这个动作的护栏，不是障碍。
   配合第 20 条：**登记表本身也要能被对账**（"登记了但库里没有"与"库里有但没登记"两种错都不会自己暴露）。

24. **加一条检查之前，先问"反方向是不是已经有人查了"。** 写检查的人往往只想着自己发现的那个方向，
   于是留下半个闭环。实见：新加的"登记了却不存在"与已有的"该登记而没登记"是同一件事的两半，
   **两条合起来才闭环**。
   反过来也成立：**反向检查若不知道"哪些不需要检查"，它报的全是日常在用的东西**——
   那种报告比不报更糟，**它会让下一个人把整条检查关掉**。

25. **裁定给出后要写"到此为止"。** 否则对方会以为还能再问，于是同一件事问两遍、或者被反复重开。
   实见：`MarkRow.chosen` 该不该并进 `Border.Row` 连问了两轮，而答案第一轮就该定——
   **"这个问题已经答过、不再讨论"本身是裁定的一部分。**

26. **删库类要问三件事，缺一条都不能删。**
   ① **现在有人用吗**（消费者扫描；有 → 不删）；
   ② **是模板件吗**（库内搜那个词、排除它自己的 selector 行；是 → 补登记）；
   ③ **有元素本该挂它、却没挂上吗**（搜"合乎它形状的元素"；有 → **不是"没人用"，是"没接上"，要接**）。

   第三条是这轮被 `GridSplitter.Split` 教出来的：**它不是"没人用"，是"该用的地方忘了挂"**——
   而它和 `Thumb`（真没人用）在扫描输出里长得一模一样。**扫描只能回答第 ①问；②③要靠人按形状去找。**

26-b. **防"自己满足自己"的规则，会同时挡住"库内真的引用"——这两者用同一个判据分不开。**
   给库声明定的规矩是"不拿自身文本当消费者"（防自己满足自己），但它**同时挡住了库内部模板对某个类的引用**，
   于是那些类显示"0 消费者"而实际正在被渲染。
   **同一个动作既防了假通过，又制造了假否定——这是"一个判据同时带来一个盲区"的又一例。**
   只能靠"**排除自身 selector 行后再搜一次**"来分：搜到了就是真引用，搜不到才是真没有。

27. **改类名之前，先 grep 测试与代码里的旧名。** `Classes.Contains("旧名")`、`Classes.Add("旧名")` 都是把手。
   实见两次：`PageTab → Tab.Inline.Boxed` 打断了 `ActionDialogTests`；`Category → Rail` 打断了
   `ActionPaletteCategoryTests`（`Expected 11 / Actual 0`）。**"改类名"与"删类"在打断把手上完全等价**，
   而这轮两种都撞过。

28. **凡是"换"的动作，落地后 grep 一次旧名，必须为 0。** "换类/改名/迁移"都有**两半**——**删旧的 + 加新的**。
   做完一半时，**屏幕上往往看不出问题**，所以不会有人回头。实见两次"报了没做"都不是懒，是"改了一部分就以为改完了"：
   `#BFE3BF` 是统计了没替换，`Category → Rail` 是删了本地定义没换元素。**这条比"仔细点"可执行。**

29. **未生效的样式等于未测试的代码；接上它等于首次执行它——而且"首次执行"常会发现它只按一种用法写过。**
   实见：`GridSplitter.Split` 的三条样式从写下起就没生效过；接上之后才暴露**它是照"竖条"写的**——
   `Width=6` 对一根横向分隔条就是"把它压到抓不住"，而**界面上不会报错**。
   **"写它说明有人想要"推不出"它写对了、写全了"。**

30. **样式不许设"由布局决定"的维度。** 宽 / 高 / 对齐属于**使用场景**，不属于**外观**：
   同一个类会被横向和纵向的分隔条共用，统一给 `Width` 必然错一半。
   **样式只写颜色、边框、悬停**；尺寸写在使用者身上。**这类错没有报错，只让某个元素抓不住。**

31. **`ThemeBrush.Of` 是快照，属性一律走 `DynamicResource`。** 令牌都在 `ThemeDictionaries` 里，
   `TryFindResource` 不带 `ActualThemeVariant` 就取不到——这个函数为此静默失败过很久，
   而**调用方的兜底把它盖住了**（于是"窗有底色"看起来正常，删掉兜底才暴露成透明）。
   两条路分工：**设属性用 `ThemeBrush.Assign`（会跟着主题变）；要一个 `IBrush` 对象（`Pen`、绘制）才用 `Of`，并知道它是快照。**

两条与记录有关的：

- **报告构建或测试状态，必须同时写产物路径与时间戳。** 本仓库有 `bin/<配置>` 与 `bin/x64/<配置>` 两条输出布局，
  只看一条会得出相反结论（"从没编译过"其实编过，只是落在另一条路径上）。
- **贴数字表必须标"哪个值对哪个底/哪个令牌"。** 数对但位错比没数更难查——下一个人会拿它去算余量。

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
| 互斥与调用图 | `MacroRunnerTests` + `DeviceActionTests` + `MacroCheckTests` | 子宏自己的互斥声明生效；调用图分析带环检测；持浏览器门时调用要页面的宏被拒；`control.holdInput` / `control.holdBrowser` 的块内步骤不再取门，嵌套只记层数 |
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
| `macro` | 都是“调用哪一个宏”；「执行子宏」这一格叫「子宏」，已退役的「启动其他宏」仍写「宏」 |
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

规范性的硬规则（文案、目录、字段名、结果清空、依赖方向）在 `AGENTS.md`，这里只写实现时容易踩的。

- 格式与风格写在 `.editorconfig`，CI 跑 `dotnet format --verify-no-changes` 卡住；共用构建属性在
  `Directory.Build.props`，单个工程只留自己特有的部分。
- **模态窗口的答案由窗口自己留着**，不从 `ShowDialog` 的返回等：屏幕拾取器在出场的整段时间里把鲸灵的窗口
  全收起来，问话的那个对话框也在内，而隐藏一个模态窗口会让 Avalonia 立刻以空值结束那一层模态循环——
  对话框还在屏幕上、步骤还没写完，等循环的人就已经走下去了。`ActionEditorWindow` 因此把答案放在 `Answer` 里。
- **动作窗口的「运行」与字段旁的测试**同拾取器一套规矩：`AppWindows.StepAside` 收起全部窗口、
  `ScreenComposed.Wait()` 等桌面重画，再读屏幕或发输入，结束时按原状回来。只最小化本窗口不够——属主编辑器
  还在它后面挡着要被试的那块画面。
- **`MacroRunner` 按动作家族拆 partial**：主文件放状态、运行循环与按 `Category` 分派的入口，每个
  `MacroRunner.<Family>.cs` 放**该家族的分派与实现**；跨家族共用的读值集中在 `MacroRunner.Reading.cs`。
  新增动作进对应家族文件：目录加行、家族分派加 case、实现写在旁边。
- **周期与生命周期**：会话（屏幕抓取、OCR 模型、浏览器）挂在 `SharedDevices` 上到退出才释放；一次录制的
  录制器用完即弃，不长期持有。
- 行列表的列名、区域四格这些跨文件重复的键用常量类，不散落字符串字面量。

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

标签推上去之后，工作流会打两个包——一个要机器上有 .NET 10 桌面运行时，一个
自己带着运行环境——自动建好 Release 并把两个包挂上去。标签名同时决定了压缩包的名字和 exe 里的
版本号，所以只有一处要改；想在网页上发也行：Actions → Release → Run workflow，填一个版本号。

建 Release 需要写权限。仓库若把 Actions 的默认权限设成了只读，要在
Settings → Actions → General → Workflow permissions 里放开一次。

两种包差别只在"要不要自己带运行环境"，其余完全一样：

| 包 | 自带运行环境 | 体积（x64 打包后 / 解压后） | 什么时候用 |
| --- | --- | --- | --- |
| `WhaleGenie-<版本>-win-x64.zip` | 否，要装 .NET 10 桌面运行时 | 116 MB / 299 MB（换 stdio 前的数） | 体积最小；已经装过运行环境 |
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
