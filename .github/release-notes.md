便携版，免安装：解压到一个你放得住的地方，双击 `Viktor.exe` 就能用。

## 先装运行环境

Viktor 不自带 .NET 运行环境，机器上需要有 **.NET 10 桌面运行时（Desktop Runtime）**。
没装的话，双击 `Viktor.exe` 会弹出提示并给出下载地址，装完再开就行。

下载：<https://dotnet.microsoft.com/download/dotnet/10.0> → Windows → 对应架构 → Desktop Runtime

## 两个包怎么选

| 包 | 什么时候用 |
| --- | --- |
| `Viktor-<版本>-win-x64.zip` | 绝大多数 64 位 Windows，功能完整，**推荐** |
| `Viktor-<版本>-win-x86.zip` | 32 位 Windows，需要 32 位的 .NET 10 桌面运行时 |

x86 包的已知限制：找图、等图、点图这几个动作依赖的 OpenCV 只提供 64 位原生库，
所以这几个动作在 32 位包里用不了，其余功能正常。机器装的是 64 位系统就选 x64。
