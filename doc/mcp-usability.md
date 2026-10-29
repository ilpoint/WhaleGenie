# MCP 易用性自检报告

[简体中文](mcp-usability.md)

## 这份文件是什么

分两部分：前半是**生成物**——来自 `WhaleGenie.exe --mcp-doctor --report <文件>`，是某个版本真实跑出来的
结果，不是手写的评价；后半末尾那一节是**方法**，讲这套自检怎么搭起来，可以照搬到别的项目。

「好用」在这里是可数的：写一个宏要几次调用、一次改动要传多少字节、多少参数带 `enum`、多少 JSON 被塞进
字符串、多少个错误能程序化定位、多少个列表会说自己截断了。数字由 `WhaleGenie/Mcp/McpSelfCheck.cs` 采集：
它用官方客户端连上服务，按同一段脚本走一遍「打开项目 → 查动作 → 写宏 → 校验 → apply → 保存 → 改一个参数」，
把每一步的请求与回答都记账。

## 第一轮：说得清

改前是同一个尺子在同一台机器上跑未改动的代码，改后是对**打包出来的 exe**
跑 `--mcp-doctor --spawn` 的结果。

| 指标 | 改前 | 改后 | 为什么算好 |
| --- | --- | --- | --- |
| 写「if + 循环 + 延时」这个宏的工具调用数 | 14 | 9 | 8 次是写宏本身，第 9 次是故意重放一次的 `apply` 检查；批量描述动作省下 5 次往返 |
| 同一段脚本的返回字节 | 8047 | 6790 | 少的是三次关键词搜索与三次单条描述 |
| 带 `enum` 的工具参数 | 0 / 63 | 6 / 73 | 鼠标键、滚轮方向、窗口部位、比较方式、提示词主题不再靠描述里的口头列举 |
| 在字符串里塞 JSON 的参数 | 2 | 0 | 宏与全局声明是对象/数组，不再双重转义 |
| 有 `title` 的工具 | 0 / 32 | 34 / 34 | 客户端列表里显示的是人话，不是 `whalegenie_get_last_failure_log` |
| 说自己截断了的列表工具 | 0 / 4 | 3 / 4 | 第 4 个是运行日志：没有运行记录时无从测量，有记录时它给 `totalLines` / `returned` / `truncated` |
| 6 类故意错误里能被定位到行的 | 2 / 6 | 6 / 6 | 校验结构里带 `stepId`；消息本身仍是本地化文案（见遗留项） |
| 有阻断错误的草稿能否被 `apply` 拦住 | 不能 | 能 | 拒绝时给 `DraftHasBlockingProblems` 与前 5 条问题，`force` 留口 |

## 第二轮：改一个参数不必重传

整宏替换的问题是「改一个数字要重发整段宏」：宏越长，越容易在重发时写错别处，也越容易被客户端截断。
现在 `whalegenie_patch_macro` 按步骤改：`set` 一个参数、`insert` 一步、`remove` 一步、`move` 一步。

| 指标 | 结果 | 说明 |
| --- | --- | --- |
| 40 步宏整段重传 | 1914 B | 同一次写入，量的是请求体积 |
| 改其中一步的一个参数 | 125 B | 一次 `patch_macro`，含请求外壳 |
| 省下的倍数 | 15.3× | 补丁体积基本不随宏变长；真实工程里 105 步的宏，倍数还会更高 |
| 步骤标识 | 40 / 40 | 客户端写的步骤现在落地即有四字符 id，跑起来不会报成 `step..outcome` |
| 放错位置的步骤 | 拒绝 | `control.case` 放在顶层被 `BadPlacement` 挡住，且草稿一步都没改 |
| 插入 + 删除同一批 | 40 → 40 | 一批操作要么全成、要么全不改 |
| 改一张表的一行 + 加一行 | 279 B | 一次 `patch_macro`（`setRow` + `addRow`）；行按自己说的话点名，不按位置 |

顺带的变化：`put_macro` / `patch_macro` 不再把整段宏回显（`includeMacro` 才回），改成回一份 `outline`——
每一步的 `id`、`type` 与 `path`。`path` 正是下一次 `insert` / `move` 要写的 `parent`，所以改一步不必先把
宏读回来。

## 第三轮：调用另一个宏的传参

写一个「调用别的宏并传参」的步骤，客户端必须知道三件目录里没有的事：实参写成 `arguments` 的一行、行列表只有
`key` / `name` 两列、可选形参留空才用默认值。这三件都属于**被调用宏的声明**，而 `get_action` 只讲动作自己的
参数，于是客户端只能猜，猜错了要等 `validate` 报错再改。

| 改了什么 | 结果 |
| --- | --- |
| 新增 `whalegenie_get_macro_signature` | 给出一个宏的 `arguments` 与 `results`，以及可直接填的 `callTemplate` |
| `get_action` 补三样事实 | `signatureSource`（`CalledMacro` 说明形参来自被调宏）、`columns`（`returns` 行只有 `key`/`name`，两列都必填）、`fields`（返回记录的字段，例如窗口记录的 `title/process/handle/left/top/width/height`） |
| 照骨架写出的调用步 | 一条 HTTP 用例从 `callTemplate` 起手写 `control.runMacro`，填一个值后 `validate.ok == true` |
| 工具数 | 34 → 35 |
| 同一段脚本的返回字节 | 6689 → 6017 |

同一轮还修了一件与「读不读得懂」直接相关的事：回答里的中文原本被转义成 `\u6570\u91CF`，一个形参名要 12 个
字符，日志里也没法看。现在按 UTF-8 原样写出——这是 JSON over 环回 HTTP，转义只在嵌进 HTML 时才有意义。

## 第四轮：查得到、缓存得住、分得清

这一轮把三件「客户端要靠猜」的事变成可以程序化使用的东西。

| 改了什么 | 结果 |
| --- | --- |
| 每条校验问题带 `code` | `blocking[]` 与 `hints[]` 的每一项多一个与语言无关的键（`NoMacro`、`BadOption`、`Declaration.RequiredWithDefault`…）。自检的四类故意错误改按代号定位，不再靠句子 |
| 动作目录作为 MCP resources | `whalegenie://actions` 与 `whalegenie://actions/{key}`；实测列出 1 个资源 + 1 个模板，能读回 176 条动作 |
| 调用审计 | `logs/mcp-<日期>.log`，一行一次调用；9 个动手工具 + 5 个写入点；`type_text` 只记字数 |
| 补丁能改表的行 | `setRow` / `addRow` / `removeRow`，一次改一行 + 加一行 279 B（见上一节） |
| 记录字段表 | `get_macro_signature` / `list_macros` 带 `fields`；接口页可编，设计期用它核对 `field(...)` |

自检从 31 条涨到 33 条（`resources.listed` / `resources.read`），仍 0 fail、0 missing。

## 新增与改变的形状

- `whalegenie_list_actions` 返回 `{total, offset, limit, hasMore, categories, actions}`，可加 `category` 过滤，
  `limit` 上限从 50 提到 200；隐藏动作默认不列（`includeHidden` 才列），因为它们只允许出现在指名它的子列表里。
- `whalegenie_get_actions(keys[])`：一次描述多个动作，未知 key 落在 `missing` 里而不是整条失败。
- `whalegenie_get_macro_signature(draftId, name)`：一个宏收什么、交回什么，外加 `argumentColumns`
  （`name` / `value` / `given`）与 `callTemplate`（只列必填形参，可选的一个都不写——「没写」才是用默认值的写法；
  `returns` 一行一个结果，`key` 与 `name` 同名）。
- `get_action` 的参数多出 `columns`（行列表允许的列、是否必填）与 `signatureSource`，返回项多出 `fields`。
- `whalegenie_put_macro` 收 JSON 对象，`whalegenie_set_globals` 收 JSON 数组。
- `whalegenie_patch_macro(draftId, name, operations[], includeMacro?)`：`{op:"set",stepId,param,value}`、
  `{op:"insert",parent,index,step}`、`{op:"remove",stepId}`、`{op:"move",stepId,parent,index}`；
  `parent` 是列表路径（`0.then`、`0.then.2.else`），空串是顶层。子宏里的步骤仍用 `put_macro` 改。
- 写入类工具回 `outline`（每步 `id` / `type` / `path`），整宏只在 `includeMacro` 时回。
- `whalegenie_validate` 与 `put_macro` 的校验结果拆成 `blocking`（阻断，带 `macro` / `stepId` / `message`）
  与 `hints`（提示，阻断不了任何东西）。
- 拒绝统一为 `{error:{code,message,hint,retryable,details}}`，并置 `isError`；错误码见 [dev.md](dev.md)。
- `get_action` 的参数多出 `conditional`（只在某些模式下必填）与 `missingKey`；`input.mouseClick.window`
  现在会被读成「后台才需要」，而不是「永远必填」。
- 所有列表类返回都带 `total`；`get_run_log` 带 `totalLines` / `returned` / `truncated`。

## 我充当客户端的一次记录

只读 `tools/list` 的描述与 schema、不看源码，写一个「按住 Shift 每 200ms 点一下、点三次、失败就截图」的宏：

1. 不知道「按住 Shift」要自己用 `input.keyDown` / `input.keyUp` 包住点击，在目录里找了很久。
2. `input.mouseClick.window` 标着必填，于是先填了一条窗口规则——其实前台点击不需要。**已修**：现在带 `conditional`。
3. `repeat` 与 `intervalMs` 一起才是「点三次、间隔 200ms」，但 `repeat` 的提示只说「次数」。**已修**：
   `repeat` 的提示现在写着「隔多久由旁边那一格决定」。

三个卡点都修掉了；它们说明「目录是对的」不等于「读的人一次能读对」，所以下面的遗留项按「读错一次要付多少
代价」排序，而不是按实现难度。

## 遗留项

- **校验消息是本地化文案，代号不是**。结构里有 `macro` / `stepId` / `code`；消息跟着界面语言走，代号不跟。
  客户端按 `code` 分支，别再比对句子。
- ~~**运行日志只有一条实**~~：**已修**，见第五轮。现在没有运行记录时给的是空日志（`totalLines: 0` 与一句
  `note` 说明还没有跑过），只有指名了一个不存在的 run id 才算错。
- **记录只有窗口规则能整条补**：`set` 收一个值，窗口规则在文件里本来就写成对象，所以能整条给（未知字段会被拒
  并列出该有的）；`rect` / `point` 在文件里是**一个值**（记录变量，或 `field($记录, "名字")`），给对象会被拒
  并说明写法。行列表另有 `setRow` / `addRow` / `removeRow`。
- **记录的字段表只在声明里写才有**：`get_macro_signature` 与 `list_macros` 带出 `fields`，但它是宏接口页里
  人写下来的东西；没写就没有，客户端读到的只是一条空表，`field(...)` 的字段名也就无从核对。
- **补丁只管宏自己的步骤**：子宏里的步骤要整段重写。
- **运行日志还不是资源**：动作目录、项目、单个宏都已经是页面了（见第七轮），跑出来的一次记录仍是工具——
  它每次都不一样，没有可缓存的版本号可言。
- **授权仍是进程级一刀切**：`DirectControlOff` 之后只能请人开开关，没有按客户端、按调用确认。事后能查到
  做过什么（审计日志），但不能在做的当时拦住一次。
- **只支持 Streamable HTTP（stateless）**：只支持 stdio 的客户端接不上，服务端主动消息、进度与 elicitation 都用不了。
- **坏 JSON 的报错现在是 SDK 的**：参数变成对象之后，非对象在 schema 层就被挡住，不再经过我们的 `BadJson`；
  它仍然被拒，只是措辞不归我们管。
- **调用的传参已经不是键名而是行列表**：`arguments` 一行一个形参（`name` / `value` / `given`），与 `returns`
  同一套形状。第三轮给出的 `field` / `givenField` 两个键名随之退场，`macro` 也改成目录里的必填；读签名的三处
  现在共用 `CallableSignature`。
- **图片仍不能新增**：可以读写已有图片的宏，但 MCP 不能新增图片资源。

## 第五轮：读的人第一次就该读对

这一轮没有加能力，只把「新会话第一次读就会读错」的四处改对。前四处改完之后，体检仍是 36 条全过，
数字只变了两格（见下表）。

| 读完会读错的地方 | 原来的样子 | 现在 |
| --- | --- | --- |
| 浮层底部那段说明 | 写着「MCP 不能运行宏；运行仍由你按」——与它上面那枚「允许 AI 运行宏」开关直接冲突 | 说清三档：读写与看屏幕不需要开关，按键与点击要「允许直接控制」，跑一条宏要「允许 AI 运行宏」 |
| 复制配置之后 | 只给一段 JSON，不说往哪儿放 | 按钮下面一行：粘到客户端的 `mcpServers` 里，重启客户端生效 |
| `get_run_log` 还没跑过任何宏 | `NoSuchRun` + `retryable: true`，客户端会当成错误去重试或分支 | 空日志：`totalLines: 0` / `lines: []` / 一句 `note` 说明怎么产生第一条 |
| `get_macro` / `list_runs` 的描述 | 「package 存的 JSON」「运行由人按，不是 MCP」——后者已经不对 | 前者说明含触发器、形状与 `put_macro` 一致；后者说明运行可能来自人、触发器或 `run_macro` |

`server.instructions` 里同一处也有一句「Driving the machine is still not running the macro: the person
presses Run」，与它前面讲 `run_macro` 的那段矛盾，一并改掉。

## 第六轮：动手之前先看得见，发错形状有人接

这一轮加了两件「人在旁边」要用的东西，和一处协议边界上的兜底。

| 加了什么 | 为什么 |
| --- | --- |
| `whalegenie_list_drafts`（草稿箱） | 草稿只在内存里，关掉服务就没了；以前要记得 id 才能回头找。现在列出还开着的那几张：id、取自哪个包、什么时候取的、几个宏、是否已 apply |
| `whalegenie_preview_apply` | `apply` 直接写进打开的项目，写坏了要人回滚。现在先问一句「会动什么」：哪些宏会新增 / 删除 / 修改，改的那几个是哪几步（按步骤 id，与 outline / patch 同一个 id）、哪几个参数变了，哪些全局声明会动；顺带把 `validate` 的结论一并给出 |
| 服务说明里的工具地图 | 49 个工具在客户端里是一长串。说明末尾按「草稿 / 读 / 写 / 目录 / 运行 / 看 / 动」分组列出名字，AI 不用从名字猜 |
| 参数形状的容错层 | `inputSchema` 保持严格（`"type": "object"` 仍是 `"type": "object"`），但参数真的不是那个形状时，在工具被调用之前回一个 `BadJson`：写明哪个参数、来的是什么、该发什么 |

容错层是这一轮唯一需要动服务端结构的地方：工具不再靠 `WithTools<T>()` 按类型反射注册，而是由
`McpServerHost.Tools(...)` 逐个建出来再套一层 `McpTolerantTool`——它保留原工具的 `ProtocolTool`
（所以 schema 一个字节都没变），只在 `InvokeAsync` 前看一眼 `arguments`。体检里那条 `reject.bad_json`
因此从「An error occurred invoking 'whalegenie_put_macro'.」变成了：

```
{"error":{"code":"BadJson","message":"whalegenie_put_macro takes an object for macro, but it arrived
 as text.","hint":"Send the object itself rather than text that holds it.
 whalegenie_macro_template shows the shape macro wants.",...}}
```

做这一层时踩到一个自己造成的坑：可省略的参数在 schema 里写成 `"type": ["string", "null"]`，
对它直接 `GetString()` 会抛——于是每一次带字符串参数的调用都会变成「调用失败」，正好是这一层要
避免的事。现在只读 `ValueKind == String` 的 `type`。

## 这一轮之后的数字

上表的数字来自 M2 那一次。把「AI 能不能自己写对、自己看见、自己跑、在哪儿跑」这一轮做完之后，
同一份体检的现状是：

| 项 | M2 那次 | 现在 |
| --- | ---: | ---: |
| 工具数 | 35 | 48（多草稿箱与预览两件，后来靶场两件随窗口一起删了） |
| 体检条目 | 33 | 35（多了两档开关；靶场缺席那条随窗口一起删了） |
| 资源 | 2 | 3（多 `whalegenie://action-matrix`） |
| 一次典型写宏的调用 | 9 | 9（同一段脚本，没有变重） |
| 请求 / 响应字节 | 480 / 6017 | 480 / 6532（多的是描述字段；两件新工具不进这段脚本） |
| 服务说明字符数 | 未量 | 4452（多的是那张工具地图） |
| 说自己带分页字段的列表 | 3 / 4 | 4 / 4（运行日志在没跑过宏时也给 `totalLines`） |
| 改一个参数的请求字节 | 125 | 125 |
| 整宏重发的字节 | 1914 | 1914 |
| 动作契约 | 未量 | 目录里的每个动作逐个跑通，见附二 |

多出来的 11 个工具是：单步与全局变量、控件读值与按选择器找控件、窗口详情与剪贴板、进程表、读图片、
运行三件（跑/停/查）。它们不是一次场景要用的，而是「AI 自己能不能定位问题」要用的。

## 第七轮：项目与宏也读一次就够

宏不是一轮读完的东西。以前每次要动它，客户端都得先 `list_macros` 看有哪些，再 `get_macro` 把整枝读出来
——四十步的宏来回改五遍，同一段 JSON 就在上下文里搬了五遍。动作目录早就是资源了，项目与宏没有理由不是。

| 资源 | 里面是什么 | 为什么是这个粒度 |
| --- | --- | --- |
| `whalegenie://project` | 版本号、包路径、是否未保存、每个宏的步数与参数 / 返回值 / 触发器数、全局变量、触发器表 | 一张页顶掉 `project_status` + `list_macros` 两次调用，还多给了「哪个宏值得读」 |
| `whalegenie://macros/{name}` | 版本号、步数、每一步的 id / 动作 / 位置 / 属于哪个宏、子宏、参数与返回值、触发它的触发器 | 位置就是 insert 与 move 要的父路径，拿着它不必再读整条宏 |

两条边界是刻意的：

- **宏页不放每一步的参数。** 放了就等于把整条宏搬上资源，缓存省下的 token 又还回去了。参数是 `get_step`
  （一步）与 `get_macro`（整枝）的事。
- **靠版本号，不靠推送。** 传输是无会话的 HTTP，服务器发不出 `resources/updated`，所以 `initialize` 里也
  没有声明 `subscribe`——探测确认过，能力里只有 `"resources":{}`。取而代之的是每张页都带 `revision`：
  同一个号下内容不变，号变了才需要重读。这是把「什么时候失效」交给客户端判断，而不是让它等一个不会来的
  通知。

两处一并改了：`McpOutline` 把 outline 与 summary 从工具里抽出来，工具与资源读同一份，答案和页面就不会
各说各的；`McpLiveProject.ReadAsync` 一次界面往返同时取快照与「有没有未保存」，免得页面上的版本号与
未保存标记来自两个不同的 revision。

体检加了两条，都在 `--mcp-doctor --spawn` 里：`resources.project` 核对项目页的版本号与 `project_status`
报的是同一个，`resources.macro` 把项目里第一个宏读成自己的页并数出 outline 的步数。只读模式下项目里没有
宏，那条记跳过而不是失败——没写过不代表坏了。

```
| resources.project | pass | the project page reads at revision 7, the same one status reports |
| resources.macro   | pass | "MCP self check" reads as its own page, 4 steps in the outline    |
```

体检条目从 35 到 37；工具数没变——这一轮没有新工具，只有新页面。

## 第八轮：体检先问开关

关于键鼠与运行的那两条检查原来只会一种答案：**期待被拒**。可服务端现在能在开关打开时起来——那时它该被放行。旧检查对着这样的服务端跑，报的是

```
| gate.control_off | fail | {"x":1,"y":1,"durationMs":0,"cursor":{"x":1,"y":1}} |
| gate.run_off     | fail | {"error":{"code":"NoSuchMacro",…}}                     |
```

`Result: fail, 29 pass, 2 fail`，退出码 1。两处都不对：一是把「本来就该放行」当成漏洞；二是那条探针
**本身就是一次真实的鼠标移动**，服务端既然被允许，就真的把指针挪到了 (1,1)，报告却说它坏了。

现在检查先读 `project_status` 里的 `control.allowed` / `control.runAllowed`，按服务端自己说的来验：

| 权限 | 关着（默认） | 开着（页面开关） |
| --- | --- | --- |
| 直接控制 | `gate.control_off`：`mouse_move` 必须被 `DirectControlOff` 拒 | `gate.control_on`：移到**指针现在所在的位置**——驱动走完一遍，桌面不动 |
| 运行宏 | `gate.run_off`：`run_macro` 必须被 `RunNotAllowed` 拒 | `gate.run_on`：用一个不存在的名字，能走到查找就说明门开了，什么都没有真的跑 |

同一个体检对两种服务端的结论：

```
只读      Result: pass, 37 pass, 0 fail, 0 missing, 0 skip
带两权限  Result: pass, 31 pass, 0 fail, 0 missing, 4 skip   （不拥有项目的那几条照旧跳过）
```

2026-10-29 复量（`--mcp-doctor`）：37 条全过、49 个工具、3 个资源 2 个模板、2 个提示；服务说明
5408 字符，典型脚本 480 / 6554 字节，补丁省 15.3 倍（125 字节对 1914）。工具、资源、检查条数与第七轮
记的一致。

## 第九轮：换成 stdio，端口与令牌一起消失

用户的一句话定了方向：本地工具不该开监听。回头看，HTTP 那一版为了「本地」多付了三样东西：

| 代价 | HTTP 版怎么做 | stdio 版 |
| --- | --- | --- |
| 谁能连 | 只绑环回地址，靠"没有别人能到"当安全边界 | 客户端给的管道，只有它这一端 |
| 端口 | 固定 3917，被占就换一个，窗口里还得能改 | 没有端口 |
| 代理 | 客户端必须显式绕开 `HTTP_PROXY`，否则环回请求被代理接走 | 没有 HTTP 请求 |
| 生命周期 | 进程在跑就一直在听，关窗要记得停 | 客户端关管道即结束 |

改法：`McpServerHost` 用 `WithStreamServerTransport` 接一对流——命令行是标准输入输出，体检里是子进程
的管道，测试里是内存里的一对管道（`McpLoopback`）。`ModelContextProtocol.AspNetCore` 与
`Microsoft.AspNetCore.App` 的引用一并删掉，程序里不再有 Kestrel。窗口里的 MCP 从"开关 + 地址 + 端口"
改成"两档开关 + 一段可直接粘贴的**客户端配置**"：开关只把参数写进那段 JSON，启动仍然由客户端发起。

同一天复量，协议面一个数都没变：

```
Result: pass, 37 pass, 0 fail, 0 missing, 0 skip      （--mcp-doctor，自己拉子进程走管道）
工具 49 · 资源 3 + 模板 2 · 提示 2 · 说明 5408 字符 · 典型脚本 480 / 6554 字节 · 补丁省 15.3 倍
```

测试也跟着换了：原来四个文件都靠 `HttpClientTransport` 连 `host.Endpoint`，现在接 `McpLoopback`
（一对内存管道），49 条 MCP 用例仍然全绿。多出来的两条把"管道语义"也钉住了：关掉客户端那一端，
会话必须结束（`Closing_the_pipe_ends_the_session`）；对着已授权的进程体检，报的是 `gate.*_on` 而不是失败。

## 附二：动作矩阵

工具这一面靠上面的检查量，动作那一面靠 `WhaleGenie.Core.Tests/Bench`：目录里的每个动作逐个按声明
合成一步，在替身设备层上跑一遍，核四件事——结局、交出来的值是不是声明的形状、声明「会发输入 / 会看屏幕」
与设备账本对不对得上（两个方向都查）、有没有参数给不出值。它只量不改，缺的东西显式写进报告。

```
$env:ACTION_MATRIX_REPORT = "TestResults/action-matrix.md"
dotnet test WhaleGenie.Core.Tests -c Release --filter "FullyQualifiedName~ActionBench"
```

三条「等」是最难量的：`vision.waitImage`、`vision.waitDetect`、`window.waitFor` 的夹具把要等的东西放到
第三次查找才出现，再核「等到之后交回来的那个值在不在」——一条在第一次就命中的等待只证明了条件本来
就成立，不能证明它会等。报告里这三行的说明写着「第三次才出现」，设备调用次数也跟着涨上去。

报告是生成物，进版本库；改前改后各留一份，数字对不上就是代码变了。今天它一份 176 行全部通过，
另有四条用例专门证明尺子会失败：把 `control.delay` 声明成会发输入、把 `input.keyPress` 声明成不发输入，
都必须报「不符」——一把不会报错的尺子，绿色什么也说明不了。

靶场那一面曾经靠第三条命令：`--mcp-doctor --spawn --range` 拉起一个有真窗口的进程，按客户端会走的路走
一遍（读窗口位置、提到前面、写一个点按钮的宏、跑它、读回按了几下、复位、三条边界各撞一次），九项全过才
算成——它量的是「合起来能不能用」，单元测试与自检都答不了那一层。这条命令连同窗口一起移出了发布包：验证
做过一次之后，产品里不该留一个谁都用不到的窗口。目标窗口现在住在测试工程里，只在无头会话里核对坐标与
复位；真实输入那一档回到手工。

## 附：这套自检怎么搭（可以照搬）

这一节不依赖上面任何数字，讲的是「怎么让一个助手自己判断一个 MCP 服务好不好用」这件事本身。换个项目时，
把「MCP 服务」换成任何能被程序调用的东西，步骤都不用变。

### 一、先把形容词拆成能数的东西

「好用」拆成六个问题，每个跑一遍就得出一个数：

| 问题 | 数什么 |
| --- | --- |
| 做完一件典型的事要几次调用 | 调用次数 |
| 同样的事传了多少字节 | 请求与回答体积 |
| 闭集参数有多少声明成了枚举 | 枚举覆盖数 / 闭集参数数 |
| 多少值被塞进字符串（本可以是对象或数组） | 双重转义数 |
| 出错时能不能程序化定位到具体位置 | 可定位错误数 / 故意错误数 |
| 列表被截断时会不会自己说出来 | 自报截断的工具数 / 列表工具数 |

关键不是这六个数选得对不对，是**每一条都要能在同一份脚本里、改前跑一次改后跑一次**。数不出来的东西，
改完只能凭感觉说「好像顺了」。

### 二、尺子只量、不改、不断言

自检代码要满足三条：

- 不抛异常、不断言、不失败。字段缺了就记 0 或 `error`，接着跑下一条。
- 只吃一个「已经连上的客户端」，不管这个客户端是怎么来的。
- 输出一份人能读的报告，外加一份机器能比的指标表。

理由只有一个：**同一份尺子必须能在没改的旧代码上跑出基线**。一旦尺子会失败，你就只能在改完之后跑它，
before 那一列永远是空的。这是整套方法里最容易做错的一步——人习惯写测试（本来就会红），而不是写尺子
（永远绿、只记数）。

### 三、让尺子自己当客户端

服务说的是一对标准输入输出流、客户端是个普通类库，就不需要人点开关、也不需要真实用户：体检里接一对
内存管道就能跑完整份，命令行里接上子进程的真实管道就能再跑一遍。「自己改、自己测」这件事的物理前提
只有这一条。

这也删掉了两件真实产品会踩、测试环境不会踩的事：

- **系统代理**：以前环回请求会被 `HTTP_PROXY` 接走回 `502`，客户端得显式绕开代理。没有 HTTP 就不存在。
- **端口冲突**：以前固定端口会和上一次没退干净的进程打架。管道没有端口可抢。

### 四、尺子要能打真实产物

单元测试跑的是源码，用户拿到的是打包后的 exe。补两个命令行入口：

- `--mcp [--open 包] [--new 文件夹]`：无窗口起服务，走这个进程的标准输入输出；Ctrl+C 或客户端关掉
  管道就停。权限来自浮层里的两个开关（写在 `settings.json`，服务每次调用前读回来），命令行上没有权限参数。
- `--mcp-doctor`：纯客户端体检。它自己拉起一个子进程、接上管道、量完关掉。退出码分级（0 全过 / 1 有失败 /
  2 参数错 / 3 说不上话 / 4 子进程起不来），这样 CI 和人都能判。

这一步最容易拖到最后，结果整批优化只在单元测试里成立。而且它经常顺手抓出真 bug：上面那个代理问题，
还有「初始化阶段直接关窗口会让消息循环先死」——都是打真实 exe 才露出来的，源码级测试永远看不见。

### 五、尺子会抓出你自己的错，先修尺子不改标准

第一次跑基线，两处失败其实是我自己脚本的错：宏里用了循环外的系统变量（读出来是空），错误定位只匹配
消息文本而漏了结构里的 `stepId`。

处理方式决定这套方法还成不成立：**改尺子（或改被量的样本），不改判定标准**。否则前后两列数字说的不是
同一件事，比较就失效了。

### 六、报告是生成物，不是手抄

`--mcp-doctor --report <文件>` 直接把报告写进版本库——改前一份、改后一份。谁都能重跑，数字对不上就是
代码变了。手抄的数字三个月后没人知道是怎么来的。

### 三条容易踩的

- **中文被转义**。回答里的 `数量` 变成 `\u6570\u91CF`，一个两字形的名字占 12 个字符。量体积时会把它
  误判成「内容太多」，其实是编码问题（JSON over 环回，转义只在嵌进 HTML 时才有意义）。
- **临时目录要认得出是自己造的**。体检会建临时工程，清理时按前缀加时间戳精确匹配，不按通配符删。
- **进程残留**。子进程没关干净，下一次跑就多一个在后台等管道的鲸灵。父进程要等子进程真的退出，而不是
  等一会儿就当它退了；反过来，子进程那边要认「输入读到结尾就是该走了」，否则客户端退出后它还在。
