# WhaleGenie（鲸灵）

Windows 上的宏自动化工具：把键盘、鼠标、屏幕和窗口上的操作编成一步步的宏，用热键、鼠标按键、
滚轮或屏幕上某个像素的颜色触发它。界面用 Avalonia 写，执行引擎与界面分离。

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

## 宏包（.vkm）

`.vkm` 是一个 zip 容器：

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
| 图片文件（`vision.*`、`condition.imageExists`） | 浏览… / 截屏… / 清除 | 选磁盘上已有的图片，或在屏幕上拖一个矩形当场存成 PNG；字段下面显示缩略图 |
| 窗口（`uia.*`、`window.*`） | 拾取窗口… | 列出当前打开的窗口（标题、程序、大小、是否最小化/最大化），可按标题或程序筛选，回车或双击选中 |
| 选择器（`uia.*`、`condition.uiaExists`） | 拾取元素… | 鼠标移到控件上，蓝色边框圈出它，点击即写入选择器；它所在的窗口会顺手填进窗口过滤 |
| 颜色 | 放大镜 | 跟随鼠标的取色器，方向键可逐像素微调 |
| 屏幕区域（x/y/width/height、起点终点、region） | 框选区域 | 在屏幕上拖一个矩形，一次填满整组参数 |

截屏存下来的图片放在宏包旁边的 `<宏名>.assets` 文件夹里，保存宏包时自动收进 `assets/`；
项目还没保存过时先放在 `%LOCALAPPDATA%\WhaleGenie\images`。路径的解析与截图落盘都在
`WhaleGenie/Storage/ImageAssets.cs`。

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

标签推上去之后，工作流会给 `win-x64` 和 `win-x86` 各打两个包——一个要机器上有 .NET 桌面
运行时，一个自己带着运行环境——自动建好 Release 并把四个包挂上去。标签名同时决定了压缩包的
名字和 exe 里的版本号，所以只有一处要改；想在网页上发也行：Actions → Release → Run workflow，
填一个版本号。

建 Release 需要写权限。仓库若把 Actions 的默认权限设成了只读，要在
Settings → Actions → General → Workflow permissions 里放开一次。

两种包差别只在"要不要自己带运行环境"，其余完全一样：

| 包 | 自带运行环境 | 体积（x64 打包后 / 解压后） | 什么时候用 |
| --- | --- | --- | --- |
| `WhaleGenie-<版本>-win-x64.zip` | 否，要装 .NET 10 桌面运行时 | 63 MB / 145 MB | 体积最小；已经装过运行环境 |
| `WhaleGenie-<版本>-win-x64-standalone.zip` | 是 | 135 MB / 315 MB | 换机器、给别人，什么都不用装 |

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
pwsh build/package.ps1 -Tag v0.01                 # x64 / x86 两种包都出
pwsh build/package.ps1 -Tag v0.01 -Rid win-x64    # 只要 x64
```

产物落在 `dist\`（已忽略）：`WhaleGenie-<标签>-<架构>.zip` 和
`WhaleGenie-<标签>-<架构>-standalone.zip`。

32 位（`-r win-x86`）两种包都能出，但找图/等图/点图依赖的 OpenCV 只有 64 位原生库，
这几个动作在 32 位包里不可用，其余功能正常。面向用户的说明写在 `.github/release-notes.md`，
发版时原样作为 Release 说明贴出去。
