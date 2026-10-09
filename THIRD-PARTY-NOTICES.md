# 第三方组件与素材

鲸灵（WhaleGenie）里由别人做的部分，都列在这张单子上。它跟着程序一起发：打包时会和
LICENSE.txt 一起放到 WhaleGenie.exe 旁边，程序里的「关于 → 第三方组件」显示的就是这一份。

每条后面是那个东西自己的许可协议，再下一行是能找到协议原文的地址。协议写着 LGPL / GPL 的，
都是独立文件（dll 或 exe）而不是并进程序里的代码，换掉对应的文件就是换掉那一份组件。

## 图标素材

"🐋" 像素风格 Emoji 由 Emojiall 创作，来源于
<https://www.emojiall.com/zh-hans/platform-pixel>，采用 CC BY 4.0 协议授权。
本图标已从原图进行缩放修改。

许可协议链接：<https://creativecommons.org/licenses/by/4.0/>

In English: pixel-art "🐋" emoji by Emojiall, from
<https://www.emojiall.com/zh-hans/platform-pixel>, licensed under CC BY 4.0.
The icon was scaled and modified from the original.

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
- Viiper.Client — MIT
  <https://github.com/Alia5/VIIPER>
- OpenCvSharp5（OpenCvSharp5、OpenCvSharp5.Windows、OpenCvSharp5.AvaloniaExtensions 等）— Apache-2.0
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
- Playwright（Microsoft.Playwright）— Apache-2.0
  <https://github.com/microsoft/playwright-dotnet>
  随包分发的是它的驱动，里面带一份 Node.js；浏览器本体不在这里，见下面「用户自己装的组件」。
- ClosedXML — MIT
  <https://github.com/ClosedXML/ClosedXML>
  它管的 .xlsx 就是 Open XML 格式，底下建在微软的 Open XML SDK 上：
  DocumentFormat.OpenXml、DocumentFormat.OpenXml.Framework — MIT
  <https://github.com/dotnet/Open-XML-SDK>
  它带进来的 ClosedXML.Parser（<https://github.com/ClosedXML/ClosedXML.Parser>）、
  ExcelNumberFormat（<https://github.com/andersnm/ExcelNumberFormat>）、
  RBush.Signed（<https://github.com/viceroypenguin/RBush>）— MIT
  SixLabors.Fonts — Apache-2.0
  <https://github.com/SixLabors/Fonts>
- CsvHelper — Apache-2.0 与 MS-PL 双许可
  <https://github.com/JoshClose/CsvHelper>
  读写的都是带分隔符的文本文件（`file.readCsv` / `file.writeCsv`）：引号、单元格里的换行、
  文件末尾不完整的行这些细节都归它管。

### 运行环境

- .NET 运行时，以及跟着它一起分发的 Microsoft 组件（System.*、PresentationCore、
  WindowsBase、UIAutomationClient、DirectX 着色器编译器这些）— MIT
  <https://github.com/dotnet/runtime>
- MicroCom.Runtime、Tmds.DBus.Protocol、Microsoft.Extensions.* — MIT

## 只在编译时用到的组件

- AvaloniaUI.DiagnosticsSupport — 只在 Debug 构建里接上（按 F12 打开 Avalonia 开发者工具），
  发行包里没有它，也就不随程序分发
  <https://avaloniaui.net/>

## 用户自己装的组件

驱动级输入要用到下面两样，它们不跟着本程序分发，程序里给的是下载入口：

- usbip-win2（USB/IP 内核驱动）— BSD-2-Clause
  <https://github.com/vadimgrn/usbip-win2>
- VIIPER 服务端 — GPL-3.0
  <https://github.com/Alia5/VIIPER>

浏览器动作要用到下面这些，同样不跟着本程序分发，程序里给的是安装命令：

- Playwright 的浏览器（Chromium、Firefox、WebKit）— 各自按 BSD-3-Clause 等开源协议授权，
  Playwright 自己的许可为 Apache-2.0
  <https://playwright.dev/docs/browsers>
