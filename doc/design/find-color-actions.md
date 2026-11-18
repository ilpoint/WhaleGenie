# 找色动作族：设计说明与实施进度

配套样板：`doc/design/find-color-editor.html`（可交互，带改前/改后对照）。
用户可见行为以 `doc/manual.md` 为准，动作事实以 `ActionSemantics.json` 为准。

## 判定层与处置层

「这一步没成」有两种意思，必须分开问：

- **否定答案**（没找到、颜色不符）是结果，交出 `found = false` / `matches = false`，由下一步的条件判断。
- **无法完成**（抓屏失败、区域在屏幕外、偏移点读不出来）才是失败，抛 `Run.*`，由 `StepErrorAction` 处置。

项目里这条规则本来统一：「必须命中」的动作各有失败键（`Run.ImageNotFound`、`Run.DetectNotFound`、
`Run.ElementNotFound`、`Run.TextNotFound`、`Run.WaitColorTimeout`），「只看一眼」的只交结果。
而 `findColor` / `findColors` 只交 `found = false`，没有自己的键——想让「没找到就停」只能另接一步判断。
补法：新增 `missingIsFailure` 判定与 `Run.ColourNotFound` / `Run.ColourMismatch`（未做）。

## 已落地

- **参数分组（通用机制）**：`ActionParameterSemantics.Group`；同一块的字段必须相邻，目录加载时校验
  （`ActionSemantics.cs`）。编辑器按块断行、块首画「编号 + 标题」（`FillRows`、`ParameterRowViewModel`、
  `ActionEditorView` 的行模板、`Controls.axaml` 的 `BlockNumber` / `BlockTitle`）。没有 `Group` 的动作与原来完全一致。
- **颜色族分组**：找色 / 多点找色 / 等待颜色 / 取色 按「在哪个范围里找 · 什么颜色 · 怎样算找到 · 识别调参」
  分组，并按块重排字段（颜色与容差归拢、位置四个字段归拢）。分组标题走 `Add.Group.*`，中英各一份。
- **执行与容错页**：新页（排在基础设置之后），含执行节奏、最多花多久、重试（三参数并组 + 计划句 `RetryPlan`）、
  失败时四张卡 + 询问告警。原「其它设置」里那七个字段已移除。
- **百分比滑块**：所有 `Unit=Percent` 的数字字段都多一条滑条，与数字框同一个值；按颜色通道量的
  `tolerance` 下方写出「≈ 每通道 ±N / 255」（`PerChannelPercent` 标记，`minCoverage` 之类不标）。
- **比色成为独立动作**：`vision.matchColor` 落地（基础页 ①看哪一个点 ②什么颜色；交 `matches` / `color` /
  `delta`）。找色与多点找色删掉了 `x`、`y`、`place` 与那条 `HasPoint` 分支——比色不再是从「找色」里
  填点填出来的隐藏模式。包格式升到 **22**：旧包直接拒绝打开（上一版填过点位的找色步骤形状已变）。
  引擎用例：定点比色、图案偏移的变量与函数各一条；三条只为「定点优先于区域」而存在的用例随行为一起删除。
- **「怎样算找到」判定块**：`missingIsFailure`（默认关）进 `findColor` / `findColors` 的基础页，与
  `minCoverage` 同块——占比从「识别调参」移到这一块，因为问的是「怎样算找到」而不是怎么调。
  打开后落空抛 `Run.ColourNotFound`（`RunKeys.ColourNotFound`，手册失败键表已补），
  由 `StepErrorAction` 处置；关着时仍交 `found = false`。引擎用例
  `A_miss_can_be_the_failure_the_step_asked_for` 覆盖两种读法。

- **`vision.clickColor`（点击颜色）**：颜色族的「找到就点」。在搜索区域里等这个颜色出现（超时是步骤的），
  按「怎么点」那一块给的按键与偏移落手；没等到就抛 `Run.ColourNotFound`，与等待颜色的失败键同一族。
  基础页四块：在哪个范围里找 · 什么颜色 · 怎样算找到 · 怎么点；高级页一块：识别调参。
  引擎用例 `Clicking_a_colour_aims_at_it_and_names_the_colour_when_it_is_not_there` 覆盖「点在哪」与「没等到说什么」。

- **偏移点是一张表**：`points` 从「一行三段文本」改成 `Shape: Rows`（`dx` / `dy` / `colour` 三列），
  编辑页是「一处一行」的表格，行末删除，下面「再加一处」与「拾取偏移与颜色…」；拾取窗口读写的是同一份行，
  不再另存一份文本。每一格仍可写算式或变量，引擎按行读（`Pattern`），少一格即 `Run.BadPoints`。
  包格式升到 **23**，旧包拒绝打开。引擎用例补了「行说不全」与「一处都没有」两条，
  基准里 `points` 也改成按列生成。

- **每一处可以有自己的容差**：`points` 加了第四列 `tolerance`（百分比，可空）。空着就用基准色那一格的
  「容差」，填了只管这一处——这是主流做法（大漠/按键精灵每个颜色后面都跟着自己的偏色，
  实战经验也是「主色严格、辅助点放宽」）。引擎侧 `ColourStep` 多一个可空的 `Tolerance`，
  `PixelSearch.Surrounded` 按 `step.Tolerance ?? 全局` 判定；空列不写进文件，旧形状不变，包格式仍是 23。

- **一处可以给几个备选色**：`colour` 格里用竖线分隔（`#E04A3F|#B03A33`），命中任一即成立；
  引擎把第一个当主色、其余放进 `ColourStep.Alternatives`，`Holds` 一并比对。比放大容差可靠：
  它说的是「就是这几种之一」，而不是「差不多就行」。
- **「不能是」（反色）**：`mustNot` 列，勾上后这一处要求不是写下的那些颜色，用来写「这里应该是空的」。
- **四个计数方向**：`orderBy` 增 `readingUp` / `readingBack` / `readingBackUp`，只给多一点找色；
  从别的角数时不再在第三个命中处提前停（`CountsFromTheFarSide`），否则「第三个」会从错的头数起。

- **四向搜索顺序（大漠 dir 0–3）**：颜色族三个动作（`findColor` / `findColors` / `clickColor`）的
  `orderBy` 就是这四种，默认第一种：`reading`（从左到右、从上到下）、`readingBack`（从右到左、从上到下）、
  `readingUp`（从左到右、从下往上）、`readingBackUp`（从右到左、从下往上）。颜色命中都是一像素，
  所以「面积最大」没有意义，「分数最高」「随机」也从这三个动作里去掉了；老宏里存过这些值，
  三个 `orderBy` 都开着 `AllowsUnknownOption`，引擎照旧认，只是新写的步骤不再列出来。
  从别的角数时不能在第三个命中处提前停（`CountsFromTheFarSide`），否则「第三个」会从错的头数起。
  中间试过一版「从中心向外」（`MatchOrder.Near` + `PixelSearch.Outwards`），用户要的是大漠的四向，
  那一版连同它的走法一起删掉了。
- **在拾取窗口里加备选色**：`Shift + 点一处` 把那个像素的颜色加成这一处的备选色（位置不动）；
  右键 →「加备选色…」打开取色器，从屏幕上另取一种（这张图上没有的颜色只能这样加）。
  一处最多 6 种，重复的不写。列表一行的颜色改画成小色块，`≠` / `±N` 标出表格里那两列。
- **拾取窗口带上放大镜**：新增 `Views/Magnifier.cs`，读拾取时已经定格的那张图（`ImageFrame`），
  不是实屏。画面缩小到一像素只有零点几点时，偏移差一格靠眼睛是看不出来的。放大镜里十字线对着
  指针那一格、红框圈住它。出现时机各不相同：框选器**只在按住拖动时**（画新框、拉边、拉角）出现，
  摆在指针右下、读数气泡挂在它下面（两样都跟着指针，只能一上一下叠着）；定点拾取器一直跟着指针
  （放在指针上方）；偏移拾取窗口在画面上跟着指针。
- **偏移点标记画在像素中心**：`LookPickWindow.ToBoardCentre` 补半格。放大到 800% 时半格就是 4 像素，
  不补的话基准圈与偏移点都偏一格，看着就是「位置不对」。
- **框选可以反着画**：抓住方块的边只往外留 3 像素（`GrabOutside`），再往外按下去就是画新框；
  拉过对边时矩形翻面而不是缩成一条线（`PullTo` 交换左右/上下）。
- **区域四个数字一行**：`NumberCellViewModel` 多一个短名（`ShortLabel`，X / Y / 宽 / 高，长说明留在提示里），
  `ActionEditorView` 加 `NumberCellTight` 模板（短名 + 数字框 + 24px 的 ƒ 按钮），区域行从 2×2 改成一行四格。
  值形状与引擎不变（`region` 仍是行表），只是编辑器排版。
- **滚轮键不再新增一处**：`LookPickWindow` 只认左键，中键常被当成拖动画面的手势，手一滑就多一条。
- **返回值只列用得上的**：`ActionReturnSemantics.Hidden`（JSON 里 `"Hidden": true`）把「取画面方式」
  （`method`）与「来源窗口」（`window`）从返回值列表里去掉（`vision.*` / `ocr.*` / `uia.find` 共 27 项）。
  `ActionReturn` 带上这个标记，`StepParameterViewModel` 建行时跳过；老宏里勾过的项在 `Load` 时记进
  `_keptResults`，保存时原样写回，界面不画但也不丢。契约测试 `Every_result_the_catalogue_names_is_one_its_own_path_writes`
  仍按完整目录核对，不受影响；`ActionPageTests` 的对照表按 `!Hidden` 过滤。
- **返回值那一块的文字取舍**：上面从「标题 + 三句说明」改成「标题 + 一句」，「勾上的各自成为一个变量，
  悬停结果名看它是什么」，命名与重名的规矩挂在标题的悬停里；每一项的长提示从常驻段落挪到结果名的悬停里，
  于是八项结果就是八行，不是八个段落。
- **返回值给出结构示例**：`ReturnsRowViewModel.Structure` / `StructureTip` 由 `ActionReturn.Type` 与 `Fields`
  拼出来——Flag 是 `true | false`，Number 是 `0`，Text 是 `"text"`，Image 是「图片」，Record 是前四个字段名
  `{x, y, left, top, …}`，List 是 `[{…}, …]`；悬停给全部字段与类型（`ActionReturn.TypeNameOf`）以及
  `field($name, "field")` 的取法。表格因此变成四列 `48,110,*,196`（留下 / 结果 / 结构 / 变量名），
  中间那片空白就是它原来空着的地方。**坑**：`field` 在 C# 14 是属性访问器的上下文关键字，lambda 参数
  不能叫这个名字，改用 `part`。
- **返回值做成表格**：`Border.Card` 里一张表头（留下 / 结果 / 变量名，`ColumnHeader`）+ 一条分隔线 +
  `ItemsControl`，行模板 `ReturnsRowEditor` 用同一套列宽 `48,*,196`，列因此跨行对齐。重名时不占表格的列：
  警告与「换个名字」缩进挂在该行下方（`Margin="56,3,0,0"`），别的行不会跟着错位。
- **区域字段与返回值对齐**：区域四个数字的提示补上「这一格就是结果里的 `left` / `top`」，变量格提示从
  `x,y,宽,高` 改成 `left,top,width,height`（同一组，一处地方就是左上角加宽高）。字段本身没改，
  包格式不变。
- **返回值搬进基础设置**：`ReturnsPage` 不再是一个页签，`Pages` 只留 基础设置 / 执行与容错 / 高级设置 / 其它；
  勾选哪些结果的那张表挂在基础页末尾（`IsVisible="{Binding HasReturns}"`）。页签少一个，读的时候少一处要翻。
- **不必写算式的地方不摆按钮**：百分比的数字字段（容差、至少占比）`TakesFormula` 为假、也不显示「表达式」按钮
  （`OffersExpressionBuilder`）；「新增一处」小窗的三格各占一行、没有表达式按钮，变量名写进三格的提示里。
- **确定按钮跟着内容走**：`UpdateConfirm()` 在打开、加一处、删一处、加备选色后都跑一遍；
  只给已有的一处加了备选色也算「改了」，确定必须能点（以前它只在新增时启用）。
- **落点按指针所在的格子**：`LookGeometry.Inside` 从四舍五入改成向下取整——右半边格子会落到下一个
  像素上，圆点画出来离手指一格，「不跟鼠标」就是这半格。
- **表与拾取窗口的外观**：对比色表套一张 `Card`（表头 `ColumnHeader`、下面一条分隔线）；拾取列表一行一条
  （序号 · 色块 · 右对齐的偏移 · ≠/±N），标题旁加个数徽标，读数改成内凹的条。都用现成的类
  （Card / Sunken / Badge / ColumnHeader），没有新增样式。
- **拾取窗口不再吞掉表格里的两列**：表与拾取窗口之间的一行写成三段（`dx,dy,colour`），
  带「不能是」或单点容差时写成五段（后两段照表格的列）。拾取窗口只改前三段，后两段原样带回来，
  所以在它里面改一条不会悄悄把容差和反色清掉。包格式不变（这两列本来就存进 `points` 的行里）。

## 未落地

| 项 | 为什么还没做 |
| --- | --- |
| 容差滑块 + 「每通道 ±N」 | 数字字段需要多一种编辑器（显示层加标记），会动到所有数字字段 |

