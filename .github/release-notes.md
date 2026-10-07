免安装：放到一个你放得住的地方，双击 `WhaleGenie.exe` 就能用。设置（`settings.json`）和运行
失败时的截图（`logs` 文件夹）都写在 `WhaleGenie.exe` 旁边，所以整个文件夹拷到哪，设置就跟到哪。

解压出来是一个 `WhaleGenie` 文件夹，`WhaleGenie.exe` 就在最外面，程序用到的 dll 都收在里面的
`lib` 文件夹，不用在一堆文件里找。

## 两种包怎么选

| 包 | 需要装什么 | 大小（x64） | 什么时候用 |
| --- | --- | --- | --- |
| `WhaleGenie-<版本>-win-x64.zip` | .NET 10 桌面运行时 | 约 63 MB | 体积最小，**推荐**；机器上已经装过运行环境时 |
| `WhaleGenie-<版本>-win-x64-standalone.zip` | 什么都不用装 | 约 135 MB | 换机器、给别人用；解压出来是一整个文件夹 |

框架依赖版没装运行环境时，双击 `WhaleGenie.exe` 会由程序自己弹窗提示并给出下载地址：
<https://dotnet.microsoft.com/download/dotnet/10.0> → Windows → 对应架构 → Desktop Runtime

自带运行时的那一版，`WhaleGenie.exe` 旁边还会多出 .NET 运行时自己的十来个文件（`hostfxr.dll`、
`coreclr.dll` 这类），它们不能挪进 `lib`，请保持原样。

浏览器动作靠 Playwright 驱动，它跟着包一起发（解开后约 100 MB，在 `WhaleGenie` 文件夹里的
`.playwright` 下）；上面表里的体积是加它之前量的，发版前按实际产物更新。

浏览器动作**默认用系统自带的 Edge**，不用装任何东西，开箱就能用；只有你特意选了
Chromium / Firefox / WebKit 时，才需要按程序给出的提示装一次那几种内核。

32 位（`win-x86`）同样是这两种包。已知限制：找图、等图、点图这几个动作依赖的 OpenCV 只提供
64 位原生库，所以这几个动作在 32 位包里用不了；浏览器动作依赖的 Playwright 驱动也只有 64 位，
同样用不了；其余功能正常。机器装的是 64 位系统就选 x64。
