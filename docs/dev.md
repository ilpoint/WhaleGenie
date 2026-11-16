# WhaleGenie 开发说明

本文件说明如何构建、修改、验证和发布 WhaleGenie。长期规范见仓库根目录的
[AGENTS.md](../AGENTS.md)，当前实现结构见 [architecture.md](architecture.md)，未完成工作见
[roadmap.md](roadmap.md)，用户操作与动作语义见 [manual.md](manual.md)。

## 开发文档索引

| 文档 | 内容 |
| --- | --- |
| [dev/environment.md](dev/environment.md) | 常用命令、MCP 自检口径、工程结构与主要依赖 |
| [dev/actions.md](dev/actions.md) | 修改动作的顺序、表单分块与各族的字段顺序 |
| [dev/field-names.md](dev/field-names.md) | 同名字段的保留理由与新增例外规则 |
| [dev/testing.md](dev/testing.md) | 测试分层、契约对账、界面与无头测试注意事项 |
| [dev/validation.md](dev/validation.md) | Debug 界面自省通道、手动验收清单与截图评审 |
| [dev/delivery.md](dev/delivery.md) | 界面出图、介绍页、性能测量、提交、CI 与发布 |

## 文档修改与检查

只修改文档时，至少运行：

```powershell
git diff --check
```

并检查 Markdown 相对链接：

```powershell
$files = @("README.md", "AGENTS.md") + (Get-ChildItem docs -Recurse -Filter *.md).FullName
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
| `docs/dev.md` 及各篇 | 命令、依赖、修改流程、测试、出图、CI 与发布 | 子系统详细设计、路线排序 |
| [architecture.md](architecture.md) | 七个产品工程、依赖方向、主链路、子系统边界与当前限制 | 操作命令、逐字段用户语义、阶段计划 |
| [roadmap.md](roadmap.md) | 未完成阶段、依赖、验收、待评估事项与非目标 | 已完成清单、工期、重复的开发规范 |

事实来源按以下规则确定：

- 用户可见行为以 `docs/manual/` 各篇为准；动作字段的机器可读语义以 `ActionSemantics.json` 为准。
- 界面展示元数据只放在 `ActionDisplay.json`，不得复制引擎事实；动作文字在 `ActionEnglish` 与
  `ActionStrings`，两者按同一组键取值。
- 失败键以 `RunKeys` 与手册失败键表为准，由 `FailureKeyTests` 对账。
- 当前结构与跨子系统设计取舍以代码和 `architecture.md` 为准；修改、测试与验收方法放在开发文档各篇。
- 未完成工作只登记在 `roadmap.md`；进度、批次过程、完成勾选和变更历史以 git 为准，不另写流水。
- 文档中的数量、体积和耗时必须可重新测量。无法保持准确时，不写具体数值；一次性检测数字不进入
  长期文档，需要比较时保留本机报告。
