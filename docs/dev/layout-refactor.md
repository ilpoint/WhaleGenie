# 目录重构：src / tests

所属：[WhaleGenie 开发说明](../dev.md)。状态：**计划，尚未实施**；是否升级为阶段见
[roadmap](../roadmap.md)。

## 1. 目标

仓库根上现在有 18 个工程目录：8 个产品工程、8 个测试工程、2 个测试支持库，与配置、文档、脚本
混在一起。目标是把产品工程收进 `src/`，测试与测试支持收进 `tests/`，根上只留仓库级文件与
工具。

这次重构的约束：

- 不改项目文件名、程序集名、根命名空间和 `WhaleGenie.exe`；不改包内形状、`avares://` 地址
  与用户数据路径。
- 不改任何行为。唯一允许的差异是文件位置，以及引用位置的路径。
- 工程内部目录（`Views/`、`Themes/`、`Assets/`、`Execution/` 等）原样随工程移动。
- `docs/`、`build/`、`site/`、`third_party/`、`WhaleGenie.slnx` 与根配置留在根上。

## 2. 目标树

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

根上保留：`.editorconfig`、`.gitattributes`、`.gitignore`、`.mcp.json`、`AGENTS.md`、
`DESIGN.md`、`Directory.Build.props`、`global.json`、`LICENSE.txt`、`NOTICE`、
`README.md`、`THIRD-PARTY-NOTICES.md`、`WhaleGenie.slnx`、`.githooks/`、`.github/`、
`build/`、`docs/`、`site/`、`third_party/`。`Directory.Build.props`、`.editorconfig` 与
`global.json` 都靠向上查找生效，工程移到 `src/`、`tests/` 后不需要复制。

## 3. 为什么分步

路径假设比目录本身多：

| 假设 | 位置 | 数量（改动前实测） |
| --- | --- | --- |
| 工程目录在根上 | 根目录 | 18 个 |
| 解决方案里的工程路径 | `WhaleGenie.slnx` | 18 条 |
| 工程之间的引用 | 各 `.csproj` | 47 条：产品→产品 20、测试/支持→产品 21、测试→测试 6 |
| 指向仓库根的 csproj 项 | 界面与 Windows 适配器 | 2 条（第三方说明、libVIIPER） |
| 测试从 `WhaleGenie.slnx` 往上找根 | 39 个测试文件 | 76 处 |
| `Path.Combine(..., "WhaleGenie", ...)` | 测试与 `ViewSource` | 47 处 |
| 按源码位置找文档 | `ManualDocument.cs` | 1 处（`[CallerFilePath]` + `..\docs`） |
| 大文件台账路径 | `.githooks/large-file-inventory.tsv` | 240 条 |
| 构建与 CI 的路径 | `build/package.ps1`、`build/site.ps1`、`.github/workflows/pages.yml` | 7 条 |
| 文档里的路径 | `DESIGN.md`、`README.md`、`docs/dev/`、`docs/manual/` | 7 条真实工程路径 + 2 条 README 仓库 URL（4 个文件） |
| 开发命令里的工程路径 | `docs/dev/environment.md` | 4 条（另 2 条解决方案路径不变） |

把 39 份各自实现的“往上找根”先收成一处，再移动工程，才能让移动提交只改路径、不改规则。
否则每一份测试都会在移动当天同时失效，出问题时分不清是测试坏了还是路径错了。

## 4. 实施步骤

### 第 0 步：基线与分支

- 从当前分支拉一个专门分支（例如 `refactor/src-tests-layout`），工作区干净。
- 记录基线：`dotnet build WhaleGenie.slnx -c Release`、全量测试、`dotnet format` 全绿；
  记下八个套件的通过数。清掉 `bin/`、`obj/` 后再跑一次，确认基线不依赖旧产物。
- 在本机确认 `build/package.ps1`（用一个临时标签）与 `build/site.ps1` 可运行；这两条在
  第 2、3 步后还要再跑。

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

### 第 2 步：产品工程移入 src/

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

### 第 3 步：测试与支持移入 tests/

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

### 第 4 步：收网与文档

- `git grep` 检查残留：`WhaleGenie/`、`..\WhaleGenie\`、`dotnet (test|run) .*WhaleGenie`、
  `WhaleGenie.slnx` 根查找、`large-file-inventory` 的路径。只允许剩下产品名、命名空间、
  程序集名与文档里的概念路径。
- 更新 `README.md` 的仓库结构说明（如果有）、`docs/architecture/interface.md` 与
  `docs/dev/environment.md` 的工程路径描述；重跑 Markdown 相对链接检查。
- 大文件台账的 240 条路径逐条比对 `git diff`；确认没有把测试工程写进 `src/`，也没有把
  产品工程写进 `tests/`。
- 提交信息按步子拆：
  `test: 统一仓库路径解析`、`refactor: 产品工程移入 src/`、
  `refactor: 测试工程移入 tests/`、`chore: 目录重构的台账与文档`。
  移动用 `git mv`，保留 Git 的重命名历史；不要用删除加新增。

## 5. 验收

- 根目录只剩第 2 节列出的文件与目录，没有 `WhaleGenie*` 工程目录。
- `dotnet build WhaleGenie.slnx -c Release` / `-c Debug`、全量测试、`dotnet format` 全绿；
  八个测试套件的通过数与重构前一致，两个支持库仍不单独跑测试。
- `build/site.ps1` 生成完整站点，`SiteTests` 通过；`build/package.ps1` 生成两种 zip，
  解压后仍是单个 `WhaleGenie.exe` + `lib/` + `doc/`，`WhaleGenie.exe` 名称不变。
- 文档相对链接、`git diff --check`、台账路径检查通过。
- 干净检出（不带 `bin`、`obj`）后能构建与测试；`git log --follow` 抽查一个移动过的窗口文件
  仍能看到重构前的历史。
- 行为、命名空间、程序集名、`avares://` 地址与用户数据路径都没有变化。

## 6. 风险与回退

- **测试的根查找**：39 个文件、76 处，先做第 1 步；如果某个测试还在按 `WhaleGenie/Themes` 之类
  的目录找根，第一步会把它改成 `WhaleGenie.slnx` 标记，移动时不会再假装“路径不存在”。
- **引用与资产**：21 条测试→产品引用、2 条仓库根资产项、包/站点/Pages 的路径都由构建或站点用例
  直接发现；每一步都跑全量测试与包脚本。
- **台账**：240 条是机械前缀，但不能批量盲改；按产品/测试两组分别 diff，确认没有把
  `WhaleGenie.App.TestSupport` 分到 `src/` 这类错误。
- **历史与并发**：四个提交都是机械移动，不夹行为变更；与仍有未合并分支的同事约定顺序，重构后
  让其它分支 rebase。任何一步失败，回退该步提交即可，没有运行时状态需要恢复。
- **本地工具**：`.build/`、`.codeartsdoer/` 等未跟踪的脚本可能硬编码了旧路径；它们不在仓库里，
  由维护者各自更新，不纳入这次提交。

## 7. 不在本次范围

- 把界面工程改名（例如 `WhaleGenie.Desktop`）或改程序集名/命名空间；这会动到安装包、用户数据
  路径与 `avares://` 地址，应该单独一轮。
- 拆分 `MacroEditorWindow`、`MainWindow` 等大文件；它们有自己的行数台账与拆分预算。
- 引入 `Directory.Packages.props`（集中包版本）、把 `site/` 或 `docs/` 再搬家、改测试工程名。
- 这些都会让“只挪位置”的 diff 混入设计变更，等目录落定后再逐项评估。
