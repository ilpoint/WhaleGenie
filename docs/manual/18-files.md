# 程序文件位置

所属：[WhaleGenie 使用说明](../manual.md)。

## 程序自己的文件放哪儿

程序自己的状态在 `%LOCALAPPDATA%\WhaleGenie\`：

| 文件 | 放的是什么 |
| --- | --- |
| `settings.json` | 设置页里的选择，包含自动保存项目、系统开关键、主题与语言 |
| `logs\` | 运行失败、MCP 与网页桥的文字日志。`runtime.log` 当前文件上限 512 KB，另有 `.1`、`.2` 两份旧文件，总量不超过约 1.5 MB；MCP 日志按日期保留最近 100 份 |
| `recovery.json` | 上次停止前尚未写盘的工程文档（宏列表、共享变量与包路径）；下次启动会问要不要恢复。编辑器中尚未保存进工程的文档不在这里 |

这些放在用户目录里，升级、重装或换一个安装位置都不会带走；程序装在只让管理员写的地方时也照样
能保存。

工程包与它的工作文件在 `%APPDATA%\WhaleGenie\projects\`：每个包有一个以包名和路径哈希命名的
文件夹，里面是 `assets\`（包里解出来的图，步骤里的图片引用指的就是它）与 `last.wgmacro.bak`
（上一次保存替换掉的包）。同目录下的 `default.wgmacro` 是**默认项目**：第一次启动没有上次项目
时，自动保存开启就用它；第一次真正保存前不会创建空文件。

公共宏库跟着程序目录走：`library\public-macros.wgmacro` 与 `library\public-macros.assets\`，
所有项目共用，把程序文件夹拷给别人才会一起带走。

脚本要的包放在程序目录下的 `packages` 里：`packages\python` 是 Python 包，`packages\nuget` 是
C# 脚本用的 NuGet 包；装不进去时改用 `%LOCALAPPDATA%\WhaleGenie\packages`。浏览器动作的登录档案
也放在程序目录的 `browsers\<浏览器>\<档案名>`，一份档案一个文件夹。

宏包本身在哪儿由你打开或另存为的位置决定；「自动保存项目」写的始终是当前这一份包，另存为后
下一份自动保存也写到新位置。第一次保存之前截的图放在系统「图片」文件夹下的 `WhaleGenie\`。
