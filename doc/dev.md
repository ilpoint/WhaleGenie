# WhaleGenie 开发说明

本文件说明如何构建、修改、验证和发布 WhaleGenie。长期规范见仓库根目录的
[AGENTS.md](../AGENTS.md)，当前实现结构见 [architecture.md](architecture.md)，未完成工作见
[roadmap.md](roadmap.md)，用户操作与动作语义见 [manual.md](manual.md)。

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

## 文档修改与检查

只修改文档时，至少运行：

```powershell
git diff --check
```

并检查 Markdown 相对链接：

```powershell
$files = @("README.md", "AGENTS.md") + (Get-ChildItem doc\*.md).FullName
$missing = foreach ($file in $files) {
  $resolved = Resolve-Path $file
  $base = Split-Path $resolved
  $text = Get-Content -Raw $resolved
  foreach ($match in [regex]::Matches($text, '\]\(([^)]+)\)')) {
    $target = $match.Groups[1].Value.Split('#')[0]
    if ($target -and $target -notmatch '^[a-z][a-z0-9+.-]*:' -and $target -notmatch '^/') {
      if (-not (Test-Path (Join-Path $base $target))) { "$resolved -> $target" }
    }
  }
}
if ($missing) { $missing; exit 1 }
```

修改手册函数表、失败键表或文案时，还需运行对应的 `FunctionTableTests`、`FailureKeyTests` 或
`LocalizationTests`。测试失败须先定位原因；偶发失败也不得仅凭一次重跑通过就直接放行。

## 文档职责与事实来源

| 文档 | 职责 | 不应包含 |
| --- | --- | --- |
| `README.md` | 项目简介、启动方式和文档入口 | 动作细节、工程命令、未完成计划 |
| [manual.md](manual.md) | 已实现的用户行为、动作、字段、失败键与限制 | 实现细节、发布流程、未来行为 |
| `AGENTS.md` | 长期定位、开发规范、跨子系统约定 | 操作手册、阶段计划、完成历史 |
| `doc/dev.md` | 命令、依赖、修改流程、测试、出图、CI 与发布 | 子系统详细设计、路线排序 |
| [architecture.md](architecture.md) | 三个产品工程、依赖方向、主链路、子系统边界与当前限制 | 操作命令、逐字段用户语义、阶段计划 |
| [roadmap.md](roadmap.md) | 未完成阶段、依赖、验收、待评估事项与非目标 | 已完成清单、工期、重复的开发规范 |
| [mcp-usability.md](mcp-usability.md) | MCP 自检指标与遗留项 | 未由检查支撑的评价 |
| [performance.md](performance.md) | 抓屏、识别与检测的单位成本及量法 | 用户教程、阶段计划 |

事实来源按以下规则确定：

- 用户可见行为以 `manual.md` 为准；动作字段的机器可读语义以 `ActionSemantics.json` 为准。
- 界面展示元数据只放在 `ActionDisplay.json`，不得复制引擎事实。
- 失败键以 `RunKeys` 与手册失败键表为准，由 `FailureKeyTests` 对账。
- 当前结构以代码和 `architecture.md` 为准；未完成工作只登记在 `roadmap.md`；变更历史以 git 为准。
- 文档中的数量、体积和耗时必须可重新测量。无法保持准确时，不写具体数值。

## 工程结构

| 项目 | 职责 |
| --- | --- |
| `WhaleGenie` | Avalonia 与 FluentAvalonia 界面、应用服务、宏文件模型、包读写、触发器、文案和 MCP 宿主 |
| `WhaleGenie.Core` | 执行引擎、动作语义目录、表达式、变量、设备契约及 Windows 设备实现 |
| `WhaleGenie.GraphicsCapture` | 四种画面获取方式、选路、会话缓存、矩形落位和 BGRA 输出 |
| `WhaleGenie.Tests` | 界面、包、MCP、文案和文档对账测试，运行于 Avalonia 无头平台 |
| `WhaleGenie.Core.Tests` | 引擎与动作契约测试，使用替身设备层，不操作真实桌面 |
| `WhaleGenie.GraphicsCapture.Tests` | 选路、缓存、复用和矩形落位测试；真实 Windows 取画面另行手动验收 |

依赖方向和主要类型见 [architecture.md](architecture.md)。开发时不必在本文重复维护文件规模、
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

## 修改动作的流程

1. **检索现有契约。** 在动作目录、`MacroRunner`、`MacroCheck`、手册和测试中检查动作键、字段名、
   记录字段及同义概念，优先复用现有名称。
2. **修改语义目录。** 在 `WhaleGenie.Core/Execution/ActionSemantics.json` 声明动作分类、参数、
   读法、形状、单位、空值、范围、默认值、必填与选项失败键、拾取器、副作用、等待、子列表和返回值。
3. **实现引擎分派。** 在对应的 `MacroRunner.<Family>.cs` 中增加分派和实现。读值与范围检查使用目录，
   记录字段使用 `FieldNames`，不得按 `type` 前缀推断行为。
4. **更新展示与文案。** 在 `ActionDisplay.json` 维护标签、提示、编辑器种类和页面归属；同步更新
   `Strings` 与 `ActionStrings`。图标映射位于 `ViewModels/AppIcons.cs`，菜单图标键位于
   `ViewModels/MenuIconCatalog.cs`。
5. **补齐失败键。** 新失败键同时进入 `RunKeys` 和手册失败键表；日志键使用独立的 `Log.*` 词表。
6. **补齐页面与返回值。** 参数只归属于一个页面；返回值页、目录声明、引擎写入键和记录字段保持一致。
7. **增加自动化用例。** 至少一条引擎用例执行新动作，并补充读法、形状、范围、副作用、返回形状、
   失败键、页面、文案及结果清空等契约断言。
8. **删除被取代的形状。** 同版移除旧参数、开关、文案、测试和文档，不保留别名或双读；不兼容的
   文件形状变化须升级包格式版本。仅删除可安全忽略的字段时可以保持版本，但不得增加迁移逻辑。
9. **完成手动验收。** 涉及真实桌面行为时，按本文手动清单记录环境、步骤和观察结果。
10. **收尾验证。** 更新手册、依赖与许可说明；完成路线图对应事项后删除该事项；运行构建、全部测试和
    格式检查。

## 测试分层与契约

| 层 | 位置 | 验证范围 |
| --- | --- | --- |
| 引擎逻辑 | `WhaleGenie.Core.Tests` | 动作行为、变量、表达式、设备请求、返回形状和输入路由 |
| 动作矩阵 | `WhaleGenie.Core.Tests/Bench` | 按目录合成动作并核对结局、返回值、副作用声明和设备记录 |
| 画面获取 | `WhaleGenie.GraphicsCapture.Tests` | 选路、缓存、复用、矩形越界与图片落位 |
| 界面与应用 | `WhaleGenie.Tests` | 参数成型、绑定、触发器、包往返、MCP、文案和无头交互 |
| 外部程序 | `BrowserTabTests`、`ScriptBridgeTests`、`ScriptHostRunTests` | 无头 Edge、真实脚本宿主和命名管道桥 |
| 真实桌面 | 手动验收 | 系统输入、真实窗口、抓屏取色、UIA、录制和驱动级输入 |
| 界面观感 | `tools/Shot` | 把真实窗口离屏渲染成 PNG，或用程序自己的抓屏读运行中的窗口 |

契约测试应遍历目录或扫描源码，避免手写第二份动作清单。新增目录事实时先增加可失败的断言，
再修改实现。主要对账关系如下：

| 契约 | 主要用例 |
| --- | --- |
| 字段读法、形状、单位、空值、范围、位置与矩形 | `ExpressionFieldTests`、`ActionContractTests`、`ValueReadTests`、`ActionPageTests` |
| 参数约束、行列表、类型检查和设计期诊断 | `MacroRunnerTests`、`MacroCheckTests`、`ActionContractTests` |
| 宏声明、直接运行、调用参数和结果交付 | `MacroSignatureTests`、`MacroCallTests`、`RunDebugTests` |
| 输入副作用、互斥门和宏调用图 | `ActionContractTests`、`DeviceActionTests`、`MacroRunnerTests` |
| 返回页、引擎返回键和记录字段 | `ReturnsPageTests`、`ActionContractTests`、`DeviceActionTests` |
| 失败键、函数表和中英文文案 | `FailureKeyTests`、`FunctionTableTests`、`LocalizationTests` |
| 字段命名与步骤落点 | `FieldNameTests`、`StepPlacementTests`、`MacroEditorWorkspaceTests` |
| 依赖方向与主题资源 | `LayeringTests`、`ControlsRegistryTests`、`StyleVocabularyTests`、`TemplatePartTests` |

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

同名字段的新增例外必须同时写入本表，并由 `FieldNameTests` 核对。

### 界面与无头测试注意事项

- 主题资源查询须携带 `ActualThemeVariant`。属性需要跟随主题时使用 `DynamicResource` 或
  `ThemeBrush.Assign`；`ThemeBrush.Of` 只提供快照。
- 验证样式时读取模板中实际接收颜色或状态的部件，不能只读控件静息属性。需要按名称命中模板部件时，
  使用 `TemplatePartTests` 验证部件存在。
- 普通窗口继承 `AppWindow`。无头平台的 `HWND` 为 0，`AppWindow` 已处理 FluentAvalonia 的句柄表；
  标题栏的实际外观仍需在真机检查。覆盖层不强制继承 `AppWindow`。
- 带内容的窗口在构造函数里调用 `WindowChrome.Attach(this)`，不要自己搭标题栏：它负责把标题栏交给
  内容、画鲸鱼与标题、留出系统按钮的宽度。漏掉它的窗口会退回库的默认标题栏——没有鲸鱼，也拖不动；
  `WindowChromeTests` 核对这一点。
- `DataTemplate` 内不使用 `x:Name` 作为实例定位手段。按类名查找元素时同时限定具体控件类型，
  避免继承关系导致多个匹配。
- 修改样式类时同时添加类并移除与其重复的本地属性；样式不设置由布局决定的宽、高或对齐。
- 列表行的高度写下限（`MinHeight`），不写定高。步骤行是两行字，名称与徽标还会换行；`Height` 定死之后
  多出来的那一行压到卡片边线下面，看起来只是"字被边框挡住"，而行本身没有任何报错。
  `MacroEditorTests` 核对行高不小于卡片高度。
- 一行里并排的东西各占一列。同一个 `Grid` 单元格里放两个元素，它们会叠在一起而不是左右排开，
  而且叠上去的那个往往有底色：运行调试的步骤缩略图就这样盖住过「第 1 次 20 毫秒」。
  `RunLookTests` 用两个元素的矩形是否相交来核对这一点。
- 提示只挂在说到它的那个小部件上。整张卡片挂一条 ID 提示，等于指针落在任何一处都弹同一句、
  挡住正文；`MacroEditorTests` 核对卡片上没有提示、ID 徽标上有。
- 一个参数模板里画着好几支（单行、数字、多行、变量…），只有一支显示，其余仍会生成控件。
  在测试里按 `DataContext` 加按钮文字去找会命中多枚，`IsVisible` 也仍是 `true`（局部值不看祖先）；
  要按位置找，或直接断言模型上的标志与行集合。
- 由工具写入的值在显示数据里标 `WrittenByTool`（不是语义里的 `Hidden`：契约测试禁止显示层重复
  引擎字段名），`BuildRows` 据此不给它生成行。值本身照常保存、校验，并在写入它的那扇窗里编辑。
- 控件模板的归属要查清楚。`FluentAvaloniaTheme` 覆盖大部分 Avalonia 控件，但 `ContextMenu` 没有：
  删掉 `Themes/Controls.axaml` 里那份模板，所有右键菜单都会打开却什么都不画，而无头用例不会发现。
  菜单类改动在无头里能验证的是主题存在、菜单有尺寸与 `ItemsPresenter`（`MenuThemeTests`）；
  外观与落点仍需真机检查。
- 读屏幕的图必须经过 `ScreenReady`，不要直接 `screen.Capture`：桌面复制器会把隐藏前的画面继续交出来，
  `DwmFlush` 并不能保证新帧已经到了。拾取器、框选后的成图和单步试看都走这一条。
- 修改类名、资源键或选择器后，检索 XAML、代码和测试中的旧引用，并运行全量测试。
- 会弹出模态确认的窗口必须在原用例中完成回答和关闭，可使用 `Ui.CloseAndAnswer` 或
  `Ui.AnswerUnsavedWork`，避免遗留调度器循环。
- 等待条件使用事件、`TaskCompletionSource` 或 `Ui.Until`，不使用固定睡眠推断异步操作已完成。
- 修改全局状态的测试使用 `[RestoreSharedState]` 并在界面线程还原；共享变量值由相关用例自行清理。

### 看图与看运行中的程序

界面改动不必每次都靠人眼：`tools/Shot` 用程序自己的窗口类与样式，把一扇窗画成 PNG，
也能读运行中的程序所占的那块屏幕。它不是程序的一部分，不进解决方案，只在需要看的时候跑。

```bash
# 设置窗口（可指定子页与深色）
dotnet run --project tools/Shot -- settings --page=CaptureInput --dark

# 动作编辑器
dotnet run --project tools/Shot -- editor

# 运行中的程序：按进程找它最大的窗口，抓下来的就是那一扇
tools/Shot/bin/Release/net10.0-windows10.0.19041/Shot.exe window --app=WhaleGenie

# 整个桌面，或其中一块
tools/Shot/bin/Release/net10.0-windows10.0.19041/Shot.exe desktop --at=0,0,1280,800
```

图片落在 `tools/Shot/bin/<配置>/<框架>/shots/`。离屏渲染看的是版式、文案与状态，看不到真实的
输入、别的程序的窗口，也看不到只在真机上才走的分支；这些仍按手动清单验收。

抓一个运行中的程序时，进程要先起来：在同一条命令里先启动、再抓、再结束，避免它被上一次会话的
残留实例挡住文件（构建会因此报“文件被锁”）。

## 手动验收清单

按受影响范围选择项目，并在提交信息中记录 Windows 版本、DPI、显示器数量、相关外部程序版本、
操作步骤和观察结果。环境变化后重新验收受影响项目。

- **输入与取色**：在记事本等现有程序中验证点击、输入、拖选和滚轮；在已知颜色位置验证抓屏取色。
- **五类拾取器**：框选、取色、点、元素和窗口规则分别进入与退出，检查焦点、Esc、回车、右键、
  鲸灵窗口隐藏与恢复；在添加动作窗口中至少验证一次。
- **识别画面上的拾取**（`LookPickWindow`）：点击图片 / 点击文字 / 点击检测到的物体的「拾取点位…」，
  以及多点找色的「拾取偏移与颜色…」。要验的是偏移以命中中心为原点、点在框外仍然有效、
  颜色取自打开那一刻的识别画面（不是此刻的屏幕），以及这一步没找到东西时给出说明而不是空窗。
  多点找色还要验：表单上不再有「周围要对的颜色」一格；「对比色（N 处）」一节（标题、序号/偏移/
  颜色/色块那一列与拾取按钮）在**容差之后**，拾完后这一列随即更新；拾取器打开时带出已有
  那几处，确定后整张表写回、取消不改；圆点与列表上的序号对得上；拖圆点改一条、右键圆点出菜单、
  空白右键不多条、滚轮键单击新增；左上的「+」与每行「编辑…」都开得出一行三框、框末带表达式的窗口。
- **定格与框选**：取色后移动背景窗口或播放视频，确认读数仍来自打开时的画面；框选图不得包含鲸灵窗口。
- **FluentAvalonia 窗口**：在深色和浅色主题下检查标题栏、窗口控制按钮、扩展标题栏拖动、文本对比度和
  关键控件布局。无头截图不包含真实标题栏，不能替代此项。
- **驱动级输入**：安装 usbip-win2，分别验证启动时加载与否、键鼠动作和手柄；退出后确认虚拟设备已移除。
- **识别与录制**：验证 YOLO 检测、等待与点击落点；验证录制热键、动作翻译和停止流程。
- **脚本与依赖**：验证批处理、PowerShell、Python、C#、AutoHotkey、包安装及断网失败路径。
- **编辑器交互**：验证表达式拖放、动作落点、取消与保存、公共宏版本复制及重启后的公共宏库。
- **其他发布资源**：涉及收款码变更时，分别用手机扫码验证内容。

真实输入须在交互式 Windows 会话中验证。不要通过落入会话 0 的 SSH 会话判断桌面输入或窗口行为。

## 界面出图

界面截图用于视觉评审，不作为行为正确性的测试证据。所有截图、局部放大图和评审用图片统一保存到
**`%temp%\WhaleGenie`**，不要写入 `%temp%` 根目录或仓库。测试仍应验证可读取的资源、模板和布局状态。

在仓库外创建 `.NET 10` 文件式程序 `shot.cs`，修改 `#:project` 为本机项目路径后运行
`dotnet run shot.cs`：

```csharp
#:property TargetFramework=net10.0-windows10.0.19041
#:property JsonSerializerIsReflectionEnabledByDefault=true
#:package Avalonia.Headless@12.1.2
#:project C:/path/to/Viktor/WhaleGenie/WhaleGenie.csproj

using Avalonia;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

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
var window = new MainWindow
{
    DataContext = new MainViewModel(_ => new McpServerViewModel()),
};
window.Show();
Dispatcher.UIThread.RunJobs();

var frame = window.CaptureRenderedFrame()
    ?? throw new InvalidOperationException("没有渲染出画面。");
var outputDirectory = Path.Combine(Path.GetTempPath(), "WhaleGenie");
Directory.CreateDirectory(outputDirectory);
using var stream = File.Create(Path.Combine(outputDirectory, "main-window-dark.png"));
frame.Save(stream, PngBitmapEncoderOptions.Default);
```

`UseSkia`、`WithInterFont` 和反射式 JSON 设置须与应用运行条件保持一致。窗口还需具有正常运行时
应由容器提供的 `DataContext`，否则截图只反映空绑定状态。对于构造函数不公开或必须经异步流程创建的
窗口，使用既有测试覆盖，不得为了出图扩大其公开 API。

## 介绍页与短片

`site/` 是 GitHub Pages 源目录，`build/site.ps1` 将页面及引用资源组装到 `_site/`。推送到
`master` 后由 `.github/workflows/pages.yml` 发布；首次使用前需在仓库 Pages 设置中选择
GitHub Actions。

重录 `site/vibe/index.html` 对应的短片时，使用临时 Playwright 安装和本机 Edge：

```powershell
npm install --prefix "$env:TEMP\whalegenie-site-tools" playwright-core@1.64.1
npx --yes playwright@1.64.1 install ffmpeg
$env:WHALEGENIE_PLAYWRIGHT = "$env:TEMP\whalegenie-site-tools\node_modules\playwright-core"
node build/record-vibe.mjs .
```

该脚本生成无声 H.264 短片、海报和社交分享图，发布成品写入 `site/assets/`。手工评审图仍按上一节
保存到 `%temp%\WhaleGenie`。录制工具不是应用运行依赖，机器需另有 `ffmpeg` 和 Edge。

## 性能测量

抓屏、识别和检测的单位成本记录于 [performance.md](performance.md)。修改画面获取或匹配算法后，
按该文档的方法重新测量并比较结果。测量程序使用真实引擎设备，先预热，再取多次测量的中位数；
临时程序不留在仓库中。

## 提交与 CI

`master` 是发布主线。`.github/workflows/ci.yml` 在推送和拉取请求上运行格式检查、构建及全部测试。
三个测试工程在 CI 中串行执行，避免计时相关用例争用处理器；失败报告保留 7 天，成功运行不保存报告。

需要在本地复现 CI 时，可使用 `act`：

```powershell
act -W .github/workflows/ci.yml -j build-and-test -P windows-latest=-self-hosted `
  --env ACT=true --env DOTNET_INSTALL_DIR="$env:LOCALAPPDATA\act-dotnet"
```

提交信息使用中文并说明原因。手动验收结果须包含环境和观察结果，不能只写“已手动验证”。

## 发布

发布由 `.github/workflows/release.yml` 完成。推送以 `v` 开头的标签，或在 Actions 中手动运行
Release 工作流并填写版本号：

```powershell
git tag v0.1.1
git push origin v0.1.1
```

标签是压缩包名称和程序集版本的唯一来源。工作流生成两个 Windows x64 包：

| 包 | 运行环境 |
| --- | --- |
| `WhaleGenie-<版本>-win-x64.zip` | 需要预装 .NET 10 桌面运行时 |
| `WhaleGenie-<版本>-win-x64-standalone.zip` | 自包含，无需预装 .NET |

本地打包使用：

```powershell
pwsh build/package.ps1 -Tag v0.1.1
```

产物写入已忽略的 `dist/`。发布包采用单文件托管发布，原生库保留在 `lib/`，Playwright 驱动位于
`.playwright/`；包根包含手册、许可证和第三方说明。只发布 Windows x64，不启用裁剪，以避免 Avalonia、
UIA 和 OCR 的反射路径被破坏。

程序按 AGPL-3.0 发布，`LICENSE.txt` 保持许可原文，`NOTICE` 保存署名。发布前确认 GitHub Actions
具有创建 Release 所需的写权限。面向用户的版本说明维护在 `.github/release-notes.md`。
