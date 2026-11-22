# 动作编辑器迁移对账

所属：[界面与桥接](interface.md)。

这张表回答一个问题：旧 Avalonia 动作编辑器、步骤树和触发设置里的用户功能，在网页是否有入口和命令消费。它不按“某个 TS 文件有没有 import”判断死代码；没有消费点的先说清楚，再决定补入口还是登记后续工作。

## 步骤树

| 旧能力 | 网页位置 | 状态 |
| --- | --- | --- |
| 步骤列表、选中、多选、折叠、拖放、复制/粘贴、分组、分节 | webui/src/features/editor/step-tree.ts | 已有 |
| 分节引导线与横向连接线 | 同文件 .sections/.step 的 ::before 与 data-depth | 本轮补齐 |
| 空分节提示 | .section-empty | 已有并保留连接线 |
| 旧 TreeView 的展开/选中外观 | 网页 Fluent 状态 | 不适用，网页使用自己的令牌 |

## 动作表单

| 旧入口 | 网页入口/桥命令 | 状态 |
| --- | --- | --- |
| 取值窗口、数字字段、文本/多行/代码 | action-field.ts、value-field.ts、actionField | 已接 |
| 图片浏览、截屏、清除、单图/列表预览 | action-field.ts、action-rows.ts、ActionImagePreviews | 本轮补齐 |
| 区域行框选、区域变量、空列表 | action-rows.ts、editor.actionPick(region,index) | 本轮补齐 |
| 颜色点拾取、颜色/必须不是/容差 | action-rows.ts、editor.actionLook(colours) | 本轮补齐 |
| 偏移点选 | action-page.ts、editor.actionLook(offset) + LookPickWindow | 本轮补齐 |
| 点位/框选/窗口位置尺寸 | editor.actionPick + 原生覆盖层 | 已有 |
| 窗口文本字段选择 | WindowPickerWindow + editor.actionPick(windowTitle) | 本轮补齐 |
| 窗口规则匹配方式/内容/句柄/预览 | wg-window-rule、editor.actionWindowPreview | 本轮补齐 |
| 窗口“本宏用过的/打开的/捕获/沿用/前台” | action-field.ts、editor.actionWindow* | 已接基本路径，规则对话框覆盖完整编辑 |
| UIA 元素拾取/测试 | ElementPickerWindow、ElementFlash、editor.actionTestElement | 本轮补齐 |
| 浏览器元素拾取/候选/测试 | BrowserPicker、editor.actionTestBrowser | 本轮补齐 |
| 窄化行页面核对 | editor.actionTestNarrow | 本轮补齐 |
| 路径浏览、返回值、步骤设置、试运行 | 已有 | 已接 |
| 键位字段/键序列键盘与捕获 | EditorKeyPad、key-pad.ts、action-rows.ts | 本轮补齐完整键表 |
| 热字串缩写字符键盘 | EditorCharacterPad、character-pad.ts | 本轮补齐 |
| 热字串结束键格子/自定义键 | HotstringEndKeys + macro-settings.ts | 本轮补齐 |
| 接口热键 | EditorTriggerSnapshot + macro-settings.ts | 本轮补齐 |

## 明确保留、不是步骤树

- **step-tree.ts**：步骤列表和引导线，本轮保留并增强。
- **step-inspector.ts**：旧参数表单，不是步骤树；确认无 import、其字段/行表/条件控件已由 action-field、action-rows 和 action-page 覆盖后删除。步骤树仍保留在 step-tree.ts。
- 旧 Avalonia 的 ActionEditorView.*、MacroEditorViewModel 仍是有用的行为参考；不因为网页有同名实现就删除其 C# 模型。
- 旧键盘窗口和字符串键盘窗口不恢复为原生窗口：网页的 key-pad.ts、character-pad.ts 是替代实现；原生 LookPickWindow、WindowPickerWindow、区域/颜色/点位/UIA 覆盖层仍作为桌面覆盖层保留。

## 没有消费点时的处理

- 先补网页入口或桥命令；
- 不能补的，在表中写明“未迁移、原因、后续任务”，不能只删源文件；
- 只有确认既无用户入口、又无命令、又无测试引用时，才删除对应小项（例如已确认无用的快照记录或无 handler 的方法）；删除前在本文件记录原因。
