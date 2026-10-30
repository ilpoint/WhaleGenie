# WhaleGenie 界面样式规范

[简体中文](ui-style.md)

界面的施工图：颜色、字号、间距、圆角的令牌，以及每个组件族在六种状态下的取值。加窗口、加控件、改样式照这里写；
`Themes/Controls.axaml` 落地时逐行对这张表核。用这个程序的人看 [manual.md](manual.md)，改这个仓库的
命令与工程结构看 [dev.md](dev.md)，工程划分看 [architecture.md](architecture.md)。

**这份文件回答"做成什么样"与"怎么判断做对了"**（令牌、形态、评审口径）；"怎么做"（动作、纪律、坑）在
[dev.md](dev.md)。
**判据要和形态放在一起看**——否则评审时要翻两份文件，而**翻第二份的那一刻，人已经开始凭印象判了**。
**一条道理只在一处成文，另一处只放一句引用 + 链接**——同一句话在两份里会各自按语境漂移，读的人就不知道该信哪份。

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

## 颜色：要区分，先问三件事

> **要区分，先问三件事：字形能不能说清？字体（等宽）能不能说清？位置或字重能不能说清？**
> 三样都不行，才用色相——因为色相只有 5 个状态 + 3 条走廊，用满就没有退路了。

**这条管取舍，下面那条"一色一义"管约束**：先决定要不要上色，再决定上色之后别冲突。
四个实例都是问"这个值想让人读成什么"问出来的——这一步比任何规则都难，所以留例不留口号：

| 值 | 想让人读成什么 | 该用什么手段 | 落法 |
|---|---|---|---|
| `#8FB3D9` **标识符**（`StepId` / 变量 `Name` / `MetaSummary`） | **这是机器造的名字**（标识符） | **等宽**（`UiCodeFont`） | `TextPrimary` + 等宽；**不为此立第 6 个语义色** |
| `#8FB3D9` `FormulaMark`（**借**标识符色，不是主用法） | **这个值由程序算出，不是你填的**（来源） | **字形 `ƒ`**（已自带） | `TextTertiary`；**不借 `Info`**——借了就说成"这是一条提示" |
| `#A9D6A9` `JsonPreview` | **这是只读的数据转储**（内容类型） | **等宽 + 凹底** | `TextBox.Code.ReadOnly`，绿色删掉；浅绿会被读成"这个 JSON 校验通过" |
| `#E4E4E4` `StepLabel` | **这一行叫什么**（主体文字） | **位置（行首）+ 字重** | `TextPrimary`；与 `#F2F2F2` 差 4%，等于没上色 |
| `#D6C08A` `Fold` | **这里能展开**（控件形状，不是文字） | **形状本身** | `TextTertiary`；原是暖色、与 `Warning` 同族，留着就得回答"这箭头是不是在警告" |

## 颜色：一色一义

**同一个色相在同一个视野里只能有一种含义。** 判据：把该视野里所有带色元素列出来；同一色相出现两次且含义
不同 → 违规。

**修复顺序：先改面积小的那个。** 人先认大面积的主色，所以面积小的更容易被读成"另一个含义"——冲突时先动
小面积，动的是"被误读的那个"，不是"被依赖的那个"。
实例：`#E5C07B` 同时是 `Script` 类别色与 `ActionEditorView:1423` 的正文前景。**正文是大面积**，它一出现
就把琥珀的含义改成了"这段是脚本"——该改的是它。

**说不出它属于哪个语义、只说得出"当时觉得这里该突出"——那是手感，手感不进令牌表。**
理由：令牌要能被别人**按语义找回来**；手感找不回来。
实例：`#C8A165` 一色四用——`RetryPlan`（预告）、`Comment`（人写的旁注）、`LookThumb` 的 hover 描边（装饰）、
`Status`（运行状态）。四条没有共同语义，共同点只是"当初都想让它显眼一点"。

**"状态用固定色"比"状态用错色"更糟。** 用错色至少还是一种色；固定色等于**没有状态**。
实例：`RunWindow:250` 用固定琥珀画 `Status`——**"已完成"和"出问题了"长得一样**。这不是配色问题，
是状态没落到状态色上：按实际值给 `Success` / `Warning` / `Danger` / `TextTertiary`。

**换色要成对：一处的前景与底都是字面值时，它们是同一个决定的两半——必须同一次改。**
只改一半，症状是"字看不见了"，而**这在暗色主题下看不出来**（暗底配深字本来就该看不清，所以没人会怀疑那里错了）。
实例：`#E0B080` 坐在 `#3A2A10` 上（速度徽标）。落法必须先定底（`WarningSurface`）、再定字（`Warning`），
**同一提交**。
判据：**改一处颜色前，先看它的容器有没有自己写死底色**；有，这就是成对的一处，不能单独落。

### 值与用法分开

| | 是什么 | 放哪 |
|---|---|---|
| **值** | 类别色的 12 个 hex、主题色样 | 代码（`ActionCategoryPresentation` 的 `switch`、`Tokens.axaml`） |
| **用法** | 出现在哪、多大面积、与谁同处一个视野 | 本文件 |

判据：**描述"界面用哪个色"→ 令牌；描述"别的东西长什么样"→ 数据。**
所以类别色的 12 个值**不进令牌**，但它们的**使用规则进本文件**——数据也需要约束，否则 12 个值会互相打架。
将来若出亮色那套变成 24 个值，仍然是数据，只是数据变多了。

### 类别色：图标主导，颜色辅助

12 个类别装不进"避开 5 个状态色相后"剩下的走廊（不到 200°，双向各需留 25°），所以**颜色不承担"12 类都能
分清"的责任**：

- **同类同色同图标；不同类图标必须不同**（`ActionCategoryPresentation.IconFor` 已在给每类专属图标）；
- **颜色只需"相邻类别不同"**；
- 因此现有 12 个值里那 8 个与状态色相近的——`Uia` Δ1、`File` Δ2、`Browser` Δ4、`Control` Δ8、`null` Δ9、
  `Script` Δ9、`System` Δ11、`Input` Δ15——**不必改**：它们撞的是"整天不在同一视野里"的状态色；
- 唯一要改的是**漏用**：类别色不许作正文前景（见上条实例）。

### 附：将来做语法高亮时的唯一来源

现在**没有**语法高亮（公式编辑器整段一色，不按词法着色）。真做时，配色只剩三条安全色相走廊——紫 265–310、
青绿 160–185、黄 50–60，因为 `Accent 13°`、`Warning 30°`、`Success 120–145°`、`Danger 355°`、
`Info 206–211°` 已占满其余色相。可用四支：

| 角色 | 令牌 | 暗（底 `#262626`） | 亮（底 `#FBFAF8`） |
|---|---|---|---|
| 关键字 | `Syntax.Keyword` | `#C586C0` 5.44 | `#7C3FA8` 6.42 |
| 字符串 | `Syntax.String` | `#4EC9B0` 7.43 | `#1B6E63` 5.82 |
| 数字 | `Syntax.Number` | `#D7BA7D` 8.08 | `#7A5A16` 6.10 |
| 注释 | `Syntax.Comment` | `#8FA88F` 5.88 | `#5C6B5C` 5.42 |

函数名用 `TextPrimary` + 600；变量用 `TextPrimary`；运算符用 `TextSecondary`。**再加第 4 个色相必然与某个
状态色同色相**（橙↔`Warning` Δ≤14、绿↔`Success` Δ≤16、蓝↔`Info` Δ≤8），而代码区旁边就摆着状态徽标。

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

裁定："与正文同行的加强" = `UiBodySize` 600 + `TextPrimary`（**不另立类**：`FieldLabel.Strong` 曾是空类，已删）。

> **颜色表达状态，字重表达结构。**

若在密集的函数签名里字重不够，退路是改 `TextBlock.Code`（等宽）——参数名用等宽比用颜色更准确，
**不要退回颜色**。

### 3. 加强标题按"有没有自己的一行"分两类

原先 21 处"加强标题"合并进 `TextPrimary` 后失去强调，但**不新增字号档**：

| 情形 | 类 | 值 |
|---|---|---|
| 独立成行的标题（面板标题、对话框标题） | `TextBlock.Title` | `UiSubtitleSize`(15) 600 · `TextPrimary` |
| 与正文同行的加强（"变量名："这类） | `TextBlock.FieldLabel` + `FontWeight=600` | `UiBodySize`(13) 600 · `TextPrimary` |

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
修饰的那几族（`Border.Card.Selectable`、`Border.Card.Thin`、`ListBox.List ListBoxItem`）。
**没有可叠加修饰的族，答"不同现"即可**，不必每族写一段。

### 窗口级样式不许与库类同名

**同名即覆盖，而且无声。** 窗口的 `<Window.Styles>` 在 `Application.Styles` 之后，所以窗口里写一条
`Border.Section` 会把库里的 `Border.Section` **整个顶掉**——库那版对这个窗口一句话都说不上，
既不报错也不警告。同一个类名在两处有两个定义，读代码的人只会看到其中一处。

`SettingsWindow` 就踩过：窗口里那条 `Border.Section` 用 `AppDock` / padding 18，库版是
`SurfacePanel` / 16，8 个 `Section` 用的是窗口版，而按规范它们该是库版。处置是删掉窗口那条。

判据：**窗口里写选择器之前，先 `grep` 一遍 `Controls.axaml` 有没有同名的类**。有同名的只有两种合法去向：
① 那条其实是**状态**（`Border.Card.Selectable.selected`）——这是「并入不管状态」允许的；
② 库类缺一格度量、而这一窗必须有——那就改库，别让一个窗口偷偷覆盖它。

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
| `Button.Secondary.Block` | Button | 继承 `Secondary`，只加 `Margin="0,0,6,6"`（格与格之间的缝） | 同 `Secondary` |
| `Button.Secondary.Chip` | Button | 继承 `Secondary`，`Height="28"`（**压回 28，`MinHeight` 压不住 `Secondary` 的 30**） | 同 `Secondary` |
| `Button.Thumb` | Button | 24×24 · `Pad0` · `RadiusControl`（**词表预留**：等"可拖动的缩略图/比例手柄"，否则是 `Ghost` 的重复） | `Transparent` · `TextSecondary` · — | `OverlayHover` · `TextPrimary` · — | `OverlayPressed` · — · — | — | — · `TextDisabled` · — | 双色环 | 层 3 |
| `GridSplitter.Split` | GridSplitter | 6px · `SurfaceControl`（**已接上**：`MacroEditorWindow` 三根分隔条挂此类） | `SurfacePanel` · — · — | `AccentFill` · — · — | — | — | — | 单色底 | 层 3 |
| `Button.Ghost` | Button | 24×24 · `Pad0` · `RadiusControl` | `Transparent` · `TextSecondary` · — | `OverlayHover` · `TextPrimary` · — | `OverlayPressed` · `TextPrimary` · — | `AccentSurface` · `AccentText` · — | — · `TextDisabled` · — | 双色环 | 层 3 全写 |
| `Button.Square` | Button | 80×80 · `PadChip` · `RadiusContainer` · **图标 22 居中、无文字** | `SurfacePanel` · — · `BorderSubtle` | `OverlayHover` · — · `BorderSubtle` | 同 hover | — | — | 单色底 | 层 3；**全名走 tooltip** |
| `Border.Row` | Border | 无边框(1px 透明) · `PadRow` · `RadiusControl` | `SurfacePanel` · — · 透明 | `OverlayHover` · — · 透明 | 同 hover | `.Picked`：`AccentSurface` + 左 3px `AccentFill`；`.DropTarget`：边 1px `AccentText` | — | 单色底 | 层 3；**两通道可同时成立** |
| `Button.Pill` | Button | 只改 `CornerRadius=12`（24 高的一半） | 不设（修饰） |
| `TextBlock.Mono` | TextBlock | 只设 `FontFamily=UiCodeFont`，**不设前景** | 不设（修饰） |
| `Button.GroupToggle` | Button | 全宽 · `PadChip` · `RadiusControl` · `UiBodySmallSize` 600 · 左对齐 | `Transparent` · `TextSecondary` | `SurfaceField` · `TextPrimary` | 同 hover | — | — · `TextDisabled` | — | 单色底 | 层 3 |
| `Button.Ghost.Tiny` | Button | 16×16 · `Pad0` · `RadiusControl` | 同 Ghost | `OverlayHover` · `AccentText` · — | `OverlayPressed` · `AccentText` · — | 同 Ghost | — · `TextDisabled` · — | 双色环 | 层 3 |
| `ToggleButton.Chip` | ToggleButton | H28 · `PadChip` · `RadiusControl` · `UiBodySmall` · 1px | `SurfaceControl` · `TextSecondary` · `BorderControl` | `SurfaceControlHover` · `TextPrimary` · `BorderControlHover` | `SurfaceControlPressed` · — · — | checked：`AccentSurface` · `AccentText` · `AccentText` | `SurfaceControl` · `TextDisabled` · `BorderSubtle` | 双色环 | 层 1 四态 + 层 3 覆盖 checked |
| `ToggleButton.Chip.Key` | ToggleButton | 32×32 · `Pad0` · `RadiusControl` · `UiBodySmall` · 1px | `SurfaceField` · `TextPrimary` · `BorderControl` | `SurfaceControlHover` · — · `BorderControlHover` | `AccentFill` · `TextOnAccent` · `AccentFill` | — | — · `TextDisabled` · `BorderSubtle` | 双色环 | 层 3（键位按下要亮） |
| `Button.Icon` | Button | 34×32 · `Pad0` · `RadiusControl` | `SurfaceControl` · `TextPrimary` · — | `SurfaceControlHover` · — · — | `SurfaceControlPressed` · — · — | — | — · `TextDisabled` · — | 双色环 | 层 3 |
| `Button.Key` | Button | 32×32 · `Pad0` · `RadiusControl` · `UiBodySmall` · 1px | `SurfaceField` · `TextPrimary` · `BorderControl` | `SurfaceControlHover` · — · `BorderControlHover` | **`AccentFillPressed`** · `TextOnAccent` · `AccentFillPressed` | — | — · `TextDisabled` · `BorderSubtle` | 双色环 | 层 3。**`ToggleButton.Chip.Key` 的按钮侧对应物**（同尺寸的开关版给结束键用）；按下走按下令牌，不用 `AccentFill`（那是静息强调底） |
| `Button.Key.Small` | Button | 同 `Key`，字号 `UiCaption`(11) | 同 `Key` | 同 | 同 | — | 同 | 双色环 | 层 3。侧边那些长词的键，一格降字号 |
| `Button.Key` + `Fill`（**不建类**） | — | `Width=NaN` + `HorizontalAlignment=Stretch` | — | — | — | — | — | — | **布局属性留元素上**：它表达"这一键撑满所在的格子"，不是外观常项 |
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
| `ListBox.List.Bare.Gapped ListBoxItem` | ListBoxItem | `Pad0` · 下边距 **6**（行距） | 同 `.Bare` | — | — | — | — | 双色环 | 层 3。**行距只能长在行上**：`ListBox` 没有 `Spacing`，`ItemsControl` 的面板不是我们能一致拿到的入口；代价是末行也多 6px |

**行选择器用后代（空格）还是子代（`>`），两种都能用——但别按"视觉树"去推断。**
这一节原先写的是"`ListBoxItem` 的真实位置是 `ListBox > ItemsPresenter > 面板 > ListBoxItem`，
`>` 从 `ListBox` 出发匹配不到任何行"，**这是错的**：Avalonia 的 `>` 看的是**逻辑父级**，
而 `ListBoxItem` 的逻辑父级就是 `ListBox`（容器是 `ItemsControl` 的逻辑子元素，面板只是视觉父级）。

实测（`GlobalVariablesWindow` 的 `FilterList`，一个 `WrapPanel` 作 `ItemsPanel` 的列表）：
`ListBox.FilterList > ListBoxItem` 上写的 `Padding="8,5"` / `Margin="0,0,4,4"` / `CornerRadius="4"`
逐项读到了行上——**规则是生效的**。

这纠正的是一种很危险的推法：按视觉树推断"这条规则匹配不到"，然后把**还活着的样式当死样式删掉**。
要判一条行样式有没有生效，只有两条路：读行上的属性（无头会话可做），或在真机上并排看。

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
| `TextBlock.Hint` | TextBlock | `UiBodySmallSize` · `UiBodySmallLine` · `TextWrapping=Wrap` | `TextTertiary` |
| `TextBlock.Note` | TextBlock | `UiBodySmallSize` · `UiBodySmallLine` · **`TextWrapping=NoWrap`** | `TextTertiary`（单行附注：同一档字号与颜色，与 `Hint` 的差别只是不折行。判据：有句读或超过 20 字用 `Hint`，否则用 `Note`） |
| `TextBlock.Problem` | TextBlock | `UiBodySmallSize` · `UiBodySmallLine` · `TextWrapping=Wrap` | `Warning` |
| `TextBlock.ColumnHeader` | TextBlock | `UiCaptionSize` 600 · `UiCaptionLine` | `TextTertiary` |
| `TextBlock.Caption` | TextBlock | `UiCaptionSize` **400** · `UiCaptionLine` | `TextTertiary`（说明性小字：与 `ColumnHeader` 同字号同色，**差别是字重**——那一类是"列的标题"(600)，这一类是正文重的说明文字；名字要能区分字重，所以叫 `Caption` 不叫 `Small`） |
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
| `ComboBox.Field` | ComboBox | H30 · `PadField` · `RadiusControl` · 1px · `MinWidth=0` | `SurfaceField` · `TextPrimary` · `BorderControl` | **层 1**，层 3 只补 H30 与 `MinWidth`（`MinWidth=0` 是"能缩多窄由父容器说了算"；不给它，Fluent 的下拉最小宽会让窄列撑开） |
| `ComboBox.Field.Cell` | ComboBox | **H24** · `PadCell` · `RadiusControl` · `UiBodySmall` · 1px | `SurfaceField` · `TextPrimary` · `BorderControl` | 层 1 给四态，层 3 只补度量。**与 `TextBox.Field.Cell` 对称**：公式行里两种格子并排，差 6px 看得出来 |
| `AutoCompleteBox.Field` | AutoCompleteBox | H30 · `PadField` · `RadiusControl` · 1px | `SurfaceField` · `TextPrimary` · `BorderControl` | 层 3 + 必须带 `AutoCompleteBox.Field TextBox { Background: Transparent }`，否则双边框 |
| `CheckBox.Field` | CheckBox | `UiBody`(13) · 勾选框几何不动 | `TextPrimary`（禁用 `TextDisabled`） | 层 3：**字段族的成员，不是新字号档**——它与 `TextBox.Field` / `ComboBox.Field` 同排同面板，文字本该同尺寸；之前这一族缺它，是漏了不是选择 |

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

## 缺变体：先判三类，再谈几档

**问"该分几档"之前，先判它是不是档位问题。** 三类处置完全不同：

| 类 | 判据 | 处置 |
|---|---|---|
| **A 承载角色 / 状态** | 用户看不出这个差别时，**会不会把甲当成乙用** | **成对区分，不分档**：给"角色 → 一个尺寸"，并立"**同角色只许一档**" |
| **B 可操作性下限** | 问题是"**能不能稳定抓住**"，不是"分几档" | **写成规则**（如"视觉宽 < 12 时命中区补到 ≥12"），不进尺寸表 |
| **C 纯实现** | 差别只在写法，用户读不出两种意思 | **合并**，不许开新款 |

**准入**：新增一个尺寸或形态之前，先回答它属于哪一类。**答不出来就是没想清楚**——上一轮"8 个灰值"的错，
本质是**把 C 类当 A 类数**（实现漂移被当成"要区分 8 种灰"）。

**族名写角色，不写外形。** 族叫"可点的方块"，下一个人就会去排 S/M/L；叫"对卡的 / 对视图的 / 对内容的"，
他才会去问角色。**名字会诱导分类**——这条是拿代价换来的：`26 / 48×50 / 80` 曾被排成 S/M/L。

**C 类合并之前先量，不许"挤了再报"。** 把最长的那一个取出来，按目标内距算它占多宽；装不下就在落之前定
（放宽内距 / 允许换行 / 减少那一行的项数）。**"能显示"不等于"好看"，把判断推迟到真机，等于推迟到没人负责的时候。**

下面的六档全是**几何**，没有一支新颜色。每档四件事：**要区分什么 / 每档定义 / 落在哪 / 少一档会怎样**——
第四条是它存在的理由，写不出就不用立。

### A · 可点的方块——按**角色**分，不按尺寸排档

- **要区分什么**：这一格**对谁做动作**——对这张卡 / 对整个视图 / 对内容。
- **每档定义**（三个角色，**互不派生**）：
  `Button.Ghost` 24×24（对卡做动作）· `Button.Rail` 48×50（切换视图，图标上文字下）·
  `Button.Rail` 之外的 `.Square` 80×80（挑内容，图标 22 居中、无文字、`RadiusContainer`、**1px `BorderSubtle`**）。
- **落在哪**：`MainWindow` 卡片右上三钮（Ghost）· `ActionPaletteView` 左栏（Rail）· `MenuIconPicker` 图标砖（Square）。
- **少一档会怎样**：图标砖挤进 48×50，22px 图标 + 8 内距放不下视觉重心；反之给 26 也排一档，则把"对卡做动作"
  写成了"小一号内容砖"。
- **为什么必须 1px `BorderSubtle`**：80 方砖若用 `SurfaceCard` 作底，与浅色面板对比只有 **1.07**——**没边就分不开**。

> **同角色只许一档。** 真正要防的不是这三个尺寸，是**第四个**：新增"可点方块"必须先说它是哪个角色，
> 已存在的角色只能用该角色那一档。

### B · 工具栏上的文字钮——**有没有底**

- **要区分什么**：这个文字钮**在主动作区还是框架区**。
- **每档定义**：框架区 = `Button.Chrome`（静息透明、hover `OverlayHover`）；主动作区 = `.Labeled.Filled`
  （H24 · `PadControl` · `RadiusControl` · `UiBody` · 底 `SurfaceControl` · **1px `BorderControl`**）。
- **落在哪**：标题栏四钮（Chrome）· `LabeledToolButton`（Filled）。
- **少一档会怎样**：工具栏上的文字钮只能透明，与标题栏一样，读不出"这里有底、可点"。
- **为什么 1px 边是硬要求**：`SurfaceControl`(亮 `#E4E1DB`) 与窗底 `#FBFAF8` 对比 **1.12**——**少这一笔，
  浅色下这一档就退化成透明的 A 档**。这是唯一一处"少一笔就失效"的档。

### C · 可点的行——**卡还是行**

- **要区分什么**：这一条是浮在面上的卡，还是列表里的一行。
- **每档定义**：`Border.Card`（有 1px `BorderSubtle`，浮起）· `Border.Row`（**无边框** · `PadRow`(8,4) ·
  `RadiusControl` · hover 底 `OverlayHover`）。
- **落在哪**：`MacroEditor` 的卡片（Card）· `LookWindow` 的 `MarkRow` 那类（Row）。
- **少一档会怎样**：无边框行只能用 Card，密集列表出现一片格子线，行与行之间的"同一组"读不出来。
- **两通道都要（不合并）**：`.Picked` 改**底** `AccentSurface` + 左 2px `AccentText`；`.DropTarget` 改**整圈
  1px 边** `AccentText`。**必须能同时渲染**——见下面那条作废。

> **作废（原判：`.picked` 与 `.drop` 是同一概念，合并为 `.Target`）。** 当时判的依据是"语义相近"，
> 而**两个语义相近的状态可以同时存在**——已选的行同时也是可放置的目标——**那它们就需要两个通道**。
> 合并的后果是拖拽时把"可放置"读成"已选"，用户放错位置：**这是误操作，不是观感。**

### D · 可点的分组头——**能不能点**

- **要区分什么**：这个分组标题**能不能点**。
- **每档定义**：不可点 = `TextBlock.GroupHeader`（纯文字）；可点 = `Button.GroupToggle`
  （全宽 · `PadChip` · `RadiusControl` · 透明底 · 左对齐 · `UiBodySmall` 600 · hover `SurfaceField`）。
- **落在哪**：`ActionEditorView` 的分组头。
- **少一档会怎样**：分组头只能借用文字标题，于是不可点、不能折叠。

### E · 等宽与颜色正交——`TextBlock.Mono`

- **要区分什么**：等宽**要不要同时决定颜色**。
- **每档定义**：`TextBlock.Mono` 只设 `FontFamily` = `UiCodeFont`，**不设 `Foreground`**。
- **落在哪**：任何"等宽 + 主色"的地方（上一轮为此手写 `FontFamily` 绕了三次）。
- **少一档会怎样**：每处手写字体名，而 `TextBlock.Code` 把字体和颜色捆在一起——**这就是那三次绕行的来源**。

### F · 窄滚动条——**暂缓**（判据比这一档本身有用）

- **要区分什么**：滚动条要不要窄。
- **每档定义**（待立）：宽 12、拇指视觉宽 8、`RadiusControl`。
- **落在哪**：目前只有 `VariableWall` 一处 `/template/ ScrollBar#PART_VerticalScrollBar` 局部覆盖。
- **为什么暂缓**：**只有一个使用者时，它就是例外。** 等第二个窗口要窄条时再立——一个令牌只有一个使用者，
  只会多一个要解释的名字。
- **顺带一条 B 类规则**：**视觉宽 ≠ 命中宽。** 视觉 < 12px 的可交互元素，命中区必须补到 **≥12×12**
  （做法：条宽 12、拇指视觉 8，差的 4px 是透明命中区；不要反过来把整条压到 8）。
  判据：**能不能不犹豫地按下**——需要瞄准就已经失败了。

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

    **改文档时还要报"锚点"**：改的是哪一节（`## 字号、间距、圆角` 这类），因为**锚点就是"我没覆盖别处"的
    证据**——只说"我加了几行"，别人无法判断那几行插在哪里；有锚点，他才敢信"只加没删"。
    **引用位置也要核**：说"某文件某行""某份文档某节"之前，确认它**现在还在那儿**。
    实例（同为我的）：说"`#10` 在 `ui-style.md`"——它在报告 §5；另一处是把旧 `git status` 当现状。
    **指认位置的成本很低（一次 `grep` / 一次 `ls`），错了的代价是别人去改一个不存在的东西。**

    这三样其实是同一个根：**我引用的是"记忆里的那个"，还是"它现在的那个"。** 数与位置都一样。
    反例三（缺锚点）：文件行数从 630 变成 853 是别人改的、我只加了我的部分，但报"913 行"时若不说锚点，
    读的人会以为我丢了 200 行。

15. **结论也要带动作。** 写"我查过 X"，不写"X 是"；查的动作要能复述——`grep 了什么`、读了哪个文件的
    哪一段。**判据：把这句话交给别人，他能不能重做一遍那个动作？不能，那就只是"我认为"。**

    14 条管**数**，这一条管**结论**：数缺维度无法核，各说各的；结论缺动作无法复述，只能信或不信。
    实例：本轮"这套界面有语法高亮"——查的是"有 11 个值跨 9 个窗"，**数字是真的，结论是编的**。
    正确的说法是"我查过有 11 个颜色值跨 9 个窗，**看起来像**语法高亮，但没有任何词法着色代码"。
    **查颜色/查类名时先问"用得最多的是哪一处"**，不是"我第一次看到的是哪一处"：`#8FB3D9` 我第一次看到的是
    `FormulaMark`（`Style` setter），据此判成"来源标记"；查了四处才发现主用法是**标识符**（`StepId`/`Name`/
    `MetaSummary`），`FormulaMark` 只是**借**它的色。**主客一颠倒，落的令牌就错一整族。**

    **动作要做到什么程度，由处数决定**——"带动作"如果不带深度，动作就会停在第一处：

    | 处数 | 判它的方法 |
    |---|---|
    | **1 处** | 读那一处就够 |
    | **2–4 处** | **全读**：可能不是同一件事（`#C8A165` 4 处就是 4 种语义） |
    | **5 处以上** | 先按"用得最多"分组，再逐组读代表 |

16. **档数以"要区分什么"为准，不以"有几种写法"为准。** 数写法会得出"8 个灰值 = 8 档"，而它们其实只有
    **3 级**；运行位置族 4 套写法，其实只有 **2 件事**。做法（处数怎么数、档数怎么问）写在 `dev.md`
    的「静默失效 · 第 19 条」，这里只留这条判定。

17. **规则要对它适用的第一个案例生效。** 刚写完一条规则时遇到的那个案例，最容易被"**我记得这条**"代替
    "**我用了这条**"——写规则时你正想着它，于是以为想着就等于在用。

    实例（我自己的）：写完"会同现的两个状态不能共用通道"之后，紧接着判 `.picked`/`.drop` 时说"两者是同一
    概念，合并"——**判据用的是"同一目的"，漏了"会不会同时出现"**，而那正是我刚写下那条规则要问的。
    后果不小：合并后拖拽时"可放置"被读成"已选"，用户放错位置（**误操作，不是观感**）。
    判据：**规则落地后的第一件事，是拿它检查手上正在做的那个决定**，不是去找"以后适用的地方"。

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
3. **扫元素上的 `Height` / `Width`**：只记录，**一处都不删**。判据三条——同值出现在三个以上窗口 =
   该建类，进"缺变体"清单；只出现在一两处 = 例外，留在元素上；与类自带值相同 = 冗余，可以零变化删
   （这一类很少：全仓 220 处里只有 30 处）。删非冗余的那些等于把尺寸掉回类的默认值，那是**二十二次
   临时改设计**，不是一次设计决定。
4. **查"用了没定义"与名字本身**：`grep` 类名本身（含测试里的 `Classes.Contains("…")`），
   再看扫出来的类在本表里有没有定义——"挂着类但从未样式化"和"样式化但没人挂"是同一个洞的两面。
5. **换类 / 删样式**：只换 `Classes`，布局属性留在元素上（见上一条）。
6. **跑** `dotnet build` + `dotnet test` + `dotnet format --verify-no-changes`，三条都绿才算迁完。
7. **记账**：删了几条本地样式（列键名）、换了几处 `Classes`、`App*` 与裸色值各减多少、行数变化、
   还剩什么（没对应物的类，逐个列名）。**还剩什么是这张账的重点**：它直接是下一步要补的词汇。
   **报"有 N 处"之前必须有计数来源**——命令输出或文件行号，二者之一。没有就说"我没数，是印象"。
   印象在报账里没有位置：一个凭感觉写的处数会让下一个人按错的分批去做，而错的分批比错的值更难发现
   （`MacroEditorWindow` 那次"12 处标签上下文"实际是 0 处，就是凭印象写的）。

**已完成（可当参照）**：`ConfirmDialog`、`TextPromptDialog`。两窗都是 0 条本地样式、0 处 `App*`、
0 处裸色值——`TextPromptDialog` 与本清单同时落地，走的正是上面七步，没有用到第八种判断。

### 存量清单：缺变体与手调痕迹（报告，不进 CI）

这两张单子只用来在收尾时做一次设计决定，不卡任何一次提交。数口径：`Views/` 下 26 个 axaml，
元素级属性（剥掉 `<Style>` 块之后再数），图元（`Image` / `Ellipse` / `Path` / `IconView`）不计。

**缺变体。** 判据是"同值出现在三个以上窗口"，但真正读出来的东西比判据本身重要：
`Button.Secondary` 一个类被用在**九个**不同高度上，125 处里只有 24 处等于它自带的 30。

| `Secondary` 上的 `Height` | 24 | 26 | 28 | **30** | 32 | **33** | 34 | 36 | 38 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 处数 | 1 | 4 | 15 | 24 | 21 | 41 | 10 | 7 | 2 |
| 窗口数 | 1 | 3 | 4 | 7 | 5 | 3 | 4 | 6 | 1 |

**33 与 32 两档占了 62 处**，其余是漂移。裁定（定了，不再逐窗讨论）：

| 现值 | 处数 | 处置 | 理由 |
|---|---:|---|---|
| **30** | 24 | **保留** | 类自带的值 |
| **28** | 15 | **保留** | 紧凑行那一档 |
| **32** | 21 | **回落 30** | 差 2px 肉眼不可辨；真需要 32 的那一处该是 `Primary` 或新变体，逐处报 |
| **33** | 41 | **回落 30** | 旧输入框高度（34/33 那批）的遗留，而输入框现在是 30 |
| **36** | 7 | **回落 30** | 除非它与 `Primary`(32) 并排——那一处单独报 |
| 24 / 26 / 34 / 38 | 7 | 回落最近一档 | 没有语义 |

**只有 28 / 30 / 32 三档有语义**，其余六档是漂移。所以收尾要补的是**至多一个紧凑行变体**，
不是九个；其余按上表回落。

执行顺序也是定好的：**先做那 30 处零变化删除**（值与类自带相同，删了不变样），其余 190 处按上表回落，
**每一处都要在提交信息里写明"改的是设计值"**——不说这句，下一个人会以为这里曾经有个 bug 被修过。

`MinWidth` 只有一个值得说：`92` 出现在 11 个窗口 16 处，是对话框次按钮的最小宽（防止两个字与
四个字的按钮并排时宽窄不一）。它已经是常项，只是没进类——**记录，收尾时与体验设计一起定**。

**字号也收敛，方向不是"取整"，是"各自有归属"。** 元素级字号全仓 234 处、12 个值，词汇表里只有
11 / 12 / 13（加 15 副标题）。漂移的四个值这样落：

| 旧值 | 处数 | 落哪 | 为什么是这一档 |
|---|---:|---|---|
| **11.5** | 48 | **12** = `UiBodySmallSize` | 小字档。`TextBlock.Hint` 早就定成 12，11.5 是它的漂移 |
| **12.5** | 59 | **13** = `UiBodySize` | 正文档。**12.5 是这套界面原本的真实正文尺寸**，正文定成 13 就是为了收它 |
| **10.5** | 10 | **11** = `UiCaptionSize` | 最小档，词表没有更小的 |
| **13.5** | 1 | **13** | 单处、不落在任何档的一半位置，按笔误处理 |

**这不是"选个方向"，是两个漂移值各有归属**：向下取整会让正文变成 12，与 `Hint` 同尺寸，
正文与提示之间的层级就没了。也正因为如此，"按用途分"（正文向上、标注向下）这个看起来更聪明的方案
不可取——它要求每个窗口重新判一次"这处算正文还是标注"，而 12.5 那 59 处里近一半是行内小字，
按用途分会在一行里同时出现 12 与 13，看着像没对齐。**一条线切到底比分类稳，因为分类会各窗不一。**

执行口径要分开报，两处修法不同：

- **元素级字号**（`FontSize=` 写在元素上）→ 换类，不是换令牌；
- **样式里的字号**（本地 `<Style>` 里）→ 换令牌。这一条在 `MacroEditorWindow` 已经归零。

**换类时判的是"位置与容器"，不是"这句话是什么"。** 这正是"按用途分"那条路走不通的地方：
它要求读懂文案，而位置和容器是可以从代码里看出来的。判据只有四问：

| 问题 | 看什么 | 落哪 |
|---|---|---|
| 需要下边距吗（标签与它管的控件之间） | 元素原本有没有下 `Margin`，或紧跟着一个控件 | `FieldLabel`（13，带 `GapField`） |
| 不需要下边距 | — | `Body`（13，无下边距） |
| 允许折行吗 | 容器会不会被折行撑高（成句的） | `Hint`（12，`Wrap`） |
| 不允许 | 固定高度的行 | `Note`（12，`NoWrap`） |
| 11px | — | `Caption`（11，400） |

按落点分批做，批内是同一个判断：`10.5` 那一批只有 `Caption` 一个去处，属完全机械；然后是"标签上下文"
（`FieldLabel`）与"行内"（`Note` / `Hint`）。**批内不逐处想，这才是不选"按用途分"之后仍能推进的原因。**

**值的去处由位置定，不由值定。** 上面那张表容易读成"12.5 一律落 13"，那不是它的意思：
**12.5 这个值本身没有固定去处**，同样是 12.5，是标签就落 `FieldLabel`(13)、是行内小字就落
`Note`/`Hint`(12)。"12.5 是这套界面原本的正文尺寸"只是**解释为什么正文定成 13 而不是 12**，
它不是一个规则。`MacroEditorWindow` 实测就是这两种都有——所以这一条不写清，下一个人会拿"12.5→13"
去套行内小字，把单位、计数这类字放大一号。

**行距也是同一个道理：不为 2px 的差开一档。** `MacroEditorWindow` 里两个列表的行距是 4 与 6，
都是"行距"，差别是漂移不是意图，并成 `List.Bare.Gapped` 的 6；要保留 4 就得开 `Gapped.Tight`，
为一个 2px 差开一档不值——这和"不为长度为一的令牌开名字"是同一条。

**小窗扫出来的第二批候选（尺寸不在三档、库类给不了的）**。小窗那 8 个文件做完时收的，
每一处都"只有一个用处"，所以留本窗，但尺寸本身说明词表缺档：

| 位置 | 尺寸 | 说明 |
|---|---|---|
| `ActionPaletteView` 的 `Button.Category` | 48×50 · 11 / 600 | **带固定尺寸的网格类别钮**，`Tile` 是 64 高、`Icon` 是 34×32，都不对；按钮上的"小字半粗"也没有对应物 |
| `VirtualGamepadWindow` 的 `Button.Control` | `MinWidth=40` | 窄键要能看清字；宽度没有档 |
| `HotstringKeyboardWindow` 的 `Button.Secondary` | `Height=36` · `MinWidth` 104/80 | 字符键比标准钮高一档 |
| `VariableWall` 的 `VarWallScroll` | 滚动条宽 8 | 库没有"窄滚动条"这一档 |
| `VariableWall` 的芯片行 | `Padding="4,2"` · `Margin="0,0,3,3"` | 五组名字挤一行，`List` 的 H32 + `PadRow` 放不下 |
| `GlobalVariablesWindow` 的 `List > ListBoxItem` | 底边 1px 分隔线 | 行分隔线"底边独占"，`List` 给不了 |
| `LookWindow` 的 `Border.MarkRow` | 底 `AppDock` · 透明占位边 1px | `Card.Thin` 是 `SurfaceCard` + 实边；并过去会把无边框行变成有边框 |

**主窗扫出来的第三批（`MainWindow`，四条都留本窗）**。这四条的价值不在"留下"，在**写清它们缺的是什么**——
等"缺变体一次定"那轮，这些描述就是判据；只写"和库里的不一样"等于没写。

| 类 | 缺的是什么 |
|---|---|
| `Button.ToolButton` | **圆形图标钮**：24×24 与 `Ghost` 同尺寸，但**圆角 12（正圆）**、悬停 `AppBorder`。**圆角是形状不是值**——它不是 `Ghost` 的另一档 |
| `Button.LabeledToolButton` | **工具栏上的带底文字钮**：H24 与 `Chrome.Labeled` 一致，但那个是标题栏用的（透明，叠在标题栏上），这个要有底（`AppControl`）。**两个位置，两种底** |
| `Button.BottomButton` | **底部条专用钮**：`Padding="14,10"` 没有档，而它撑起了底部条的高度——改内距就是改布局 |
| `Button.CardButton` | **26 方钮**：26×26 不在任何档上（`Ghost` 24、`Icon` 34×32） |

**表达式编辑器扫出来的（第四批）**：

| 位置 | 缺的是什么 |
|---|---|
| `ExpressionBuilderWindow` 的 `Border.Row` + `.picked` / `.drop` | **双通道行**：`.picked`（已经选进这批）走 **Info** 通道、`.drop`（正拖到这上面）走 **Accent** 通道。**这是两个状态，不是一个状态的两个色**——库里 `Card.Thin` 只有 `chosen` 一档，并过去会丢掉其中一个 |
| `TextBox.ExprInput` / `TextBox.Filter` | **公式输入框的底**：现在用 `SurfaceWindow`（"底色"），而 `TextBox.Field` 用 `SurfaceField`（"字段底"）。两个角色，等灰阶/底色那轮定 |

### 五组"待定"的现状（**押后，不是忘了**）

区别在于有没有这张表：**"押后"与"忘了"在进度表里长得一样**。每组都写清"为什么现在不定"。

| 组 | 值数 | 用处数 | 影响窗 | 现状 |
|---|---:|---:|---:|---|
| **类别色**（原以为的 `Syntax`） | 12 | 35 | 9 | **不立令牌**：它是**数据**（`ActionCategoryPresentation.cs` 的一个 `switch` 十二支），与主题色样同类。**一处不改值** |
| 灰阶 | 8 | 13 | 5 | **押后**：值数 8 > 用处数 13 —— **值比用处多，说明不成档**。要定的是"这套界面需要几级灰"，已答**四级文字灰 + 一级线**，等做那轮 |
| 运行位置 | 6 | 14 | 5 | **规则已立、可做**：见下 |
| 录音 | 11 | 16 | 2 | **押后**：要定的是"录音要不要成为一种界面表态"（红板 + 脉冲 + 停止块），那是产品问题 |
| 药丸底 | 3 | 3 | 1 | **已做**：不是立族，是接上现成的 `Badge`（三个 `<Border>` 换类） |

**原来以为的 `Syntax` 组散了，因为前提错了**：这套界面**没有语法高亮**——公式编辑器不按词法着色，
整段一个颜色。那 11 个值其实是两族别的东西：**动作类别色 12 个** + **几处强调/状态文字**。
**没有那个功能，就没有那组令牌。**

**类别色的三条规矩**（它不进令牌，但要有约束）：

1. **同类同色同图标；不同类图标必须不同；颜色只要"相邻类别不同"即可**——**图标主导，颜色辅助**；
2. 12 个里 **8 个与状态色相撞，本轮一个都不改**：它们撞的是"整天不在同一视野里"的状态色；
   而 12 个类别色装不进剩下的 3 条色相走廊，改完会互相挤；
3. **类别色只做小面积**（图标描边 + 12px 标签），且必须与状态色差 ≥25° 色相或 ≥20% 饱和度。
   违反"面积"的那一处（类别色漏进正文）已修——**正文是大面积，一上去就把"这段是脚本"改成了"这里有警告"**。

**运行位置族已立的规则**：**"当前位"只许改底色（`WarningSurface`）与左边条，不许改前景色**——
它标记的是"位置"，不是"这段文字是什么"。

**灰阶那轮已有的答案**：**四级文字灰（正文 / 次要 / 注释 / 禁用）+ 一级线**，第 5 级指不出对象。
判据一句话：**说不出它属于哪一级的灰，就是多余的灰。**

### 同一个色相，同一个视野，只能有一种含义

判据：**把该视野里所有带色元素列出来；同一色相出现两次且含义不同 → 违规。**
**修复顺序：先改面积小的那个**——面积小的更容易被读成"另一个含义"，因为人先认大面积的主色。

这条和"类别色图标主导"是一对：**前者防冲突，后者防挤不下。**

**"布局常量"这条线（与"缺变体"分开）**：判据是**"构件要不要多一档"（缺变体）vs "这一处该怎么摆"（布局常量）**，
后者按"≥3 窗同值即升令牌"处理。已记的候选：

| 位置 | 值 | 状态 |
|---|---|---|
| 标题栏左右内缩 | `12,0` / `8,0` | 16 / 17 个窗口，够"≥3 窗"，但**加一类"行"是词汇表层面的决定**，等收尾 |
| `MenuMacroPicker` 的搜索框高度 | `36` | **仅一处，观察**——不进令牌，记着它比标准字段高一档 |

**手调痕迹。** 199 处带左右偏移的 `Margin`，集中在 `MacroEditorWindow`(42)、`ActionEditorView`(41)、
`SettingsWindow`(28)、`RunWindow`(25)。其中 `2,12,0,6`(11)、`2,5,0,0`(10)、`2,8,0,0`(9)、`2,4,0,0`(8)
是典型的"往右让 2px 才对齐"——**不判违规**：它们在表达这一行里某个元素比旁边高半个字、要让 2px 才
对得齐，那是布局，不是间距。登记它们的唯一目的是：迁到那一行时逐个问一句"它在修什么"——答得出来
就留着，答不出来就删。

`8,0,0,0`（18 处 / 6 窗）不属于手调，它是**缩进**：同一列里某一块比别的块靠右 8。垂直栈给不了
单个孩子的缩进，所以它只能留在元素上；六个窗口同值说明它是个常项，值得一个名字（`Indent*`），
**但那是新造词，等词汇表裁定，本轮不建**。

**"留本窗"的前提是"有消费者、且库没有对应物"。** 没有消费者的本地样式**不是例外，是死样式**——
不管它名字多合理、注释多完整。这条是 `.Active` 那两条教出来的：team-lead 先裁"留本窗"，
扫描一跑发现零消费者，而且旁边注释自己写着 *"No Active state: there is no server in this window"*
——**名字存在与它被用到，是两件事**。（判据的收紧来自一次错裁，不是来自一次讨论。）

**已裁定的例外（留窗口里，不是漏网）**。这几处都"没有第二处需求"，所以不进词汇表，但要有账。
（原先还有一条"11px/400 的尾标"——它已经升成 `TextBlock.Caption`，不在例外里了。）

| 位置 | 是什么 | 为什么留 |
|---|---|---|
| `MacroEditorWindow` 的 `Button.RecordButton` | 录音红板（六个红棕）+ "录制中"的脉冲动画 | **动画表"进行中"**，词汇表里没有这种表达；要立族得先有第二处需要（比如"录制屏幕""采样中"） |
| `MacroEditorWindow` 的 `Border.DropTarget` | 半透明落点 + `.Rejected` | 细边框落点标记，半透明是为了看见底下的内容——与 `Card.Target` 的"整卡接管"是两种表达，见 `Controls.axaml` 里那句注释 |
| `MacroEditorWindow` 的 `Border.StepHead` / `StepFoot` | 块的"头/尾"两条线，`StepFoot` 是**左 2px 竖线** | `Border` 四边共用一支笔，左条画不出来——这是已知限制，不是没想到（要画就得加元素，属结构改动） |
| `MacroEditorWindow` 的命令入口 `Width="44"` | 比同级图标块宽 10 | 高度能收敛到 28/30/32，宽度没有对应的档；这是"这一格更要醒目"的存量度量 |

**标题栏的 `12,0` 与 `8,0`。** 分别出现在 16 与 17 个窗口，形态完全一致（`StackPanel … Margin="12,0"`
带着鲸鱼图标与标题、右侧按钮组 `Margin="8,0"`）。**它们不是兄弟之间的间隙**，是这一行的左右内缩，
按规则 1 归父容器的 `Padding`。16 个窗口一字不差，说明标题栏是"一族"而不是"每窗布局"——
但词汇表里只有控件类、没有"行"类，**加一类"行"是词汇表层面的决定，不是迁移能定的**。
所以：**就地改成 `Padding`，"标题栏行"记进缺变体清单**，收尾时与体验设计一起定。
在那之前不动那一族：就地改成 `Padding` 是本轮的事，升成类不是。

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
