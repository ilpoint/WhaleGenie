# 引擎的公共面

所属：[WhaleGenie 架构](../architecture.md)。

`WhaleGenie.Core` 是仓库里唯一作为库发布的工程：`net10.0`、引用闭包只有 BCL 与 `CsvHelper`，
界面、适配器、画面获取和 MCP 都引用它，它不引用其中任何一个。它连同两个 Windows 工程住在
`core/` 下——那是 [ilpoint/WhaleGenie.Core](https://github.com/ilpoint/WhaleGenie.Core) 以
`git subtree` 引入的一份仓库，**改动去那边做**，这里只读。这一篇说明公共面分几档、为什么这样分、
以及公共 API 基线怎么维护。开发与验收命令见 [dev.md](../dev.md)。

## 1. 为什么要有分档

引擎起初把什么都写成 `public`：能编译、能用，但「哪些是承诺、哪些随时会变」没有答案。调用方看不出
`PixelSearch` 的扫描顺序是不是可以依赖，维护者也看不出改哪个签名要通知谁。分档就是把这个问题答成
一条可检查的规则。

## 2. 判定规则

**外部产品程序集（`WhaleGenie`、`App`、`Domain`、`Mcp`、`Localization`、`Platform.Windows`、
`GraphicsCapture`）真的用到的才是 public。只有测试用不算理由**——测试经 `InternalsVisibleTo` 看
internals，而那条通道只留给引擎自己的用例。

据此分四档：

| 档 | 内容 | 变动的代价 |
| --- | --- | --- |
| 契约 | `IDeviceLayer` 与 15 个 `I*Device` 契约；`IRunHost`/`IRunLooks`/`IMacroLibrary`；设备值类型（`ScreenRect`、`CaptureWay`、`ScreenLayout`、`HookKey` 等）；键词汇表 `KeyNames`；`Value`/`Formula`/`ExpressionNode` 家族；`ActionSemantics*`；`RunResult`/`StepRun`/`LogEntry`；`VariableStore`；`Recording*` | 破坏性改动要当作跨工程变更处理 |
| 引擎入口 | `MacroRunner`、`StepTrial`、`ActionSemanticsCatalog`、`NullDeviceLayer` | 同上；`NullDeviceLayer` 产品侧零使用，留着是因为降级路径要能用，而降级不需要内部成员 |
| 进程级共享 | `FrameShelf`、`WhaleGenieInputGate`、`SharedDoors`、`InputLimit` | 改的是并发语义，不只是签名 |
| 适配器工具 | `PngWriter`、`TextEncoding`、`TextCompare`、`MousePath`、`ScriptProtocol`、`ScriptBridge`、`CommandLine`、`ColourReach`、`ColourPointText` | 引擎与适配器共用，留一份好过各留一份 |

实现细节一律 `internal`：`OcrPreprocess`、`PixelSearch`（含 `ColourStep`）、`DelimitedFile`、
`ScriptFiles`、21 个动作族、`IRunContext` 与 `MacroRunner` 的全部 partial。这些是引擎可以换掉的
做法，不是给外部的承诺。

`NullDeviceLayer` 和「进程级共享」两档是这条规则的两个例外，理由记在上表里；其他的判断都按
「产品侧有没有真的用到」来做。

## 3. 公共 API 基线

`Microsoft.CodeAnalysis.PublicApiAnalyzers` 在编译期比对
[`PublicAPI.Shipped.txt`](../../core/src/WhaleGenie.Core/PublicAPI.Shipped.txt) 与
[`PublicAPI.Unshipped.txt`](../../core/src/WhaleGenie.Core/PublicAPI.Unshipped.txt)：

- 新增公共成员而不写进 `Unshipped.txt` → `RS0016`，构建失败。
- 基线里有、程序集里没有 → `RS0017`，构建失败。
- 分析器随构建走（`PrivateAssets=all`），不进包、不进发布物。

维护方式：

1. 改了公共面，先看构建报的 `RS0016`/`RS0017` 指向哪些成员。
2. 新增与删除都写进 `PublicAPI.Unshipped.txt` 对应的一行；发版时整体并入 `Shipped.txt`、清空
   `Unshipped.txt`（只留 `#nullable enable`）。
3. 文件名与路径不要动：两个 `AdditionalFiles` 在 `WhaleGenie.Core.csproj` 里按名字引用。

第一次生成的办法（也适用于大改之后）：两个文件只留 `#nullable enable`，构建一次拿到全部 `RS0016`，
再 `dotnet format analyzers core/src/WhaleGenie.Core/WhaleGenie.Core.csproj --diagnostics RS0016`
批量写入，最后搬进 `Shipped.txt`。

## 4. 打包

打包与发布都在库自己的仓库里，构建设置也随之住在 `core/`：

```pwsh
dotnet pack src/WhaleGenie.Platform.Windows/WhaleGenie.Platform.Windows.csproj -c Release -p:Platform=x64
```

三个包都落在库仓的 `dist/nuget/`。引擎包的 `lib/` 下只有 `net10.0`，XML 文档与包自述随包；两个
Windows 包各在 `lib/net10.0-windows10.0.19041/` 下放自己的程序集。程序自己的工程（界面、App、
Domain、Mcp、Localization）在根 `Directory.Build.props` 里统一 `IsPackable=false`：它们是程序的
一部分，不是给别人的库。

## 5. 分发：一个引用，三个程序集

库不只有引擎那一个包。三个工程各自可打包，依赖链把后端两个挂在引擎上：

| 包 | 目标框架 | 内容 | 依赖 |
| --- | --- | --- | --- |
| `WhaleGenie.Core` | `net10.0` | 引擎 `WhaleGenie.Core.dll` + XML 文档 + 包自述 | 只有 `CsvHelper` |
| `WhaleGenie.GraphicsCapture` | `net10.0-windows10.0.19041` | 画面获取 `WhaleGenie.GraphicsCapture.dll` | `WhaleGenie.Core`、`Vortice.*` |
| `WhaleGenie.Platform.Windows` | `net10.0-windows10.0.19041` | Windows 适配器 `WhaleGenie.Platform.Windows.dll`、`runtimes/win-x64/native/libVIIPER.dll`、`buildTransitive/WhaleGenie.Platform.Windows.props` | 上面两个 + FlaUI、OpenCV、PaddleOCR、Playwright、Roslyn/NuGet、SharpHook |

**前端只加一行**：

```xml
<PackageReference Include="WhaleGenie.Platform.Windows" Version="x.y.z" />
```

三个程序集一次到位，Avalonia 界面与将来的 WebView 前端都是同一行。只做无窗口批处理、不碰机器的
程序，改引 `WhaleGenie.Core`，拿到的就是纯 `net10.0` 的引擎。

两处只有包才知道的事：

- **原生库**：`libVIIPER.dll` 放在 `runtimes/win-x64/native/`，宿主经 `deps.json` 找到它，按 RID
  构建与不按 RID 构建都一样。
- **Playwright 的驱动**：它给五个平台各带一份 node，运行时不钉 RID 会按包里的兜底分支全抄
  （约 474 MB）。包的 `buildTransitive` props 在消费方把 `PlaywrightPlatform` 默认成 `win-x64`，
  仓库自己则由根 `Directory.Build.props` 设同一个值。

验收办法是拿真包验，而不是只看包里的文件：在仓库外建一个 `net10.0-windows` 控制台，`nuget.config`
指向 `dist/nuget`，只写上面那一行 `PackageReference`，跑起来读 `ActionSemanticsCatalog.All.Count`
（证明内嵌语义目录随包）、`KeyNames.Resolve("ctrl")`（证明键词汇表在引擎里）、
`Assembly.Load` 三个程序集名，再确认 `runtimes/win-x64/native/libVIIPER.dll` 在输出目录下。

## 6. 债务

- **XML 文档缺口**：约 1,314 个公共类型与成员没有 `<summary>`，目前以 `CS1591`/`CS1573` 抑制。
  新增公共成员应当带注释，这条债务只能靠新代码不再扩大。
- **`HookCode` 是公共枚举**：等于把键码编号写进公共契约。编号来自 SharpHook 的 `KeyCode`，
  `KeyCodeParityTests` 会逐名逐值对照，升级该库时会红。
- **动作族是 `internal`，`MacroRunner` 的 partial 也是**：外部要用某个动作的具体实现没有门，只能
  经 `MacroRunner` 走。这是有意的：动作是目录里的声明，不是可替换的部件。
