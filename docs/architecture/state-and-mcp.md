# 持久化、项目模型与 MCP

所属：[WhaleGenie 架构](../architecture.md)。

## 8. 持久化与项目模型

`.wgmacro` 是工程 ZIP 包，包含 `manifest.json`、说明、`nodes/` 下的宏 JSON 和资源目录。
`Storage/MacroPackage.cs` 负责读写，采用临时文件加替换的原子写入方式。项目包格式当前为 33，
加载时要求版本严格相等。工程和公共宏库是**同一种包**：公共库在包里多一个 `library/entries.json`，
其中保存条目 id、版本与文档路径，包内没有触发器或工程变量；工程包没有这个区段。库条目区段自己带
结构版本 1，外层包版本不因它改变。引用只写名字（`{"name"}`），运行期只按名字取值；更早的包会被
拒绝。不兼容的形状变化必须升级包格式，不保留旧包迁移分支；版本沿革由 git 与发布说明保存。

宏文档另有两项运行设置：`inputRate`（每秒事件上限，缺省跟随程序设置）与 `toggleKey`
（启用/停用键，缺省没有）。两者只在写过时出现，旧读取器会安全忽略，因此不单独改变包格式版本。

包里的图片存在 zip 的 `assets/` 条目里；打开时解出来、截图时写入的是**程序数据目录**下这个包专属的工作目录
（`%APPDATA%\WhaleGenie\projects\<宏包名>-<路径哈希>\assets`），上一次保存替换掉的包也留在同一目录的
`last.wgmacro.bak`。它们不放在包旁边：项目文件夹因此只有 `.wgmacro`，包放在只读位置时也有地方解图，
两个同名的包也不会互相覆盖。
写包时找图有两个来源：包自己旁边，以及调用方交过来的来源目录（`Save` 的 `assetFolder`）——
导出所选宏、另存到别处都写在图片从来没去过的地方，不给这一项，`assets/x.png`
就解析不到，图会被悄悄漏掉。`AssetCollector` 对每个目录先试 `目录/assets/x.png`，再试 `目录/x.png`，
因为值可能是相对包的路径，也可能只是解包目录里的文件名。

`ProjectTriggers` 是项目级触发入口的唯一来源，触发入口、菜单页面、界面布局和禁止呼出规则属于项目。
菜单呼出键与系统总开关属于当前机器的 `LocalSettings`，切换项目不改变它们。父宏与内嵌子宏保存在
同一宏文档中；公共宏拖入项目时复制内容并固定来源版本，不随后续库更新。

工程文档状态由 `src/WhaleGenie.Domain/Models/ProjectWorkspace` 持有：宏、触发入口、共享变量、包路径、脏标记、修订号、
`Changed` 与整体替换 `Replaced` 事件都在这一处更新。`MainViewModel` 只保留系统开关并把文档转发给原生
菜单、轮盘与设置；宏列表的搜索、分组、视图与排序由页面自己持有，编辑器与 `RunFactory` 直接读
`ProjectWorkspace`。`McpLiveProject` 实现 `Mcp/IProjectGateway`，负责把读写切到界面线程；MCP 工具与
资源只依赖这个接口，不依赖窗口模型。

`ProjectSaver`（App）是包文件的唯一写入口：工程文档变更后按短防抖复制计划、在工作线程写 zip，再按
`ProjectSavePlan.Revision` 决定能否清脏；显式保存、退出和 MCP 保存也走同一个写者。打开/重载用
`ReplaceAsync` 等待在途写并暂停新的自动保存，避免旧文档覆盖刚打开的包。宏编辑器自己的未保存改动不算
工程变更；它要在关闭页签、离开编辑器或退出时被明确提交进工程之后，才轮到自动保存写盘。首次启动没有
上次项目时，`ProjectStartup` 落到 `%APPDATA%\WhaleGenie\projects\default.wgmacro`；文件在第一次真正
保存前不创建。

编辑器保存是一次“项目先说接受不接受”的提交：`EditorDocumentService` 按稳定 `documentId` 找到文档，
`ProjectMacroCommit` 只在目标宏仍在列表中时写入并回答 `replaced/added/unchanged`；目标已被草稿替换或
删除时回答 `conflict`。页面收到逐文档结局，只把 `saved/unchanged` 当成功；`blocked/conflict` 保留脏
状态并显示原因，避免把用户编辑静默写丢。子宏保存先写入父宏编辑模型，父宏保存才提交整个宏。工程替换
前由 `ProjectSession.PrepareProjectChangeAsync` 处理打开的文档和工程未保存改动；有打开的编辑文档时
MCP 应用草稿返回可重试拒绝。

包外状态分为：

| 状态 | 位置 | 说明 |
| --- | --- | --- |
| 本机设置 | `%LOCALAPPDATA%\WhaleGenie\settings.json` | 主题、捕获、MCP 权限、自动保存和启动行为等 |
| 全局变量（声明与当前值） | 内存 | `VariableSession`：组合根建一个，`ProjectWorkspace` 持有，编辑器、运行面板、触发器和 MCP 从它读写；项目包保存声明，值只在本进程内共享，不随包走 |
| 持久化全局变量值 | `%LOCALAPPDATA%\WhaleGenie\persistent-variables.json` | 只保存声明为持久化的值；声明仍在包内。文件版本 2 按名字保存、值写成 JSON；更旧的形状不迁移，更高版本只读不覆盖。由 `PersistedVariables` 实例持有路径、定时器与索引，经容器注入；勾选只在保存时显式写入，运行期写入由表的变更事件补写 |
| 崩溃恢复 | `%LOCALAPPDATA%\WhaleGenie\recovery.json` | `RecoveryStore` 与 `RecoveryWriter` 保存未提交编辑状态 |
| 脚本包 | 程序目录 `packages/`，必要时回退到 `%LOCALAPPDATA%\WhaleGenie\packages` | Python 与 NuGet 包的独立缓存 |

## 9. MCP

### 9.1 进程与传输

客户端始终启动 `whalegenie --mcp` 子进程，并通过 stdio 交换 JSON-RPC：

- 若窗口进程已运行，子进程通过当前用户命名管道 `whalegenie-mcp-<用户>` 转发到窗口，工具操作
  屏幕上的项目，包括未保存修改。
- 若没有窗口，子进程直接提供 stdio 服务。可以通过 `--open` 或 `--new` 指定项目；未指定时
  先尝试加载上次项目，无法加载则使用空项目。

该结构不监听网络端口，也不需要令牌。stdio 专用于协议，诊断文字只能写入 stderr。

### 9.2 权限、草稿与工具

权限只有本机设置文件一个来源：直接控制与运行宏分别由开关控制，默认关闭。服务在每次调用前读取
`McpGrants`；文件缺失、不可读或只写入一半时按未授权处理。只读项目、读取状态和读取日志不需要开启
控制权限。

`WhaleGenie.Mcp` 保存协议宿主、草稿、补丁、预览与项目编辑工具，不引用 Avalonia；`McpLiveProject`
是 `IProjectGateway` 的调度器实现，留在应用层（`WhaleGenie.App`），`McpPreview` 提供应用前差异。
`McpTolerantTool` 统一处理参数形状错误，`McpErrors` 提供稳定拒绝码。工具靠扫描发现：`McpServerHost`
接收要扫描的两个程序集（`WhaleGenie.Mcp` 与 `WhaleGenie.App`），反射找出带 `[McpServerTool]` 方法的
类型并逐个从容器取实例——容器里有就取注册的那份，没有就用 `ActivatorUtilities.CreateInstance` 按
构造参数现建，同一类型只取一次。机器控制、运行宏与运行日志工具留在应用层（`WhaleGenie.App`），
项目编辑工具在 `WhaleGenie.Mcp`；组合根只登记它们需要的依赖，不再维护 owner 清单。新增工具就是写
方法与声明：方法上加 `[McpReadOnly]`（只看不记）或 `[McpAudited(...)]`（记一行），两者都没有或同时
出现时宿主拒绝启动，`tools.list` 由对账用例核对。

`--mcp` 在 Avalonia 之前运行：窗口化运行注册 Avalonia 的调度器与窗口让位，无窗口运行注册直接调度器
与“没有窗口可让”的实现，因此协议、草稿与控制工具都不需要启动界面框架。

`McpResources` 提供动作目录、动作矩阵、项目与宏资源。当前不声明资源订阅，客户端通过修订号判断
缓存是否失效。`RunFactory` 复用普通运行路径，MCP 运行宏仍受单运行限制。工具拒绝统一返回稳定 `code`、
本地化 `message`、`hint`、`retryable` 与 `details`；客户端按 `code` 分支，不比较说明文字。

### 9.3 写入与读取契约

项目写入遵循“草稿 → 校验 → 预览 → 应用 → 保存”。一次会话最多保留 8 张草稿；达到上限时先丢弃
最旧的已应用草稿，没有可丢弃项则返回 `TooManyDrafts`，不删除尚未落地的修改。局部补丁按步骤标识和
列表路径定位，支持 `set`、行表增删改、步骤插入/删除/移动、步骤元数据、宏设置与签名声明。行表按内容
匹配，零行或多行命中都拒绝；子宏路径写作 `sub:a/sub:b`。写入结果默认返回步骤大纲，调用方需要完整
宏时显式请求 `includeMacro`。

动作与宏资源提供修订号；宏资源只带摘要、步骤大纲和声明，逐步参数通过 `get_step` 或 `get_macro` 读取。
运行日志没有稳定修订号，因此由工具按运行编号读取，不伪装成资源。图片写入只进入项目资源目录，读取只限
项目与日志目录。记录字段提示依赖宏已声明的返回字段；点位和矩形在补丁中按文件形状写整值，不另造对象
补丁语法。授权是进程级开关，不是按客户端或按调用授权。

### 9.4 自检与审计

`--mcp-doctor` 使用真实 MCP 客户端驱动协议检查，覆盖发现、草稿、补丁、资源、权限和运行入口。
它验证协议面，不替代引擎测试或真实桌面验收。

`McpAudit` 将改变状态、操作机器或导出完整图像的调用写入 `logs/mcp-YYYY-MM-DD.log`。文本输入只记录
长度，只读调用不写行；格式是时间、工具、摘要、结果四段制表符分隔，失败日志与 MCP 审计按文件名前缀区分。
审计靠声明：`[McpAudited("run {name}")]` 由统一拦截器 `McpAuditedTool` 在 `McpTolerantTool` 外层调用后补一行，
模板里的 `{参数名}` 取本次请求的参数，数组与对象写成数量、字符串截短，失败时结果写拒绝码；
`[McpAudited(Handled = true)]` 表示工具体内自己写行、拦截器不重复写。`McpToolCensusTests` 核对每个方法恰好
一种声明并与 `tools.list` 对账，`McpAuditTests` 核对拦截器写行与声明规则，`McpRunTests` 核对 run 与
stop 各写一行。
