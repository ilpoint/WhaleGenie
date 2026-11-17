# WhaleGenie 视觉契约

本文件是 WhaleGenie（鲸灵）全部界面的视觉契约。它回答“应该长什么样、什么算完成”，不记录实现
历史，也不复制令牌值。实现结构与资源分层见 [docs/architecture/interface.md](docs/architecture/interface.md)，
具体颜色、字号、间距与圆角值以 [Tokens.axaml](WhaleGenie/Themes/Tokens.axaml) 为准；自定义强调色由
`ThemePalette` 在运行时派生并覆盖这组强调令牌，是唯一的例外。

## 1. 契约与优先级

- **视觉意图与规则以本文件为准**；值以 `WhaleGenie/Themes/` 下的令牌与样式为准；实现结构以架构文档为准。
- 优先级从高到低：本文件的规则 → 项目令牌与控件样式 → FluentAvalonia/Avalonia 的默认外观。
- 库的默认值是底座，不是契约。库默认与项目令牌冲突时，修项目实现，不用库默认覆盖契约。
- `avalonia-docs` MCP 只解释 Avalonia/FluentAvalonia 的 API、控件和属性；它不能替代本文件，也不能
  成为新增颜色、字号或间距的依据。
- 视觉决定、令牌、控件样式与验收测试在同一轮完成；只改 XAML 或只改文档都不算完成。

## 2. 视觉基调

- Windows 桌面工具，游戏与办公同等重要：信息密度优先于装饰，扫读优先于表演。
- 中性表面承载内容，**只有一个强调色家族**；成功、警告、危险、信息只用于表达状态，不用于装饰。
- 深色是默认体验，浅色必须同等可读；自定义强调色是同一套语义令牌的替换，不是第二套主题。
- 中文与英文界面共用同一套度量；文字变长时优先换行或增高，不挤压控件、不裁字。
- 图标、圆角、边框和叠层服从既有令牌与 Fluent 控件族；不引入第二套图标语言或装饰性渐变。

## 3. 资源层级

| 层 | 文件 | 契约 |
| --- | --- | --- |
| 语义令牌 | `Tokens.axaml` | 颜色、字体、字号、间距、内边距、圆角的默认值只有这一处；自定义强调色运行时覆盖 |
| Fluent 资源键 | `FluentKeys.axaml` | 只把 Fluent 模板会读取的键映射到项目令牌 |
| 控件类与模板 | `Controls.axaml` | 只补 Fluent 缺的模板、状态与项目控件类 |

- 不在窗口、视图或局部样式里另建颜色体系；不用十六进制，也不用 `Brushes.*` 表示项目颜色
  （`Transparent` 这类无值常量除外），这些都必须走令牌。
- 新的 Fluent 键必须核对当前库版本里真实存在；写错键不会报错，只会静默退回库默认。
- 能由 `FluentKeys.axaml` 改掉的，不写进 `Controls.axaml`；层 3 只处理层 1 表达不了的部分。
- 资源键和样式类改名时，同步检索 XAML、代码与测试中的旧引用。

## 4. 颜色角色

- **表面**：`SurfaceWindow`、`SurfaceChrome`、`SurfaceSunken`、`SurfacePanel`、`SurfaceField`、
  `SurfaceCard`、`SurfaceFlyout`、`SurfaceHover`、`SurfacePressed`、`SurfaceControl` 及其
  悬停/按下变体。层级用表面区分，不靠阴影堆叠。
- **文字**：`TextPrimary`、`TextSecondary`、`TextTertiary`、`TextDisabled`、`TextOnAccent`。
  正文用 Primary，说明与次要信息用 Secondary/Tertiary，禁用态才用 Disabled。
- **边框与焦点**：`BorderSubtle`、`BorderControl`、`BorderControlHover`、`BorderStrong`、
  `FocusRing`。焦点必须可见，不能只靠悬停色暗示。
- **强调**：`AccentFill` 家族（Fill/Hover/Pressed/Text/Surface）。一个界面只有一个强调色家族；
  自定义强调色必须通过 `ThemePalette` 的对比度底线，不能为了“好看”牺牲可读性。
- **状态**：`Success`、`Warning`、`Danger`、`Info` 及对应 Surface。状态色用于状态，不用于装饰；
  紧凑状态点（如宏卡片圆点）必须在其操作入口或提示里给出等价文字，新增状态不要只靠颜色。
- **叠层**：`OverlayHover`、`OverlayPressed` 用于任意表面上的半透明交互反馈；不要为“不画”
  新建令牌。
- **拾取器**：框选、取色、点、元素、窗口规则五个窗口的遮罩令牌不随明暗变化；它们读取的是屏幕像素，
  不能因为主题切换而改变读数。

## 5. 字体与密度

- 正文使用 `UiFont`；代码、表达式和需要等宽的内容使用 `UiCodeFont`，不用等宽字体做普通界面字。
- 字号/行高只使用 `UiCaption`、`UiBodySmall`、`UiBody`、`UiSubtitle`、`UiTitle`、`UiDisplay`
  这组角色（每档对应 `*Size` / `*Line` 两个令牌）；不新增“差不多大”的字号档。
- 密度沿用 `FluentAvaloniaTheme` 的紧凑基线；表格行、列表项、菜单项随库密度走，不手写另一套行高。
- 行高写下限（`MinHeight`），不写定高；两行文字、徽标或换行必须能撑开行而不被裁切。
- 数字输入、计数和需要纵向对齐的表格使用固定列宽并右对齐；不靠空格对齐。

## 6. 间距、内边距与圆角

- 间距使用 `Space1` 至 `Space8`；`StackPanel.Spacing` 使用 `StackGap`（它是 `Double`，
  不能拿 `Thickness` 的 `Space*` 代替）。
- 内边距使用 `Pad*` 族：`PadControl`、`PadField`、`PadCell`、`PadChip`、`PadRow`、
  `PadSection` 等按用途命名，不按数字命名。
- `Gap*` 只用于没有父容器能提供间距的少数位置；新增 `Gap*` 前先说明为什么 `Spacing` 做不到。
- 圆角只用 `RadiusControl`、`RadiusContainer`、`RadiusOverlay` 三个角色。
- 列宽、窗口尺寸、图标尺寸等结构尺寸可以单独写；同一模式重复出现时抽成令牌或共享常量，不散落魔法数。

## 7. 状态与交互

- 每个可交互控件至少要有静息、悬停、按下、禁用和焦点五种可读状态；焦点态必须能在两种主题下看见。
- 状态类遵守 `Controls.axaml` 头部的 `STATE` / `VARIANT` / `LAYER1` / `STATIC` 登记：
  不发明新的状态词，新类要么自己画悬停态，要么登记为由层 1 绘制。
- `selected` 表示“当前选中”，`chosen` 表示“已经选好”，`problem` 表示“这里有问题”；
  同名概念只用一个词。
- 禁用不是“把文字调淡就算了”：背景、边框与文字一起退后，但仍要能读出控件是什么。
- 动效只用于解释状态变化（展开、弹出、切换）；不做与操作无关的循环装饰，不遮挡焦点和内容。

## 8. 窗口与浮层

- 普通窗口统一继承 `AppWindow`（`FAAppWindow`）；内容伸入标题栏的窗口统一调用
  `WindowChrome.Attach(this)`，由它画鲸鱼、标题并留出系统按钮宽度，不在窗口里自搭标题栏。
- 主窗口、宏编辑器、动作编辑器、运行窗把内容扩展到标题栏；其他普通窗口使用库默认标题栏。
- 设置、宏编辑器、运行窗是并列窗口，不是主窗的模态附属；拾取器打开期间按既定规则收起鲸灵窗口，
  结束后恢复。
- 框选、取色、点与元素拾取、屏幕框、菜单浮窗和浮动面板使用无标题栏覆盖层；覆盖层不为了“统一”
  强行套普通窗口框架。
- 不重新引入 `<FluentTheme/>`：`ContextMenu` 等缺模板的控件由 `Controls.axaml` 补齐；删掉那份模板
  的结果是菜单能打开但屏幕上什么都不画。

## 9. 主题与可访问性

- 深色与浅色都必须通过同一组语义令牌；主题相关值使用 `DynamicResource` 或主题画笔，快照只用于检查。
- 文字与背景的对比度、强调色文字与窗口的对比度按 `ThemePalette` 的底线执行；自定义强调色不满足
  底线时必须拒绝或回退，不能静默换成另一个颜色。
- 键盘焦点、禁用态、错误态和选中态不能只靠颜色区分；图标、边框、文本至少再给一个信号。
- 用户可见文字统一来自 `Strings` / `ActionStrings`，不在 XAML 或代码里写死。
- 100% 与 150% DPI 下检查裁字、错位和点击目标；窗口最小尺寸下允许内容滚动，不允许控件互相覆盖。

## 10. 视觉验收

自动化检查（无头）：

| 检查 | 用例 |
| --- | --- |
| 状态词表与登记表一致 | `StyleVocabularyTests` |
| 每个类画悬停或登记为层 1 | `ControlsRegistryTests` |
| 右键菜单有主题、有尺寸和 `ItemsPresenter` | `MenuThemeTests` |
| 模板部件存在，颜色/状态读取实际部件 | `TemplatePartTests` |
| 普通窗口统一走 `WindowChrome` | `WindowChromeTests` |
| 强调色派生与对比度底线 | `ThemePaletteTests` |

人工检查：

- 深色与浅色各走一遍受影响窗口；100% 与 150% DPI 至少各看一次。
- 检查标题栏、窗口按钮、焦点环、禁用态、错误态、长中文/长英文换行和最小窗口尺寸。
- 出图优先用 Debug 自省通道截运行中的窗口；手动清单见 [docs/dev/validation.md](docs/dev/validation.md)。

## 11. 变更流程

1. 先判断是**已有角色的新值**还是**新角色**。新值只改 `Tokens.axaml`，不复制到本文件。
2. 新角色先补本文件的角色与用途，再改 `Tokens.axaml`；需要 Fluent 键时再改 `FluentKeys.axaml`，
   最后才改 `Controls.axaml` 与视图。
3. 视觉规则变化必须同轮更新本文件；不要把设计决定藏进某个窗口的局部样式。
4. 改类名、资源键或选择器后，检索旧引用并运行第 10 节的检查与全量测试。
5. 需要查 Avalonia/FluentAvalonia API 时使用仓库根 `.mcp.json` 中的 `avalonia-docs`；查到的用法
   必须落回项目令牌与现有控件类，不能直接粘贴库默认样式。

## 12. 快速检查清单

- [ ] 颜色、字号、间距、圆角、状态色都来自 `Themes/` 令牌，没有局部十六进制。
- [ ] 没有新增未登记的样式类或状态词。
- [ ] 深色与浅色都看过，焦点、禁用、错误态都能读出来。
- [ ] 长中文/长英文、最小窗口、150% DPI 没有裁字或重叠。
- [ ] 窗口仍走 `AppWindow` / `WindowChrome`，没有自搭标题栏或第二套主题。
- [ ] 视觉规则、令牌、控件样式、测试和 `DESIGN.md` 在同一轮完成。
