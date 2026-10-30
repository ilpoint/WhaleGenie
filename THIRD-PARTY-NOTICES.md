# 第三方组件与素材

鲸灵（WhaleGenie）里由别人做的部分，都列在这张单子上。它跟着程序一起发：打包时会和
LICENSE.txt 一起放到 WhaleGenie.exe 旁边，程序里的「关于 → 第三方组件」显示的就是这一份。

每条后面是那个东西自己的许可协议，再下一行是能找到协议原文的地址。协议写着 LGPL / GPL 的，
都是独立文件（dll 或 exe）而不是并进程序里的代码，换掉对应的文件就是换掉那一份组件；其中
GPL-3.0 的那一份与程序链接在一起，所以程序本身要是 GPL 兼容的才行，而它是 AGPL-3.0
（见下面 libVIIPER 那一条）。

## 图标素材

"🐋" 像素风格 Emoji 由 Emojiall 创作，来源于
<https://www.emojiall.com/zh-hans/platform-pixel>，采用 CC BY 4.0 协议授权。
本图标已从原图进行缩放修改。

许可协议链接：<https://creativecommons.org/licenses/by/4.0/>

In English: pixel-art "🐋" emoji by Emojiall, from
<https://www.emojiall.com/zh-hans/platform-pixel>, licensed under CC BY 4.0.
The icon was scaled and modified from the original.

界面图标来自 Lucide 图标库，并通过 Lucide.Avalonia 0.2.27 渲染：
<https://github.com/lucide-icons/lucide> 与 <https://github.com/dme-compunet/Lucide.Avalonia>。
Lucide 图标采用 ISC 协议，Lucide.Avalonia 采用 MIT 协议。

In English: interface icons come from Lucide and are rendered through
Lucide.Avalonia 0.2.27. Lucide is licensed under ISC and Lucide.Avalonia under MIT.

## 随程序一起分发的组件

### 界面

- Avalonia（Avalonia、Avalonia.Desktop、Avalonia.Themes.Fluent、Avalonia.Fonts.Inter）— MIT
  <https://github.com/AvaloniaUI/Avalonia>
- Avalonia.Controls.ColorPicker — MIT
  <https://github.com/AvaloniaUI/Avalonia>
- Avalonia.Controls.DataGrid — MIT
  <https://github.com/AvaloniaUI/Avalonia>
- FluentAvalonia（FluentAvaloniaUI）— MIT
  <https://github.com/amwx/FluentAvalonia>
  它把 WinUI 的控件与模板带到 Avalonia；WinUI 与 Avalonia 均为 MIT。
- MicroCom.Runtime — MIT
  <https://github.com/kekekeks/MicroCom>
  Avalonia.Fonts.Inter 里带的是 Inter 字体，字体本身按 SIL OFL 1.1 授权：
  <https://github.com/rsms/inter>
- Avalonia.Angle.Windows.Natives（OpenGL 后端，ANGLE 的 libGLESv2）— BSD-3-Clause
  <https://github.com/google/angle>
- CommunityToolkit.Mvvm — MIT
  <https://github.com/CommunityToolkit/dotnet>
- Lucide.Avalonia 0.2.27 — MIT
  <https://github.com/dme-compunet/Lucide.Avalonia>
  图标图形来自 Lucide，按 ISC 授权：<https://github.com/lucide-icons/lucide>
- SkiaSharp 及其原生库 Skia — 托管层 MIT，Skia 为 BSD-3-Clause
  <https://github.com/mono/SkiaSharp>
- HarfBuzzSharp 及其原生库 HarfBuzz — MIT
  <https://github.com/mono/SkiaSharp>

### 引擎

- SharpHook — MIT
  <https://github.com/TolikPylypchuk/SharpHook>
  它自带的 Windows 原生库 uiohook.dll 来自 libuiohook，按 LGPL-3.0 授权：
  <https://github.com/kwhat/libuiohook>
- libVIIPER（随包分发的 libVIIPER.dll，以及仓库里的 libVIIPER.h 与 licenses.txt）— GPL-3.0
  <https://github.com/Alia5/VIIPER>
  驱动级输入（`input.*` 的「驱动级」与 `gamepad.*`）用的虚拟 USB 键鼠与手柄由它提供：它是一个
  独立的 dll，鲸灵按名字加载它并调用它，它是上游 v0.9.0 发行包里的那一份，一字未改，
  对应的源码见上面那个地址的 v0.9.0 标签。它按 GPL-3.0 授权，而本程序按 AGPL-3.0 授权：
  AGPL-3.0 第 13 节允许把两者合成一个作品，合成后的整体仍按 AGPL-3.0 发布。
  它自己的许可说明随包放在 libVIIPER-LICENSES.txt。
- OpenCvSharp5（OpenCvSharp5、OpenCvSharp5.Windows 等）— Apache-2.0
  <https://github.com/shimat/opencvsharp>
  原生库 OpenCvSharpExtern.dll 里的 OpenCV 按 Apache-2.0 授权：
  <https://github.com/opencv/opencv>
  原生库 opencv_videoio_ffmpeg500_64.dll 里的 FFmpeg 按 LGPL-2.1 授权：
  <https://ffmpeg.org/legal.html>
- Sdcb.SimdPaddleOCR，以及它带的模型 Sdcb.SimdPaddleOCR.Models.ChineseV6Tiny、
  Sdcb.SimdPaddleOCR.Models.TextLineOrientation — Apache-2.0
  <https://github.com/sdcb/SimdPaddleOCR>
  模型来自 PaddleOCR，同样按 Apache-2.0 授权：
  <https://github.com/PaddlePaddle/PaddleOCR>
- FlaUI（FlaUI.Core、FlaUI.UIA3）— MIT
  <https://github.com/FlaUI/FlaUI>
- Interop.UIAutomationClient — MIT
  <https://github.com/Roemer/UIAutomation-Interop>
- Vortice.Windows（Vortice.DXGI、Vortice.Direct3D11，以及它们带的 Vortice.DirectX、
  Vortice.Mathematics、SharpGen.Runtime）— MIT
  <https://github.com/amerkoleci/Vortice.Windows>
  整屏画面获取用它：桌面复制（DXGI Desktop Duplication）把显卡正在合成的整块画面交出来，
  比 GDI 复制屏幕便宜，也是独占全屏的游戏唯一能取到画面的路。它只是一层托管包装，调用的是
  Windows 自带的 Direct3D 11 与 DXGI，不随包分发任何原生库。Graphics Capture 那一路也用它，
  把一帧画面所在的显存纹理读成像素；Windows Graphics Capture 本身是系统自带的一套接口，
  由目标框架里的类型库提供，同样不随包分发任何东西。两者都由 `WhaleGenie.GraphicsCapture` 引用。
- Playwright（Microsoft.Playwright）— Apache-2.0
  <https://github.com/microsoft/playwright-dotnet>
  随包分发的是它的驱动，里面带一份 Node.js；浏览器本体不在这里，见下面「用户自己装的组件」。
- ModelContextProtocol（以及 `ModelContextProtocol.Core`）— Apache-2.0（项目同时保留 MIT 过渡声明）
  <https://github.com/modelcontextprotocol/csharp-sdk>
  MCP 服务器用它说话：协议、工具发现与那一对标准输入输出流上的传输都在这里。没有 HTTP、
  没有监听端口，所以不再引用 ASP.NET Core。不随包分发额外的原生库。
- Microsoft.Extensions.Hosting（以及它带进来的 Hosting.Abstractions、DependencyInjection、
  Logging、Configuration 等同族包）— MIT
  <https://github.com/dotnet/runtime>
  无窗口那个 MCP 会话跑在一个通用宿主里：装配工具、启动会话、在流关闭时收尾。它不装控制台
  生命周期，也不打印任何东西——那些流上的标准输出就是协议本身。
- CsvHelper — Apache-2.0 与 MS-PL 双许可
  <https://github.com/JoshClose/CsvHelper>
  读写的都是带分隔符的文本文件（`file.readCsv` / `file.writeCsv`）：引号、单元格里的换行、
  文件末尾不完整的行这些细节都归它管。
- Roslyn（Microsoft.CodeAnalysis.CSharp，以及它一起带进来的 Microsoft.CodeAnalysis.Common、
  System.Collections.Immutable、System.Reflection.Metadata）— MIT
  <https://github.com/dotnet/roslyn>
  运行 C# 脚本时用的编译器：程序把宏里的那段 C# 编成一份 dll，再交给自己的宿主进程运行。
  它就是 `dotnet build` 用的那个编译器，只在引擎里被调用，不随包分发任何原生库。
- NuGet（NuGet.Protocol、NuGet.Packaging、NuGet.Frameworks、NuGet.Versioning）— Apache-2.0
  <https://github.com/NuGet/NuGet.Client>
  下载 C# 脚本要用的包：到官方源上问一个包有哪些版本、把 nupkg 取下来、解出要引用的 dll，
  以及比较目标框架与版本区间。包本身是用户装的，见手册的「脚本」一节。

### 运行环境

- .NET 运行时，以及跟着它一起分发的 Microsoft 组件（System.*、PresentationCore、
  WindowsBase、UIAutomationClient、DirectX 着色器编译器这些）— MIT
  <https://github.com/dotnet/runtime>
- System.Security.Cryptography.Pkcs — MIT
  <https://github.com/dotnet/runtime>
  下载 C# 脚本要用的 NuGet 包时做签名校验。程序的目标框架是 net10.0-windows，这一份由
  WindowsDesktop 共享框架提供，所以包里没有它自己的文件；引擎为什么把它单独钉一个版本，
  见 doc/dev.md 的「依赖的包」。
- MicroCom.Runtime、Tmds.DBus.Protocol、Microsoft.Extensions.* — MIT

## 只在编译时用到的组件

- AvaloniaUI.DiagnosticsSupport — 只在 Debug 构建里接上（按 F12 打开 Avalonia 开发者工具），
  发行包里没有它，也就不随程序分发
  <https://avaloniaui.net/>

## 用户自己装的组件

驱动级输入还用到一个东西，它不跟着本程序分发，程序里给的是下载入口：

- usbip-win2（USB/IP 内核驱动）— BSD-2-Clause
  <https://github.com/vadimgrn/usbip-win2>

浏览器动作要用到下面这些，同样不跟着本程序分发，程序里给的是安装命令：

- Playwright 的浏览器（Chromium、Firefox、WebKit）— 各自按 BSD-3-Clause 等开源协议授权，
  Playwright 自己的许可为 Apache-2.0
  <https://playwright.dev/docs/browsers>
