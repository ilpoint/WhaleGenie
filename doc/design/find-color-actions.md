# 找色动作族：设计取舍

动作事实（字段、默认值、返回值）见 `ActionSemantics.json`。这一族为什么长成现在这样、哪些地方不能随便改，
写在下面；本文不记做过哪些批次。

## 判定层与处置层

「这一步没成」有两种意思，必须分开问：

- **否定答案**（没找到、颜色不符）是结果，交出 `found = false` / `matches = false`，由下一步的条件判断。
- **无法完成**（抓屏失败、区域在屏幕外、偏移点读不出来）才是失败，抛 `Run.*`，由 `StepErrorAction` 处置。

项目里这条规则统一：「必须命中」的动作各有失败键（`Run.ImageNotFound`、`Run.DetectNotFound`、
`Run.ElementNotFound`、`Run.TextNotFound`、`Run.WaitColorTimeout`），「只看一眼」的只交结果。

找色与多点找色在两可之间，所以给了它们一格「没找到就算失败」（`missingIsFailure`，默认关）：关着交
`found = false`，打开抛 `Run.ColourNotFound`，由 `StepErrorAction` 处置。等待颜色与点击颜色不需要这一格——
它们本来就是「等到才往下走」，落空分别报 `Run.WaitColorTimeout` 与 `Run.ColourNotFound`。比色只交答案
（`matches` / `color` / `delta`），从不失败。

## 几处刻意的取舍

- **每一处可以有自己的容差**：`points` 的第四列空着就跟基准色那一格的「容差」，填了只管这一处。实战里
  主色严格、辅助点放宽，反过来把它压成全局只会逼人放宽基准。
- **一处可以给几个备选色**：`colour` 格里用竖线分隔（`#E04A3F|#B03A33`），命中任一即成立，第一个当主色。
  这比放大容差可靠——它说的是「就是这几种之一」，而不是「差不多就行」。反色用 `mustNot` 列，写
  「这里应该是空的」。
- **`orderBy` 就是四向**：`reading` / `readingBack` / `readingUp` / `readingBackUp`，默认第一种。颜色命中都是
  一像素，「面积最大」没有意义，这一族也不给「分数最高」「随机」；老宏里存过的其它值靠
  `AllowsUnknownOption` 照旧认，只是新写的步骤不再列出来。从别的角数时不能在第三个命中处提前停
  （`CountsFromTheFarSide`），否则「第三个」会从错的那头数起。中间试过一版「从中心向外」（大漠之外的
  一种走法），用户要的是四向，那一版连走法一起删掉了。
- **拾取窗口读的是定格的那张图**：放大镜与取色都取自打开那一刻的 `ImageFrame`（`Views/Magnifier.cs`），
  不读实时屏幕。画面缩小到一像素只有零点几点时，偏移差一格靠眼睛看不出来，跟着实时屏幕反而会错位。
