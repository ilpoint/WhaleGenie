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

## 构建与验收

```pwsh
dotnet build WhaleGenie.Core.slnx -c Release
dotnet test  WhaleGenie.Core.slnx -c Release
dotnet format WhaleGenie.Core.slnx --verify-no-changes --no-restore
dotnet pack src/WhaleGenie.Platform.Windows/WhaleGenie.Platform.Windows.csproj -c Release -p:Platform=x64
```

包落在 `dist/nuget/`。三套用例（引擎、画面获取、Windows 适配器）都在这个仓库里，适配器那套会
驱动真实的 Windows 设备。

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
