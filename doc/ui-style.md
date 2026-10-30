# WhaleGenie 界面样式规范

[简体中文](ui-style.md)

界面的施工图：颜色、字号、间距、圆角的令牌，以及每个组件族在六种状态下的取值。加窗口、加控件、改样式照这里写；
`Themes/Controls.axaml` 落地时逐行对这张表核。用这个程序的人看 [manual.md](manual.md)，改这个仓库的
命令与工程结构看 [dev.md](dev.md)，工程划分看 [architecture.md](architecture.md)。

**规矩只有一条：实现不发明类名，也不写裸值。** 表里出现的 `SurfaceCard`、`PadControl`、`RadiusControl`
都是资源键；表里没有的类，先回到这份文件加一行，再写 XAML。

## 三层归属：谁决定外观

| 层 | 是什么 | 在哪 |
|---|---|---|
| 层 1 | Fluent 主题键覆盖：`Button*`、`TextControl*`、`ComboBox*`、`ScrollBar*`、`MenuFlyout*`、`ToolTip*`、`TableView*`、`CheckBox*`、`ToggleButton*`、`ToggleSwitch*`、`ControlCornerRadius`、`OverlayCornerRadius`、`ContentControlThemeFontFamily`、`ControlContentThemeFontSize` | `Themes/FluentKeys.axaml` |
| 层 2 | `FluentTheme.Palettes` 的 `ColorPaletteResources`，让由系统色派生的刷子与 ColorView 同色 | `App.axaml` |
| 层 3 | 我们自己画的族：容器、页签、标题栏、拾取器覆盖层，以及所有偏离 Fluent 默认的部分 | `Themes/Controls.axaml` |

**顺序不能倒。** 层 1 的键是否真的被模板引用，必须先用探针验过：

1. 写 `FluentKeys.axaml`（层 1 键 + `DensityStyle=Compact`）；
2. 跑六控件探针：`Button` / `TextBox` / `ComboBox` / `ScrollBar` / `TableView` / `ColorView`；
3. 填出"跟随 / 不跟随"的实测表；
4. **然后**才写 `Controls.axaml`——跟随的族按"层 1 给四态"写，不跟随的族改由层 3 全写。

不先探针就写，最坏情况是下面这三族全部重写，而症状是"看起来没问题"：

| 族 | 外观由谁决定 | 若层 1 不生效 |
|---|---|---|
| `Button.Secondary` | 层 1 的 `ButtonBackground*` / `ButtonForeground*` / `ButtonBorderBrush*` | 静默退回 Fluent 默认外观，`Controls.axaml` 里看不出异常 |
| `TextBox.Field` | 层 1 的 `TextControlBackground*` / `Foreground*` / `BorderBrush*` / `PlaceholderForeground*` | 同上 |
| `ComboBox.Field` | 层 1 的 `ComboBoxBackground*` / `DropDownBackground` | 同上 |

## 颜色通道：属性名对不等于生效

| 载体 | 吃的属性 | 不吃什么 |
|---|---|---|
| `IconView`（本项目自绘图标） | `Stroke` + `StrokeThickness` | 不吃 `Foreground`、不吃 `Fill` |
| `Shape`（`Path` / `Ellipse` / `Rectangle`） | `Fill`（描边用 `Stroke`） | **不吃 `Foreground`** |
| `TextBlock` / `TextBox` 等 `TextElement` 派生 | `Foreground` | — |

`Foreground` 只对继承自 `TextElement` 的元素有效。把颜色挂在一个 `Path` 上再指望继承，属性名对、语法合法，
就是不生效。所以状态色一律走独立属性（标记用 `Fill`、图标用 `Stroke`、文字用 `Foreground`），由 `Classes`
决定，不跨载体继承。唯一例外是宏卡片的颜色 chip（`HexColorToBrushConverter`，那是内容数据不是状态）。

## 字号、间距、圆角

字号六档，资源名成对出现（`<名字>Size` 与 `<名字>Line`）：

| 档 | 字号 | 行高 | 用途 |
|---|---|---|---|
| `UiCaption` | 11 | 15 | 提示、hex 读数、脚注、徽标、分组小标题 |
| `UiBodySmall` | 12 | 16 | 密集行（步骤行、表格单元、列表项）、菜单项、**成句提示** |
| `UiBody` | 13 | 18 | 默认正文、按钮、输入框、对话框正文 |
| `UiSubtitle` | 15 | 20 | 面板标题、对话框主标题、数值读数 |
| `UiTitle` | 18 | 24 | 区块大标题、空状态标题 |
| `UiDisplay` | 24 | 30 | 只给"关于"窗口标题 |

三条硬规则：

1. `UiCaption`(11px) 只许**单行、不换行、≤8 字**的中文短标签；任何 `TextWrapping="Wrap"` 的中文必须 ≥12px。
2. 成句提示一律 12（`UiBodySmall`）；13（`UiBody`）只给正文、控件、短标签。
3. 视图里不出现 `FontSize=` 字面量；`TextBlock` 的字号只来自 `Classes`。

间距 `Space1`=4、`Space2`=8、`Space3`=12、`Space4`=16、`Space6`=24、`Space8`=32。
圆角 `RadiusControl`=4（控件）、`RadiusContainer`=6（容器）、`RadiusOverlay`=8（浮层）；圆形用
`Classes="Pill"`（半径 = 高/2），**没有第四个圆角值**。

### 必须新增的复合令牌

`Space*` 都是单值 `Thickness`，表达不了 `12,0` 这种"只横向"的内边距。不补这一组，实现只能写
`Padding="12,0"`，那就是裸值。需要在 `Tokens.axaml` 增加：

```
Pad0        = 0
Pad1        = 1
PadChrome   = 8,0      （标题栏动作钮）
PadControl  = 12,0     （次按钮、页签、行内按钮）
PadControlWide = 16,0  （主按钮、对话框页脚）
PadField    = 8,6      （输入框、下拉）
PadCell     = 6,1      （表格单元输入）
PadChip     = 8,2      （芯片、标签、徽标）
PadRow      = 8,4      （列表行、卡片）
GapSection  = 0,0,0,12 （区块底距）
```

另有一组**命名的纵向间隙** `Gap*`，同样必须在 `Tokens.axaml` 里定义：两个单值令牌落不进一个 `Margin`，
而这几处又都不能交给父容器的 `Spacing`（区块之间不相邻；标签与它管着的控件之间隔着一层模板；
标题的上/下间隙本来就不等）。**新增第七个 `Gap*` 之前，先说明 `Spacing` 为什么做不到。**

```
GapSection     = 0,0,0,12    （区块底距）
GapField       = 0,0,0,8     （标签 → 它管着的控件）
GapDivider     = 0,12,0,12   （分隔线两侧；它常是 Grid 里独立的一行）
GapPageHeader  = 0,24,0,16   （页标题）
GapGroupHeader = 0,24,0,8    （分组标题）
GapSectionHeader = 0,16,0,4  （单行分隔标题）
StackGap       = 8            （Double：`StackPanel.Spacing`）
GapEmpty       = 0,32,0,32    （空状态的上下留白；它的父容器给不了）
```

**令牌有类型。** `Space*` / `Gap*` 是 `Thickness`、`StackGap` 是 `Double`、`Radius*` 是 `CornerRadius`、
字号是 `Double`。**名字不能互借**——`Spacing` 要 `Double`，那里写 `Space2` 编译不过。
这是十处规范错误里**唯一在编译期就能抓到**的一类，所以它该排在最前：写之前先看类型，
比写完再被实现撞出来便宜一个数量级。

外加一个字体令牌 `UiCodeFont`（现值 `Consolas, Cascadia Mono, monospace`；本仓库只做 Windows，
不用 `Menlo` 这类只在别的系统上有的名字），否则等宽字体只能写字面量。透明不设令牌，理由见下面结构约定的第 2 条。

### 高度：族常量，不是令牌

高度（以及控件自身的宽度）**不令牌化**，但受两条对偶的约束，缺一条它就会被用成借口：

1. **只在族定义里出现，一族一次。** 且同一条注释里说明为什么是这个数——30 与 32 的差别是有意的。
2. **`Views/` 里不给"族成员"写 `Height=` / `MinHeight=` / `Width=`**——`Button`、`TextBox`、`ComboBox`、
   `ToggleButton`、`Border`、`ListBoxItem`、`GridSplitter` 的尺寸由族定义给。
   例外：`Grid` 的行列定义、`Window` 的初始尺寸，以及**叶子图元**（`IconView`、`Image`、`Ellipse`、`Path`）
   的尺寸——图标的光学尺寸随语境变（标题栏 10、菜单 15、卡片 13），令牌化会得到一堆长度为一的键，
   与"高度不令牌化"是同一条理由。

这条规则的价值不在"允许"，在"只在那一处"：`H30` 写在族里是决定，写在窗口里是散落，而散落正是这轮要
消灭的东西。

为什么不令牌化：高度是每个族最具体的那个决定，做成令牌会得到一堆长度为一的键。

### 图元尺寸：允许值清单

叶子图元的尺寸允许字面值，**但只能取这张清单里的值**。不是不让写字面值，是不让同一个决定散落在几十处各拍一次。
清单按现状归并得出（`Views/` 里 101 个带尺寸的图元：`IconView` 75、`Image` 23、`Ellipse` 3）：

| 档 | 值 | 用途 | 现状归并自 |
|---|---|---|---|
| 标记点 | **8** | 状态点、颜色点 | 6, 8 |
| 图标·小 | **10** | 标题栏动作钮、树内折叠箭头 | 9, 10 |
| 图标·中 | **12** | 卡片动作钮、行内小图标 | 11, 12, 13 |
| 图标·常规 | **15** | 菜单项、列表项、按钮内图标 | 14, 15 |
| 图标·大 | **16** | 面板标题旁、录制指示 | 16, 17 |
| 图标·特大 | **22** | 空状态、分类栏、窗口图标 | 22 |

**内容尺寸不在此列**：预览画布、缩略图与截图（现状 34 / 56 / 190 / 252）由内容决定，按内容写，
不受本清单约束。

超出清单的值要在评审里说明为什么不能归并到已有档位——"看着合适"不是理由。

**归并的代价不是尺寸，是对齐。** 三处要各给一句说明，不是不许归并，是归并之后被看过一眼：

| 归并 | 处数 | 要看什么 |
|---|---|---|
| 13 → 12 | 7–8 | 缩小 1px 本身看不见，但图标与相邻文字的**基线**可能从"齐"变成"差 1px"；截图看不出，要并排比对 |
| 17 → 16 | 2 | 同上；并确认它同排的是 12 还是 15 |
| 6 → 8 | 1 | **唯一变大的一档**；状态点在小行里放大 2px 会显眼，要看它有没有顶破行高 |

### 四条结构约定（违反即评审不过）

1. **`Controls.axaml` 里不写 `Margin`**，纵向间距由父容器的 `Spacing` / `RowSpacing` 承担（`Space3`）。
   例外只允许**命名的** `Gap*` 令牌（见上表，现有 6 个）。理由不是洁癖：`Margin="2,4,0,0"` 是"用间距调对齐"的症状，
   收样式前满地都是；而**命名的 `Margin` 不是病**——它查的是"这个间隙有没有名字"，不是"有没有写 Margin"。
2. **不写裸值**：颜色、字号、圆角、内边距与间距四类，一律引令牌。**唯一例外是 `Transparent`**——它没有值可
   漂移、不参与对比度核算，而且模板里每个键各自写一支透明画笔（`<SolidColorBrush x:Key="…" Color="Transparent"/>`），
   不共享一支，所以不必也不该为它设一个长度为一的令牌。
3. **焦点环不归 `Controls.axaml` 管**：颜色来自层 1 的 `SystemControlFocusVisualPrimaryBrush` /
   `SystemControlFocusVisualSecondaryBrush`，值取 `FocusRing` / `FocusRingInner`。**厚度层 1 给不了**
   ——主题里只有这两个刷子键，没有 Thickness 键（已把主题资源整个枚举核对过），所以 2 / 1 只能由
   `FocusAdorner` 显式给。**焦点环归接点击的那个控件**：自绘元素（`Border.Card.*`、`Ellipse.Dot`、
   `GridSplitter.Split`）只有在**它自己接点击**时（带 `PointerPressed` / `Tapped`，或本身就是可点的
   `Button`）才需要 `Focusable="True"` + 同款 `FocusAdorner`；若它只是 `Button` / `ListBoxItem` 内部的
   外观，焦点由外层承担，**给它加会造出一个键盘停靠点**，Tab 会在同一处停两次。
   实见于本仓库：`Views/` 的 124 个 `<Border>` 里接点击的是 **0 个**，卡片类全部挂在 `Button` 上
   （`CardButton` 3、`ActionCard` 2、`Card`/`FnCard` 3、`TriggerCard` 1），所以当前这批
   `Border.Card.*` **一个焦点环都不写**。
4. **态只改颜色，不改尺寸。** 需要更粗的边，默认就写同粗细的 `Transparent`；需要更大的内边距，默认就留出来。
   **可 grep**：态选择器（`:pointerover` / `:pressed` / `:selected` / `:checked` / `:disabled` / `.xxx`）里出现
   `BorderThickness` / `Padding` / `Height` / `Width` 即违规。理由：改尺寸的态会在切换瞬间移动内容，
   而这类位移在截图里看不出来，只有并排比对才明显。

## 四处裁定

### 1. 过滤器芯片（`ListBox.List.Pill`）为什么靠描边而不是靠底色

问题：`AppDock` 并进 `SurfacePanel` 后，胶囊底色与它所在的面板同值，亮主题下看不见了。

不能靠"换一个更亮的填充"解决——**根因不是缺一个颜色，是它用"两个填充之间的亮度差"来被认出来**，
而这两个填充被合并了。改成由描边承担定义：

| 态 | 值 | 暗 / 亮实测 |
|---|---|---|
| 默认 | bg `SurfaceControl` · 1px `BorderControl` | 填充 1.25 / 1.12，描边 2.19 / 1.75 |
| 选中 | bg `AccentSurface` · 1px `AccentText` | 有明确色相差，不依赖亮度 |

`SurfaceControl` 在暗色下只比面板高一档（与 `Card` 同级），不会太跳。真机若嫌亮主题偏淡，**把描边调到
`BorderControlHover`，不要再去提亮填充**——再亮就进入按钮权重了。

### 2. 字段标签的"强调"用字重，不用颜色

原先 4 处参数槽标签用琥珀色与普通标签区分。琥珀色已并进 `Warning`，若跟着变警告色，会把"警告"稀释成
"标签"——这是不可逆的语义损耗。

裁定：`TextBlock.FieldLabel.Strong` = `UiBodySize` 600 + `TextPrimary`。

> **颜色表达状态，字重表达结构。**

若在密集的函数签名里字重不够，退路是改 `TextBlock.Code`（等宽）——参数名用等宽比用颜色更准确，
**不要退回颜色**。

### 3. 加强标题按"有没有自己的一行"分两类

原先 21 处"加强标题"合并进 `TextPrimary` 后失去强调，但**不新增字号档**：

| 情形 | 类 | 值 |
|---|---|---|
| 独立成行的标题（面板标题、对话框标题） | `TextBlock.Title` | `UiSubtitleSize`(15) 600 · `TextPrimary` |
| 与正文同行的加强（"变量名："这类） | `TextBlock.FieldLabel.Strong` | `UiBodySize`(13) 600 · `TextPrimary` |

判据是"它有没有自己的一行"。混着用一个类会把行内加强也抬到 15px，打乱行高。

### 4. 会同现的状态不能共用同一个通道

`Border` 四条边共用一个 `BorderBrush`，画不出"只左边变色"；而 `.selected` 若也改描边，两个状态就抢同一处，
只剩"谁覆盖谁"可答。**靠覆盖就要有优先级，而优先级只能靠书写顺序或额外规则——两样都不可靠。**

所以按通道分工：

| 状态 | 只改哪个通道 |
|---|---|
| `.selected` | **只改底**：`AccentSurface` + 前景 `AccentText`，**不设描边**（保持卡片的 `BorderSubtle`） |
| `.problem` | **只改描边**：整圈 1px `Warning`，底不动 |
| `.chosen` | **只改描边**：整圈 1px `Success`，底不动 |

同现时的读法是"底偏红＝选着它，描边发黄＝它有问题"，两条信息同时成立，不需要仲裁。

> **会同现的两个状态不能共用同一个通道。** 一定会同现、又没有通道可分时，说明它们该拆成两个类，
> 或者其中一个是另一件的标记。

**单个状态用两个通道是正当的**——`ListBoxItem` 的选中态同时改底（`AccentSurface`）与左边条（左 2px
`AccentText`），说的是同一件事的两个侧面，比只用其中一个更清楚；它没和谁抢通道，所以不违规。
规则防的从来不是"用几个通道"，是"两个态抢同一处"。

**拆不开时，把优先级写进选择器。** 同一通道上的两个态若无法分到不同通道，用 `:not(...)` 显式排除，
不要靠书写顺序：

```
Border.Card.Selectable:pointerover:not(.selected)   /* 选中时不显示 hover 底 */
```

理由：书写顺序是一种**隐式**优先级，改一次样式顺序就悄悄改了行为；`:not(...)` 是**显式**的，读的人一眼看到"这两个态
互斥"，改顺序也不会坏。先拆通道，拆不开就显式排除。

## 组件族表

状态格格式 `背景 · 前景 · 边框`。`—` = **该态不设 setter**（沿用默认值）。**`Button*` 与
`ToggleButton*` 六个态必须写满**：层 1 已给这些控件的四态默认值，空一格就会露出 Fluent 的外观，
不是"没有变化"。

**表里每一格写的属性，必须能在该控件的属性表里找到。** 查得到就写；查不到就把这一格改成"由谁承担"。
已知不存在的：`Border` 没有 `Foreground`——文字色由内层 `TextBlock` 给（写法见"容器"那节）。
不要写成"注意确认载体有这个属性"：那与"注意别改尺寸"同类，读者会点头然后照旧；**"找不到"才会把格式逼到正确。**

**第二个问题：这个取值能不能落进那个属性的一次赋值？** 属性存在，值也可能落不进去——

- `Margin` 一次只接受**一个** `Thickness`，所以"上 `Space6` 下 `Space4`"（两个单值）写不进去：
  要么合成一个复合令牌（`GapPageHeader` 就是这么来的），要么改用父容器的 `Spacing`；
- `BorderThickness` 一次给**四个**数，所以"左边 2px、其余 0"能落进去（`3,0,0,0`），
  而"只左边变色"落不进去——`BorderBrush` 只有一支。

两个问题合起来才完整：**属性存在吗**（载体有没有它）、**值落得进去吗**（形状对不对）。
问不出答案的那一格，就是写不成的那一格——这是"规则要能失败"在这里的具体形态。

**修饰靠类叠加，不靠复制。** 用修饰类时 `Classes` 必须同时含父类——`Classes="Ghost Tiny"`，**不能只写 `Tiny`**。
叠加才继承父类的态（`Button.Ghost:pointerover` 继续命中）；只写修饰名时父类那条选择器不匹配，
症状是"看起来有样式、但不跟随父类改色"——又一种静默失效。**要一条用例**：窗口里凡用修饰类，`Classes` 含父类。

每一族还要回答一句"**它的态会不会同现**"；会同现，就按裁定第 4 条拆通道，拆不了说明这是两个类。
这一句不在表的任何一格里面，所以必须单独问。

只有**多态族**需要列出同现组合——带 `.selected` / `.problem` / `.chosen` / `.Current` / `.Occupied` 这类可叠加
修饰的那几族（`Border.Card.Selectable`、`Border.Card.Thin`、`Button.Tile`、`ListBox.List ListBoxItem`）。
**没有可叠加修饰的族，答"不同现"即可**，不必每族写一段。

### 按钮

| 类名 | 控件 | 尺寸 | 默认 | hover | pressed | selected/on | disabled | focus | 归属 |
|---|---|---|---|---|---|---|---|---|---|
| `Button.Chrome` | Button | H24 · `PadChrome` · `RadiusControl` | `Transparent` · `TextPrimary` · — | `OverlayHover` · `TextPrimary` · — | `OverlayPressed` · `TextPrimary` · — | `AccentSurface` · `AccentText` · — | — · `TextDisabled` · — | 双色环 | 层 3（须压住层 1） |
| `Button.Chrome.Icon` | Button | **30×24** · `Pad0` · `RadiusControl` | `Transparent` · `TextPrimary` · — | `OverlayHover` · `TextPrimary` · — | `OverlayPressed` · `TextPrimary` · — | `AccentSurface` · `AccentText` · — | — · `TextDisabled` · — | 双色环 | 层 3（图标钮是**固定方形**，不是文字钮的 padding 自适应——图标宽度本来就不同，靠 padding 会让一排钮参差不齐） |
| `Button.Chrome.Icon.Wide` | Button | **42×28** · `Pad0` · `RadiusControl` | `Transparent` · `TextPrimary` · — | `OverlayHover` · `TextPrimary` · — | `OverlayPressed` · `TextPrimary` · — | `AccentSurface` · `AccentText` · — | — · `TextDisabled` · — | 双色环 | 层 3（拾取器/运行窗那类工具钮：比图标大一圈，**不与 `Chrome.Icon` 合并**——缩小 12×4 是凭空的设计决定，规范里没有依据） |
| `Button.Chrome.Icon.Quiet` | Button | 修饰：尺寸沿用父类 | `Transparent` · **`TextTertiary`** · — | `OverlayHover` · **`TextTertiary`** · — | `OverlayPressed` · `TextTertiary` · — | 同父类 | — · `TextDisabled` · — | 双色环 | 层 3（"更弱的工具钮"：**尺寸与强度是两个维度**，各自一个修饰，不合成一个类名） |
| `Button.Chrome.Labeled` | Button | H24 · `PadControl` · `RadiusControl` · `UiBody` | `Transparent` · `TextPrimary` · — | `OverlayHover` · — · — | `OverlayPressed` · — · — | `AccentSurface` · `AccentText` · — | — · `TextDisabled` · — | 双色环 | 层 3 |
| `Button.Primary` | Button | H32 · `PadControlWide` · `RadiusControl` · `UiBody` 600 | `AccentFill` · `TextOnAccent` · — | `AccentFillHover` · `TextOnAccent` · — | `AccentFillPressed` · `TextOnAccent` · — | — | `SurfaceControl` · `TextDisabled` · — | 双色环 | 层 3 覆盖层 1 |
| `Button.Secondary` | Button | H30 · `PadControl` · `RadiusControl` · `UiBody` · 1px | `SurfaceControl` · `TextPrimary` · `BorderControl` | `SurfaceControlHover` · `TextPrimary` · `BorderControlHover` | `SurfaceControlPressed` · `TextPrimary` · `BorderControl` | — | `SurfaceControl` · `TextDisabled` · `BorderSubtle` | 双色环 | **层 1 给四态**；层 3 只补度量与 disabled 去色 |
| `Button.Ghost` | Button | 24×24 · `Pad0` · `RadiusControl` | `Transparent` · `TextSecondary` · — | `OverlayHover` · `TextPrimary` · — | `OverlayPressed` · `TextPrimary` · — | `AccentSurface` · `AccentText` · — | — · `TextDisabled` · — | 双色环 | 层 3 全写 |
| `Button.Ghost.Tiny` | Button | 16×16 · `Pad0` · `RadiusControl` | 同 Ghost | `OverlayHover` · `AccentText` · — | `OverlayPressed` · `AccentText` · — | 同 Ghost | — · `TextDisabled` · — | 双色环 | 层 3 |
| `ToggleButton.Chip` | ToggleButton | H28 · `PadChip` · `RadiusControl` · `UiBodySmall` · 1px | `SurfaceControl` · `TextSecondary` · `BorderControl` | `SurfaceControlHover` · `TextPrimary` · `BorderControlHover` | `SurfaceControlPressed` · — · — | checked：`AccentSurface` · `AccentText` · `AccentText` | `SurfaceControl` · `TextDisabled` · `BorderSubtle` | 双色环 | 层 1 四态 + 层 3 覆盖 checked |
| `ToggleButton.Chip.Key` | ToggleButton | 32×32 · `Pad0` · `RadiusControl` · `UiBodySmall` · 1px | `SurfaceField` · `TextPrimary` · `BorderControl` | `SurfaceControlHover` · — · `BorderControlHover` | `AccentFill` · `TextOnAccent` · `AccentFill` | — | — · `TextDisabled` · `BorderSubtle` | 双色环 | 层 3（键位按下要亮） |
| `Button.Icon` | Button | 34×32 · `Pad0` · `RadiusControl` | `SurfaceControl` · `TextPrimary` · — | `SurfaceControlHover` · — · — | `SurfaceControlPressed` · — · — | — | — · `TextDisabled` · — | 双色环 | 层 3 |
| `Button.Thumb` | Button | `Pad1` · `RadiusControl` · 1px | `SurfaceSunken` · — · `BorderControl` | — · — · `AccentText` | — · — · `AccentText` | — | — · — · `BorderSubtle` | 双色环 | 层 3 |
| `Button.Tile` | Button | MinH64 · `PadChip` · `RadiusControl` · 1px | `SurfaceCard` · `TextPrimary` · `BorderSubtle` | `SurfaceHover` · — · — | `SurfacePressed` · — · — | `.Occupied` → 底 `SurfaceHover`（只改底）；`.Current` → 描边 1px `AccentText`（只改描边，**不改粗细**） | — · `TextDisabled` · `BorderSubtle` | 双色环 | 层 3 |
| `Button.Field` | Button | H34 · `PadField` · `RadiusControl` · 1px · `UiBody` · 左对齐 | `SurfaceField` · `TextPrimary` · `BorderControl` | `SurfaceControlHover` · — · `BorderControlHover` | `SurfaceControlPressed` · — · — | — | — · `TextDisabled` · `BorderSubtle` | 双色环 | 层 3（输入框形状的按钮） |

### 导航

| 类名 | 控件 | 尺寸 | 默认 | hover | pressed | selected/on | disabled | focus | 归属 |
|---|---|---|---|---|---|---|---|---|---|
| `Button.Tab.Root` | Button | MinH32 · `PadControl` · `RadiusControl` · `UiBody` · 底 2px | `Transparent` · `TextTertiary` · `Transparent` | `OverlayHover` · `TextPrimary` · — | `OverlayPressed` · `TextPrimary` · — | — · `TextPrimary` · `AccentFill` | — · `TextDisabled` · — | 双色环 | 层 3 |
| `Button.Tab.Root.Compact` | Button | **MinH28**（不设 Height）· `PadControl` · `UiBodySmall` · 底 2px | 同 Root | 同 Root | 同 Root | 同 Root | 同 Root | 双色环 | 层 3 |
| `Button.Tab.Side` | Button | H32 · `PadControl` · `RadiusControl` · `UiBody` · 左 3px | `Transparent` · `TextTertiary` · `Transparent` | `OverlayHover` · `TextPrimary` · — | `OverlayPressed` · — · — | `AccentSurface` · `TextPrimary` · `AccentFill` | — · `TextDisabled` · — | 双色环 | 层 3 |
| `Button.Tab.Inline.Solid` | Button | H28 · `PadControl` · `RadiusControl` · `UiBodySmall` | `Transparent` · `TextTertiary` · — | `OverlayHover` · `TextPrimary` · — | `OverlayPressed` · — · — | `AccentFill` · `TextOnAccent` · — | — · `TextDisabled` · — | 双色环 | 层 3 |
| `Button.Tab.Inline.Boxed` | Button | H28 · `PadControl` · `RadiusControl` · `UiBodySmall` · 1px | `Transparent` · `TextTertiary` · `BorderControl` | `OverlayHover` · `TextPrimary` · `BorderControlHover` | `OverlayPressed` · — · — | `SurfaceCard` · `TextPrimary` · `AccentText` | — · `TextDisabled` · `BorderSubtle` | 双色环 | 层 3 |
| `Button.Rail` | Button | 48×50 · `Pad0` · `RadiusControl` · `UiCaption` 600 | `Transparent` · `TextTertiary` · — | `OverlayHover` · `TextPrimary` · — | `OverlayPressed` · — · — | `AccentSurface` · `AccentText` · — | — · `TextDisabled` · — | 双色环 | 层 3（分类栏，**不属 Tab 族**） |

四档页签一眼分开：**Root 横条在底、Side 竖条在左、Inline 无条、Rail 不是页签。**

### 容器

| 类名 | 控件 | 尺寸 | 默认 | 状态 | 归属 |
|---|---|---|---|---|---|
| `Border.Section` | Border | `Space4`(16) · `RadiusContainer` | `SurfacePanel` · — · — | — | 层 3 |
| `Border.Panel` | Border | `Space4` · `RadiusContainer` | `SurfacePanel` · — · — | — | 层 3 |
| `Border.Sunken` | Border | `Space4` · `RadiusContainer` · 1px | `SurfaceSunken` · — · `BorderSubtle` | — | 层 3 |
| `Border.Card` | Border | `PadRow` · `RadiusControl` · 1px | `SurfaceCard` · — · `BorderSubtle` | — | 层 3 |
| `Border.Card.Thin` | Border | `PadRow` · `RadiusControl` · 1px | 同 Card | `.selected` → `AccentSurface` · （文字自带） · —（**不设描边**）；`.chosen` → — · （文字自带） · `Success` | 层 3 |
| `Border.Card.Selectable` | Border | `PadRow` · `RadiusControl` · 1px | `SurfaceCard` · （文字自带） · `BorderSubtle` | `:pointerover:not(.selected)` → `SurfaceHover`；`.selected` → `AccentSurface` · （文字自带） · —（**不设描边**）；`.problem` → — · （文字自带） · `Warning`（**整圈 1px**，不指定某条边） | 层 3 |
| `Border.Card.Target` | Border | `PadRow` · `RadiusControl` · 2px | `AccentSurface` · — · `AccentFill` | `.Rejected` → `DangerSurface` · — · `Danger` | 层 3（**不透明**，不用 30% 透明） |
| `Border.Tag` | Border | H18 · `PadChip` · `RadiusControl` · `UiCaption` · 1px | `SurfacePanel` · （文字自带） · `BorderSubtle` | 无 hover / pressed（只读；内层文字 `TextSecondary`） | 层 3 |
| `Border.Badge` | Border | H18 · `PadChip` · `RadiusControl` · `UiCaption` 600 | 只有几何，颜色由下面四个修饰给 | 无 | 层 3（档位由 H18 决定，不由字号规则决定） |
| `Border.Badge.Info` | Border | 同 `Border.Badge` | `InfoSurface` · （文字自带） · — | 内层文字 `Info` | 层 3 |
| `Border.Badge.Success` | Border | 同 `Border.Badge` | `SuccessSurface` · （文字自带） · — | 内层文字 `Success` | 层 3 |
| `Border.Badge.Warning` | Border | 同 `Border.Badge` | `WarningSurface` · （文字自带） · — | 内层文字 `Warning` | 层 3 |
| `Border.Badge.Danger` | Border | 同 `Border.Badge` | `DangerSurface` · （文字自带） · — | 内层文字 `Danger` | 层 3 |
| `Border.Divider` | Border | 高 1px · 上下 `Space3` | `BorderSubtle` | — | 层 3 |
| `GridSplitter.Split` | GridSplitter | 宽 6px | `Transparent` | hover **整条** 6px `AccentFill`（不居中）；可聚焦。**已知偏差的原因不是属性缺失**：`GridSplitter` 有 `Background`，但**没有内容层**——居中那条 2px 线得靠子元素画，而它的模板里没有能放子元素的层。要严格居中就得加那一层，属结构改动 × 22 个窗口；真机若太扎眼，迁移时再议，**不为它造元素** | 层 3 |

`Border.Card.Thin` / `.Selectable` / `.Target` 是**修饰**：只改状态，不改壳。判断一个新需求属于容器还是修饰，
问"我要装东西时会不会想到它"——会，是容器；不会，是修饰。

**`Border` 没有 `Foreground`。** 这几族的前景列写"（文字自带）"：文字的色由里面的 `TextBlock` 决定，不由
`Border` 的类决定。需要让文字随 `Border` 的状态变（例如选中时转 `AccentText`），用**后代选择器**在同一个文件里写
（`Border.Card.Selectable.selected TextBlock { Foreground: AccentText }`），**不要给 `Border` 设 `Foreground`**
——那条 setter 会静默无效。这是 `dev.md` 第 4 条（颜色通道随载体）的同一族：属性名对、载体没有它。

### 列表

| 类名 | 控件 | 尺寸 | 默认 | hover | pressed | selected | disabled | focus | 归属 |
|---|---|---|---|---|---|---|---|---|---|
| `ListBox.List` | ListBox | `Pad0` | `Transparent` | — | — | — | — | — | 层 3 |
| `ListBox.List ListBoxItem` | ListBoxItem | H32 · `PadRow` · `RadiusControl` · 左 2px 预留（默认 `Transparent`） | `Transparent` · `TextPrimary` · — | `/template/ ContentPresenter` → `OverlayHover` | `OverlayPressed` | `AccentSurface` · `TextPrimary` · 左 2px `AccentText` | — · `TextDisabled` · — | 双色环 | 层 3（`ListBoxItem*` 无键可覆盖） |
| `ListBox.List.Boxed` | ListBox | 1px · `RadiusControl` · `Pad1` | `SurfaceWindow` · — · `BorderControl` | — | — | — | — | — | 层 3 |
| `ListBox.List.Pill ListBoxItem` | ListBoxItem | H24 · `PadChip` · `RadiusControl` · `UiCaption` · 1px | `SurfaceControl` · `TextSecondary` · `BorderControl` | `SurfaceControlHover` · `TextPrimary` · `BorderControlHover` | `SurfaceControlPressed` | `AccentSurface` · `AccentText` · `AccentText` | — · `TextDisabled` · `BorderSubtle` | 双色环 | 层 3 |
| `ListBox.List.Bare ListBoxItem` | ListBoxItem | `Pad0` | 六态全 `Transparent`（外观交给子 `Border.Card.Selectable`） | — | — | — | — | 双色环 | 层 3 |

**行选择器用后代（空格），不用子代（`>`）。** `ListBoxItem` 的真实位置是
`ListBox > ItemsPresenter > 面板 > ListBoxItem`，`>` 从 `ListBox` 出发匹配不到任何行——**而且不报错**，
行会安静地保持 Fluent 默认外观。`App.axaml` 里既有的 `ListBox.Dock ListBoxItem` 用的也是空格。

列表行的选中态**文字统一 `TextPrimary`**，不改纯白。

### 文本

> `Text.` 只在本表里表示"文本族这一目"；写进 XAML 必须是 `TextBlock.`——类名不等于控件类型。

| 类名 | 控件 | 尺寸 | 颜色 |
|---|---|---|---|
| `TextBlock.PageHeader` | TextBlock | `UiSubtitleSize` 600 · `UiSubtitleLine` · `GapPageHeader` | `TextPrimary` |
| `TextBlock.Title` | TextBlock | `UiSubtitleSize` 600 · `UiSubtitleLine` | `TextPrimary` |
| `TextBlock.GroupHeader` | TextBlock | `UiBodySmallSize` 600 · `UiBodySmallLine` · `GapGroupHeader` | `TextTertiary` |
| `TextBlock.SectionHeader` | TextBlock | `UiCaptionSize` 600 · `UiCaptionLine` · `GapSectionHeader` | `TextTertiary`（单行短标签） |
| `TextBlock.FieldLabel` | TextBlock | `UiBodySize` 400 · `UiBodyLine` · `GapField` | `TextSecondary` |
| `TextBlock.Body` | TextBlock | `UiBodySize` 400 · `UiBodyLine` · 下边距无 | `TextSecondary` |
| `TextBlock.FieldLabel.Strong` | TextBlock | `UiBodySize` 600 · `UiBodyLine` | `TextPrimary` |
| `TextBlock.Hint` | TextBlock | `UiBodySmallSize` · `UiBodySmallLine` · `TextWrapping=Wrap` | `TextTertiary` |
| `TextBlock.Note` | TextBlock | `UiBodySmallSize` · `UiBodySmallLine` · **`TextWrapping=NoWrap`** | `TextTertiary`（单行附注：同一档字号与颜色，与 `Hint` 的差别只是不折行。判据：有句读或超过 20 字用 `Hint`，否则用 `Note`） |
| `TextBlock.Problem` | TextBlock | `UiBodySmallSize` · `UiBodySmallLine` · `TextWrapping=Wrap` | `Warning` |
| `TextBlock.ColumnHeader` | TextBlock | `UiCaptionSize` 600 · `UiCaptionLine` | `TextTertiary` |
| `TextBlock.Code` | TextBlock / SelectableTextBlock | `UiCodeFont` · `UiBodySmallSize` · `FontFeatures=tnum` | `TextPrimary` |

`FieldLabel` 的下边距是"标签 → 控件"的间距；**正文不设下边距，间距由父容器给**。正文借 `FieldLabel`
写作时每段下面会多一道 `Space2`，所以成段说明与对话框正文用 `Body`。

`TextTertiary`（暗 `#A8A8A8` / 亮 `#676058`）的合法底面判据是**余量 ≥ 0.2，即对比度 ≥ 4.7**，
不是"明度 ≤ 某个值"：暗主题上底面越暗越合法，亮主题上越亮越合法，两个方向的明度轴相反，一个阈值
写不出两套主题。所以这里给的是**每套主题各一张禁用表**，括号里是提亮后的对比度：

| 主题 | 禁用底面 |
|---|---|
| 暗 | `SurfacePressed`（4.23）、`SurfaceControlHover`（4.57） |
| 亮 | `SurfaceControlPressed`（3.80）、`SurfaceControlHover`（4.27）、`SurfacePressed`（4.36） |

其余底面都可用，最低的两处是暗主题的 `SurfaceHover` 4.93 与亮主题的 `SurfaceControl` 4.75。
判据按**对比度**、不按状态名：同样是 pressed，暗主题的 `SurfaceControlPressed #2B2B2B` 比静息更暗
（5.95 合法），亮主题的 `#CFCAC1` 比静息更亮（3.80 禁用）。
带副文本的交互元素一律用 `TextSecondary`，它在所有这些面上都过。

**这张表是派生的，不是常量：`TextTertiary` 的值一改就要重算。** 两套主题共 12 个底面，改值时把
24 个比值重跑一遍，别照抄上一版。

留 0.2 的余量而不是卡在 4.5：现值 `#9E9E9E` 在 `SurfaceFlyout` 上是 4.51、在亮主题的
`SurfaceControl` 上是 4.59，余量 0.01 与 0.09，四舍五入一次就跌破。提亮后这两处到 5.08 与 4.75，
`SurfaceFlyout` 与 `SurfaceHover` 因此从"禁用"变成"可用"。

代价是第三档与第二档挨得近了：`TextTertiary` 对 `TextSecondary` 的比值从 1.27 降到 1.20，层次窄
了一点但还在。真机上若看着发糊，**抬 `TextSecondary`（`#C0C0C0`），不要把 `TextTertiary` 压回去**——
压回去就是把刚补上的 0.01 又送掉。

### 输入

| 类名 | 控件 | 尺寸 | 默认 | 归属 |
|---|---|---|---|---|
| `TextBox.Field` | TextBox | H30 · `PadField` · `RadiusControl` · 1px · `UiBody` | `SurfaceField` · `TextPrimary` · `BorderControl` | **层 1 全给四态**，层 3 只补 H30 |
| `TextBox.Field.Cell` | TextBox | **H24** · `PadCell` · `RadiusControl` · `UiBodySmall` | `SurfaceField` · `TextPrimary` · `BorderControl` | 层 3 只补度量（24 档必须留，否则公式行涨 6px） |
| `TextBox.Code` | TextBox | `PadField` · `RadiusControl` · 1px · `UiBodySmall` · `MinH58` · `UiCodeFont` | `SurfaceSunken` · `TextPrimary` · `BorderSubtle` | 层 3（无 hover） |
| `TextBox.Code.ReadOnly` | TextBox | 同上 + `IsReadOnly=True` | `SurfaceSunken` · `TextSecondary` · `BorderSubtle` | 层 3 |
| `ComboBox.Field` | ComboBox | H30 · `PadField` · `RadiusControl` · 1px | `SurfaceField` · `TextPrimary` · `BorderControl` | **层 1**，层 3 只补 H30 |
| `ComboBox.Field.Cell` | ComboBox | **H24** · `PadCell` · `RadiusControl` · `UiBodySmall` · 1px | `SurfaceField` · `TextPrimary` · `BorderControl` | 层 1 给四态，层 3 只补度量。**与 `TextBox.Field.Cell` 对称**：公式行里两种格子并排，差 6px 看得出来 |
| `AutoCompleteBox.Field` | AutoCompleteBox | H30 · `PadField` · `RadiusControl` · 1px | `SurfaceField` · `TextPrimary` · `BorderControl` | 层 3 + 必须带 `AutoCompleteBox.Field TextBox { Background: Transparent }`，否则双边框 |

### 表格、空状态、拾取器

| 类名 | 控件 | 尺寸 | 取值 | 归属 |
|---|---|---|---|---|
| `TableView`（默认） | TableView | 行与单元格 padding 由 `DensityStyle=Compact` 给 | 行底 `SurfaceCard`、表头底 `SurfacePanel`、列头分隔 `BorderSubtle` | 层 1 `TableView*` 给行底与分隔线（**选中态有键、表头底无键**），表头底由**层 3 补** |
| `StackPanel.Empty` | StackPanel | `StackGap` · `GapEmpty` | 图标 `TextTertiary` · 标题 `TextBlock.Title` · 说明 `TextBlock.Hint` | 层 3 |
| `Ellipse.Dot` | Ellipse | 8×8 · 无边框 | `Fill` 由 `Classes` 给 `Success` / `Warning` / `Danger` / `Info` / `TextDisabled`，**不靠继承**。**用例**：加 `.Success` 后 `Fill` 为 `Success` **且 `Foreground` 未被设置**——`Foreground` 挂在 `Ellipse` 上是合法语法、静默无效，只有断言能拦 | 层 3 |
| `Ellipse.Dot.Disabled` | Ellipse | 同上 | `Fill` = `TextDisabled` | 层 3（`Ellipse` 没有 `:disabled`——它不是 `InputElement`，所以禁用是**类**不是伪类） |
| `Border.PickerScrim` | Border | 铺满 | `PickerScrim` | 层 3，**主题不变** |
| `Border.PickerTarget` | Border | 铺满 | `PickerTargetScrim` | 层 3，**主题不变** |
| `Border.Readout` | Border | `PadChip` · `RadiusControl` · 1px | `PickerReadout` · `TextPrimary` · `BorderControl` | 层 3，**主题不变** |

拾取器的硬约束：① 不加圆角以外的任何装饰（阴影、渐变、发光）；② 不加半透明或模糊背景（会污染取色像素与
截图）；③ 不跟随主题（明暗同值），否则暗色用户取色时会被自己的界面影响；④ `WindowDecorations="None"`
的窗口不加 `CornerRadius`（会露黑角）。选区描边用双色（外 `PickerEdgeDark` + 内 `PickerEdgeLight`），
保证在任意截图上可见。

## 评审口径

`Controls.axaml` 落地后逐条核。第一条是方法，后面每一条都必须是它要求的形状：

1. **每条判据都要能失败。** 只会被引用、永远判不出"不过"的规则不是规则，是口号。下面的 10 条之所以成立，
   是因为每条都能 grep、能数、能红——加新规则时先问："它怎么判不过？"
2. 每个类都能在这份文件里找到出处；**多一个没写过的类，与少一个同样违规**。
3. 字号只有 6 档、圆角只有 4/6/8、内边距只用 `Pad*` / `Space*`（扫字面值）。
4. 每个自定义类恰好一条 `:pointerover`。
5. 每个可聚焦的自定义类都有 `:focus-visible`。
6. `Controls.axaml` 里 `App*` 残留为 0（`Views/` 里还有约 680 处，那是迁移中的正常状态）。
7. 四类字面量为 0：**颜色、字号、圆角、内边距与间距**（`Transparent` 除外，见结构约定第 2 条）。
   高度与宽度不在此列——它们是族常量，只出现在本文件的
   族定义里；`Views/` 里**族成员**（`Button`、`TextBox`、`ComboBox`、`ToggleButton`、`Border`、`ListBoxItem`、
   `GridSplitter`）的尺寸为零。`Grid` 行列、`Window` 初始尺寸与叶子图元除外。
8. 图元尺寸只取清单档位（8 / 10 / 12 / 15 / 16 / 22）；内容尺寸（预览、缩略图、截图）不受约束。
9. 修饰只改状态不改壳（`.Selectable` / `.Thin` / `.Target` / `.On` / `.Running` / `.Occupied` / `.Current`）。
10. 报告里每条"文件:行"先核自己写的，核不到就删掉这条建议。
11. 测试若带手抄清单，必须与真源做**集合**比较（不是计数，不是顺序）；读仓库文件不依赖 `bin` 布局。
12. **等价实现必须当场留理由。** 与这份文件不同、但同样成立的写法，在实现的注释里写一句"与规范 X 等价，
    因为 Y"。没写理由的等价等于一个待复发的偏差——下一轮评审会以同一个理由再提一次。
13. **"类目"的取法写死，两侧同法**，否则数与结果永远对不上：规范侧取组件族表**第一列里的反引号名**（一行一个）；
    实现侧取 `Selector=` 里**第一个空格之前**那一段整体（`ListBox.List.Pill` 是一个名字，不是 `ListBox`），
    再去掉 `/template/`、伪类、`>`、`,` 之后的部分。比法是**两条都要**：
    `规范 ⊆ 实现`（管缺，缺一个就红）+ **从右往左逐段剥离**（管多：剥掉的每段必须是状态/变体词，剥完仍命不中即红）。
    **可剥的词表必须固定成常量，并与 `Controls.axaml` 的 `STATE` 登记断言一致**——否则今天剥 `Success`、
    明天剥 `Field`，剥离就成了万能后门，第二条等于没写。

    **"实现有、规范无"红了怎么处置**（按序执行，不走样）：

    1. 逐条判**规范缺**还是**实现多**——判据：这个类表达的样式，规范里有没有对应物？
       有 → 实现多了名字；没有 → 规范缺一行。
    2. 判"规范缺"就**补规范**，并在那一行写清"**为什么它是类不是状态**"（`Ellipse.Dot.Disabled` 即此例：
       `Ellipse` 不是 `InputElement`，没有 `:disabled` 伪类）。
    3. **禁止为了让用例变绿而扩可剥词表。** 放宽一次，用例立刻从"保护"变成"装饰"，而且**不留痕迹**——
       它仍然绿，只是绿得没有意义。

    判不清就让它红着：用例里标 `Skip` 并写明待定。**红的用例会有人看，放宽的规则没人看。**

14. **引用数字必须带三样：谁算的 + 前提是什么 + 哪一版。**

    > **数 = 值 + 谁算的 + 前提 + 哪一版**

    行号只能证明"从哪读的"；一个数还要证明"它是怎么来的"（口径、假设）和"哪一版"（提交号，或
    "**工作区，未提交**"）。三次修订各补一维，每次都是**给出去的信息少了一维，而缺的那维恰好是别人要核的那维**：

    - 反例一（缺前提）：常规页 951 有出处，但前提是"每条提示折两行"——引成事实就会得出"8 条提示超限"。
    - 反例二（缺版本）：文档在工作区改了、读者按 HEAD 核，得出"文档写错了"——两边都没说谎，
      但看的不是同一份东西。**工作区未提交时必须写"（工作区，未提交）"**。

## 迁移前置条件（动第一个窗口之前必须定）

`Views/` 里已有本地类与本表的类同名：`Classes="Cell"`、`Classes="Card"`、`Classes="Key Small Cell"`——
与新增的 `Cell` / `Card` / `Field` **撞名**。`ControlsRegistryTests` 的注释已写明这条限制：
迁移之后，只留 leaf 的元素与本地同名类长得一模一样，用例**抓不到**，只能靠两窗对比。

处置按一条判据，不靠拍脑袋：**这个本地类定义的样式，本表里有没有对应物？**

- **有 → 并入**（`Cell` → `Field.Cell`）：不产生新名字；
- **没有 → 改成带窗口域的本地名**（`Expression.Cell`），并注明为什么它不是词汇表里的东西：
  带域的名字不会再撞第二次。

两件事必须一次定完，不能混：混着改的话，迁到第十个窗口时已经分不清哪个 `Cell` 是哪个，而那时再统一，
成本按窗口数翻倍。**先出"名字 → 何去何从"的一张表，再动窗口**；判据可以逐个用，但结论要一次落。

**每窗迁移的固定步骤，漏一步就会有一端静默失效：**

1. 改标记之前，先 `grep -rn 'Classes.Contains("' WhaleGenie.Tests` —— 用例按类名找元素的点要一起改。
   **只改标记不改查找，检查会去找一个已经不存在的东西**，症状是"找不到元素"而不是"样式不对"
   （`ExpressionBuilderTreeTests.Chip(...)` 就踩过一次）。这与"删 `DynamicResource` 键之前先把引用
   改完"是同一族：改了一端，另一端不报错。
2. 删本地样式之前，先用 `grep -c 'Classes="<类名>"'` 确认这一窗里还有多少处引用它。
3. 迁完记一笔账：改了几处 `Classes`、删了几条内联样式（列键名）、文件行数变化、每个新类用了几次、
   还剩几条内联。**两个样本才连得出一条线，一个点连不成线。**

**迁移只换 `Classes`，不动元素上的布局属性。** 类管"外观常项"（字号、颜色、间距、字重），不管"这一处
怎么摆"：同一个 `SectionHeader` 在 A 处左对齐、B 处居中，是**两处用法不同**，不是两个类。所以
`HorizontalAlignment` / `VerticalAlignment` / `Grid.Row` 这类定位属性留在元素上。

**删一条本地样式之前，先看它除了外观还带了什么。** 本地样式常常顺手替实例写了 `HorizontalAlignment`，
删掉样式就把它一起删了——**那不是迁移的预期变化，是误删**，而症状是"这段文字怎么跑到左边去了"，
在密集页面里很难被当成 bug 报上来。

### 并入不管状态

**并入只管外观；状态若没有对应物，就留在窗口里挂在库类上。**

本地类常常是"外观 + 一个状态"两件事压在一个名字里（`ActionCard` / `ActionCard.selected`）。
外观有对应物、状态没有时，只并外观，状态那条样式留在窗口里，选择器改成挂在新的库类上：

```xml
<!-- 窗口里留下的那一条：底由 Border.Card.Selectable 给，选中由窗口自己说 -->
<Style Selector="Border.Card.Selectable.selected">
    <Setter Property="BorderBrush" Value="{DynamicResource AccentFill}" />
</Style>
```

`MainWindow` 的 `.Active` / `.Pinned` 是同一个模式。判据不是"要不要一起并"，而是**"新词汇表里有没有
这个状态词"**：`selected` / `chosen` / `problem` 在 `STATE` 清单里，可以并；`.Active` / `.Pinned` 不在，
留着——**并在窗口外，状态留在窗口里，两件事互不相干**。反过来说，为了让一个状态能并进去而给它改个
名字（`.Active` → `.selected`），是拿词表去凑现状，症状是同一件事在两个窗口里叫两个名字。

### 每窗标准动作（模板已在 `TextPromptDialog` 上跑通）

前四步只扫不改，第五步才动手。漏一步就有一端静默失效：

1. **扫 `<Styles>`**：逐条问"本表有没有对应物"。有 → 并入（值以本表为准，不以本地为准）；没有 → 留在窗口里。
2. **扫元素上的裸色值**（`grep -o '"#[0-9A-Fa-f]\{6\}"'`）：换成令牌。语义色优先（`Danger` / `Success` /
   `Warning` / `Info`），其余按通道（`Surface*` / `Text*` / `Border*`）。**这一扫与本地样式无关**：
   `TextPromptDialog` 一条本地样式都没有，却有 5 处 `App*`；`RegionPickerWindow` 同样零本地样式，
   裸色值却有 12 处。按"样式条数"排窗口，排出来的顺序与"离迁完还有多远"不是一回事。
3. **扫元素上的 `Height` / `Width`**：族成员不写尺寸（类里一族一次）。窗口若确实要另一档高度，
   那是"缺一个变体"，报上去，**不要在元素上留一个数字**。
4. **查"用了没定义"与名字本身**：`grep` 类名本身（含测试里的 `Classes.Contains("…")`），
   再看扫出来的类在本表里有没有定义——"挂着类但从未样式化"和"样式化但没人挂"是同一个洞的两面。
5. **换类 / 删样式**：只换 `Classes`，布局属性留在元素上（见上一条）。
6. **跑** `dotnet build` + `dotnet test` + `dotnet format --verify-no-changes`，三条都绿才算迁完。
7. **记账**：删了几条本地样式（列键名）、换了几处 `Classes`、`App*` 与裸色值各减多少、行数变化、
   还剩什么（没对应物的类，逐个列名）。**还剩什么是这张账的重点**：它直接是下一步要补的词汇。

**已完成（可当参照）**：`ConfirmDialog`、`TextPromptDialog`。两窗都是 0 条本地样式、0 处 `App*`、
0 处裸色值——`TextPromptDialog` 与本清单同时落地，走的正是上面七步，没有用到第八种判断。

## 页高预算与「均值模型」的边界

设置窗内容列可用高 **584px**（`Height 700` − 标题 36 − 根页签 40 − 上下边距 40）。

**按行类型量，不按均值：**

| 行类型 | 高 |
|---|---|
| `PageHeader` | 36 |
| 组标题 | 48 |
| 勾选（一行标签） | 60 |
| 状态行 | 24 |
| 按钮行 | 38 |
| 独立提示段 | 32 |
| 带提示的字段（标签 + 控件 + 两行提示） | 107 |

**常规页首屏分解**（用来判"滚动断点落在哪"）：

| 段 | 构成 | 小计 |
|---|---|---|
| `PageHeader` | — | 36 |
| 启动 | 组头 48 + 勾 44 × 3 | 180 |
| 文件与日志 | 组头 48 + 勾 44 + 路径 91 + 日志（两行）107 | 290 |
| **首屏合计** | | **506** |

584 − 506 = **78px 余量** → 滚动起点落在「管理员权限」之上，**不需要为断点压任何文案**；切点在组边界，
不会落在"某个字段的标签与控件之间"。

**两条硬规则（页高预算）：**

1. **一条提示最多两行——按真实文案数，不要假设折行。** 判据链：内容宽 **676px**（迁移后边距：
   900 − 左右各 16 − 侧栏 180 − 侧栏右距 12）÷ 12px 汉字 ≈ **56 字/行**（理论）；实测 **52 汉字/行**；
   两行 ≈ **104 字**。**规则取 90 字**（约 180 字符）。
   90 是"两行的**下界**"，不是两行的近似值——它保证"看起来没超两行"的文案在任何 DPI 与中西文混排下都不超。
   写表时按 90 数，比按 104 数安全。
   超了改文案，不许折成三行——三行说明那句话该拆成"是什么"与"什么时候要改"两句，其中一句被写长了。
   **写表时必须数真实文案**：同一页按"假设每条折两行"算得 951、按真实字数算得 847
   （两者均为**内容高口径，不含 `ScrollViewer` 40**），差 **104px**——那 104px 不是误差，
   是"假设"与"实测"的差别。
2. **一页不超过 4 条两行提示。** 常规页按真实文案实测 **1 条**两行（日志那条 72 字），其余 7 条 45–49 字、
   一行放得下——上限 4 留了很大余量，它防的是将来文案变长，不是当前这页。
   同一页三种口径的数：**全按两行 959 / 实际 847 / 全按一行 831**（均为**内容高口径，不含 `ScrollViewer` 40**）。
   三个数都要在，只报一个就会误导。

为什么限每条行数而不只限条数：**4 条两行 = 8 行 = 128px，而 3 条三行 = 9 行 = 144px**。

**指标叫「滚动行程」，不叫"内容高"。** 它不是五个数相加，是**两组**：

> **滚动行程 = 逐项行高 + 行距（每处 8）+ `Section` 32 + `ScrollViewer` 40**
> 其中**逐项行高 = 行数 × 该档行高**，而**行数 = 字数 ÷（内容宽 ÷ 字号）**。

| 组 | 内容 | 作用 |
|---|---|---|
| **输入**（3 个） | 字数、字号档、内容宽 | 决定"占几行"，进而决定逐项行高 |
| **加数**（3 个） | 行距 8/处、`Section` 32、`ScrollViewer` 40 | 结构性常数，直接相加 |

并列写成"五段"会让下一个读的人问"字宽要不要加进去"——**加数与输入不是一类东西，混在一行就会混着用**。
改名也不是为了整齐：**"内容高"会让人以为算到内容为止，而 `ScrollViewer` 的 padding 属于容器**，争论都从
这里开始；换成"滚动行程"之后，问的是"从顶部滚到底要多少像素"，那个数唯一。

**五页滚动行程**（迁移后口径：`Text.Hint` 12px、内容宽 676）：

| 页 | 滚动行程 | 屏 | 需滚 | 余量 |
|---|---|---|---|---|
| 常规 | **919** | 1.57 | **是** | — |
| 输入 | **387** | 0.66 | 否 | 197 |
| 快捷键 | **546**（待校） | 0.93 | 否 | **38** |
| 屏幕捕获 | **478** | 0.82 | 否 | 106 |
| 驱动级输入 | **302** | 0.52 | 否 | 282 |

**快捷键那格标"待校"**：546 = 474（体验设计的修正值，**提出方未复算**）+ 72。按类型表我能列出的算式给
458 → +72 = **530**，差 16。**两个数并存**（530 / 546），第一次实测时按实测落定；在那之前谁也不要把它
当成确定值——**两个数并存不丢人，丢人的是一个数装成确定的**。

**迁移时快捷键页改动任何一处间距或文案，先按 546 重算**：它是唯一一页"改动余量不足 40px"（38px）的。

**两条记账规则**（错法都是加法）：

1. **同排的两行取较大值，不相加。** 热键条目里显示值行 30 与按钮行 38 是**同一行**，
   写成"30 + 38"会平白多出 54px。
2. **临界带（通式）**：字数落在 `(56.3k, 58.8k]`（k = 1..4）里，11.5px 与 12px 下的行数不同，必须重算：

   | k | 带 | 含义 |
   |---|---|---|
   | 1 | (56.3, 58.8] | 一行 ⇄ 两行 |
   | 2 | (112.6, 117.6] | 两行 ⇄ 三行 |
   | 3 | (168.9, 176.4] | 三行 ⇄ 四行 |
   | 4 | (225.2, 235.2] | 四行 ⇄ 五行 |

   **带的位置随字号档变**：换字号就要按新字号重算带。**带外免检、带内必算**——这才是"逐条重算"的正确
   范围（113–117 那一带正是两三行文案最常落的区间，固定成 56–58 会整段漏检）。

**存量提示的豁免与债务。** 迁移前已有的超长提示——`CaptureWindowHint` 256 字（5 行）、`CaptureScreenHint`
201 字（4 行）、`DriverAttachAtStartHint` 129 字（3 行）、`DriverHint` 104 字（2 行）——**不违反 90 字规则，
它们豁免**（这两页的 roadmap 明写本轮内容不动），但它们违反阅读。所以记成**债务**，带触发条件：
**这两页一加字段、或一动布局（含本次迁移），同一提交里必须先把它们压下来**。
修复方向：把逐档说明移出提示（选项名已经自述"窗口（PrintWindow）""桌面（DXGI）"），提示只留"管什么 + 什么时候改"。
迁移评审时凡见到真机上超过两行的提示，**记一条"该压"——不判它不过，但要有记录**，否则这四条会随迁移原样搬进新页面。
限每条的行数比限条数更直接地压住那一项。

**均值模型的边界。** `68px/字段` 这类均值**只能估"22 个窗口的总工作量"，不能判"某一页塞不塞"**。
它的误差方向相反且互相抵消：勾选项均值**高 8**、带两行提示的字段均值**低 39**（因为均值里根本没有提示项）。
抵消只保证总数，不保证任何一页——判单页必须按上表逐项加。

## 试点与收敛判据（阈值先定，数据后到）

`ConfirmDialog` 试点要报**三个数**，格式固定：

1. **内联样式条数 → 删后条数**（算单价）；
2. **新类名使用次数，格式"类名 → 使用次数（窗口数）"**；**零次的那几个单独列在最前**；
3. **保留的例外**，每条带一句名字。

**判据必须在试点之前定死**——数据一到再想"这算多还是少"，结论就会变成"看着差不多就达标"。

### 删除比例 × 类名使用分布

两个维度都要看：删除比例只回答"重复多不多"，回答不了"这些类有没有人用"。

| 删除比例 | 类名使用分布 | 结论 | 收敛目标 |
|---|---|---|---|
| **≥ 60%** | — | 以重复为主，前两轮的覆盖对了 | 沿用原目标：整仓内联样式 **≤ 30 条** |
| **30–60%** | 集中在少数族 | 混合，重复仍占主要 | "≤ 30 条 **或** 每条都有名字"，按实际取 |
| **< 30%** | 集中在少数族 | 以特例为主 | **放弃数量指标**，改为"**每条内联样式必须有名字**"：归入某个类、成为命名令牌、或成为带窗口域的局部类 |
| **< 30%** | **散在很多族** | **有的类写了没人用**——与上一行是同一个现象（窗口里仍有一堆内联样式）、不同的原因 | **先删类**，再按上一行重判 |

第二行与第四行的分开是这张表的关键：**"没删掉多少"和"类没人用"表现相同，原因相反**。只看删除比例，
会把后者误判成"特例太多"，然后去补一批本来该删的文档。

### 三条读数规则

- **"写了没人用"的族是删除候选，不是补文档的理由。** 规范超前于产品时，删规范比补实现便宜；
  零次使用的那几个，比"删得少"更该先处理。
- **保留下来的例外要逐个起名。** 例外不是"来不及收"，是"还没名字"。
- **起不出名字的例外，当场拆成两条。** 这个判法把"这段样式说不清是什么"变成了一个可执行的动作，
  而不是"再想想"。

第三种结果（以及第四行删完类之后）不是失败：它说明乱的地方不在重复，在**没名字**。而"没名字"是可以修的
——每个都有名字之后，下一个读的人就知道它为什么在那儿。
