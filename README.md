# WhaleGenie（鲸灵）

Windows 上的宏自动化工具，面向**游戏宏**：把键盘、鼠标、手柄、屏幕和窗口上的操作编成一步步的宏，
用热键、鼠标按键、滚轮或屏幕上某个像素的颜色触发它。文件、数据、网页那些办公向的能力也在，
只是它们不是这个工具的重点。界面用 Avalonia 写，执行引擎与界面分离。

## 需要什么

- Windows 10 / 11（x64）
- .NET SDK，版本按 `global.json` 固定（当前 `10.0.400`，允许同一条功能带内的更新）

## 常用命令

```powershell
dotnet build WhaleGenie.slnx            # 构建
dotnet test WhaleGenie.slnx             # 跑全部测试
dotnet run --project WhaleGenie         # 启动程序
dotnet format WhaleGenie.slnx           # 按 .editorconfig 整理格式
```

构建把警告当成错误（`Directory.Build.props` 里的 `TreatWarningsAsErrors`），
所以"能编译过"就等于"零警告"。

## 项目结构

| 项目 | 职责 |
| --- | --- |
| `WhaleGenie.Core` | 执行引擎。动作目录、变量、表达式、宏包读写、以及所有真正碰机器的设备（输入、屏幕、OCR、UI Automation、文件、进程） |
| `WhaleGenie` | 界面。窗口、视图模型、文案、触发器监听 |
| `WhaleGenie.Core.Tests` | 引擎的测试，跑在替身设备层上，不碰真实桌面 |
| `WhaleGenie.Tests` | 界面的测试，跑在 Avalonia 无头平台上 |

## 引擎用到的包

`WhaleGenie.Core` 真正依赖的就这么几个，每个都对应一类碰机器的设备；版本写在
`WhaleGenie.Core/WhaleGenie.Core.csproj` 里，这里不抄第二遍：

| 包 | 在引擎里干什么 |
| --- | --- |
| `SharpHook` | 全局键盘鼠标钩子与注入：热键 / 鼠标按键 / 滚轮这些触发器，`input.*` 的前台路径，录制，以及各种拾取 |
| `Viiper.Client` | 驱动级输入：连本机 VIIPER 服务端，把按键和指针交给虚拟键鼠对（`input.*` 的"驱动级"），以及虚拟手柄（`gamepad.*`，一只 Xbox 360） |
| `OpenCvSharp5.Windows` | 找图、找色、等图这类画面动作：OpenCV 的托管 API（`Cv2`、`Mat`）和它的 Windows 原生库 |
| `Sdcb.SimdPaddleOCR`、`Sdcb.SimdPaddleOCR.Models.ChineseV6Tiny` | 屏幕取字（OCR）：识别库和随包带的中文模型，不联网下载 |
| `FlaUI.UIA3` | UI Automation：元素的取值、选择、勾选、展开、滚动到可见、读表格 |
| `Microsoft.Playwright` | 浏览器动作（`browser.*`）：驱动跟着包走，默认开系统自带的 Edge |
| `CsvHelper` | 带分隔符的文本文件（`file.readCsv` / `file.writeCsv`）：引号、单元格里的换行、行尾、空行这些细节归它管。自己拆字符串在这些地方都会悄悄读错 |
| `OpenCvSharp5.AvaloniaExtensions` | 没人用它：引擎和界面都没有调用它的 API，它只会把 Avalonia 带进引擎的依赖图 |

这些包各自带进来的原生库（`OpenCvSharpExtern.dll`、`uiohook.dll`、Playwright 的驱动等）
和许可原文，列在 `THIRD-PARTY-NOTICES.md` 里。

## 测试怎么分层

1. **纯逻辑**（`WhaleGenie.Core.Tests`）：引擎的每一步、变量与表达式、宏包往返。用替身设备层，
   断言的是"引擎要求了什么"，不是"桌面上发生了什么"。
2. **界面逻辑与控件**（`WhaleGenie.Tests`）：参数成型、触发器比对、卡片显示、文案完整性，
   以及能真的开窗口、发按键的无头用例。无头会话把测试放在同一条 UI 线程上跑，
   `Ui.Run(...)` 是入口。
3. **手动**：真的移动鼠标、真的抓屏取色、真的注入按键这类事情无法在测试里验证，
   改动这部分时按用例手动过一遍。

文案是两份手写的表（`Strings` 的英文与中文、`ActionStrings` 的中文），
`WhaleGenie.Tests` 里的本地化用例会检查键是否两边齐全、动作的每个参数和每个下拉选项是否有中文。

## 代码约定

- 格式与风格写在 `.editorconfig`，CI 会跑 `dotnet format --verify-no-changes` 卡住不合规的提交。
- 每个项目共用的构建属性写在 `Directory.Build.props`，单个项目只留自己特有的部分。
- 注释写"为什么"，不写"做了什么"；用户能看到的文字一律走文案表，不写死在代码里。

## 宏包（.wgmacro）

`.wgmacro` 是一个 zip 容器：

| 内容 | 说明 |
| --- | --- |
| `Manifest.json` | 版本、宏的列表、全局变量 |
| `README.md` | 打开包时能看到的一份说明，列出里面的宏 |
| `assets/` | 宏用到的图片，引用写作 `assets/<文件名>` |
| `<宏名>.json` | 每个宏的步骤树 |

读写都在 `WhaleGenie/Storage/MacroPackage.cs`：打开时把 `assets/` 解到磁盘并在步骤里改写成绝对路径，
保存时再把用到的图片收进包里。

## 拾取器

编辑动作时，需要"屏幕上的某个东西"的参数都有对应的拾取按钮，不用手写坐标、路径或标题：

| 参数 | 按钮 | 做什么 |
| --- | --- | --- |
| 目标图片（`vision.findImage` / `waitImage` / `clickImage`、`condition.imageExists`） | 加一张 / 浏览… / 截屏… / 删除 | **一列图，一行一张**，从上往下试，第一张认出来的就是这一步用的那张（同一个按钮换了字、图标另一个尺寸，写一行就行）；每行可以从磁盘选图，也可以在屏幕上拖一个矩形当场存成 PNG，行首显示缩略图 |
| 单个图片（`clipboard.writeImage`） | 浏览… / 截屏… / 清除 | 选磁盘上已有的图片，或在屏幕上拖一个矩形当场存成 PNG；字段下面显示缩略图 |
| 窗口（`uia.*`、`window.*`） | 拾取窗口… | 列出当前打开的窗口（标题、程序、大小、是否最小化/最大化），可按标题或程序筛选，回车或双击选中 |
| 选择器（`uia.*`、`condition.uiaExists`） | 拾取元素… | 鼠标移到控件上，蓝色边框圈出它，点击即写入选择器；它所在的窗口会顺手填进窗口过滤 |
| 颜色 | 放大镜 | 跟随鼠标的取色器，方向键可逐像素微调 |
| 屏幕区域（x/y/width/height、起点终点、region） | 框选区域 | 在屏幕上拖一个矩形，一次填满整组参数 |
| 按键（`input.keyPress` / `keyDown` / `keyUp`） | 虚拟键盘 | 弹出画着键盘的一页，点哪个键就填入哪个键名（左右修饰键分开：左Ctrl 与 右Ctrl 是两个名字，和触发器绑定的一致）；F13–F24、媒体键、音量键、浏览器与程序键都在页面下方。字段本身也仍然可以手写，或从全部键名里筛 |
| 手柄控件（`gamepad.*`） | 虚拟手柄 | 弹出画着手柄的一页：按键点在键上，摇杆和扳机点在哪一根上就填哪一侧 |
| 变量（任何可以填变量的字段） | 变量 | 弹出变量墙：按本宏 / 共享 / 鲸灵自带 / 步骤结局筛，点一个就插到光标处。悬浮在按钮上能看见它是什么、装什么、是哪一步留下的 |

看屏幕的动作（找图、找色、找文字、截图）旁边多一个「试找一下」：**只识别，不点击、不按键、
不写变量**，跑完弹出"识别结果"窗口 —— 截图作底，识别范围、全部候选、选中的那个、动作真正
要落的地方用四种颜色分开，框上标分数，文字识别在框上标出读到的字；右侧每一条命中都可以
勾选显隐、悬浮联动、点一下拉到窗口中间。找不到时不弹空窗，而是把最高分和它需要的门槛一起
说明白。窗口右上角有「全屏」（F11 一样，Esc 退回），双击画面在像素原本大小与适应窗口之间
切换；找图列了几张参考图时，顶部写着"参考图 2/3"并显示认出来的那张。
「保存图片…」能把带框那张按原本大小存成 PNG，「保存原图…」存的是没有标记的那一张
（拿它剪新的参考图），两张都是原本大小。运行调试时每一步最后看到的画面也会
挂到它的步骤行上做缩略图（没有识别的步骤不显示），点开就是同一个窗口，跑完还能回看。

找图（`vision.findImage` / `waitImage` / `clickImage`）的**识别算法**是一枚下拉，四选一，
默认归一化相关：**归一化相关**先抵消画面整体明暗再比对；**相关**不抵消明暗，参考图很小时
更稳；**逐像素**要求两处几乎一样，纯色图标或对话框截图用它；**特征比对**把两边明显的特征点
配对，画面缩放或有小改动也认得出，慢一些且只返回一个命中。选了特征比对，表单上就只剩它用得
上的那几项（最少特征对数、忽略的颜色）—— 用不上的字段不会留在那里让人以为它起作用。
**参考图里会变的那一块可以不参与比对**：填上那个颜色（忽略的颜色），按钮上的数字、会走动的
进度条就不再影响识别。

截屏存下来的图片放在宏包旁边的 `<宏名>.assets` 文件夹里，保存宏包时自动收进 `assets/`；
项目还没保存过时先放在 `%LOCALAPPDATA%\WhaleGenie\images`。路径的解析与截图落盘都在
`WhaleGenie/Storage/ImageAssets.cs`。

运算编辑器（字段旁的"运算编辑器…"）里也是同一面墙：运算符是按钮，变量也是按钮，点一下插到
光标处，不必先记住名字再手打。

选择器、按键和手柄控件旁边都有一个"测试"，用来在写宏的时候当场确认选的是不是想要的那个：

- **选择器**是把它在屏幕上高亮一下（`uia.*` 的边框、页面里元素的闪烁），**不点它**——验的是
  "选中了是不是它"，真点下去会把用户的东西弄乱。
- **按键和手柄控件**是真的发一次：按键按这一步自己的输入方式走（前台 / 后台消息 / 驱动级），
  手柄是按一下、拨一下或扣一下，随后自动松开。按之前对话框会让开，好让你在目标窗口或游戏里
  看一眼对不对；手柄要看见效果，游戏得在前台。

试的就是**这一步本身**（按关掉对话框后会存下来的那份设置跑），而且只跑这一步：引擎里
`MacroRunner.TryAsync` 走的是和运行宏同一套代码，跑完把还按着的东西松开。

## 分隔符文件（CSV）

`file.readCsv` / `file.writeCsv` 读写的是一行一行的文本：分隔符可以点名，也可以让它自己从文件里
认出来（`auto`），引号、单元格里的换行、行尾、空行这些细节归 `CsvHelper` 管。

读的时候和表格一样能按列名取值、按内容找行（`columns` / `matchColumn`）。写的时候选"追加"再勾上
「按列名放进同名的列」（`align`），每个值就落进文件第一行里同名的那一列——别人的程序多一列、
少一列，都不会把后面的内容挤错位；文件里没有那个名字时会报出来点名。空单元格默认写成两个分隔符
之间留空，非要看见格子的程序可以用「空单元格写成」让它写成两个引号（`emptyCells`）。

## 程序自己的文件放哪儿

`settings.json`（设置窗口里的选择）和 `logs`（运行失败时的截图）都写在 `WhaleGenie.exe` 所在的
目录：整个文件夹拷走就等于把设置一起带走，两个副本放在一起也不会互相干扰。装在只让管理员
写的地方（比如 `Program Files`）时这两样写不进去，程序照常运行，只是设置不落盘、失败不留图。

宏和宏包不跟着程序走：默认在「文档」目录下的 `WhaleGenie` 里（`%USERPROFILE%\Documents\WhaleGenie`），
它在哪由打开的宏包决定。

## 致谢与许可

「DeepSeek 帮忙写的小助手，所以是鲸灵啦。」程序本身按 MIT 协议授权，全文见 `LICENSE.txt`。

里面用到的别人的东西、以及图标素材的出处，写在 `THIRD-PARTY-NOTICES.md`：仓库根目录放一份，
发布包里也放一份在 `WhaleGenie.exe` 旁边，程序里的「关于 → 第三方组件」显示的就是同一份。
图标是 Emojiall 的像素风鲸鱼 emoji，按 CC BY 4.0 使用，已缩放修改。

## 提交与 CI

`master` 是主线，`.github/workflows/ci.yml` 在推送与 PR 上跑格式检查、构建和全部测试，
失败即红。测试报告作为构建产物保留。

## 发布版本

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
| `WhaleGenie-<版本>-win-x64.zip` | 否，要装 .NET 10 桌面运行时 | 101 MB / 249 MB | 体积最小；已经装过运行环境 |
| `WhaleGenie-<版本>-win-x64-standalone.zip` | 是 | 173 MB / 420 MB | 换机器、给别人，什么都不用装 |

解压后那多出来的一大块主要是浏览器动作用的 Playwright 驱动（`WhaleGenie` 文件夹里的
`.playwright`，约 100 MB），它必须跟 exe 一起走，所以两个包都带着。

不再出单文件包：exe 旁边的那些 dll 单文件版一样要在第一次启动时解压到系统临时目录，
省下来的只是"拷一个文件"这点方便，却多一种要验的形态。

两种包解开后形状一样，只有一个 `WhaleGenie` 文件夹，exe 就在最外层：

```
WhaleGenie\
    WhaleGenie.exe              ← 双击它
    WhaleGenie.dll
    WhaleGenie.deps.json
    WhaleGenie.runtimeconfig.json
    LICENSE.txt                 ← 程序自己的许可
    THIRD-PARTY-NOTICES.md      ← 用到的别人的东西，见下面「致谢与许可」
    lib\                    ← 程序用到的 dll 都在这一层文件夹里
```

自带运行时的那一个，根目录还会多出 .NET 运行时自己的十来个文件（`hostfxr.dll`、`coreclr.dll`
这些）。它们必须和 exe 放在一起：宿主是先找 `hostpolicy`、再找 `coreclr`、再由 `coreclr` 找
JIT 和核心库的，都按"exe 所在目录"找，放进 `lib` 程序连报错的机会都没有。其余两百多个 dll
都在 `lib` 里，运行时按清单（`WhaleGenie.deps.json`）里的路径去那儿取，清单里的路径在打包时
跟着一起改写。

体积里不含调试符号：打包时会把 `.pdb` 删掉，它比程序本身还大（Skia 一份就 80 MB）。不做裁剪
（`PublishTrimmed`）：Avalonia 的 XAML、UIA 和 OCR 都靠反射找类型，裁了就得一个动作一个动作地
验，不值当；体积几乎全在原生库上（OpenCV 的 `OpenCvSharpExtern.dll` 约 73 MB、ffmpeg 约 29 MB，
加上 OCR 模型、Skia、HarfBuzz）。

本地想打同样的包，跑打包脚本就行——工作流里跑的也是它：

```powershell
pwsh build/package.ps1 -Tag v0.01
```

产物落在 `dist\`（已忽略）：`WhaleGenie-<标签>-<架构>.zip` 和
`WhaleGenie-<标签>-<架构>-standalone.zip`。

只出 64 位（`win-x64`）：找图/等图/点图依赖的 OpenCV 和浏览器动作用到的 Playwright 驱动
都只有 64 位一份，32 位的包装出来也是坏的，所以不再出。面向用户的说明写在 `.github/release-notes.md`，
发版时原样作为 Release 说明贴出去。
