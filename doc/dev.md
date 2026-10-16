# WhaleGenie 开发说明

[简体中文](dev.md)

写给改这个仓库的人：常用命令、工程怎么分块、依赖了谁、测到哪一层为止、怎么量性能、代码约定，以及提交、
CI 与发布。用这个程序的人看 [manual.md](manual.md)，程序往哪儿走看仓库根目录的 `AGENTS.md`。

## 常用命令

```powershell
dotnet build WhaleGenie.slnx -c Release          # 构建（含警告检查）
dotnet test  WhaleGenie.slnx -c Release          # 全部测试
dotnet format WhaleGenie.slnx --verify-no-changes --no-restore   # 格式检查
dotnet run --project WhaleGenie                  # 启动程序
```

构建把警告当成错误（`Directory.Build.props` 里的 `TreatWarningsAsErrors`），
所以"能编译过"就等于"零警告"。

## 工程结构

| 项目 | 职责 |
| --- | --- |
| `WhaleGenie.Core` | 执行引擎。动作目录、变量、表达式、宏包读写、以及所有真正碰机器的设备（输入、屏幕、OCR、UI Automation、文件、进程） |
| `WhaleGenie.GraphicsCapture` | 从 Windows 拿像素：Graphics Capture、桌面复制（DXGI）、屏幕 GDI 与 PrintWindow 四种取法，以及会话缓存与选路。它不认识引擎的类型，引擎通过 `ProjectReference` 用它 |
| `WhaleGenie` | 界面。窗口、视图模型、文案、触发器监听 |
| `WhaleGenie.Core.Tests` | 引擎的测试，跑在替身设备层上，不碰真实桌面 |
| `WhaleGenie.GraphicsCapture.Tests` | 画面获取的测试：选路次序、会话缓存与复用、图片落位。真正调用 Windows 的那几条按手动用例过 |
| `WhaleGenie.Tests` | 界面的测试，跑在 Avalonia 无头平台上 |

## 依赖的包

`WhaleGenie.Core` 真正依赖的就这么几个，每个都对应一类碰机器的设备；版本写在
`WhaleGenie.Core/WhaleGenie.Core.csproj` 或 `WhaleGenie.GraphicsCapture/WhaleGenie.GraphicsCapture.csproj`
里，这里不抄第二遍。只有 `libVIIPER` 不是 NuGet 包：它是别人编译好的原生库，跟着仓库走。

| 包 | 在引擎里干什么 |
| --- | --- |
| `SharpHook` | 全局键盘鼠标钩子与注入：热键 / 鼠标按键 / 滚轮这些触发器，`input.*` 的前台路径，录制，以及各种拾取。名字只出现在引擎的钩子与注入实现里：钩子交出的是引擎自己的 `IInputHook` 与 `HookKey`、`HookMouseButton`、`HookWheelAxis` |
| `libVIIPER` | 驱动级输入：把按键和指针交给虚拟键鼠对（`input.*` 的"驱动级"），以及虚拟手柄（`gamepad.*`，一只 Xbox 360）。它不是 NuGet 包，是上游 v0.9.0 的 Windows 原生库，放在 `third_party/libVIIPER/`（dll、头文件、它自己的许可说明和一份来源说明），由 `WhaleGenie.Core.csproj` 拷到 exe 旁边；服务端在程序自己的进程里跑，没有第二个程序 |
| `OpenCvSharp5.Windows` | 找图、找色、等图这类画面动作：OpenCV 的托管 API（`Cv2`、`Mat`）和它的 Windows 原生库 |
| `Sdcb.SimdPaddleOCR`、`Sdcb.SimdPaddleOCR.Models.ChineseV6Tiny` | 屏幕取字（OCR）：识别库和随包带的中文模型，不联网下载 |
| `FlaUI.UIA3` | UI Automation：元素的取值、选择、勾选、展开、滚动到可见、读表格 |
| `Microsoft.Playwright` | 浏览器动作（`browser.*`）：驱动跟着包走，默认开系统自带的 Edge |
| `CsvHelper` | 带分隔符的文本文件（`file.readCsv` / `file.writeCsv`）：引号、单元格里的换行、行尾、空行这些细节归它管。自己拆字符串在这些地方都会悄悄读错 |
| `Microsoft.CodeAnalysis.CSharp`（Roslyn） | 运行 C# 脚本（`script.csharp`）：把宏里的那段 C# 编成一份 dll。它就是 `dotnet build` 用的那个编译器，引擎按运行时给出的可信平台程序集加 `packages\nuget\references.json` 里的 dll 当引用 |
| `NuGet.Protocol`、`NuGet.Packaging`、`NuGet.Frameworks`、`NuGet.Versioning` | 下载 NuGet 包（`script.nuget`）：到官方 v3 源上问一个包有哪些版本、把 nupkg 取下来、按目标框架挑最贴近的一份 lib、按版本区间解析传递依赖。包本身不跟着程序分发，下到程序自己的 `packages` 目录里 |
| `Vortice.Direct3D11`、`Vortice.DXGI`（都由 `WhaleGenie.GraphicsCapture` 引用，引擎不直接引） | 画面获取：Graphics Capture 与桌面复制是 Direct3D 11 与 DXGI 的调用，它们只是一层托管包装，调用的是 Windows 自带的那两个库，不随包分发原生库。边界见「工程结构」 |

这些包各自带进来的原生库（`OpenCvSharpExtern.dll`、`uiohook.dll`、Playwright 的驱动等）
和许可原文，列在 `THIRD-PARTY-NOTICES.md` 里。

## 脚本怎么跑

脚本分两条路。`script.run` 那种是机器上别的程序在跑，引擎只写一份临时脚本、把命令行交给设备层，
CSV 那套编码规则管着脚本文件怎么写。`script.run` 的「脚本来源」选文件时，那份文件就地运行：引擎
只把它的路径放进命令行，不写副本、不替换占位符、不改编码，只有「保留窗口」那一种会另写一个两行的
批处理（`call` 那份文件 + 一行标记）交给窗口。另外三个动作——`script.python`、`script.csharp` 与
`script.ahk`——是引擎自己起进程跑的，三条都走 `IScriptDevice`（`ScriptDevice`）：脚本能读宏的变量、
按四字符标识跑宏里的某一步、往运行日志写一行、交回这一步的结果，走的都是同一条**命名管道**——两边
按行传 JSON，管道名经 `WHALEGENIE_PIPE` 交给子进程，请求在走宏的那条线程上就地回答（要跑的那一步
就当场跑），stdout 与 stderr 各由一条读线程收着逐行进运行日志，超时到点连进程树一起结束。

- **Python**：临时目录里写出脚本、桥模块 `whalegenie.py` 与引导用的 `boot.py`，`python -X utf8 boot.py script.py 参数`
  起来；`WhaleGenie` 从 `builtins` 里进去，任何模块都看得见。`packages\python` 永远在 `PYTHONPATH` 上。
- **C#**：引擎用 Roslyn 把脚本与注入的一份 `__InjectedServices`（一个 `public static dynamic DynamicObject`）
  一起编成 dll，再让**程序自己的 exe 起第二个进程**执行（`WhaleGenie.exe --script-host <dll> 参数…`）。
  `Program.Main` 在 Avalonia 之前分支：宿进程不建窗口、不占通知区域，脚本里的 `Environment.Exit`、
  栈溢出与死循环都伤不到主程序，超时也能直接杀进程。宿主从 `packages\nuget\references.json` 里认
  编译时用到的那些 dll（`NuGetStore.References`），并把包里的 `runtimes\win-x64\native` 挂到 `PATH` 上。
- **AutoHotkey**：AutoHotkey 的 `FileOpen` 打不开命名管道（实测会卡住），所以这一条不由脚本自己连管道，
  而是由**程序自己的 exe 再起一个进程**当桥：临时目录里写两份文件，`script.ahk` 是两行开头加用户正文
  （`#ErrorStdOut` 让语法错误写成错误输出而不是弹窗，`#Include` 把桥拉进来），`whalegenie.ahk` 是预置的
  桥——五个函数加一个 `OnError` 处理器，第一次用到时用 `ComObject("WScript.Shell").Exec` 起
  `WhaleGenie.exe --script-bridge <问> <答>`，一个脚本只起一份。桥那一支读一行请求（动词、名字、是不是
  数字、值），把它翻成管道上那套 JSON，再把回答写回另一个文件、在标准输出上说一声，AHK 侧看到 `done`
  才去读答案；两边的行号差由预置桥里的 `__OFFSET__` 补回来，所以报的错用的是脚本自己的行号。
  `Run.AhkNeedsV2` 只在版本号读得出来时才判（搜索路径上的转发程序读不到版本，就照跑）。

`packages` 目录跟着 exe 走，写不进去时退到 `%LOCALAPPDATA%\WhaleGenie\packages`；`python` 与 `nuget`
各占一格，脚本动作里因此不再列包。

## 宏文件的形状

一个宏是一份 JSON（打包后叫 `<宏名>.json`），顶层节点分三块：触发与节奏（`triggerMode`、`schedule`、
`watch`、`program`、`window`、`record` 等，对应编辑器里触发那一页）、**调用声明**、以及 `steps`。声明是
这个宏对调用者的要求，形状如下：

```json
{
  "arguments": [
    { "name": "次数", "default": "3", "required": false, "hint": "要做多少遍。" },
    { "name": "标签", "default": "", "required": true, "hint": "写在每一项前面的文字。" }
  ],
  "returns": [
    { "name": "答案", "hint": "整理完留下的数。", "type": "Number" }
  ]
}
```

两边都为空就写空数组，没在调用的宏因此不会多出什么。同一个声明在代码里有两份读法：引擎读的是
`WhaleGenie.Core/Execution/MacroSignature.cs` 里的 `MacroSignature`（只要名字、默认值、是否必填），
编辑器与设计期检查读的是 `WhaleGenie/Models/MacroCallable.cs` 里的 `MacroCallable`（多一列说明，
调用处的表单拿它当字段的提示），两者都由 `MacroItem` 读写。

调用另一个宏的步骤（`control.runMacro`）写三个部分：

```json
{
  "type": "control.runMacro",
  "params": {
    "macro": "整理",
    "arg.次数": "$n",
    "arg.标签": "开始",
    "returns": [ { "key": "答案", "name": "答案" } ]
  }
}
```

`macro` 是被调用宏的名字；每一个形参各占一格，名字是 `arg.<形参名>`——带前缀是因为形参名是被调用宏
自己的，不加前缀就会和 `macro`、`returns` 这些调用自己的键撞上。`returns` 一行一个要取回的结果：
`key` 是被调用宏那边叫这个名字，`name` 是本宏这边落在哪个变量里。引擎按被调用宏声明的形参逐个读
`arg.<名字>`（留空用默认值，必填留空报 `Run.MissingArgument`），并从 `returns` 的每一行取回一个值
（名字没被声明过报 `Run.UndeclaredResult`），要取回什么在调用开始之前就说清楚，出错因此发生在送输入
之前。

## 测试分层

| 层 | 项目 | 验证内容 |
| --- | --- | --- |
| 纯逻辑 | `WhaleGenie.Core.Tests` | 引擎每一步、变量与表达式、宏包往返；跑在替身设备层上，断言的是"引擎要求了什么"，不是"桌面上发生了什么" |
| 画面获取 | `WhaleGenie.GraphicsCapture.Tests` | 选路次序、会话缓存与复用、图片落位、矩形越界；真正调用 Windows 取画面那几条按手动用例过 |
| 界面 | `WhaleGenie.Tests` | 参数成型、触发器比对、卡片显示、文案完整性，无头会话里的界面逻辑与 Avalonia 输入（`Ui.Run` 是入口），以及拾取器与录制器交来替身设备层后的情形 |
| 真的驱动别的程序 | `BrowserTabTests`、`ScriptBridgeTests` | 开一个不显示窗口的 Edge，跑浏览器动作；真的起一个解释器跑一段脚本，看它读值、写值、跑一步与交回结果走的是不是那条管道（机器上没有 Python 时跳过） |
| 手动 | — | 真实输入、抓屏取色、UIA 拾取、页面拾取、录制、窗口布局，以及驱动级输入：真机装一次 usbip-win2 驱动，勾上/不勾「启动时先把虚拟键鼠装上机器」各跑一次运行，键鼠四种动作（点击、打字、拖拽、滚轮）与手柄各来一次，退出程序后看设备管理器里虚拟设备是不是没了；三种屏幕拾取器（框选区域、取色放大镜、元素拾取）各进出一次：打开时鲸灵自己的窗口（含问话的那个对话框）都不在屏幕上，拾取器一开就拿到焦点、Esc 与回车与右键当场有效，拾完窗口按原状回来——其中一次在添加动作窗口里拾，看它自己也回来（它是编辑器的属主窗口，收起时跟着编辑器一起走）；框选之后截一张，图片里没有鲸灵自己的窗口；识别结果与偏移拾取窗口的标题栏都能拖着走（标题栏拖动是手动那一档，无头会话里只验到接没接线）；脚本动作的三种窗口模式各跑一次（窗口留着/关着/不显示）、批处理、PowerShell 与 AutoHotkey 各一次，AutoHotkey 那一条还要在一台没装它的机器上看一次报错；Python 脚本与安装 PIP 包各一次（装的包能 import），C# 脚本与下载 NuGet 包各一次（装的包能编译进脚本），断网时看两个依赖动作各报什么；表达式编辑器里把变量、函数与树上已有的一支拖到位置上（拖到空位＝填入、拖到已有的一段＝换掉、拖自己的一支进自己的后代＝不理会、拖到窗口外＝取消，落点要跟着亮；两行函数按钮与「翻译结果」一行在 900×620 下排得下、滚动正常，深嵌套公式的那句人话读起来顺） |

界面用例走的是 Avalonia 自己的输入管线，不是系统里的鼠标与按键：真的注入按键、真的抓屏取色这类事情在
无头会话里验不了，只能按手动用例过。因此**能在无头会话里跑到的部分要有用例**，只留人眼能判的那一档给
手动。

每加一个动作至少有一条引擎用例走到它；用户可见文案由 `LocalizationTests` 卡住两边齐全。

整套通常一分钟上下：界面那套最长，约一分钟；引擎十二秒上下，与它同时跑；画面获取那一套不到一秒。界面那套串
行（无头会话只有一条界面线程），单条中位数三毫秒，等待系统通知的只有文件监听三条；整套偶尔约十分钟或整套失
败，属无头会话竞态，用例本身仍全部通过。这几个数在本机这么量：跑一次
`dotnet test WhaleGenie.slnx -c Release`，看每个工程那一行末尾的持续时间；换机器以后重量一遍。

文案是两份手写的表（`Strings` 的英文与中文、`ActionStrings` 的中文），
`WhaleGenie.Tests` 里的本地化用例会检查键是否两边齐全、动作的每个参数和每个下拉选项是否有中文。

## 代码约定

- 格式与风格写在 `.editorconfig`，CI 会跑 `dotnet format --verify-no-changes` 卡住不合规的提交。
- 每个项目共用的构建属性写在 `Directory.Build.props`，单个项目只留自己特有的部分。
- 注释写"为什么"，不写"做了什么"；用户能看到的文字一律走文案表，不写死在代码或 XAML 里。
- 界面只认引擎交出的接口：屏幕、UIA、窗口表、录制器、全局钩子、驱动级输入与浏览器都由组合根
  （`WhaleGenie/Execution/SharedDevices.cs` 与 `App.axaml.cs`）交来，窗口、视图模型与拾取器里不出现
  `WhaleGenie.Core.Devices.Platform`——`LayeringTests` 扫源码卡住这一条，替身设备层也就够驱动它们。

## 性能怎么量

抓屏与识别的单位成本量一次记在 [performance.md](performance.md)（另有
[英文版](performance.en.md)），改画面获取或匹配算法之后重测一遍与表里比。量法写在那一篇的开头：
临时建一个引用 `WhaleGenie.Core` 的小程序，直接用引擎自己的设备，每项先热身、再取若干次的中位数，
量完把小程序删掉。数字说的是"看一次"的成本，谁读屏幕、什么时候读见
[manual.md](manual.md) 的「画面从哪来、怎么取」。


## 提交与 CI

`master` 是主线，`.github/workflows/ci.yml` 在推送与 PR 上跑格式检查、构建和全部测试，
失败即红。三个测试工程在 CI 上一个一个跑（`-m:1`）：它们同时跑会抢那几个核，按墙钟说话的用例
就会替别的工程花掉的 CPU 时间买单。测试报告作为构建产物保留。

## 发布

发布走 `.github/workflows/release.yml`：**打一个 `v` 开头的标签就是发版**。

```powershell
git tag v0.01
git push origin v0.01
```

标签推上去之后，工作流会打两个包——一个要机器上有 .NET 桌面运行时，一个自己带着运行环境——
自动建好 Release 并把两个包挂上去。标签名同时决定了压缩包的名字和 exe 里的版本号，所以只有
一处要改；想在网页上发也行：Actions → Release → Run workflow，填一个版本号。

建 Release 需要写权限。仓库若把 Actions 的默认权限设成了只读，要在
Settings → Actions → General → Workflow permissions 里放开一次。

两种包差别只在"要不要自己带运行环境"，其余完全一样：

| 包 | 自带运行环境 | 体积（x64 打包后 / 解压后） | 什么时候用 |
| --- | --- | --- | --- |
| `WhaleGenie-<版本>-win-x64.zip` | 否，要装 .NET 10 桌面运行时 | 117 MB / 300 MB | 体积最小；已经装过运行环境 |
| `WhaleGenie-<版本>-win-x64-standalone.zip` | 是 | 189 MB / 471 MB | 换机器、给别人，什么都不用装 |

体积是本地这么量出来的：跑一次 `build/package.ps1`，`Expand-Archive` 解开两个 zip，再用
`Get-ChildItem -Recurse -File | Measure-Object Length -Sum` 把文件长度加起来（前者是压缩后的，
后者是解压后的）。换机器或换版本都要重新量。

解压后那多出来的一大块主要是浏览器动作用的 Playwright 驱动（`WhaleGenie` 文件夹里的
`.playwright`，约 100 MB，其中 node 一份就占 89 MB），它必须跟 exe 一起走，所以两个包都带着。
这个驱动里只放 Windows 那一份 node（根 `Directory.Build.props` 里的 `PlaywrightPlatform`）：
驱动原本给五个平台各带一份，别的平台的 node 在 Windows 上永远不会被执行，抄进去只是让每个包
多出四百多兆。

不再出单文件包：exe 旁边的那些 dll 单文件版一样要在第一次启动时解压到系统临时目录，
省下来的只是"拷一个文件"这点方便，却多一种要验的形态。

两种包解开后形状一样，只有一个 `WhaleGenie` 文件夹，exe 就在最外层：

```
WhaleGenie\
    WhaleGenie.exe              ← 双击它
    WhaleGenie.dll
    WhaleGenie.deps.json
    WhaleGenie.runtimeconfig.json
    LICENSE.txt                 ← 程序自己的许可，AGPL-3.0 原文
    NOTICE                      ← 项目名与版权署名
    THIRD-PARTY-NOTICES.md      ← 用到的别人的东西
    libVIIPER-LICENSES.txt      ← 虚拟 USB 库自己的许可说明（它的 dll 在 lib 里）
    doc\manual.md               ← 使用说明，帮助菜单的「快速上手」打开的就是这一份
    lib\                    ← 程序用到的 dll 都在这一层文件夹里
```

驱动级输入那个 `libVIIPER.dll` 也在 `lib` 里：它是原生库，跟着其余 dll 一起搬过去，程序启动时
把 `lib` 交给 Windows 的查找路径（`Program.HookLibraryFolder`），按名字就能找到它。开发目录里
它就在 exe 旁边，两种形状都认（`DriverInput.LibraryPresent`）。包根那份 `libVIIPER-LICENSES.txt`
是打包脚本从 `third_party/libVIIPER/licenses.txt` 抄过来的。

程序按 AGPL-3.0 授权：条款原文随包里的 `LICENSE.txt` 发，署名在 `NOTICE` 里，两份都放在包根；对应源码就是
本仓库里这个标签。`LICENSE.txt` 是许可原文，一字未改——GitHub 是靠文件内容认协议的，在它前后加项目名或
版权行会让主页显示成"Other"，所以这两份的行尾转换在 `.gitattributes` 里关掉了，仓库里存什么字节，取出来
就是什么字节。

自带运行时的那一个，根目录还会多出 .NET 运行时自己的十来个文件（`hostfxr.dll`、`coreclr.dll`
这些）。它们必须和 exe 放在一起：宿主是先找 `hostpolicy`、再找 `coreclr`、再由 `coreclr` 找
JIT 和核心库的，都按"exe 所在目录"找，放进 `lib` 程序连报错的机会都没有。其余两百多个 dll
都在 `lib` 里，运行时按清单（`WhaleGenie.deps.json`）里的路径去那儿取，清单里的路径在打包时
跟着一起改写。

体积里不含调试符号：打包时会把 `.pdb` 删掉，它比程序本身还大（Skia 一份就 80 MB）。不做裁剪
（`PublishTrimmed`）：Avalonia 的 XAML、UIA 和 OCR 都靠反射找类型，裁了就得一个动作一个动作地
验，不值当；体积几乎全在原生库上（OpenCV 的 `OpenCvSharpExtern.dll` 约 73 MB、ffmpeg 约 29 MB，
加上 OCR 模型、Skia、HarfBuzz）。运行 C# 脚本用的 Roslyn 与 NuGet 客户端也在里面，解压后一共
约 19 MB，其中约 6.5 MB 是编译器自带的二十几种语言的提示文字：留着它，脚本编译不过时的提示
跟着界面语言走（中文系统上就是中文）。

本地想打同样的包，跑打包脚本就行——工作流里跑的也是它：

```powershell
pwsh build/package.ps1 -Tag v0.01
```

产物落在 `dist\`（已忽略）：`WhaleGenie-<标签>-<架构>.zip` 和
`WhaleGenie-<标签>-<架构>-standalone.zip`。

只出 64 位（`win-x64`）：找图/等图/点图依赖的 OpenCV 和浏览器动作用到的 Playwright 驱动
都只有 64 位一份，32 位的包装出来也是坏的，所以不再出。面向用户的说明写在 `.github/release-notes.md`，
发版时原样作为 Release 说明贴出去。
