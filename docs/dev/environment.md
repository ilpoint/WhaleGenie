# 构建环境与依赖

所属：[WhaleGenie 开发说明](../dev.md)。

## 常用命令

```powershell
dotnet build WhaleGenie.slnx -c Release
dotnet test WhaleGenie.slnx -c Release
dotnet format WhaleGenie.slnx --verify-no-changes --no-restore
dotnet run --project WhaleGenie
pwsh build/site.ps1
```

解决方案只提供 `x64` 平台。根目录 `Directory.Build.props` 统一设置目标平台并将警告视为错误，
因此构建通过也意味着没有编译警告。

运行测试时不要混用解决方案构建与 `dotnet test <工程> --no-build`：两种调用可能使用不同的输出
目录。直接运行 `dotnet test <工程> -c Release`，或为构建和测试显式指定相同的
`-p:Platform=x64`。

动作矩阵与 MCP 自检分别使用：

```powershell
$env:ACTION_MATRIX_REPORT = "TestResults/action-matrix.md"
dotnet test WhaleGenie.Core.Tests -c Release --filter "FullyQualifiedName~ActionBench"
Remove-Item Env:ACTION_MATRIX_REPORT

dotnet run --project WhaleGenie -c Release -- --mcp-doctor --report TestResults/mcp-after.md
dotnet run --project WhaleGenie -c Release -- --mcp
```

报告属于本机验证产物，不提交到仓库。MCP 服务使用标准输入输出，不监听网络端口；权限来自
`settings.json`，命令行不提供权限参数。

### MCP 自检口径

`--mcp-doctor` 用固定场景覆盖状态读取、草稿、动作查询、写宏、校验、应用、保存、局部补丁、资源、
权限拒绝和清理。比较修改前后的报告时必须使用同一场景与命令；动作数、工具数、字节数等随实现变化的
数字只留在本机报告中，不另维护一份长期基线。带权限与无窗口路径都要覆盖，跳过项须能由权限状态解释。

`McpSelfCheck` 负责采集并尽量继续执行，不用自检代替行为断言；正确性由 MCP 测试、引擎测试和真实桌面
验收负责。若失败来自自检脚本或样本宏，先修测量，不能为得到好看的数字改变判定标准。退出码为
0（全部通过）、1（有失败）、2（参数错误）、3（无法通信）、4（子进程无法启动）。

## 工程结构

| 项目 | 职责 |
| --- | --- |
| `WhaleGenie` | Avalonia 与 FluentAvalonia 界面、应用服务、运行桥、触发器、MCP 宿主与设备控制工具 |
| `WhaleGenie.Core` | 执行引擎、动作语义目录、表达式、变量与设备契约；不引用任何 Windows 适配器 |
| `WhaleGenie.Platform.Windows` | SharpHook、FlaUI、OpenCV、PaddleOCR、Playwright、Roslyn/NuGet、VIIPER 等 Windows 适配器与脚本宿主 |
| `WhaleGenie.Localization` | 界面字、动作名、表达式与日志措辞的文案表；本机设置与 Avalonia 刷新仍在界面工程 |
| `WhaleGenie.Domain` | 宏文件模型、工程文档、设计期检查、包读写与图片文件；不引用 Avalonia |
| `WhaleGenie.Mcp` | 协议宿主、草稿、补丁、资源与项目编辑工具；不引用 Avalonia、Views 或 Runtime |
| `WhaleGenie.GraphicsCapture` | 四种画面获取方式、选路、会话缓存、矩形落位和 BGRA 输出 |
| `WhaleGenie.Tests` | 界面、包、MCP、文案和文档对账测试，运行于 Avalonia 无头平台 |
| `WhaleGenie.Core.Tests` | 引擎与动作语义测试，只依赖替身设备层；不引用任何 Windows 适配器 |
| `WhaleGenie.Platform.Tests` | 通过引擎驱动真实适配器的测试（浏览器、驱动、VIIPER、OCR、UIA、钩子、脚本、文件与剪贴板） |
| `WhaleGenie.TestSupport` | 两个引擎侧测试工程共用的替身设备层、样本文件与测试特性；自身没有测试 |
| `WhaleGenie.GraphicsCapture.Tests` | 选路、缓存、复用和矩形落位测试；真实 Windows 取画面另行手动验收 |

`WhaleGenie.Platform.Tests` 显式关掉了测试宿主的父进程监听：在本机它打开父进程句柄被拒绝
（`Win32Exception (5)`，Windows 报成 `0xe0434352`），宿主会在发现测试之前终止。关掉只放弃
“启动方退出时顺带结束宿主”这一条。若该套件在此环境里报 `%TEMP%` 无权限，是命令沙箱对新建
工程路径的限制，换 CI 或本机正常终端运行即可，与测试内容无关。

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

依赖方向和主要类型见 [架构文档](../architecture.md)。开发时不必在本文档重复维护文件规模、
动作数量或测试数量。

## 主要依赖

版本以各项目文件为准，本节只说明用途。新增依赖时同步更新本节和
`THIRD-PARTY-NOTICES.md`。

| 依赖 | 使用位置与用途 |
| --- | --- |
| `SharpHook` | Core：全局键鼠钩子、前台输入、录制和拾取 |
| `libVIIPER` | Core：驱动级虚拟键鼠和 Xbox 360 手柄；原生库位于 `third_party/libVIIPER/` |
| `OpenCvSharp5.Windows` | Core：找图、找色和 YOLO 检测 |
| `Sdcb.SimdPaddleOCR`、`Sdcb.SimdPaddleOCR.Models.ChineseV6Tiny` | Core：离线 OCR 与中文模型 |
| `FlaUI.UIA3` | Core：UI Automation 查询与操作 |
| `Microsoft.Playwright` | Core：浏览器自动化，默认使用系统 Edge |
| `CsvHelper` | Core：CSV 的引号、换行、空行和行尾处理 |
| `Microsoft.CodeAnalysis.CSharp` | Core：编译 C# 脚本 |
| `NuGet.Protocol`、`NuGet.Packaging`、`NuGet.Frameworks`、`NuGet.Versioning` | Core：下载并解析脚本使用的 NuGet 包 |
| `System.Security.Cryptography.Pkcs` | Core：固定安全修复版本，避免 NuGet 审计警告阻断还原 |
| `Vortice.Direct3D11`、`Vortice.DXGI` | GraphicsCapture：Windows Graphics Capture 与桌面复制 |
| `Avalonia`、`Avalonia.Desktop`、`Avalonia.Fonts.Inter` | 界面：桌面应用、输入、渲染和统一字体 |
| `FluentAvaloniaUI` | 界面：`FluentAvaloniaTheme`、控件模板和 `FAAppWindow` 标题栏 |
| `Avalonia.Controls.DataGrid` | 界面：表格展示与编辑 |
| `Avalonia.Controls.ColorPicker`、`Avalonia.Themes.Fluent` | 界面：自定义强调色及相关 Fluent 资源；应用主主题由 FluentAvalonia 提供，ColorPicker 样式文件来自 ColorPicker 包 |
| `CommunityToolkit.Mvvm` | 界面：可观察属性和源生成命令 |
| `Lucide.Avalonia` | 界面：动作、工具栏和菜单图标 |
| `ModelContextProtocol`、`Microsoft.Extensions.Hosting` | 界面：stdio MCP 服务、工具发现与通用宿主 |
| `AvaloniaUI.DiagnosticsSupport` | 仅 Debug：界面诊断支持 |
| `Declarative.Avalonia.AgentTools` | 仅 Debug：进程内的 MCP 检查器，让 agent 自己调试正在运行的界面 |

### Debug 构建与分部窗口

`Declarative.Avalonia.AgentTools` 顺带引入一个声明式视图注册生成器。它按类声明逐个生成，窗口拆成
分部类后会重复写出同名 hintName 并报 CS8785。本项目界面全部是 XAML，不需要这份注册；
`WhaleGenie.csproj` 在 `CoreCompile` 前移除该分析器，自省通道与 Release 构建都不受影响。
