# 目录重构：src / tests

所属：[WhaleGenie 开发说明](../dev.md)。状态：**计划，尚未实施**；是否升级为阶段见
[roadmap](../roadmap.md)。

## 1. 目标

仓库根上现在有 18 个工程目录：8 个产品工程、8 个测试工程、2 个测试支持库，与配置、文档、脚本
混在一起。目标按参考布局收成标准形状：

- 产品工程收进 `src/`，测试与测试支持收进 `tests/`；
- 在根上新增 `Directory.Packages.props`，集中全部 NuGet 包版本（CPM）；
- 根上只留仓库级文件、配置与工具。

这次重构的约束：

- 不改项目文件名、程序集名、根命名空间和 `WhaleGenie.exe`；不改包内形状、`avares://` 地址
  与用户数据路径。
- 不改任何行为。唯一允许的差异是文件位置、引用位置的路径，以及包版本的声明位置。
- 工程内部目录（`Views/`、`Themes/`、`Assets/`、`Execution/` 等）原样随工程移动。
- `docs/`、`build/`、`site/`、`third_party/`、`WhaleGenie.slnx` 与根配置留在根上。

## 2. 参考布局与本仓库的映射

参照的骨架是：

```text
WhaleGenie/                    ← 仓库根（本仓库检出目录名可以不同，不在里面再建一层）
├── WhaleGenie.slnx
├── Directory.Build.props      # 集中管理编译属性
├── Directory.Packages.props    # 集中管理 NuGet 包版本
├── global.json
├── .editorconfig
├── docs/
├── src/
│   ├── WhaleGenie.App/
│   ├── WhaleGenie.Core/
│   ├── WhaleGenie.Domain/
│   └── WhaleGenie.Platform.Windows/
└── tests/
    ├── WhaleGenie.App.Tests/
    ├── WhaleGenie.Core.Tests/
    ├── WhaleGenie.Domain.Tests/
    └── WhaleGenie.Platform.Tests/
```

参考树列的是骨架，不是本仓库的完整清单。本仓库实际有 8 个产品工程与 10 个测试/支持工程，
按同一规则全部收进 `src/`、`tests/`，不合并、不改名：

| 参考布局 | 本仓库的对应工程 | 处理 |
| --- | --- | --- |
| `src/WhaleGenie.App` | `WhaleGenie.App` | 移入 `src/`，保留 |
| `src/WhaleGenie.Core` | `WhaleGenie.Core` | 移入 `src/`，保留 |
| `src/WhaleGenie.Domain` | `WhaleGenie.Domain` | 移入 `src/`，保留 |
| `src/WhaleGenie.Platform.Windows` | `WhaleGenie.Platform.Windows` | 移入 `src/`，保留 |
| — | `WhaleGenie`（界面）、`WhaleGenie.Localization`、`WhaleGenie.Mcp`、`WhaleGenie.GraphicsCapture` | 同一规则移入 `src/`，保留 |
| `tests/WhaleGenie.App.Tests` | `WhaleGenie.Tests`（界面套件） | 保留现名：它测的是界面工程 `WhaleGenie`，不是 App 层 |
| `tests/WhaleGenie.Core.Tests` | `WhaleGenie.Core.Tests` | 移入 `tests/`，保留 |
| `tests/WhaleGenie.Domain.Tests` | `WhaleGenie.Domain.Tests` | 移入 `tests/`，保留 |
| `tests/WhaleGenie.Platform.Tests` | `WhaleGenie.Platform.Tests` | 移入 `tests/`，保留 |
| — | `WhaleGenie.Editor.Tests`、`WhaleGenie.Actions.Tests`、`WhaleGenie.Mcp.Tests`、`WhaleGenie.GraphicsCapture.Tests` | 同一规则移入 `tests/`，保留 |
| — | `WhaleGenie.TestSupport`、`WhaleGenie.App.TestSupport` | 只服务测试，移入 `tests/`，不单独跑测试 |

把 8 个产品工程合并成 4 个、把测试合并成 4 个，或把界面工程改名为 `WhaleGenie.App`，都会改变
程序集身份、测试套件边界和层依赖，不属于这次目录移动；需要另开阶段先定边界。

## 3. 目标树

```text
src/
  WhaleGenie/                  界面（Avalonia、窗口、ViewModel、组合根）
  WhaleGenie.App/              触发器、运行编排、MCP 宿主与工具
  WhaleGenie.Core/             引擎、动作语义、表达式、设备契约
  WhaleGenie.Domain/           宏文件模型、包读写、设计期检查
  WhaleGenie.GraphicsCapture/  屏幕像素获取
  WhaleGenie.Localization/     中英文文案表
  WhaleGenie.Mcp/              协议宿主、草稿、补丁、项目编辑
  WhaleGenie.Platform.Windows/ Windows 适配器与脚本宿主
tests/
  WhaleGenie.Tests/
  WhaleGenie.Core.Tests/
  WhaleGenie.Domain.Tests/
  WhaleGenie.Mcp.Tests/
  WhaleGenie.Editor.Tests/
  WhaleGenie.Actions.Tests/
  WhaleGenie.Platform.Tests/
  WhaleGenie.GraphicsCapture.Tests/
  WhaleGenie.TestSupport/      测试替身库（不跑测试）
  WhaleGenie.App.TestSupport/  应用侧测试夹具（不跑测试）
```

根上保留：`WhaleGenie.slnx`、`Directory.Build.props`、`Directory.Packages.props`、
`global.json`、`.editorconfig`、`.gitattributes`、`.gitignore`、`.mcp.json`、
`AGENTS.md`、`DESIGN.md`、`README.md`、`LICENSE.txt`、`NOTICE`、
`THIRD-PARTY-NOTICES.md`、`.githooks/`、`.github/`、`build/`、`docs/`、`site/`、
`third_party/`。`Directory.Build.props`、`Directory.Packages.props`、`.editorconfig` 与
`global.json` 都靠向上查找生效，工程移到 `src/`、`tests/` 后不需要复制。

## 4. 为什么要分步

路径与包声明假设都不少，直接 `git mv` 会让它们同时失效：

| 假设 | 位置 | 数量（改动前实测） |
| --- | --- | --- |
| 工程目录在根上 | 根目录 | 18 个 |
| 解决方案里的工程路径 | `WhaleGenie.slnx` | 18 条 |
| 工程之间的引用 | 各 `.csproj` | 47 条：产品→产品 20、测试/支持→产品 21、测试→测试 6 |
| 指向仓库根的 csproj 项 | 界面与 Windows 适配器 | 2 条（第三方说明、libVIIPER） |
| 测试从 `WhaleGenie.slnx` 往上找根 | 39 个测试文件 | 76 处 |
| `Path.Combine(..., "WhaleGenie", ...)` | 测试与 `ViewSource` | 47 处 |
| 按源码位置找文档 | `ManualDocument.cs` | 1 处（`[CallerFilePath]` + `..\docs`） |
| NuGet 包版本写在 csproj | 各 `.csproj` | 68 条 PackageReference / 34 个包 / 0 个版本冲突 |
| 大文件台账路径 | `.githooks/large-file-inventory.tsv` | 240 条 |
| 构建与 CI 的路径 | `build/package.ps1`、`build/site.ps1`、`.github/workflows/pages.yml` | 7 条 |
| 文档里的路径 | `DESIGN.md`、`README.md`、`docs/dev/`、`docs/manual/` | 7 条真实工程路径 + 2 条 README 仓库 URL（4 个文件） |
| 开发命令里的工程路径 | `docs/dev/environment.md` | 4 条（另 2 条解决方案路径不变） |

## 5. 实施步骤

### 第 0 步：基线与分支

- 从当前分支拉一个专门分支（例如 `refactor/src-tests-layout`），工作区干净。
- 记录基线：`dotnet build WhaleGenie.slnx -c Release`、全量测试、`dotnet format` 全绿；
  记下八个套件的通过数。清掉 `bin/`、`obj/` 后再跑一次，确认基线不依赖旧产物。
- 记录包基线：`dotnet list WhaleGenie.slnx package --include-transitive`（或从
  `obj/project.assets.json` 读出解析到的版本），供第 2 步逐包比对。
- 在本机确认 `build/package.ps1`（用一个临时标签）与 `build/site.ps1` 可运行。

### 第 1 步：集中“仓库在哪”（行为不变）

- 在 `WhaleGenie.TestSupport` 新增 `RepoPaths`：从 `AppContext.BaseDirectory` 往上找
  `WhaleGenie.slnx`，给出 `Root`、`Source(params string[])`、`Tests(params string[])`、
  `Docs(params string[])` 和 `Path(params string[])`。`WhaleGenie.App.TestSupport` 增加对
  `WhaleGenie.TestSupport` 的项目引用（两个都只服务测试，不引入产品依赖）。
- 把 39 个测试文件里各自的 `RepositoryRoot` / `Repository` 搜索换成 `RepoPaths.Root`；
  把 47 处 `Path.Combine(..., "WhaleGenie", ...)` 换成 `RepoPaths.Source("WhaleGenie", ...)`。
- `ViewSource` 改用 `RepoPaths.Source("WhaleGenie", "Views")`；`ManualDocument` 改用
  `RepoPaths.Docs("manual")`，去掉对源码相对层级的依赖。
- 工程仍在根上，路径结果不变；跑全量测试与格式检查。这个提交独立保留，方便单独回退。

### 第 2 步：集中 NuGet 包版本（Directory.Packages.props）

- 根上新增 `Directory.Packages.props`：
  `<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>`，以及 34 个
  `<PackageVersion Include="..." Version="..." />`。现状没有同一包多版本，先不做
  `CentralPackageTransitivePinningEnabled`；它能改变解析图，除非另有测量理由。
- 从 18 个 csproj 的 68 条 `PackageReference` 删除 `Version` 属性，保留 `Include`、
  条件（如仅 Debug 的 `Declarative.Avalonia.AgentTools`）、`IncludeAssets`/`PrivateAssets`
  与 `WhaleGenie` 工程里的分析器移除目标。条目的版本统一进 `Directory.Packages.props`。
- 不改任何版本号。验收：`dotnet restore --no-cache` 通过；逐包比对第 0 步的解析版本完全一致；
  全量构建、测试、格式与包脚本通过；`git grep 'PackageReference.*Version=' -- '*.csproj'`
  为空。
- 这一步只动包声明，不移动文件；独立提交，失败可单独回退。

### 第 3 步：产品工程移入 src/

- `git mv` 8 个产品工程目录到 `src/`；`WhaleGenie.slnx` 的 8 条产品路径加 `src/`。
- 产品工程之间的 20 条 `ProjectReference` 仍是 `..\WhaleGenie.X\...`（同层），不改。
- 测试与支持工程对产品的 21 条引用改成 `..\src\WhaleGenie.X\...`；测试仍在根上，
  这一步可以单独编译、跑全量测试。
- 两条指向仓库根的 csproj 项各加一层：界面的 `..\THIRD-PARTY-NOTICES.md` 与 Windows
  适配器的 `..\third_party\libVIIPER\...` 改成 `..\..\...`。
- 同时更新：`build/package.ps1` 的界面工程路径、`build/site.ps1` 的三条资产路径、
  `.github/workflows/pages.yml` 的三条触发路径、`README.md` 的两张打赏图、
  `DESIGN.md` 的 `Tokens.axaml` 链接、`docs/dev/validation.md` 的构建/启动路径、
  `docs/manual/04-vision-details.md` 的 `ImageAssets.cs` 路径，以及架构/开发文档里以
  `WhaleGenie.X/...` 写的概念路径（逐条核对，不一定是链接）。
- 台账里产品工程的条目加 `src/` 前缀（例如 `WhaleGenie/Themes/Controls.axaml` →
  `src/WhaleGenie/Themes/Controls.axaml`），一行不改就失去记录的意义。
- 验收：全量构建、测试、格式；`build/site.ps1` 与 `SiteTests`；包脚本能生成 zip 并检查形状；
  `git log --follow -- src/WhaleGenie/Views/Run/VirtualKeyboardWindow.axaml` 能看到历史。

### 第 4 步：测试与支持移入 tests/

- `git mv` 8 个测试工程与 2 个支持库到 `tests/`；`WhaleGenie.slnx` 的 10 条路径加 `tests/`。
- 测试/支持对产品的 21 条引用从 `..\src\...` 改成 `..\..\src\...`。
- 测试之间的 6 条 `ProjectReference` 仍是 `..\WhaleGenie.X\...`（同层），不改；
  `WhaleGenie.Editor.Tests` 与 `WhaleGenie.Actions.Tests` 链接的
  `..\WhaleGenie.Tests\*.cs` 也不变（两边一起到 `tests/`，仍是同层）。
- 台账里测试工程与两个支持库的条目加 `tests/` 前缀。
- 文档命令：`docs/dev/environment.md` 的 `dotnet run --project WhaleGenie` 改为
  `src/WhaleGenie`，`dotnet test WhaleGenie.Core.Tests` 改为
  `tests/WhaleGenie.Core.Tests`；`docs/dev/testing.md`、`docs/dev/validation.md` 里涉及
  工程路径的行一并核对。解决方案路径 `WhaleGenie.slnx` 仍在根上，不改。
- `ManualDocument` 已走 `RepoPaths`，不再需要按源码相对层级改。
- 验收：全量构建、测试、格式；包脚本与站点脚本；干净检出（删掉 `bin/`、`obj/`）后重跑。

### 第 5 步：收网与文档

- `git grep` 检查残留：`WhaleGenie/`、`..\WhaleGenie\`、`dotnet (test|run) .*WhaleGenie`、
  `WhaleGenie.slnx` 根查找、`large-file-inventory` 的路径。只允许剩下产品名、命名空间、
  程序集名与文档里的概念路径。
- 更新 `README.md` 的仓库结构说明（如果有）、`docs/architecture/interface.md` 与
  `docs/dev/environment.md` 的工程路径与依赖说明（补一句 CPM 在哪）；
  重跑 Markdown 相对链接检查。
- 大文件台账的 240 条路径逐条比对 `git diff`；确认没有把测试工程写进 `src/`，也没有把
  产品工程写进 `tests/`。
- 提交信息按步子拆（沿用仓库的中文自由格式，不加 conventional prefix）：
  `统一测试的仓库路径解析`、`集中 NuGet 包版本到 Directory.Packages.props`、
  `产品工程移入 src/`、`测试工程移入 tests/`、`目录重构后的台账与文档`。
  移动用 `git mv`，保留 Git 的重命名历史；不要用删除加新增。

## 6. 验收

- 根目录只剩第 3 节列出的文件与目录，没有 `WhaleGenie*` 工程目录。
- `dotnet build WhaleGenie.slnx -c Release` / `-c Debug`、全量测试、`dotnet format` 全绿；
  八个测试套件的通过数与重构前一致，两个支持库仍不单独跑测试。
- `dotnet restore` 在 CPM 下解析出的包集合与版本与重构前逐包一致；csproj 里不再写版本。
- `build/site.ps1` 生成完整站点，`SiteTests` 通过；`build/package.ps1` 生成两种 zip，
  解压后仍是单个 `WhaleGenie.exe` + `lib/` + `doc/`，`WhaleGenie.exe` 名称不变。
- 文档相对链接、`git diff --check`、台账路径检查通过。
- 干净检出（不带 `bin`、`obj`）后能构建与测试；`git log --follow` 抽查一个移动过的窗口文件
  仍能看到重构前的历史。
- 行为、命名空间、程序集名、`avares://` 地址、包内形状与用户数据路径都没有变化。

## 7. 风险与回退

- **测试的根查找**：39 个文件、76 处，先做第 1 步；如果某个测试还在按 `WhaleGenie/Themes` 之类
  的目录找根，第一步会把它改成 `WhaleGenie.slnx` 标记，移动时不会再假装“路径不存在”。
- **CPM**：68 条引用、34 个包，但版本没有冲突；先做第 2 步并逐包比对解析结果。若某个包在 CPM
  下需要不同版本，才使用 `VersionOverride` 并记录原因；不得借机升级版本。
- **引用与资产**：21 条测试→产品引用、2 条仓库根资产项、包/站点/Pages 的路径都由构建或站点用例
  直接发现；每一步都跑全量测试与包脚本。
- **台账**：240 条是机械前缀，但不能批量盲改；按产品/测试两组分别 diff，确认没有把
  `WhaleGenie.App.TestSupport` 分到 `src/` 这类错误。
- **历史与并发**：五个提交都是机械移动/声明位置调整，不夹行为变更；与仍有未合并分支的同事约定
  顺序，重构后让其它分支 rebase。任何一步失败，回退该步提交即可，没有运行时状态需要恢复。
- **本地工具**：`.build/`、`.codeartsdoer/` 等未跟踪的脚本可能硬编码了旧路径；它们不在仓库里，
  由维护者各自更新，不纳入这次提交。

## 8. 不在本次范围

- 把 8 个产品工程合并成参考树的 4 个，或把 8 个测试工程合并成 4 个；这会改变层边界、程序集身份
  和套件划分，需要单独立项。
- 把界面工程改名（例如 `WhaleGenie.Desktop`）或改程序集名/命名空间；这会动到安装包、用户数据
  路径与 `avares://` 地址。
- 拆分 `MacroEditorWindow`、`MainWindow` 等大文件；它们有自己的行数台账与拆分预算。
- 把 `site/` 或 `docs/` 再搬家、升级依赖版本、引入 transitive pinning。
- 这些都会让“只挪位置、只换版本声明位置”的 diff 混入设计变更，等目录落定后再逐项评估。
