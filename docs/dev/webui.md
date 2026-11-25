# 网页壳结构、拆分与工程规范

所属：[WhaleGenie 开发说明](../dev.md)。网页壳是界面层的一部分，桥与边界见
[architecture/interface.md](../architecture/interface.md#76-网页壳与桥接)；用户行为见
[manual.md](../manual.md)。

## 1. 分层与依赖方向

```text
main.ts
  └─ app/            壳、标题栏、导航（组合 features）
       └─ features/  页面、store、service、controller、子组件
            └─ shared/  bridge、lit、input、navigation、window-rule、通用 store
```

- `app/` 可以 import `features/` 与 `shared/`；`features/` 只能 import `shared/` 和自己的文件。
- `shared/` 不得 import `features/` 或 `app/`。
- feature 之间默认不互相 import；确需组合时只经对方的 `index.ts`（当前例子：`macros -> mcp`、
  `library -> editor`、`app -> editor`）。
- `tests/architecture.test.ts` 按解析后的路径检查这些规则；原生 `no-restricted-imports` 只看
  import 字符串，不解析相对路径，因此边界以该测试为准。

## 2. 目录约定

现有健康文件不为了「树好看」而搬家。新文件和被拆文件按职责落位：

```text
features/<feature>/
  <feature>-page.ts       路由入口与页面装配
  <feature>-store.ts      跨组件状态 + 命令门面（可以只是组合）
  state/                  state 类型、初始值、快照应用
  services/               命令族；可以依赖 BridgeApi，不 import lit 或组件
  model/                  纯函数与选择逻辑
  components/             独立 UI 区块（各自一个文件）
  controllers/            生命周期相关逻辑（订阅、捕获、拖拽状态机）
  styles/                 纯样式模块
  index.ts                只有需要被其他 feature 组合时才建
```

共享代码按用途放 `shared/bridge`、`shared/lit`、`shared/input`、`shared/navigation`、
`shared/window-rule` 与既有的 `shared/strings|theme|app|project|window`。

## 3. 文件命名

| 角色 | 约定 | 例子 |
| --- | --- | --- |
| 页面 | `<feature>-page.ts` | `settings-page.ts` |
| store | `<feature>-store.ts` | `editor-store.ts` |
| service | `<feature>-service.ts` 或 `<family>-service.ts` | `session-service.ts` |
| controller | `*-controller.ts` | `settings-capture-controller.ts` |
| 纯逻辑 | `*-model.ts` | `editor-model.ts` |
| 样式 | `*-css.ts` | `settings-page-css.ts` |
| 协议 | `protocol/<domain>.ts` | `protocol/editor.ts` |
| 单元测试 | `tests/**/<name>.test.ts` | `tests/features/editor/editor-model.test.ts` |

组件文件 kebab-case，自定义元素 `wg-<feature>-<part>`，类名 PascalCase；保留 `wg-` 前缀。
不建全局 `utils.ts`、`types.ts`、`helpers.ts`；按业务关注点命名。

## 4. 新代码放哪里

| 要写的东西 | 放哪里 |
| --- | --- |
| 一次桥命令、快照应用、错误归一 | feature 的 service/store |
| 跟视图无关的选择、过滤、键位映射 | feature 的 `*-model.ts` + 单测 |
| connectedCallback 订阅、window 监听、捕获模式 | `shared/lit` 的 controller |
| 跨组件/跨路由的 UI 状态 | feature store |
| 只影响本组件的临时状态（展开、搜索、对话框） | 组件的 `@state` |
| 独立 UI 区块（有自己生命周期或需要独立渲染） | Lit 子组件 |
| 同一 Shadow DOM 内的字段分组、行渲染 | 同目录渲染模块，不强行拆组件 |
| 布局、间距、颜色 | `*-css.ts`，只用 Fluent 令牌 |

## 5. 桥的规则

- 页面只通过 `shared/bridge/bridge.js` 的 `bridge` 门面或 `bridgeApi` 说话；`transport.ts` 与
  `pick.ts` 是唯一可以读 `window.invokeCSharpAction` / `window.__whale` 的文件。
- 需要可测的命令族依赖 `BridgeApi`（`call` / `on`），不要直接依赖 `HostTransport`。
- 视图可以做一行命令（如窗口按钮），但多步编排、快照应用、`actionError/valueError` 归必须进
  service/store。
- `protocol.ts` 是 barrel；新代码可以直接 import `protocol/<domain>.ts`，旧 import 保持不变。
- **线缆类型与方法名由 C# 生成**：`WhaleGenie.Contracts` 里带 `[BridgeContract]` 的记录写成
  `webui/src/shared/bridge/generated/types.ts`，`BridgeMethods.All` 写成
  `generated/methods.ts` 的 `BridgeMethod` 联合。改了记录或注册方法就跑
  `dotnet run --project tools/WhaleGenie.ContractsGen -- webui/src/shared/bridge/generated` 重新生成；
  `ContractGenerationTests` 在没有重新生成时失败。生成文件不手改，页面只从它 import 类型与
  `BridgeMethod`；`client.call` 的方法名因此由编译器检查。
- **同一聚合的命令用判别联合**：C# 的 `[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]` +
  `[JsonDerivedType]` 会生成 TypeScript 判别联合；派生记录的可空字段是 `field?: T | null`
  （命令请求可省略）。首批是 `editor.value`（open/command/confirm/cancel）与
  `editor.trigger`（add/remove/field 等）、`editor.action`（page/field/row 等）、
  `editor.declaration`（family/kind 矩阵）、`editor.condition`（行/组/join）、
  `editor.variable`（local/global × add/patch/remove）、`editor.session`（open/new/activate/close/…）、
  `editor.steps`（多选/复制/粘贴）、`editor.step`（单步/参数）、
  `editor.record`（start/stop/state/option/delayScale）。保存不是会话联合的一员：
  `editor.save` / `editor.saveAll` 返回 `EditorSaveResponse`，逐文档给出
  `saved/unchanged/blocked/conflict/missing` 与原因；只有前两种算成功。`editor.discardAll`、
  `editor.unsavedDocuments` 与 `editor.session { kind:'deactivate' }` 分别负责离开时的丢弃、名单与草稿收尾。
- 编辑命令的返回值是带 `documentId`/`tabs`/`activeDocumentId`/`actionForm` 的完整快照；页签身份
  是稳定 `documentId`，不是宏名。宿主在命令之外关闭文档时用事件 `editor.changed` 推同一形状的
  快照（没有活动文档时为 `null`），页面按 revision 应用。
- 快照带单调 `revision`，由桥在整条编辑器线上递增，不是每个文档各自递增；页面可以跨页签
  比较它。页面丢弃 revision 不大于当前值的快照，迟到的响应不会覆盖新状态。页面自己的编辑命令
  串行执行（`EditorCore` 队列），不与激活、保存互相穿插。
- 宿主关闭窗口/退出时问“这些宏还没保存”的通道是事件 `editor.leaveAsked`（带 `requestId` 与
  `documents`）与命令 `editor.leaveAnswered`（`{ requestId, leave }`）。页面用同一个
  `wg-editor-unsaved` 回答；事件用 `BridgePushMode.All`，两个询问不会互相覆盖。超时由宿主推
  `editor.leaveCancelled` 让页面关闭同一请求。
- 编辑器反馈只有一层对话框：`EditorDialogService` 统一处理关闭文档、离开编辑器与宿主询问，
  保存失败/冲突的逐条原因留在对话框里；页面没有第二个 `closeRequest` 副本。C# 对“页面以为干净、
  实际脏”的关闭回 `Editor.Unsaved`，页面识别后打开同一三选一，禁止写成红色横幅。空状态不渲染
  文档级 notice/error。
  矩阵型命令用一条 `[BridgeRequest]` 记录，可空字段生成
  `?:`，不必为每个 kind 建派生记录。新增编辑器命令时优先扩展现有联合，不再加一条按字段拆的
  `editor.*` 方法。
- 状态类推送在服务端按事件名合并；日志等流式事件必须传 `BridgePushMode.All`，否则中间值会被
  最新值覆盖。
- C# 仍是唯一事实来源；页面不解析公式、不按动作名推断字段。
- 方法名清单由 `BridgeMethodInventoryTests` 对账；新增/改名桥方法要同步 C# 与页面。

## 6. 状态与 Controller

- `StoreController<T>`：把 store 订阅绑定到元素生命周期，订阅时立即回调，断开时退订。
- `WindowEventController`：绑定一个 window 监听，支持 capture 选项。
- `SubscriptionController`：给桥事件或任何返回 unsubscribe 句柄的 API 用。
- 控制器字段用 `protected readonly`，避免被 TypeScript 当作未使用私有成员；控制器只负责生命周期，
  不把纯函数塞进去。
- 只有跨组件、跨路由、需要生命周期清理的状态才进 store；局部临时状态留在组件，避免造一个更大的
  全局 store。当前不为 prop drilling 引入 `@lit/context`。

## 7. 子组件与渲染模块

Lit 组件有自己的 Shadow DOM，父组件的 CSS 不会进入子组件。因此：

- 独立区块抽组件时，把对应样式一起搬进子组件，并保留 e2e 使用的标签、类名与 `data-*`。
- 与父组件强耦合的行、字段分组（如 step-tree 的行、action-field 的字段族）抽成同目录渲染模块，
  仍在父组件的 Shadow DOM 内渲染；拆组件会把样式和事件绑定撕碎。
- 组件标签名、类名、`data-*` 是 e2e 契约；优先保留，不轻易改测试。

## 8. 规模与复杂度

ESLint（`src/**/*.ts`）：

- `max-lines`: 400（跳过空行/注释）
- `max-lines-per-function`: 80
- `complexity`: 15
- `max-depth`: 4

`src/**/*-css.ts` 关闭规模规则；样式是单一关注点，单文件超过 700 行时人工评估是否按页面区域拆。
`tests/**` 与 `e2e/**` 的文件上限为 800 行，函数长度不设限（场景叙事）。

阈值用 ESLint 9 的 bulk suppressions 渐进收紧：

1. 新规则或新文件产生违规时，先修；确实要延后的，才进 `eslint-suppressions.json`。
2. `npm run lint` 带 `--max-warnings 0`，新增违规不会因为「只是警告」而漏过。
3. 每拆完一个文件跑一次 `--prune-suppressions`；refactored 文件不应保留抑制项。
4. 文件改名/移动后必须 prune，否则旧路径的抑制项失效。

判据：拆完一个文件应至少减少 30% 行数或完整移走一个关注点；service 超过 400 行说明混了多个命令族，
组件抽完 CSS 仍超过 400 行说明混了多个 UI 区块。不要为凑行数机械切割。

## 9. 测试

- 纯函数：`tests/features/**`，覆盖边界与失败输入。
- service/store：用 `tests/fakes/bridge.ts` 的 `FakeBridge` 记录 `{method, params}`，按方法返回/抛错；
  成功、失败、状态应用、返回值都要有。
- controller：用 `tests/fakes/host.ts` 的 `FakeHost` 验证连接/断开、重复连接、requestUpdate。
- 结构：`tests/architecture.test.ts` 检查 import 路径、宿主全局与 DOM-free 层。
- e2e：入口是 `npm run e2e`（`playwright test`，配置在 `webui/playwright.config.ts`，走 mock 宿主与
  系统 Edge）。它需要先有构建产物（`npm run build`），因此在 CI 的构建步骤之后执行，`npm run check`
  不包含它；拆分组件时优先保留 DOM 契约，而不是改测试。
- mock 宿主的固定数据在 `webui/src/shared/bridge/data/*.json`（动作目录、表单骨架、步骤模板、
  浮层快照等），命令分派与状态机留在 `mock-*.ts`：改数据只动 JSON，改行为才动 TS。
- C# 对账：网页改动至少跑 `WhaleGenie.Tests` 中 `BridgeMethodInventoryTests`、`WebUiBoundaryTests`、
  `SourceEncodingTests` 与 `AboutPageTests`。

## 10. 拆分 playbook

1. 先做关注点体检：行数、方法簇、状态字段、CSS、桥调用、生命周期监听。
2. 纯类型按域移到 `protocol/<domain>.ts`，保留 barrel；纯样式移到 `*-css.ts`。
3. 纯函数移到 `*-model.ts` 并加单测；先不改调用点。
4. 桥命令按族移到 service；保留旧门面转发，逐个子提交。
5. 独立 UI 区块抽组件（连同样式）；强耦合行/字段抽渲染模块。
6. 生命周期逻辑抽 controller；最后把旧门面方法删除，组件切到命名空间 API。
7. 每步跑 `npm run check`；改动页面后等构建完成再跑 `npm run e2e`（受影响的 Playwright）；最后跑
   全量 C# 测试。

每一步只做一类改动：机械移动、行为变化、规则变化分开提交。任何一步都可以单独 revert。

## 11. 新增页面/命令/组件

新增页面：

1. 在 `shared/navigation/router.ts` 加路由，在 `app/shell.ts` 的 `renderRoute` 接页面。空 hash 与旧
   `#home` 都落到 `macros`；页面切换走异步 `go()`，需要拦下离开时用 `setNavigationGuard`，编辑器用它
   询问未保存的宏。
2. 导航项由 `Shell.<Route>` 键驱动；在 `Strings` 两种语言补键。
3. 在 `features/<name>/` 建 page；需要状态时先想清楚是本地还是跨组件。
4. 加一条 e2e 冒烟；文案键由 `WebUiBoundaryTests` 对账。

新增桥命令：

1. C# 端用 `域.动作` 命名并注册。
2. TS 端只在 service/store 里调用；结果类型放进 `protocol/<domain>.ts`。
3. 更新 `BridgeMethodInventoryTests` 的 Expected 列表（仅新增方法时）。
4. 加 service 的成功/失败用例。

新增组件：

1. 先判断它是否有独立生命周期或需要独立 Shadow DOM；没有就用渲染模块。
2. 组件文件与标签按命名约定；样式随组件走，只用 Fluent 令牌。
3. 需要订阅时用 controller，不在 connectedCallback 里手写 subscribe/unsubscribe 对。
