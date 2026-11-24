# 永久保留的原生面

所属：[界面与桥接](interface.md)。

设置、编辑器、宏列表、运行面板、公共宏库、关于与第三方组件都已经搬到网页壳。下面这些面没有
搬进网页，是因为它们的工作必须先于网页存在，或者必须直接操作桌面；它们不是第二套界面，只做
一次性的桌面动作或系统对话。

## 契约

- 模型与状态仍在 C#：窗口只读取组合根交来的 ViewModel/服务，不保存第二份设置、宏或运行状态。
- 用户可见文字一律走 `Strings`；颜色、字号、间距与窗口框架只取 `Themes/` 令牌。
- 普通窗口继承 `AppWindow`；需要把内容伸进标题栏的拾取器（`LookPickWindow`、`PickEntryWindow`）
  调用 `WindowChrome.Attach`，无标题栏的覆盖层使用 `Window`，打开期间经 `AppWindows.StepAside`
  收起鲸灵窗口。
- 网页需要打开它们时只经桥事件或命令，不由页面自己创建窗口；窗口完成一次动作后把结果作为命令
  返回值交回。
- 这些面不再长出编辑模型、设置页或第二套项目快照。新的页面能力优先放进网页。

## 清单

| 面 | 位置 | 为什么保留 |
| --- | --- | --- |
| 网页外壳 | `Web/WebShellWindow` | WebView2 宿主、窗口外框、尺寸/拖动与焦点探测；网页本身负责页面 |
| 网页浮层宿主 | `Web/WebOverlayWindow` | 透明置顶、点击穿透的网页路由（菜单/轮盘/面板）；窗口与输入策略只能原生做 |
| 系统级钩子与呼出 | `Runtime/TriggerHost`、`Runtime/NativeSurfaceHost` | 全局钩子与指针轨迹必须在本进程最前面；浮层本身已由网页承载 |
| 取色覆盖层 | `Views/Pickers/ColorPickerWindow` | 冻结桌面、放大镜与像素读数只能在原生窗口里完成 |
| 区域/拖拽/截图/窗口尺寸 | `Views/Pickers/RegionPickerWindow` | 全屏框选、箭头拖拽、截图落盘和实时移动目标窗口需要桌面覆盖层 |
| 点位覆盖层 | `Views/Pickers/PointPickerWindow` | 十字线、UIA 描边与窗口坐标基准只能在桌面上取 |
| UIA 元素覆盖层 | `Views/Pickers/ElementPickerWindow` | 跨进程 UI Automation 命中测试与描边 |
| 窗口拾取覆盖层 | `Views/Pickers/WindowPickWindow` | 跟着指针框住桌面上任意窗口，必须跨进程读窗口；规则表单与窗口列表已在网页 |
| 偏差点选窗 | `Views/Pickers/LookPickWindow` | 在步骤刚看到的取景图上点偏移或颜色点，含放大镜、缩放和备选色 |
| 颜色点行编辑 | `Views/Pickers/PickEntryWindow` | 偏差点选里逐行键入偏移与颜色，可用变量与公式 |
| 元素测试框 | `Views/Common/ScreenFrameWindow`、`Views/Run/ElementFlash` | UIA 元素测试需要跨进程框出控件；动作仍不执行 |
| 浏览器选择 | `Views/Pickers/BrowserPicker` | 在设备层的浏览器页面上描边取元素，没有打开的浏览器就先打开目标页面 |
| 后台问话/确认 | `Views/Shell/TextPromptDialog`、`ConfirmDialog` | 触发器、后台运行与退出未保存没有网页在场，仍由原生窗口作答 |
| 网页缺失提示 | `Web/WebViewMissingWindow` | WebView2 运行时缺失时给出安装指引，不能依赖网页渲染 |

调试构建里的 `ThemeProbeWindow` 与 `OverlayProbeWindow` 只是视觉与首帧核对工具，不进入发行界面，
也不属于用户可见的原生面。

## 已退役

编辑器、动作编辑、取值、全局/局部变量、设置主窗、高级设置窗、主题调色板、原生关于窗、菜单图标
与宏选择窗、原生轮盘动作编辑窗、运行调试窗、导入/导出选择与全局变量冲突窗、窗口列表选择窗都已
删除。导入/导出与冲突问答改由网页 `wg-transfer` 向导承担，窗口列表由 `window.list` 与
`wg-window-list` 提供，图标选择由页面内的 `wg-icon-picker` 承担。动作字段的桌面拾取器随后以
覆盖层形式恢复，入口仍由网页桥命令打开；偏差点选、颜色点行编辑与 UIA 元素测试框也保留在同一侧。
对应的窗口用例随窗口退役；编辑、设置、轮盘规则与运行行为由 `HeadlessEditorSession`、
`SettingsViewModel`、`RunViewModel` 的 C# 用例和网页用例覆盖。
