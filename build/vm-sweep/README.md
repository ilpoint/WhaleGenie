# 在虚拟机里跑一遍 MCP 与宏

这台机器上的靶场会被 AI 真的点、真的打字。把同一份构建放进一台虚拟机，让它在那里点，桌面就不必让出来。
`--mcp-doctor --spawn --range` 检查的是产品自己走的那九步；这里的脚本检查的是**客户端能做的那些事**：
28 个场景，每个先把靶场复位，做一件事，再读一个数。

## 准备

虚拟机上装好同一份 Release 输出，用**交互式会话**起服务（SSH 登录落在会话 0，没有桌面，
窗口建得出来但输入到不了）：

```
schtasks /Create /TN WhaleGenieServe /TR C:\wg\serve.bat /SC ONCE /ST 23:59 /IT /F
schtasks /Run /TN WhaleGenieServe
```

`serve.bat` 只有两行：

```
cd /d C:\wg
WhaleGenie.exe --mcp --range --port 3917 > C:\wg\serve.log 2>&1
```

## 跑

```powershell
pip install paramiko
$env:WG_VM_HOST = "169.254.81.45"   # 默认就是实验虚拟机
$env:WG_VM_USER = "1"
$env:WG_VM_PASSWORD = "1"
python build/vm-sweep/sweep.py      # 报告写到 TestResults/vm-mcp-sweep.md
```

`mcpvm.py` 起一条 SSH 隧道，把本机的一个端口接到虚拟机的 `127.0.0.1:3917`，再用最小的 MCP 客户端
（`initialize` → `tools/call`）走这条路。服务本身只监听回环，不对外开端口。

## 什么时候该重跑

改了工具、动作目录、运行入口或靶场之后。报告里的每一行都有一个读数，例如 `presses` 从 0 变 1、
输入框里的字、色块的颜色 —— 只回答"调用成功"的检查证明不了任何事。
