# ADR 0001：网页壳的分层与拆分取舍

- 状态：已采纳
- 日期：2026-11-22
- 相关：[docs/dev/webui.md](../dev/webui.md)、[architecture/interface.md](../architecture/interface.md)

## 背景

网页壳从 Avalonia 原生界面迁移到 Lit 后，`editor-store.ts` 长到 1574 行、约 160 个方法，
`protocol.ts` 1229 行，宏设置、设置页、步骤树与动作字段也各自混了多种关注点。桥已经收口在
`shared/bridge`，但页面仍在多处直接调用 `bridge.value.call`，组件重复手写订阅/退订，缺少
规模与边界检查。

## 决策

1. **保留 `shared/` 作为跨 feature 层，不重命名为 `core/`。**
   重命名会改 90+ import，没有行为收益；`shared/bridge` 已经是通信唯一入口。

2. **协议按域拆文件，`protocol.ts` 只做 barrel。**
   一个全局 `types.ts` 会成为新的上帝文件；barrel 让现有 import 零改动，新代码可以直接 import
   域文件。C# DTO 与文件格式不变。

3. **EditorStore 只做组合根，命令按族进 service。**
   `editor-core` 持有唯一状态盒、监听与通用请求助手；session/action/steps/value/triggers/
   declarations/variables/recording/ui 各自成 service。C# 仍是唯一事实来源，service 是命令网关与
   快照应用层。组件调用改为 `editor.<family>.<command>`，由 TypeScript 穷举。

4. **页面状态与生命周期用 store + controller，不引入 `@lit/context`。**
   当前没有 prop drilling；所有组件是同一棵树下的模块单例消费者。`StoreController`、
   `WindowEventController`、`SubscriptionController` 已覆盖订阅与监听。只有当同一页面需要
   多个隔离的 store 实例或被嵌入两个宿主时，才重新评估 context。

5. **子组件与渲染模块分开使用。**
   Lit 组件的 Shadow DOM 会把父组件 CSS 挡在外面。独立区块抽组件时连同样式一起搬；step-tree 行、
   action-field 字段族与父组件的选中、焦点、拖放、CSS 强耦合，因此抽同 Shadow DOM 的渲染模块。

6. **ESLint 规模规则用 error + bulk suppressions 渐进收紧。**
   规则为 400 行文件、80 行函数、复杂度 15、深度 4；样式文件豁免。先给现有违规建基线，
   `lint --max-warnings 0` 让新增违规立即失败，拆完一个文件就 prune。bulk suppressions 只处理
   error 级别的违规，因此这些规则设成 error 而不是 warning。

7. **不加路径别名、不加 lint-staged、不新增 npm 依赖。**
   `@/*` 需要同步 tsconfig、vite、vitest 三处并改约 150 个 import，收益低于风险；`npm run check`
   与 CI 已覆盖提交前检查。新增依赖须按 AGENTS 的发布与许可证规则单独评估。

8. **单元测试继续放 `webui/tests/`，不改成同目录 `*.test.ts`。**
   让 `src` 只含生产代码；测试按 feature 镜像目录。e2e 留在 `e2e/`。

## 后果

- 编辑器的命令入口从扁平方法变成命名空间；所有调用点已由 TypeScript 检查并完成迁移。
- `editor-store.ts` 从 1574 行降到约 115 行；最大的 service 约 330 行。
- 组件不再手写 store 订阅/退订；窗口监听走 controller。
- 架构边界由 `tests/architecture.test.ts` 守护；规模由 ESLint 抑制基线守护。
- 宏设置、设置页、步骤树、动作字段已按同一 playbook 拆到 400 行以内；其中较长的模板函数仍留在
  `eslint-suppressions.json`，下一批可按字段/区块继续拆。
- mock 宿主仍静态打进单文件产物。步骤工具已拆成展示（327 行）与结构编辑（380 行）两个文件，
  设置/词表夹具也已移出；`mock.ts` 已按 settings/run/shell 路由到 handler，降到 365 行。
  `mock-editor.ts` 的 `run` 已拆到 action/value/session handler，action 表单方法也已移到
  `mock-editor-action-form`，文件降到 924 行；下一批拆 `openValue`/`valueCommand` 与 steps/trigger 方法。
  要剔除 mock 需另立 ADR。
