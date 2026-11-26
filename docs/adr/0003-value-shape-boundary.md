# ADR 0003：值的形状边界与往返法则

- 状态：已采纳（实现待开始）
- 日期：2026-11-26
- 相关：[路线图](../roadmap.md)、[构建环境](../dev/environment.md)、[测试分层](../dev/testing.md)、[架构](../architecture.md)

## 背景

动作的字段在模型里是形状（`ExpressionNode`），在表单和桥上是给人看的行。两者的边界没有守住：

- [RegionRowViewModel](../../src/WhaleGenie.Editing/Actions/RegionRowViewModel.cs) 的 `ToRow` 用 `cell.Text`（显示行）和 `SetLine` 重建区域行。变量 `{"name":"区域"}` 变成字符串 `"区域"`，公式 `{"op":"+","left":{"name":"x"},"right":10}` 变成字符串 `"x + 10"`。
- [HeadlessActionSession](../../src/WhaleGenie.Editing/Actions/HeadlessActionSession.cs) 的 `PatchRow` 直接给 `row.X.Text` 赋值，不更新 `_shape`；页面拿到的是 [EditorRegionRowSnapshot](../../src/WhaleGenie.Contracts/EditorActionContracts.cs) 的五个字符串。
- [FormulaJson](../../core/src/WhaleGenie.Core/Expressions/FormulaJson.cs) 的 `Read` 对 `JsonValuePrimitive<decimal>` 的 `TryGetValue<string>` 与 `TryGetValue<double>` 都失败，落到 `Formula.Text(value.ToJsonString())`。从文本读（文件）正常，从内存节点读（保存计划、导入、库、恢复的 `MacroItem.FromJson(macro.ToJson())`）把数字变成 `TextNode`。

这不是一个孤立的笔误，而是两类结构问题：

1. **显示文本被当成模型输入。** 表单视图与纯变换住在同一个类里，`ToRow`/`ToStepParameter` 可以从 `Text` 重建模型。
2. **线缆比模型表达力更弱。** 能持有名字或公式的行单元格只传字符串，页面没有信息可保，C# 也没有形状可收。

未发布的阶段可以改合同，但一次会话内的“存了再读还是同一个值”不能丢；否则迁移只会把丢失带进新结构。

## 决策

1. **模型只收形状。** `StepParameter.Written` 与 `StepParameterRow.Cells` 只接受 `ExpressionNode`。`Value`/`Text` 是渲染，永远不作为模型输入。`StepParameterRow.SetLine` 删除；需要文字时在输入适配器里显式写 `Formula.Text`。
2. **一行字变成形状只发生一次。** 只在输入适配器（表单输入、页面补丁）里发生，且只变成 `Formula.Text`。名字只能来自取值窗口，落成 `VariableNode`。区域 `text` 列按内容判断：看起来像矩形字面量 → `Formula.Text`，否则 → `Formula.Name`；照 [ColourRowViewModel](../../src/WhaleGenie.Editing/Actions/ColourRowViewModel.cs) 的 `Stored()` 写法。
3. **线缆表达力必须不小于模型表达力。** 能持有名字或公式的行单元格，载荷必须带 `EditorValuePartSnapshot`（已有类型）或等价的 {kind, text, name}；只有纯文字字段可以只传字符串。命令回传同样的结构，未编辑的单元格不重写。
4. **纯变换与表单分开。** `ToRow`、`ToStep`、`ApplyValue` 一类放进 `Editing/Value` 的纯函数；表单 VM 只持有草稿与显示，不产出模型。页面只渲染 parts、收集输入，不解释形状。
5. **等式用规范化 JSON，不用 record 默认相等。** `ConcatNode`、`CallNode`、`ListNode` 内部是 `IReadOnlyList`，record 相等对列表是引用比较。法则里的 `≡` 定义为 `JsonNode.DeepEquals(FormulaJson.Write(a), FormulaJson.Write(b))`；步骤层用 `JsonNode.DeepEquals(step.ToJson(), other.ToJson())`。`HoleNode` 是编辑期占位，与 `Nothing` 视为同一，不进入文件。
6. **两仓改动按库仓优先。** Core 的 codec 与 L1/L2 在 [ilpoint/WhaleGenie.Core](https://github.com/ilpoint/WhaleGenie.Core) 提交并验收；主仓 `core/` 只读，按 [dev/environment.md](../dev/environment.md) 的 subtree/`sync-core` 流程取回。`FormulaJson.Read` 签名不变，不新增公共比较器；Core 公共面基线不变。

## 法则

以下六条是这一阶段的“形式规格”，由测试生成器执行：

- **L1 值往返**：对每个 `ExpressionNode` v（`HoleNode` 除外），`Read(Write(v)) ≡ v`。
- **L2 文件幂等**：对 `Write` 产生的规范 JSON j，`Write(Read(j)) ≡ j`。
- **L3 步骤往返**：对每个动作、参数与行形状，`FromJson(ToJson(s)) ≡ s`。
- **L4 保存幂等**：`ToJson(FromJson(ToJson(s))) ≡ ToJson(s)`。
- **L5 草稿往返**：对每个 `StepParameter` p，`ToStep(Apply(p)) ≡ p`；改一格时只有该格的 JSON 变化。
- **L6 线缆往返**：对每个字段类型，`model(decode(encode(snapshot))) ≡ snapshot`。

测试位置：L1/L2 在库仓的 `WhaleGenie.Core.Tests`；L3/L4 在 `WhaleGenie.Domain.Tests`；L5 在 `WhaleGenie.Editor.Tests`；L6 在桥测试。生成器覆盖 Text、Number、Date、Name、Run、Call、Unary、Binary、Group、List 与中文名字；数字用 decimal/double/int 三种 JSON 表示。

## 职责

| 层 | 拥有 | 不得 | 法则 |
| --- | --- | --- | --- |
| `Core.Expressions` | 值代数、形状编解码 | 文件、工程、页面 | L1、L2 |
| `Core.Execution` | 运行语义、动作分派、设备契约 | 项目文件、编辑 | 动作矩阵、回放 |
| `Domain.Documents` | 宏/工程模型、步骤文件编解码、设计期检查 | 窗口、设备、编辑交互 | L3、L4 |
| `Domain.Storage` | 包、manifest、zip | 解释字段形状（用 Documents） | 包格式版本 |
| `Editing.Value`（纯） | 草稿与形状的纯变换 | UI、IO、字符串回写 | L5 |
| `Editing.Forms`（外壳） | 显示、输入、页面状态 | 产出模型行 | 只产草稿 |
| `Contracts` | 线缆载荷、生成 TS | 形状解释、业务规则 | L6 |
| `webui` | 渲染、编辑、拾取入口 | 解释/修复形状、第二套解析器 | 只收发 parts |
| `App` | 组合根、桥、触发器、MCP 宿主 | 模型规则 | 桥往返 |

## 替代方案

- **只修 webui**：拒绝。页面只收到字符串，修页面等于在 TS 里复制形状代数，C# 仍会丢数据。
- **先做函数式核心重写**：拒绝。FP 不修数据边界；先重写会把同一类丢失带进新结构，再修一遍。
- **保留字符串并按内容猜名字**：拒绝。行文里的名字与普通文字无法可靠区分；这正是当前 bug 的来源。
- **新增 `WhaleGenie.Shapes` 工程**：拒绝。Core 已经拥有形状；新工程只增加依赖方向与构建成本。
- **现在就上 TLA+/Coq**：拒绝。本类问题是数据代数，不是并发协议；轻量形式化足够。

## 后果

- 正确的旧文件保持兼容：名字本来写成 `{"name":...}`；本修复不改变包格式版本。
- 已被写成文本的行无法自动恢复（信息已丢），只能由只读扫描器列出可疑行，人工修。扫描器不自动改。
- 行单元格契约变更会重新生成 TypeScript；`ContractGenerationTests` 守住一致性。
- 法则与变异测试进入验收；codec 与行变换的存活变异须逐条给理由。
- Core 公共成员不变；`PublicAPI.Shipped.txt` 不动。

## 非目标

- 不在本阶段做引擎运行时重写、动作合并或包格式断代；它们属于后续阶段。
- 不把形状代数搬进页面，不新增第二套协议类型。
- 不自动修复已坏工程，不做旧宏格式迁移。
- 不用 TLA+/Coq 证明整个程序；并发、门与取消留给引擎阶段的 model-based tests，必要时再评估 TLA+。
