# 引擎的公共面

所属：[WhaleGenie 架构](../architecture.md)。

`WhaleGenie.Core` 是仓库里唯一作为库发布的工程：`net10.0`、引用闭包只有 BCL 与 `CsvHelper`，
界面、适配器、画面获取和 MCP 都引用它，它不引用其中任何一个。这一篇说明公共面分几档、为什么这样
分、以及公共 API 基线怎么维护。开发与验收命令见 [dev.md](../dev.md)。

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
[`PublicAPI.Shipped.txt`](../../src/WhaleGenie.Core/PublicAPI.Shipped.txt) 与
[`PublicAPI.Unshipped.txt`](../../src/WhaleGenie.Core/PublicAPI.Unshipped.txt)：

- 新增公共成员而不写进 `Unshipped.txt` → `RS0016`，构建失败。
- 基线里有、程序集里没有 → `RS0017`，构建失败。
- 分析器随构建走（`PrivateAssets=all`），不进包、不进发布物。

维护方式：

1. 改了公共面，先看构建报的 `RS0016`/`RS0017` 指向哪些成员。
2. 新增与删除都写进 `PublicAPI.Unshipped.txt` 对应的一行；发版时整体并入 `Shipped.txt`、清空
   `Unshipped.txt`（只留 `#nullable enable`）。
3. 文件名与路径不要动：两个 `AdditionalFiles` 在 `WhaleGenie.Core.csproj` 里按名字引用。

第一次生成的办法（也适用于大改之后）：两个文件只留 `#nullable enable`，构建一次拿到全部 `RS0016`，
再 `dotnet format analyzers src/WhaleGenie.Core/WhaleGenie.Core.csproj --diagnostics RS0016`
批量写入，最后搬进 `Shipped.txt`。

## 4. 打包

```pwsh
dotnet pack src/WhaleGenie.Core/WhaleGenie.Core.csproj -c Release -p:Platform=x64
```

产物是 `dist/nuget/WhaleGenie.Core.<版本>.nupkg`，`lib/` 下只有 `net10.0`，XML 文档与包自述
随包。其余工程在根 `Directory.Build.props` 里统一 `IsPackable=false`：只有引擎是库，界面与它带
的应用工程都不是。

## 5. 债务

- **XML 文档缺口**：约 1,314 个公共类型与成员没有 `<summary>`，目前以 `CS1591`/`CS1573` 抑制。
  新增公共成员应当带注释，这条债务只能靠新代码不再扩大。
- **`HookCode` 是公共枚举**：等于把键码编号写进公共契约。编号来自 SharpHook 的 `KeyCode`，
  `KeyCodeParityTests` 会逐名逐值对照，升级该库时会红。
- **动作族是 `internal`，`MacroRunner` 的 partial 也是**：外部要用某个动作的具体实现没有门，只能
  经 `MacroRunner` 走。这是有意的：动作是目录里的声明，不是可替换的部件。
