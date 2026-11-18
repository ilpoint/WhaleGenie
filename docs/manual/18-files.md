# 程序文件位置

所属：[WhaleGenie 使用说明](../manual.md)。

## 程序自己的文件放哪儿

`settings.json`（设置窗口里的选择）和 `logs`（运行失败时的文字日志）都写在 `WhaleGenie.exe` 所在的
目录：整个文件夹拷走就等于把设置一起带走，两个副本放在一起也不会互相干扰。装在只让管理员
写的地方（比如 `Program Files`）时这两样写不进去，程序照常运行，只是设置不落盘、失败不留日志。

脚本要的包放在同一个目录下的 `packages` 里：`packages\python` 是 Python 包，`packages\nuget` 是 C# 脚本
用的 NuGet 包。它们与设置一样跟着程序目录走，装不进去时改用 `%LOCALAPPDATA%\WhaleGenie\packages`。

浏览器动作的**登录档案**也放在这里：`browsers\<浏览器>\<档案名>` 是「打开浏览器」那一格「档案」说的地方，
一份档案一个文件夹，删掉文件夹就等于让它重新登录一次（见「浏览器」）。

宏和宏包不跟着程序走：默认在「文档」目录下的 `WhaleGenie` 里（`%USERPROFILE%\Documents\WhaleGenie`），
它在哪由打开的宏包决定。
