# WhaleGenie.Core

鲸灵（WhaleGenie）的宏引擎与 Windows 适配器：动作语义、表达式、变量、设备契约、录制翻译，
以及键鼠钩子、UI Automation、找图找色、OCR、浏览器、脚本宿主、手柄驱动和画面获取。

这个仓库是鲸灵的内核部分，不含界面。它做三件事：

| 工程 | 目标框架 | 是什么 |
| --- | --- | --- |
| `src/WhaleGenie.Core` | `net10.0` | 执行引擎。没有窗口、没有平台依赖，引用闭包只有 BCL 与 `CsvHelper` |
| `src/WhaleGenie.GraphicsCapture` | `net10.0-windows10.0.19041` | 画面获取：Graphics Capture、桌面复制、GDI、PrintWindow |
| `src/WhaleGenie.Platform.Windows` | `net10.0-windows10.0.19041` | 上面那些设备契约的 Windows 实现 |

界面（Avalonia 壳）是另一个仓库里的消费者，只引用这里打出的一个包。

## 用起来

```xml
<!-- 一个引用，三个程序集一次到位 -->
<PackageReference Include="WhaleGenie.Platform.Windows" Version="x.y.z" />
```

只做无窗口批处理、不碰机器的程序改引 `WhaleGenie.Core`，拿到的是纯 `net10.0` 的引擎：

```csharp
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;

// 引擎自己就能说清有哪些动作，不需要任何设备
foreach (var action in ActionSemanticsCatalog.All)
{
    Console.WriteLine($"{action.Key}: {action.Parameters.Count} 个参数");
}

// 键的名字与键码是同一套词汇
var key = KeyNames.Resolve("ctrl")!;
Console.WriteLine(KeyNames.Name(key));   // Ctrl
```

要真的操作机器，再用 `WhaleGenie.Platform.Windows` 里的 `WindowsDeviceLayer` 实现
`IDeviceLayer`；不想碰真设备时用 `NullDeviceLayer`，它在被真正用到时才报错。

## 被鲸灵主仓使用

主仓 [ilpoint/WhaleGenie](https://github.com/ilpoint/WhaleGenie) 用 `git subtree` 把整个仓库
vendor 在 `core/` 下：

```bash
git subtree add  --prefix=core core main --squash    # 第一次
git subtree pull --prefix=core core main --squash    # 之后
```

所以引擎、适配器与它们的用例**在那边是只读的**：改动在这里提交，主仓取回。主仓的
`sync-core.yml` 每天自动做一次，取到新提交就先构建、跑完它的八套件，全绿才推。

## 构建与验收

```pwsh
dotnet build WhaleGenie.Core.slnx -c Release
dotnet test  WhaleGenie.Core.slnx -c Release
dotnet format WhaleGenie.Core.slnx --verify-no-changes --no-restore
dotnet pack src/WhaleGenie.Platform.Windows/WhaleGenie.Platform.Windows.csproj -c Release -p:Platform=x64
```

包落在 `dist/nuget/`。三套用例（引擎、画面获取、Windows 适配器）都在这个仓库里，适配器那套会
驱动真实的 Windows 设备。

引擎那套（`tests/WhaleGenie.Core.Tests`）里有一条动作矩阵：按语义目录把每个动作合成一个步骤，
在替身设备层上跑一遍，核对结局、返回值与副作用声明。它只在要报告时写文件：

```pwsh
$env:ACTION_MATRIX_REPORT = "TestResults/action-matrix.md"
dotnet test tests/WhaleGenie.Core.Tests -c Release --filter "FullyQualifiedName~ActionBench"
Remove-Item Env:ACTION_MATRIX_REPORT
```

引擎的引用闭包、目标框架、`InternalsVisibleTo` 名单与两个方向（引擎不命名画面工程与 Windows
适配器）钉在 `EngineLayeringTests` 里：改动先在这里红，再谈打包。

### 变异测试

值编解码、录制翻译与动作规则用 Stryker.NET 5.1.0 做开发期基线（Apache-2.0，不随 NuGet 包发布）：

```pwsh
dotnet tool restore
pwsh tools/stryker.ps1 -Area recording   # 或上表的任一区域，或 all
```

每个区域是一小组文件、判定它的用例与自己的报告；报告写 `TestResults/Stryker/<区域>/`，不提交。
区域按“一组测试大概多久”切开：值规则与键名由快用例判定，窗口与动作规则由替身动作用例判定，
一个区域跑几分钟，才适合每次改完都跑。

Release 基线（2026-11，本机）：

| 区域 | 计分变异 | 分数 | 说明 |
| --- | --- | --- | --- |
| formula-json | 36 | 91.67% | 其余三项不是行为缺口：JSON 标量默认分支与数字解析失败的消息只出现在不可达输入上，`Number` 的 decimal 快路径对合法 JSON 数字等价 |
| recording | 231 | 90.48% | 边界用例已补；余下是录制器写不出的形状与不改变输出的路径 |
| devices-names | 172 | 80.81% | 别名与有符号拼写已覆盖，剩下的别名不是宏会写出来的字 |
| devices-colour | 672 | 73.96% | 存活集中在 PNG 头的位深分支与颜色近似的边界 |
| devices-window | 39 | 61.54% | `WindowRule`/`WindowHow` 的 JSON 读写、十种比较与默认值已补 24 条；其余覆盖仍靠带 Window 名字的动作测试 |
| devices-files | 50 | 76.00% | 引号、双引号、单元格里的换行、空白行开关、自动分隔与写回都已补用例 |
| expressions-value | 208 | 58.17% | 多数存活是 `Value` 构造与格式化里的等价路径；bool/date 的读数已补 |
| expressions-writer | 196 | 54.59% | 渲染补了 12 条（括号、优先级、转义、占位），这才是编辑器的显示契约；剩下的是 `Expression`/`Formula` 的构造与改名路径 |
| rules | 475 | 59.37% | 重试的三个决定抽成 `RetryPolicy`（16 条用例），`StepChecks` 的字段、选择与返回形状补了 10 条；其余需要按文件看：`CallBinder` 的断言偏“调用到了” |

形状往返的判定仍以 `FormulaJsonTests` 的规范比较为准；录制区域的判定是 `RecordingTests`。

只有 64 位 Windows 能编：适配器依赖的 OpenCV 与 Playwright 驱动都只有 x64 一份。SDK 版本由
`global.json` 钉住（10.0.400，允许向后取特性带）。引擎本身是 `net10.0`，任何平台都能引用它。

## 发布

推一个 `v*` 标签就会发布三个包：

```pwsh
git tag v0.2.0
git push origin v0.2.0
```

标签决定版本号（`v0.2.0` → `0.2.0`）；不想打标签时，手动触发 `build.yml` 并填版本号、勾上发布。
流水线用 GitHub 的 OIDC 令牌向 nuget.org 换一张只活一小时的临时 API key——仓库里没有长期密钥。

前提是 nuget.org 上的 Trusted Publisher 策略对得上：仓库 `ilpoint/WhaleGenie.Core`、工作流文件名
`build.yml`、三个包 ID 都列进去（新包 ID 第一次发布前要先加）。策略里若指定了 environment，
`build.yml` 的 job 上要补同名的 `environment:`。

## 公共面

公开什么、为什么是这几档、公共 API 基线怎么维护：见 [docs/public-api.md](docs/public-api.md)。
引擎的公共成员写在 `src/WhaleGenie.Core/PublicAPI.Shipped.txt`，增删不改基线构建就会失败。

## 许可

AGPL-3.0-only，见 [LICENSE.txt](LICENSE.txt)。