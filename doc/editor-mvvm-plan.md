# 编辑器 MVVM 化：分五步

`MacroEditorWindow` 3104 行、56 个字段、131 个方法；`MacroEditorViewModel` 3438 行、18 个字段。它们不是"一个视图 + 一个模型"，而是**两个对象各拿一半状态**：VM 造宏、窗口拼最终对象；VM 抛 `MacroLifted`、窗口接住塞进自己的文档表。

这份文件只解决一件事：**把状态归位**。目标不是"MVVM 教科书"，是"改一处时知道该看哪个文件"。

不做什么：

- 不引 MVVM 框架，不引入消息总线。
- 不给每个控件造 ViewModel；XAML 里的 `Click="OnXxx"` 保留——它们本来就是视图的事。
- 不追求"窗口零逻辑"。窗口该做的（拖动、落点、滚动、焦点）继续留在窗口。

## 现状：字段按归属分三类

| 类 | 字段 | 现在在哪 | 应该在哪 |
|---|---|---|---|
| **文档态** | `_project`、`_editing`、`_lifted`、`_documents`、`_pendingSubMacro`、`_lastEditorNames`、`_assetFolder` | 窗口 | **session**（本计划的产物） |
| **UI 态** | `_switchingDocuments`、`_pageBeforeTabClose`、`_allowClose`、`_prompting`、`_closing`、`_mustStayUnsaved`、`_syncingSelection`、`_dragOrigin`、`_pressedRow`、`_dropSlot`…、`_captureModifiers`、`_swallowReleases` | 窗口 | 留窗口 |
| **服务** | `_devices`、`_recovery`、`_triggerFrames`、`_publicMacros`、`_running`、`_hotstringKeyboard` | 窗口 | 容器或 session（分阶段） |

判断标准只有一条：**关掉这个窗口，它还有意义吗。** 有意义的（项目、抬起列表、资产目录）进 session；没有的（拖动位置、Tab 关到哪一页）留窗口。

## 阶段 1：`MacroEditorSession` 持文档态（纯搬迁，零行为变化）

新增 `WhaleGenie/ViewModels/MacroEditorSession.cs`，持有上表第一行的七项：

```csharp
internal sealed class MacroEditorSession
{
    public IReadOnlyList<MacroItem> Project { get; }          // 整个项目，供按名字找宏
    public MacroItem? Editing { get; }                        // 正在编辑的那个
    public string AssetFolder { get; }                        // 截图落哪
    public List<MacroItem> Lifted { get; } = [];              // 抬起出来、随保存进入项目的宏
    public ObservableCollection<MacroDocumentPageViewModel> Documents { get; } = [];
    public Dictionary<MacroEditorViewModel, string> EditorNames { get; } = [];
    public PendingSubMacro? Pending { get; set; }
}
```

窗口的七处字段换成 `_session.X`，构造里建一次。**不动任何方法体逻辑**，只改字段来源。

验收：全量测试全绿（这条改动理想情况下一条用例都不用改）；`git diff` 里除字段声明与引用外没有别的。

## 阶段 2：保存合成 —— 查过之后不做

原先写的是"`BuildRootSnapshot`（约 480 行的文档合成）搬成 `session.BuildSnapshot()`"。实际它是 8 行：

```csharp
_rootViewModel.SubMacros.Clear();
foreach (var page in _documentPages.Where(page => !page.IsCurrent))
    _rootViewModel.SubMacros.Add(page.Editor.BuildMacro());
return _rootViewModel.BuildMacro();
```

合成的手艺在 `MacroEditorViewModel.BuildMacro()` 里，本来就在 VM。搬这 8 行只多一层调用。那个 480 行是把文档页管理那一片看成它了——**先读代码再估工作量**，这是第二次栽在同一件事上。

## 阶段 3：录制会话搬进 session　✅ 已完成

`StartRecorder`/`StopRecorder` 与它们的状态（正在录、选项、累计时间）搬成 session 的一部分，`IDeviceLayer` 从 session 拿。

验收：录制用例全绿；窗口里只剩"按钮按下时告诉 session"。

## 阶段 4：键位捕获 —— 先不动

两套捕获形似神不同：`ActionEditorView` 往一个字段或一行里填值，`MacroEditorWindow` 认的是一个独立绑定；单个修饰键的语义也不一样（窗口那一侧"按下单个 Ctrl 就是一种绑定"，编辑器那一侧不是）。合并会被迫在里面加"这次是哪一种"的分支，比两份各自清楚更难读。等真出现第三种捕获时再谈。

## 阶段 5（可选）：窗口只剩绑定

做完前四步，窗口大约会少 700–900 行，剩下的都是 UI 与文件对话框。是否继续把它压到"只绑定"，取决于前四步之后的实际形状——现在不预设。

## 推进方式

一次一个阶段、一次一个提交、每次全量测试 + `dotnet format`。任何一阶段都可以停下：阶段 1 只改字段来源，阶段 2 只改一个方法的位置，都不留半截状态。

如果某一步发现"耦合比表里写的深"，**就地记下来并停在能编译的状态**，不要硬推——上一轮把 `doc/refactor-plan.md` 第 2 条估成"搬一块"，查下来 `SwitchToSubMacro` 整个围着 `ListBox` 转，就是没先看代码的代价。
