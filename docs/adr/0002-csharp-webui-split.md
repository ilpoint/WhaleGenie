# ADR 0002：C# 应用层与 Lit 视图的分工

- 状态：已采纳
- 日期：2026-11-22
- 相关：[架构](../architecture.md)、[网页壳结构](../dev/webui.md)、[界面与桥接](../architecture/interface.md)

## 背景

界面已整体迁到 WebView2 + Lit，但 C# 仍按“原生窗口的视图模型”组织：编辑、运行与设置模型住在界面工程，
带 Avalonia 的位图与画笔；桥是 189 个按字段拆的远程调用，返回匿名对象；页面另写一份协议类型，并用
5,259 行 mock 复刻宿主。结果是协议无对账、命令要追请求、运行面板 4Hz 推整份快照，原生时代的视图模型
与样式还留在仓里。

## 决策

1. **C# 是应用与事实来源，页面是视图与临时状态。**
   文档、动作语义、校验、撤销、运行、触发器、设置、持久化与原生面在 C#；选择、展开、滚动、焦点、
   输入草稿、列表搜索/筛选/排序在页面。
2. **新增 `WhaleGenie.Contracts`（只依赖 BCL）与 `WhaleGenie.Editing`（无 Avalonia）。**
   Contracts 放桥的记录、命令/事件联合、方法表与 UI 端口；Editing 放编辑模型与会话。
   `WhaleGenie.App` 放运行、设置、项目服务、桥路由与处理器；`WhaleGenie` 只剩窗口、原生面与适配器。
3. **契约由 C# 生成 TypeScript。**
   `tools/WhaleGenie.ContractsGen` 输出 `webui/src/shared/bridge/generated/`；`BridgeApi.call` 按方法名推导
   参数与返回值；生成文件与源码不一致时由 `ContractGenerationTests` 失败。
4. **编辑命令按聚合收成联合。**
   `editor.session/step/parameter/trigger/variable/global/action/value/record` 各一条，命令是带 `kind` 的记录，
   C# 用无 default 的 `switch` 表达式分派；新增命令未处理即编译失败。
5. **命令结果只回变化的切片，并带 `revision`。**
   文档、步骤树、单行、动作表单、取值快照分别可空；页面丢弃过期结果，不再用追请求补齐页签与表单。
6. **运行、日志与取景分开推。**
   运行、步骤、日志与取景分开推：`run.changed` 只带标量/参数/全局/局部，`run.steps` 按签名、
   `run.log` 带 `logFrom` 游标只追加、`run.look` 只在变化时推送（最长边 320 的 BMP 缩略图）；
   离开面板调用 `run.unload` 停止运行、计时器与订阅。
7. **设置是内存模型 + 类型化补丁。**
   `AppSettings` 启动读一次、原子写、路径可注入；`settings.update` 接收可空字段的补丁，不再解析
   `{field, value}` 字符串。
8. **处理器不引用 Avalonia/WebView。**
   文件对话框、拾取器、窗口按钮、主题、声音、图像编码都经 Contracts 的端口；UI 工程实现端口。
   分层由 `LayeringTests` 与新增的 `WhaleGenie.Bridge.Tests` 保证。

## 暂缓

- 长耗时处理器的 IO 下放与 busy 事件延后到 P6/P7：保存/打开目前仍由界面线程持有的
  `ProjectWorkspace` 完成；先拆出 App/Editing 与 `IUiDispatcher`，再把纯文件 IO 放到工作线程，
  busy 复用同一条推送通道。

## 后果

- 桥方法从 189 降到 ≤70，编辑命令从 119 降到 ≤12；处理器按域拆分，每个 <400 行。
- 编辑器、动作表单、取值与变量模型可在不加载 Avalonia 的测试工程里运行；桥测试不再需要无头会话。
- 原生主题按“仍在使用”收缩，死样式与只为主持它们的测试删除。
- 页面仍是唯一视图，但不再手写 wire 类型；mock 由生成的契约与 C# 夹具约束。
- 迁移分阶段进行，阶段之间不要求全仓常绿；过时断言直接删除，不保留 `skip`。

## 非目标

- 不把宏编辑器重写成纯 TS；文档、草稿与历史继续由 C# 持有。
- 不引入协议版本协商（程序与页面同包同版）。
- 不新增 UI 框架、状态库、CSS 体系或第三方依赖。
- 不动 `core/` 子树，不保留旧宏格式与旧桥方法名兼容。
