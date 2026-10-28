# 重构计划

把评审里的债按「会不会出事」排序，分五个阶段推进。每个阶段能独立提交、独立验证，任何一阶段停下来都不留半截状态。

排序原则：

- **先修会出错的，再动结构。** 结构与正确性同时改，出了问题分不清是哪一边。
- **一次只改一类。** 一个阶段里既有"改行为"又有"改依赖方向"时，测试红了不知道找谁。
- **化繁为简**：能用一处改动解决的，不铺开成三处；能删代码的，不新增抽象。

## 阶段 0：把结论改对（文档与约定）　✅ 已完成（`918bba4`）

评审里"没有 DI 容器"原先被写成**有意的取舍**，这是错的。.NET 项目的依赖由容器组装是常规做法，手工 `new` 只是省了配置，代价是把依赖关系散在 46 个构造调用里。这一阶段只改文字与约定，不动代码：

- `doc/architecture.md` 第 13 章删掉"没有 DI 容器"这一条，改为第 14 章里的一项待办；`LICENSE` 式的措辞（"容器换来的间接层不划算"）一并删掉。
- `AGENTS.md` 加一条规范：依赖从构造函数进来，组装只发生在组合根；新增服务必须注册，不许在窗口里 `new` 一个带状态的依赖。
- 新增这份计划，挂进 `README.md` 与 `AGENTS.md` 的文档表。

验收：文档里不再出现"不需要 DI"的结论；计划可执行。

## 阶段 1：正确性（P0，三条）　✅ 已完成（`744a211`、`1f00f1b`、`8745fa4`）

三条都会真实地做错事，且都不需要动结构。

1. **备份下沉到 `MacroPackage.Save`。** 现在只有 MCP 通道在写之前 `File.Copy(.bak)`，用户按 Ctrl+S 不备份。改法：`Save` 在 `File.Move(tmp, path)` 之前把已存在的目标复制成 `.bak`，然后删掉 `McpLiveProject.SaveAsync` 里那段。验收：两条路径都产生 `.bak`，用例覆盖"第二次保存才有 `.bak`、第一次没有"。
2. **校验的阻断与提示统一。** `MacroProblem` 加 `Severity`（`Blocking` / `Hint`），`Inspect` 逐条标注；编辑器与 `McpDraftStore` 都只按这一档判断，删掉两处各自的解释。验收：同一个宏在界面与 MCP 侧得到同样的"能不能存/能不能 apply"。
3. **试运行的看屏幕清单从目录派生。** `CanLook` 的 14 项手抄清单换成 `ActionSemanticsCatalog` 的 `LooksAtScreen`，`SeeOnce` 补齐目录里多出来的三条（`vision.waitStable` / `findColors` / `pixelMatches`）。验收：目录里每个 `LooksAtScreen` 的动作都能"试一下"，且有一条用例把两边的集合钉死。

## 阶段 2：依赖注入（结构，一次到位）　✅ 已完成（`78445d3`）

引 `Microsoft.Extensions.DependencyInjection`（已随 ASP.NET Core 进来，不新增包）。目标不是"用上容器"，是让**谁能拿到什么**从 46 个 `new` 收成一张注册表。

1. **组合根**：`App.axaml.cs` 里建 `ServiceCollection`，`BuildServiceProvider()`，退出时 `Dispose`。
2. **注册什么**：`IDeviceLayer`（单例，懒）、`MainViewModel`（单例）、`MacroTriggerService`、`ProjectMacroLibrary`、`McpDraftStore`、`McpLiveProject`、`McpServerHost` 与四个工具类、`McpResources`。凡是"有状态、要被共享"的，注册；纯数据载体（行、选项 VM）不注册。
3. **窗口**：`MainWindow` 由容器解析；带参数的子窗口用工厂委托注册（`Func<MacroItem, MacroEditorWindow>`），窗口内部不再 `new` 依赖。
4. **MCP 两套合一**：`McpServerHost.Tools(...)` 的手写 `owners` 数组改为从容器解析，删掉"忘了加进数组就静默不注册"这个坑。
5. **无窗口模式**：`--mcp` 走同一份注册，只是不解析窗口。

不做的事：不改 Avalonia 的 XAML 实例化（窗口仍从工厂来）；不给 ViewModel 套接口（这个项目不需要为每个 VM 造 `IXxxViewModel`）。

验收：`LayeringTests` 仍然绿（组合根之外仍不许 `new` 平台实现）；`--mcp-doctor --spawn` 仍然 37 条全过；测试能注入替身而不必改构造调用。

## 阶段 3：引擎内聚（P1）　✅ 已完成（`6315cc3`、`ca1351d`、`ad11459`）

1. **运行器装配收成一处**：`RunViewModel`、`MacroTriggerService`、`McpRunTools` 三处逐行相同的装配换成一个 `RunFactory`（放进阶段 2 的容器）。
2. **重试判断合一**：`RunAttempts` 里 signal 分支与 catch 分支的两段判断合成一个 `Outcome`，再决定重试/跳过/停止。
3. **共用原语归位**：`Give`（Window）、`Pause`/`WaitForFlagAsync`（Script）、`Gate`（Detection）、`CanLook`/`SeeOnce`（Input）提到 `MacroRunner.Support.cs`，按族切分的文件只放本族的东西。

   **实际做的是注释，不是搬迁。** 搬到一起是纯装饰：不改变行为，却会切断 `git blame`（以后查"这行为什么这样写"要跳文件）。真正的痛点是"按文件名找代码会找错三次"，所以改成在类开头写明这几处的实际位置。真要搬，等哪个方法自然要改时顺带挪。
4. **`Inspect` 拆分**：1083 行的单方法按检查族拆成小类，45 个裸 code 提成常量（与 `RunKeys` 同样的待遇）。

   **只做了常量那一半。** `MacroProblemCode` 让 45 个 code 与 `Blocking` 集合的拼写由编译器对上——上一轮就因为漏了元组里的五个，体检红在 `reject.child_keys`。拆分没做：`Inspect` 内部已经是一串按族命名的局部函数（`CheckCondition`、`CheckPlacement`、`CheckUserFailureKey`…），拆成方法要穿过十几个被捕获的状态，收益不抵风险。它结构上是"一个调度方法 + 按族分开的检查"，不是泥球。
5. **动作分派与目录对齐**：先加一条"目录声明的动作必须被某个 `case` 覆盖"的用例，把 217 个字面量钉在编译期与测试之间；重写分派留到以后。

   **已经有了。** `Core.Tests/Bench` 的 `Every_action_runs_and_matches_its_declaration` 把目录里每一条都合成一步真的跑一遍，没被任何 `case` 接住的动作会以 `Run.Unsupported` 结束、判成失败。所以这条守卫早就在，缺的只是"重写分派"本身——那留到以后。

验收：引擎层 1461 条用例全绿；重试与跳过行为有专门用例（现在没有，因为两处写的时候没人能同时看到）。

## 阶段 4：界面与设备（P2，分批）

这一阶段不专门开一轮，按"碰到哪改哪"推进，但方向定死：

1. **设备接口按消费者收窄**：构造函数只接真正用到的窄接口（`IScreenDevice`、`IWindowDevice`…），聚合接口留给组合根。替身随之变瘦（`FakeDeviceLayer` 1534 行会自己掉下来）。
2. **视图搬业务**：`MainWindow` 的触发器与恢复、`MacroEditorWindow` 的文档与录制、`ActionEditorView` 的试运行，一处一处搬到应用层服务（阶段 2 的容器正好用来拿）。
3. **项目状态合并**：全局变量从静态 `VariableCatalog.Globals` 变成项目状态的一部分（或 `IGlobalVariables` 注入），让 `McpLiveProject.Snapshot()` 不必一半实例一半静态。

   **做了装载收口，没做注入。** 三个入口（打开、导入、恢复）各自 Clear + Add + `PersistentVariables.ApplyTo`，抽成 `LoadSharedVariables(globals, replace)`，并加了一条"打开另一个项目时旧变量不残留"。注入没做：94 处访问、17 个文件，而 `VariableCatalog` 的静态方法（`IsGlobal` / `FindGlobal` / `NamesFor`）还被 `MacroCheck` 这类静态类用着，改成注入要连带改那些签名。收益是概念纯度——程序一次只开一个项目，静态就是"当前项目那一份"，行为上没有差别。等真要支持多项目或并行测试隔离时再做。
4. **`Execution` 改名**：应用层那个改 `WhaleGenie.App`，一次性机械改，看栈追踪不再分不清。

验收：每个小项独立提交；`FakeDeviceLayer` 行数持续下降；窗口类不再创建服务。

## 顺序与判断

阶段 0 与 1 一起做（都是小改动，一次验证）。阶段 2 单独一轮，因为它改的是所有构造点，必须独占一次全量验证。阶段 3 可以按条拆成多个提交。阶段 4 长期存在。

每阶段结束都跑三样：`dotnet format --verify-no-changes`、`dotnet test WhaleGenie.slnx -c Release`、`whalegenie --mcp-doctor --spawn`。三者全绿才算完成——这也是 `AGENTS.md` 第 1 条的要求。
