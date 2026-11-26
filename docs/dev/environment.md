# 构建环境与依赖

所属：[WhaleGenie 开发说明](../dev.md)。

## 常用命令

```powershell
dotnet build WhaleGenie.slnx -c Release
dotnet test WhaleGenie.slnx -c Release
dotnet format WhaleGenie.slnx --verify-no-changes   # 格式检查；让它先还原（界面自省包只在 Debug 还原）
dotnet build core/WhaleGenie.Core.slnx -c Release
dotnet run --project src/WhaleGenie
pwsh build/site.ps1
```

网页壳（`webui/`）用 npm 构建，构建界面工程时会自动执行：网页源变了只重新构建；只有
`package.json` / `package-lock.json` 变了才先 `npm ci`。`npm ci` 会重装 `node_modules`，开着
`npm run dev` 时先停掉开发服务器，否则正在使用的 esbuild 可执行文件会被占用。

```powershell
cd webui
npm install          # 首次或 package.json 变化后
npm run dev          # 带 mock 宿主的热重载开发，浏览器直接打开
npm run test         # Vitest 单测
npm run lint         # ESLint；新增违规按错误失败，旧违规在 eslint-suppressions.json
npm run check        # lint + 单测 + 类型检查 + 构建
npm run build        # 类型检查 + 单文件构建，产物 dist/index.html
npm run e2e          # 端到端冒烟（系统 Edge，走构建产物的 preview；CI 在构建之后跑同一条）
```

网页壳是默认窗口；装不上 WebView2 Runtime 的机器回退原生窗口。DEBUG 下
`--webui-dev[=http://localhost:5173]` 让壳加载 Vite 开发服务器而不是打包页面。只想跑引擎测试
而本机没有 Node 时，用 `-p:WhaleGenieSkipWebUi=true` 跳过网页构建目标。

`core/` 是库（引擎、画面获取、Windows 适配器与它们的三套用例）的独立仓库，以 `git subtree`
引入到这里：**它不在这里改**，改动去 [ilpoint/WhaleGenie.Core](https://github.com/ilpoint/WhaleGenie.Core)，
再 `git subtree pull --prefix=core core main --squash`。库的构建设置与包版本跟着它自己走
（`core/Directory.Build.props`、`core/Directory.Packages.props`），打包与发布也在那边的 CI。
分发关系见 [core-api.md 的分发一节](../architecture/core-api.md)。顺序是先合并库仓改动，再由
`sync-core`（每日）或上面的 `git subtree pull` 取回；主仓里的 `core/` 目录不要提交改动。

解决方案只提供 `x64` 平台。根目录 `Directory.Build.props` 统一设置目标平台并将警告视为错误，
因此构建通过也意味着没有编译警告。分析器只对产品工程启用 Recommended 集，测试工程除外；
不采纳的规则连同理由写在同一文件里，新增例外须给出理由而不是加进 `NoWarn`。

运行测试时不要混用解决方案构建与 `dotnet test <工程> --no-build`：两种调用可能使用不同的输出
目录。直接运行 `dotnet test <工程> -c Release`，或为构建和测试显式指定相同的
`-p:Platform=x64`。

动作矩阵与 MCP 自检分别使用：

```powershell
$env:ACTION_MATRIX_REPORT = "TestResults/action-matrix.md"
dotnet test core/tests/WhaleGenie.Core.Tests -c Release --filter "FullyQualifiedName~ActionBench"
Remove-Item Env:ACTION_MATRIX_REPORT

dotnet run --project src/WhaleGenie -c Release -- --mcp-doctor --report TestResults/mcp-after.md
dotnet run --project src/WhaleGenie -c Release -- --mcp
```

报告属于本机验证产物，不提交到仓库。MCP 服务使用标准输入输出，不监听网络端口；权限来自
`settings.json`，命令行不提供权限参数。

### Avalonia 文档 MCP

仓库根 `.mcp.json` 配置只读文档服务 `avalonia-docs`（HTTP `https://docs-mcp.avaloniaui.net/mcp`）。
Visual Studio 2022 17.14+ 或 Visual Studio 2026 打开解决方案时按解决方案根的 `.mcp.json` 自动读取，
GitHub Copilot 的 Agent 工具列表里应出现它。`.vscode/` 按 `.gitignore` 属于个人编辑器配置、不进仓库；VS Code 的原生 Copilot 使用
`mcpServers` 结构，不能直接复用这份 `servers` 配置，需要时按客户端文档另配。

该服务只提供 Avalonia/FluentAvalonia 的 API 与控件文档查询，不参与构建，也不覆盖
[DESIGN.md](../../DESIGN.md) 与 `Themes/` 的视觉决定。

### MCP 自检口径

`--mcp-doctor` 用固定场景覆盖状态读取、草稿、动作查询、写宏、校验、应用、保存、局部补丁、资源、
权限拒绝和清理。比较修改前后的报告时必须使用同一场景与命令；动作数、工具数、字节数等随实现变化的
数字只留在本机报告中，不另维护一份长期基线。带权限与无窗口路径都要覆盖，跳过项须能由权限状态解释。

`McpSelfCheck` 负责采集并尽量继续执行，不用自检代替行为断言；正确性由 MCP 测试、引擎测试和真实桌面
验收负责。若失败来自自检脚本或样本宏，先修测量，不能为得到好看的数字改变判定标准。退出码为
0（全部通过）、1（有失败）、2（参数错误）、3（无法通信）、4（子进程无法启动）。

## 工程结构

仓库根只放仓库级文件：解决方案、`Directory.Build.props`、`Directory.Packages.props`、
`global.json`、`.editorconfig`、脚本与文档。8 个产品工程在 `src/`，8 个测试工程与两个支持库在
`tests/`。目录只决定文件放在哪：工程名、程序集名、根命名空间与 `avares://` 地址都不随目录变化。
所有 NuGet 包版本集中在根上的 `Directory.Packages.props`（中央包管理）；工程文件里的
`PackageReference` 只写 `Include` 和它真正的条件（仅在 Debug 还原、`IncludeAssets`/`PrivateAssets`），
不写 `Version`。

| 项目 | 职责 |
| --- | --- |
| `WhaleGenie` | Avalonia 与 FluentAvalonia 界面、窗口与 ViewModel、窗口化组合根、界面与设备适配；`Web/` 是网页壳宿主（WebView 装载与传输适配），`Bridge/` 是桥协议与处理器 |
| `webui/` | 网页壳前端（Lit + Fluent UI Web Components），npm 工作区；产物是内联的单个 `index.html` |
| `WhaleGenie.Core` | 执行引擎、动作语义目录、表达式、变量、设备契约与键词汇表；`net10.0`，只依赖 BCL 与 `CsvHelper`，是唯一可打包的工程 |
| `WhaleGenie.Platform.Windows` | SharpHook、FlaUI、OpenCV、PaddleOCR、Playwright、Roslyn/NuGet、VIIPER 等 Windows 适配器与脚本宿主 |
| `WhaleGenie.App` | 触发器、运行编排、录制翻译、项目服务、MCP 宿主与工具、应用层组合根；不引用 Avalonia |
| `WhaleGenie.Localization` | 界面字、动作名、表达式与日志措辞的文案表；Avalonia 刷新仍在界面工程 |
| `WhaleGenie.Domain` | 宏文件模型、工程文档、设计期检查、包读写与图片文件；不引用 Avalonia |
| `WhaleGenie.Mcp` | 协议宿主、草稿、补丁、资源与项目编辑工具；不引用 Avalonia、Views 或 Runtime |
| `WhaleGenie.GraphicsCapture` | 四种画面获取方式、选路、会话缓存、矩形落位和 BGRA 输出；引用 Core，实现它的画面契约 |
| `WhaleGenie.Editor.Tests` | 宏编辑窗、步骤树、变量墙、窗口规则与菜单的无头测试 |
| `WhaleGenie.Actions.Tests` | 动作对话框、页面归属、返回值与拾取器的无头测试 |
| `WhaleGenie.Tests` | 运行与调试、MCP、设置、界面文案与文档对账，运行于 Avalonia 无头平台 |
| `WhaleGenie.Domain.Tests` | 宏文档、包格式、设计期检查、变量、触发器与运行期服务；不引用界面工程 |
| `WhaleGenie.Mcp.Tests` | 协议、工具、拒绝码与容器；不引用界面工程 |
| `WhaleGenie.App.TestSupport` | 应用侧套件共用的步骤构造与假桌面；自身没有测试 |
| `WhaleGenie.Core.Tests` | 引擎与动作语义测试，只依赖替身设备层；不引用任何 Windows 适配器 |
| `WhaleGenie.Platform.Tests` | 通过引擎驱动真实适配器的测试（浏览器、驱动、VIIPER、OCR、UIA、钩子、脚本、文件与剪贴板） |
| `WhaleGenie.TestSupport` | 两个引擎侧测试工程共用的替身设备层、样本文件与测试特性；自身没有测试 |
| `WhaleGenie.GraphicsCapture.Tests` | 选路、缓存、复用和矩形落位测试；真实 Windows 取画面另行手动验收 |

`WhaleGenie.Platform.Tests` 显式关掉了测试宿主的父进程监听：在本机它打开父进程句柄被拒绝
（`Win32Exception (5)`，Windows 报成 `0xe0434352`），宿主会在发现测试之前终止。关掉只放弃
“启动方退出时顺带结束宿主”这一条。若该套件在此环境里报 `%TEMP%` 无权限，是命令沙箱对新建
工程路径的限制，换 CI 或本机正常终端运行即可，与测试内容无关。

`WhaleGenie.Tests` 的每次 `Ui.Run` 结束时，无头会话会把队列里剩下的任务泵完再拆除应用，预算
5 秒；一个仍在自我续期的任务——渲染、动画、还在和界面说话的运行——会耗尽预算并以
`You've caused dispatcher loop` 结束。`Ui` 把这种收尾失败当作收尾失败而不是测试失败：测试体
已经跑完并拿到了结果，遗留队列属于即将丢弃的应用，下一次调用会重建 dispatcher。
`UiIsolationTests` 用一个自我投递的任务守住这条。

## 在本机跑 CI

Windows 上可以用 `act` 以自托管模式跑 `.github/workflows/ci.yml`，不经过容器：

```bash
git worktree add ../Viktor-ci HEAD     # 干净检出：不带 bin/obj，跑的就是提交的内容
cd ../Viktor-ci
act -j build-and-test -P windows-latest=-self-hosted --env ACT=true --action-offline-mode
```

`ACT=true` 让工作流跳过 `setup-dotnet` 与产物上传（本机不需要，且 act 的产物服务不兼容）。
`--action-offline-mode` 使用 `~/.cache/act` 里已经下载的动作，避免 act 自己再拉一次。工作流在
act 的宿主工作区里运行，因此 `%TEMP%` 与进程创建不受本机命令沙箱限制——这也是 `Platform.Tests`
在这里能跑通的原因。

`~/.cache/act` 会随每次运行复制一份工作区而涨到数 GB，可以整个删掉。删掉后第一次运行要重新
`git clone --depth 1 --branch <tag> https://github.com/actions/<name>` 到
`~/.cache/act/actions-<name>@<tag>`，或者临时去掉 `--action-offline-mode` 让 act 自己下载。

依赖方向和主要类型见 [架构文档](../architecture.md)。开发时不必在本文档重复维护文件规模、
动作数量或测试数量。

## 主要依赖

版本以各项目文件为准，本节只说明用途。新增依赖时同步更新本节和
`THIRD-PARTY-NOTICES.md`。

| 依赖 | 使用位置与用途 |
| --- | --- |
| `SharpHook` | Platform.Windows：全局键鼠钩子与事件翻译；引擎用自己的 `HookCode` 与 `KeyNames` 命名按键，不依赖它，`KeyCodeParityTests` 保证这份列表与 `SharpHook.Data.KeyCode` 同名同值 |
| `libVIIPER` | Platform.Windows：驱动级虚拟键鼠和 Xbox 360 手柄；原生库位于 `third_party/libVIIPER/` |
| `OpenCvSharp5.Windows` | Platform.Windows：找图、找色和 YOLO 检测 |
| `Sdcb.SimdPaddleOCR`、`Sdcb.SimdPaddleOCR.Models.ChineseV6Tiny` | Platform.Windows：离线 OCR 与中文模型 |
| `FlaUI.UIA3` | Platform.Windows：UI Automation 查询与操作 |
| `Microsoft.Playwright` | Platform.Windows：浏览器自动化，默认使用系统 Edge |
| `CsvHelper` | Core：CSV 的引号、换行、空行和行尾处理；Core 只有这一个包 |
| `Microsoft.CodeAnalysis.CSharp` | Platform.Windows：编译 C# 脚本 |
| `NuGet.Protocol`、`NuGet.Packaging`、`NuGet.Frameworks`、`NuGet.Versioning` | Platform.Windows：下载并解析脚本使用的 NuGet 包 |
| `System.Security.Cryptography.Pkcs` | Platform.Windows：固定安全修复版本，避免 NuGet 审计警告阻断还原 |
| `Vortice.Direct3D11`、`Vortice.DXGI` | GraphicsCapture：Windows Graphics Capture 与桌面复制 |
| `Avalonia`、`Avalonia.Desktop`、`Avalonia.Fonts.Inter` | 界面：桌面应用、输入、渲染和统一字体 |
| `Avalonia.Controls.WebView`（传递依赖 `Microsoft.Web.WebView2`） | 界面 `Web/`：网页壳的 WebView 装载、消息与导航管控；运行时需要系统的 WebView2 Evergreen Runtime |
| `FluentAvaloniaUI` | 界面：`FluentAvaloniaTheme`、控件模板和 `FAAppWindow` 标题栏 |
| `Avalonia.Themes.Fluent` | 界面：Avalonia 自带的 Fluent 主题资源；应用主主题由 FluentAvalonia 提供 |
| `CommunityToolkit.Mvvm` | Domain 与界面：可观察属性和源生成命令 |
| `Lucide.Avalonia` | 界面：动作、工具栏和菜单图标 |
| `ModelContextProtocol`、`Microsoft.Extensions.Hosting` | Mcp：stdio 协议服务、草稿与工具；界面：MCP 宿主与工具发现 |
| `AvaloniaUI.DiagnosticsSupport` | 仅 Debug：界面诊断支持 |
| `Declarative.Avalonia.AgentTools` | 仅 Debug：进程内的 MCP 检查器，让 agent 自己调试正在运行的界面 |
| `lit`、`@fluentui/web-components`、`@fluentui/tokens`（npm） | webui：网页壳的组件与设计令牌；以脚本形式内嵌进 `index.html` 随包分发 |
| `vite`、`typescript`、`eslint`、`vitest`、`jsdom`、`@playwright/test`（npm，dev） | webui：构建、类型检查、静态检查、单测与端到端冒烟；只在开发与 CI 使用，不随包分发 |

### Debug 构建与分部窗口

`Declarative.Avalonia.AgentTools` 顺带引入一个声明式视图注册生成器。它按类声明逐个生成，窗口拆成
分部类后会重复写出同名 hintName 并报 CS8785。本项目界面全部是 XAML，不需要这份注册；
`WhaleGenie.csproj` 在 `CoreCompile` 前移除该分析器，自省通道与 Release 构建都不受影响。