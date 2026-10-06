# Viktor

Windows 上的宏自动化工具：把键盘、鼠标、屏幕和窗口上的操作编成一步步的宏，用热键、鼠标按键、
滚轮或屏幕上某个像素的颜色触发它。界面用 Avalonia 写，执行引擎与界面分离。

## 需要什么

- Windows 10 / 11（x64）
- .NET SDK，版本按 `global.json` 固定（当前 `10.0.400`，允许同一条功能带内的更新）

## 常用命令

```powershell
dotnet build Viktor.slnx            # 构建
dotnet test Viktor.slnx             # 跑全部测试
dotnet run --project Viktor         # 启动程序
dotnet format Viktor.slnx           # 按 .editorconfig 整理格式
```

构建把警告当成错误（`Directory.Build.props` 里的 `TreatWarningsAsErrors`），
所以"能编译过"就等于"零警告"。

## 项目结构

| 项目 | 职责 |
| --- | --- |
| `Viktor.Core` | 执行引擎。动作目录、变量、表达式、宏包读写、以及所有真正碰机器的设备（输入、屏幕、OCR、UI Automation、文件、进程） |
| `Viktor` | 界面。窗口、视图模型、文案、触发器监听 |
| `Viktor.Core.Tests` | 引擎的测试，跑在替身设备层上，不碰真实桌面 |
| `Viktor.Tests` | 界面的测试，跑在 Avalonia 无头平台上 |

## 测试怎么分层

1. **纯逻辑**（`Viktor.Core.Tests`）：引擎的每一步、变量与表达式、宏包往返。用替身设备层，
   断言的是"引擎要求了什么"，不是"桌面上发生了什么"。
2. **界面逻辑与控件**（`Viktor.Tests`）：参数成型、触发器比对、卡片显示、文案完整性，
   以及能真的开窗口、发按键的无头用例。无头会话把测试放在同一条 UI 线程上跑，
   `Ui.Run(...)` 是入口。
3. **手动**：真的移动鼠标、真的抓屏取色、真的注入按键这类事情无法在测试里验证，
   改动这部分时按用例手动过一遍。

文案是两份手写的表（`Strings` 的英文与中文、`ActionStrings` 的中文），
`Viktor.Tests` 里的本地化用例会检查键是否两边齐全、动作的每个参数和每个下拉选项是否有中文。

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

读写都在 `Viktor/Storage/MacroPackage.cs`：打开时把 `assets/` 解到磁盘并在步骤里改写成绝对路径，
保存时再把用到的图片收进包里。

## 拾取器

编辑动作时，需要"屏幕上的某个东西"的参数都有对应的拾取按钮，不用手写坐标、路径或标题：

| 参数 | 按钮 | 做什么 |
| --- | --- | --- |
| 图片文件（`vision.*`、`condition.imageExists`） | 浏览… / 截屏… / 清除 | 选磁盘上已有的图片，或在屏幕上拖一个矩形当场存成 PNG；字段下面显示缩略图 |
| 窗口（`uia.*`、`window.*`） | 拾取窗口… | 列出当前打开的窗口（标题、程序、大小、是否最小化/最大化），可按标题或程序筛选，回车或双击选中 |
| 颜色 | 放大镜 | 跟随鼠标的取色器，方向键可逐像素微调 |
| 屏幕区域（x/y/width/height、起点终点、region） | 框选区域 | 在屏幕上拖一个矩形，一次填满整组参数 |

截屏存下来的图片放在宏包旁边的 `<宏名>.assets` 文件夹里，保存宏包时自动收进 `assets/`；
项目还没保存过时先放在 `%LOCALAPPDATA%\Viktor\images`。路径的解析与截图落盘都在
`Viktor/Storage/ImageAssets.cs`。

## 提交与 CI

`master` 是主线，`.github/workflows/ci.yml` 在推送与 PR 上跑格式检查、构建和全部测试，
失败即红。测试报告作为构建产物保留。
