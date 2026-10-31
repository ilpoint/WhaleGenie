# 交接：FluentAvalonia 迁移的剩余部分

## 这是什么项目

`WhaleGenie`（鲸灵）：Windows 上的鼠标键盘宏工具，Avalonia 12.1.2 / .NET 10 / MVVM。
界面三层：Avalonia 控件 → FluentAvalonia 的主题（`FluentAvaloniaTheme`）→ 我们自己的令牌与样式
（`WhaleGenie/Themes/Tokens.axaml`、`FluentKeys.axaml`、`Controls.axaml`）。

**界面语言的规矩**：颜色、度量、状态归我们，模板与控件行为归库。不要因为库有新控件就搬外观过来。

## 现在到哪了（截至 `37b4727`）

已完成：主题换成 `FluentAvaloniaTheme`；43 条按钮颜色样式从 `/template/` 移到控件；裸按钮三态；
列表项 `MinWidth=0`；右键菜单 DataContext 补齐；`TextFillColor*`、`PivotHeaderItemSelectedPipeFill`、
`SystemAccentColor` 一族键映射；设置窗三个根页换 `TabControl`；动作编辑器四个页 → `TabStrip`；
数字框换 `FANumberBox`（删了 103 行模板手术）；死样式清理；测试隔离与偶发失败定位；
以及 T1（关窗发问一律作答）与 T2（剩下 16 扇窗逐个出图，修掉自动补全字段被裁的边框，见下）。

现状验证（三条都应通过）：

```bash
cd G:/DATA/MyAPP/Viktor
dotnet build                                    # 0 错误 0 警告
dotnet test                                     # 34 + 1460 + 1488 通过，1 跳过
dotnet format --verify-no-changes               # 退出码 0
```

## 硬约束（不要试图绕过）

**窗口不能用 `FAAppWindow`。** 它的构造里 `Win32WindowManager` 以 `HWND` 为键建静态表，而 headless
测试里每个窗口句柄都是 `0`，第二个就抛：

    ArgumentException: An item with the same key has already been added. Key: HWND

关掉第一个也不移除表项。所以 24 个窗口继续用普通 `Window` + 自绘 38px 标题行。
要解掉它必须先满足其一：无头平台给每个窗口不同句柄，或者库改成按窗口实例建表。**在那之前别碰。**

这条已经复核过，结论不变（Avalonia 12.1.2 + FluentAvaloniaUI 3.1.0）：库里是
`static Dictionary<HWND, Win32WindowManager>`，键取 `_window.TryGetPlatformHandle().Handle`，
构造函数里 `Add`，只有收到 `WM_DESTROY` 才 `Remove`。无头会话里实测两个 `FAAppWindow`：

    first handle=0 | second threw ArgumentException:
    An item with the same key has already been added. Key: FluentAvalonia.Interop.Win32.HWND

## 剩余任务

### T1（必做）扫掉"关窗发问"的测试隐患 —— 已完成

**背景**：`ShowDialog` 会在当前 dispatch 内部再起一个调度器循环。用例关了会发问的窗口却不回答，
循环就留在调度器上，**下一条用例**一 dispatch 就炸：

    InvalidProgramException: You've caused dispatcher loop  (Dispatcher.ResetForUnitTests)

症状是随机受害者 + 单条耗时 5～11 秒。

**做法**：全部会发问的关窗点改走 `Ui.CloseAndAnswer(window)`（关窗后泵一轮、把该窗自己的
`ConfirmDialog` 答掉），另有 `Ui.AnswerUnsavedWork(window)` 用于只答提示、不关窗的场合。改过的文件：
`HotstringKeyboardTests`、`MacroEditorWorkspaceTests`、`MacroEditorChromeTests`、`PickerDeviceTests`、
`StepPlacementTests`、`StepTreeTests`、`RecoveryTests`、`MainScrollTests`（项目未保存）、
`GlobalVariablesTests`（含原先扫错窗口的那条）。

两处不只是加作答：

- `GlobalVariablesTests.Closing_the_manager_without_save_changes_nothing` 原来遍历的是外层编辑器的
  `OwnedWindows`，而问话是管理器窗发的，扫不到；现在直接答管理器窗的那条问话并用「丢弃」把它真关掉，
  否则编辑器（属主）关掉时管理器会再问一次。
- `StepTreeTests.Double_clicking_a_step_inside_a_block_edits_that_step` 用 `Thread.Sleep(60)` 造两次点击
  的间隔，机器一忙就被拉过双击窗口、两次点击变两次单击；改成泵一轮队列。

配方与判据写进了 `doc/dev.md` 的「关掉会发问的窗口：就地作答，不要走开」。

**验收**：全量连跑 3 次全绿（34 + 1460 + 1488 通过、1 跳过，4.1～5.2 分钟）。**没有完全做到「单条不超 2 秒」**：
`GlobalVariablesTests` 里那三条持久化相关的用例、`MacroEditorWorkspaceTests.Setting_a_variable_*`、
`VariableWallDockTests.The_group_chips_stay_on_one_line` 仍会到 9～14 秒，同一组单独跑是 1～4 秒，
所以是跨类累积的调度延迟（怀疑仍是没答干净的问话把调度器循环叠起来了），但不影响结果的绿。
想继续查这条，注意一个坑：任何在 `Ui.Run` 的 dispatch 里再包一层的写法（哪怕只是 try/finally）都会让
发现阶段卡死在 `Discovering: WhaleGenie.Tests`，进程 0% CPU——二分过一次，别再试同一条路。

### T2（必做）把剩下没看过的窗口逐个过一遍图 —— 已完成

已看过：ConfirmDialog、MainWindow、MacroEditor、Settings、DelayScale、About、ThemePalette、
GlobalVariables、Run。这一轮补上的：`BreakpointWindow`、`LookWindow`、`OffsetPickerWindow`、
`WindowRuleWindow`、`MacroMenuWindow`、`RegionPickerWindow`、`ColorPickerWindow`、`ExpressionBuilderWindow`、
`HotstringKeyboardWindow`、`TextPromptDialog`、`VariablePickerWindow`、`VirtualKeyboardWindow`、
`VirtualGamepadWindow`、`WindowPickerWindow`、`FloatingSurfaceWindow`，以及动作表单的三张脸
（重复 / 按键 / 日志 / 条件）。

**原来那张名单里有七个名字在当前代码里已经不存在**（`WatermarkWindow`、`UpdateWindow`、`ErrorWindow`、
`PaletteWindow`、`AddActionWindow`、`RunDebugWindow`、`MacroInterfaceWindow`）：动作表单是
`ActionEditorWindow` + `ActionEditorView`，运行窗是 `RunWindow`，接口页在宏编辑器里。别按旧名单找。

**结论：只有一处是真的没主题化，已修。**

- `AutoCompleteBox.Field`：格子本该由模板里那支 `TextBox` 画，可度量（高 30、内边距）写在了外层，
  内层按自己的最小高度排成 32 高，上下两条边框被裁掉，底色又被 `Background=Transparent` 清成窗口背景。
  界面上是"只有左右括号、没有底色"的输入框（断点窗的条件框最明显），而**每个颜色都是对的**——
  画笔断言一条都看不出来。修法：外层只占位（高 30、内边距 0、无边框），度量与底/边框交给内层；
  回归用例 `FieldStyleTests` 量的是两个盒子的包含关系（撤掉修复它会红，消息里带着实测的
  `inner=0,-1,355,32` 与 `field=0,0,355,30`）。

**看着像问题、量完不是问题的**（记下来免得下一轮又去查）：

- 函数按钮的"蓝边"、运算符按钮的"彩边"：那是文字次像素反锯齿的彩边（`487297`、`81C0E5`、`264A85`），
  取边框窄条统计颜色只有 `BorderControl` 的 `#555555`。
- 未勾选的 `CheckBox` 在暗色下是一枚灰底圆角方块：`CheckBoxCheckBackgroundFillUnchecked` 一族键早就映射到
  `SurfaceField`/`BorderControl`，放大看是对的。
- 页签上"返回值·"那个小圆点：`ActionEditorView` 的 `HasDot` 指示（这一页有附注），不是分隔符错位；
  代码里已标了"结构改进待办"（用颜色表达"有没有附注"太贵），与主题无关。
- 表达式窗左下"插入一个值"被截断：那两块调色板在 `BuildLeft` 的 `ScrollViewer` 里，是滚动视口，
  滚得到；只是 Fluent 的滚动条自动隐藏，看起来像被切掉。
- 滚动条本身：2px 细拇指、`#6A6A6A` 上下贯穿，是库的 overlay 样式，正常。
- 动作表单页签、`FANumberBox`、`NumericUpDown` 微调按钮、`ComboBox`、`ListView` 选中态都对得上令牌。

**没出图的**（构造函数是 `internal` 或只有 `static ShowAsync(owner, …)`，从外部程序建不出来，由用例覆盖）：
`MacroChooseWindow`、`MenuIconPickerWindow`、`MenuMacroPickerWindow`、`PublicMacroLibraryWindow`、
`GlobalConflictWindow`、`ElementPickerWindow`、`PointPickerWindow`、`VariableFieldPickerWindow`、
`ScreenFrameWindow`、`ThemeProbeWindow`。

`ToggleSwitch`、`Slider`、`ColorPicker`、`GridView`/`DataGrid` 这几族在界面里**根本没用到**（`ColorPickerWindow`
是取色放大镜，不是颜色选择控件），所以不必为它们补键。

### T3（可选）自绘标题栏向 WinUI 靠拢

17 个窗口各有一个 `Name="CloseButton"`，现在两种写法（`Classes="Ghost"` / `Classes="Chrome Icon"`）。
若要做：加一个 `Button.Caption` 类（46×32、hover 用 `SurfaceControlHover`，关闭键 hover 用 WinUI 的
`#C42B1C`），替换这 17 处，并更新 `ThemedDialogTests` 里对关闭键宽度/底色的断言（现在断言 24 宽、无色）。
**这不是必须的**，现有标题栏是一致可用的。

### T4（可选）`LanguageRefresh.Refresh()` 的健壮性

它遍历所有窗口逐个 `RefreshLanguage()`，任何一个陈旧 VM 抛异常都会砸到"切换语言的人"身上
（`WhaleGenie/Localization/LanguageRefresh.cs:17`）。可以改成逐个 try/catch 并汇总，但要先想清楚
"吞掉异常"是否掩盖问题——没有证据前不要动。

## 工作方式（这个仓库的规矩）

- **先量再改**。出现过三次"看起来像 A、实际是 B"：`/template/` 够不着、键名对不上、`HWND` 撞键。
  探针（临时 xunit 用例，`Assert.Fail` 打印实测值）比截图和推理都可靠。
- **不要用截图当证据**，除非它的输入和运行时一致（主窗缺 DataContext 骗过一次）。
- **不许有假绿**：断言按真实部件名（`UpSpinButton`/`DownSpinButton`），组件名写错会得到永远为真的断言。
- **提交信息写中文、写理由**。说清"为什么这么改"和"没解决什么"，不要罗列改动。
- **动完跑全量**，本窗绿不等于别处绿（撞名与全局状态都会跨窗）。
- 界面决策写进 `doc/architecture.md` §7.x；踩坑与配方写进 `doc/dev.md`。

## 一条命令确认当前状态

```bash
cd G:/DATA/MyAPP/Viktor && git log --oneline -5 && dotnet --version && dotnet test 2>&1 | tail -4
```
