免安装：放到一个你放得住的地方，双击 `WhaleGenie.exe` 就能用。设置（`settings.json`）和运行
失败时的截图（`logs` 文件夹）都写在 `WhaleGenie.exe` 旁边，所以整个文件夹拷到哪，设置就跟到哪。

解压出来是一个 `WhaleGenie` 文件夹，`WhaleGenie.exe` 就在最外面，程序用到的 dll 都收在里面的
`lib` 文件夹，不用在一堆文件里找。

## 两种包怎么选

| 包 | 需要装什么 | 大小（x64） | 什么时候用 |
| --- | --- | --- | --- |
| `WhaleGenie-<版本>-win-x64.zip` | .NET 10 桌面运行时 | 约 101 MB | 体积最小，**推荐**；机器上已经装过运行环境时 |
| `WhaleGenie-<版本>-win-x64-standalone.zip` | 什么都不用装 | 约 173 MB | 换机器、给别人用；解压出来是一整个文件夹 |

框架依赖版没装运行环境时，双击 `WhaleGenie.exe` 会由程序自己弹窗提示并给出下载地址：
<https://dotnet.microsoft.com/download/dotnet/10.0> → Windows → 对应架构 → Desktop Runtime

自带运行时的那一版，`WhaleGenie.exe` 旁边还会多出 .NET 运行时自己的十来个文件（`hostfxr.dll`、
`coreclr.dll` 这类），它们不能挪进 `lib`，请保持原样。

包里那多出来的一大块是浏览器动作用的 Playwright 驱动（`WhaleGenie` 文件夹里的 `.playwright`，
解开约 100 MB），它必须跟 exe 一起走。

浏览器动作**默认用系统自带的 Edge**，不用装任何东西，开箱就能用；只有你特意选了
Chromium / Firefox / WebKit 时，才需要按程序给出的提示装一次那几种内核。

只提供 64 位（`win-x64`）的包：找图、等图、点图依赖的 OpenCV，和浏览器动作用到的 Playwright
驱动，都只有 64 位一份，做不出能用的 32 位包，所以干脆不出。
