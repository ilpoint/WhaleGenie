# 永久保留的原生面

所属：[界面与桥接](interface.md)。

设置、编辑器、宏列表、运行面板和公共宏库已经搬到网页壳。下面这些窗口没有搬进网页，是因为它们的
工作必须先于网页存在，或者必须直接操作桌面；它们不是第二套界面，只做一次性的桌面动作或系统对话。

## 契约

- 模型与状态仍在 C#：窗口只读取组合根交来的 ViewModel/服务，不保存第二份设置、宏或运行状态。
- 用户可见文字一律走 `Strings`；颜色、字号、间距与窗口框架只取 `Themes/` 令牌。
- 普通窗口继承 `AppWindow` 并调用 `WindowChrome.Attach`；无标题栏的拾取器/浮层使用 `Window`，
  打开期间经 `AppWindows.StepAside` 收起鲸灵窗口。
- 网页需要打开它们时只经桥事件，不由页面自己创建窗口；窗口完成一次动作后把结果作为命令返回值交回。
- 这些面不再长出编辑模型、设置页或第二套项目快照。新的页面能力优先放进网页。

## 清单

| 面 | 位置 | 为什么保留 |
| --- | --- | --- |
| 网页外壳 | `Web/WebShellWindow` | WebView2 宿主、窗口外框、尺寸/拖动与焦点探测；网页本身负责页面 |
| 宏菜单 / 轮盘 / 浮层 | `Views/Editor/MacroMenuWindow`、`WheelMenuWindow`、`Views/Shell/FloatingSurfaceWindow` | 全局钩子与指针轨迹必须在本进程最前面，网页无法接管系统级呼出 |
| 窗口规则编辑与拾取 | `Views/Editor/WindowRuleWindow`、`Views/Pickers/WindowPickWindow` | 指向桌面上任意窗口、取进程/类名，必须跨进程读窗口 |
| 浏览器选择 | `Views/Pickers/BrowserPicker` | 列出本机浏览器配置并打开目标浏览器，属于桌面能力 |
| 工具栏/菜单图标与宏选择 | `Views/Editor/MenuIconPickerWindow`、`MenuMacroPickerWindow` | 菜单与轮盘编辑器的原生子对话框，沿用同一套图标/宏目录 |
| 导入/导出选择 | `Views/Editor/MacroChooseWindow`、`Projects/ProjectInteraction` | 在包内挑选宏，属于文件格式与导入导出编排 |
| 全局变量冲突 | `Views/Variables/GlobalConflictWindow` | 导入时逐项询问覆盖/保留，必须模态回答 |
| 后台问话/确认 | `Views/Shell/TextPromptDialog`、`ConfirmDialog` | 触发器、后台运行与退出未保存没有网页在场，仍由原生窗口作答 |
| 高级设置 | `Views/Settings/AdvancedSettingsWindow` | 菜单、窗口规则、轮盘与自定义主题需要桌面预览；设置主页已在网页 |
| 主题调色板 | `Views/Settings/ThemePaletteWindow` | 自定义强调色需要实时预览与对比度检查，避免网页与原生两套主题状态 |
| 网页缺失提示 | `Web/WebViewMissingWindow` | WebView2 运行时缺失时给出安装指引，不能依赖网页渲染 |

## 已退役

编辑器、动作编辑、取值、全局/局部变量、设置主窗和运行调试窗已全部删除；对应的窗口用例随窗口
退役，编辑/运行/变量的行为由 `HeadlessEditorSession`、`RunViewModel` 与 `RunBridge` 的 C# 用例和
网页用例覆盖。
