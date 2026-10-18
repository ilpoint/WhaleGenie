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

界面上的动作分类图标与步骤工具栏图标来自 Andrés Raxón 的 andrsrxn/icons 7.6.0：
<https://github.com/andrsrxn/icons>，采用 MIT 协议授权，版权 2026 Andrés Raxón。
本程序只取每个图标的描边轮廓，去掉了原图标的 20% 填充层；矩形、圆与变换已转成路径，
描边按 1.2–1.5 像素绘制。许可原文如下。

In English: the category and step-toolbar icons are from andrsrxn/icons 7.6.0 by
Andrés Raxón, <https://github.com/andrsrxn/icons>, MIT license, copyright 2026
Andrés Raxón. Only the stroked outline is used; the 20% fill layer was dropped,
rectangles, circles and transforms were flattened to paths, and the stroke is
drawn at 1.2–1.5 px.

MIT License

Copyright (c) 2026 Andrés Raxón (andrsrxn)

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

## 随程序一起分发的组件

### 界面

- Avalonia（Avalonia、Avalonia.Desktop、Avalonia.Themes.Fluent、Avalonia.Fonts.Inter）— MIT
  <https://github.com/AvaloniaUI/Avalonia>
  Avalonia.Fonts.Inter 里带的是 Inter 字体，字体本身按 SIL OFL 1.1 授权：
  <https://github.com/rsms/inter>
- Avalonia.Angle.Windows.Natives（OpenGL 后端，ANGLE 的 libGLESv2）— BSD-3-Clause
  <https://github.com/google/angle>
- CommunityToolkit.Mvvm — MIT
  <https://github.com/CommunityToolkit/dotnet>
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
