import type { BridgeTransport } from './transport.js';
import type {
  AppInfo,
  EditorConditionNode,
  EditorSnapshot,
  EditorStep,
  McpState,
  ProjectState,
  SettingsMenuState,
  SettingsWheelSlot,
  SettingsWheelState,
  StringsBundle,
  ThemeState,
  WindowEntryState,
  WindowRuleState,
  WindowState,
} from './protocol.js';

/**
 * 浏览器里的假宿主：不依赖程序就能开发壳、跑 Playwright。它回答壳用到的每个方法，并留出
 * `push()` 让测试往页面里灌事件。真实词表以程序下发的为准——这里只放壳需要的键，与真表
 * 同步更新（有测试对账）。
 */

const demoRule: WindowRuleState = {
  title: 'Untitled - Notepad', process: 'notepad', className: 'Notepad',
  handle: 0, how: 'titleContains', caseSensitive: false, trim: true, multiline: false,
};

const demoWheelGeometry = {
  diameter: 340, center: 37.4, inner: 95.2, outer: 159.8, third: 0, edge: 159.8,
  outerCount: 8, thirdCount: 0, hasOuter: true, hasThird: false,
};

function demoWheelSlots(): SettingsWheelSlot[] {
  const slots: SettingsWheelSlot[] = [];
  const rings = [
    { name: 'inner', count: 8, radius: 66 },
    { name: 'outer', count: 8, radius: 128 },
    { name: 'extended', count: 8, radius: 165 },
  ];
  for (const ring of rings) {
    for (let index = 0; index < ring.count; index++) {
      const angle = (index * Math.PI * 2) / ring.count;
      const macro = ring.name === 'inner' && index === 0 ? '演示宏' : '';
      const angleLabel = ring.name === 'inner' ? '内圈' : ring.name === 'outer' ? '外圈' : '划动圈';
      slots.push({
        ring: ring.name,
        index,
        x: ring.radius * Math.sin(angle),
        y: -ring.radius * Math.cos(angle),
        position: `${angleLabel}第 ${index + 1} 格`,
        macro,
        note: '',
        label: macro,
        icon: null,
        inherited: false,
        overridden: macro.length > 0,
        empty: macro.length === 0,
        tone: ring.name === 'extended' ? 'extended' : macro ? 'global' : 'empty',
      });
    }
  }
  return slots;
}

const demoMenu: SettingsMenuState = {
  launcher: 'Mouse Middle', display: '鼠标中键', status: '就绪',
  isMiddle: true, isCustom: false, problem: '',
  rules: [
    {
      key: 'default', summary: 'Default', pageCountLabel: '1 页', isDefault: true,
      isBlocked: false, canAddPage: true, rule: { ...demoRule, title: '', process: '', className: '' },
    },
    {
      key: 'notepad', summary: 'Notepad · 标题包含', pageCountLabel: '1 页', isDefault: false,
      isBlocked: false, canAddPage: true, rule: { ...demoRule },
    },
  ],
};

const demoWheel: SettingsWheelState = {
  enabled: true, launcher: 'Mouse Right', launcherStatus: '就绪', launcherMessage: '',
  onlyInPrograms: false, diameter: 420, fontSize: 11, thirdCapacity: 0, timeoutMs: 20000,
  repeatKey: 'F1', repeatDisplay: 'F1', wideOuter: false, hideEmptyOuter: true,
  hideLabelWithIcon: false, centerName: false, clampToScreen: true,
  movePointerToCenter: false, showGuide: true, extendedHint: true, followTheme: true,
  gloss: true, hoverBorder: true, popAnimation: true, isSystemOff: false, problem: '',
  launcherChoices: [
    { value: 'Mouse Right', label: '鼠标右键' },
    { value: 'Mouse Middle', label: '鼠标中键' },
    { value: 'Mouse 5', label: '鼠标前进键' },
    { value: 'Mouse 4', label: '鼠标后退键' },
  ],
  thirdChoices: [
    { value: 0, label: '关闭（两圈）' },
    { value: 8, label: '8 格' },
    { value: 16, label: '16 格' },
    { value: 24, label: '24 格' },
  ],
  scenes: [
    { key: 'global', summary: '全局', isGlobal: true, rule: { ...demoRule, title: '', process: '', className: '' } },
    { key: 'notepad', summary: 'Notepad · 标题包含', isGlobal: false, rule: { ...demoRule } },
  ],
  selectedScene: 'global',
  geometry: demoWheelGeometry,
  slots: demoWheelSlots(),
  preview: demoWheelSlots(),
  macros: [
    { name: '演示宏', enabled: true },
    { name: '备份', enabled: true },
  ],
};

const demoWindows: WindowEntryState[] = [
  {
    handle: 1, title: 'Untitled - Notepad', process: 'notepad', className: 'Notepad',
    detail: '800×600', summary: 'notepad · Notepad · 800×600',
  },
];

const mockExtra: Record<string, { en: string; zh: string }> = {
  'Settings.GeneralCardNote': { en: 'What happens at start', zh: '启动时做什么' },
  'Settings.SoundsNote': { en: 'A cue when it switches', zh: '开关时的提示音' },
  'Settings.ReadingNote': { en: 'Where the picture comes from', zh: '画面从哪里读' },
  'Settings.FramesCardNote': { en: 'How often, and whether it is reused', zh: '多久读一次，是否复用' },
  'Settings.InputCardNote': { en: 'How your input is counted', zh: '你的输入怎么计算' },
  'Settings.DriverNote': { en: 'Input that looks real', zh: '看起来像真实设备的输入' },
  'Settings.StorageNote': { en: 'Kept across updates', zh: '更新后仍保留' },
  'Settings.MenuLauncher': { en: 'Menu shortcut', zh: '菜单呼出键' },
  'Settings.MenuLauncherNote': { en: 'One key opens the menu', zh: '一个按键唤出菜单' },
  'Settings.MenuLauncherHint': { en: 'One keyboard shortcut, the middle or a side button.', zh: '一个键盘快捷键，或鼠标中键、4/5 侧键。' },
  'Settings.MiddleButton': { en: 'Middle mouse button (default)', zh: '鼠标中键（默认）' },
  'Settings.MenuWindowRules': { en: 'Lower-region window rules', zh: '下方区域窗口规则' },
  'Settings.MenuWindowRulesNote': { en: 'The foreground window decides', zh: '由前台窗口决定' },
  'Settings.MenuRuleHint': { en: 'First match from the top wins; otherwise the default pages.', zh: '从上往下第一条命中的规则生效；都不命中时用默认页。' },
  'Settings.MenuRuleDefault': { en: 'Default', zh: '默认' },
  'Settings.MenuRuleBlocked': { en: 'Launcher disabled', zh: '呼出已停用' },
  'Settings.MenuRuleAdd': { en: 'Add rule', zh: '添加规则' },
  'Settings.MenuRuleAddPage': { en: 'Add page', zh: '添加页' },
  'Settings.MenuRuleEdit': { en: 'Edit', zh: '编辑' },
  'Settings.MenuRuleRemove': { en: 'Remove', zh: '删除' },
  'Settings.MenuRuleUp': { en: 'Move up', zh: '上移' },
  'Settings.MenuRuleDown': { en: 'Move down', zh: '下移' },
  'Settings.WheelTitle': { en: 'Wheel menu', zh: '轮盘菜单' },
  'Settings.WheelTitleNote': { en: 'Hold, move, aim, release', zh: '按住、移动、指向、松开' },
  'Settings.WheelGestureHint': { en: 'Hold the trigger and move to open the wheel; release over a sector to run it.', zh: '按住触发键并移动即可弹出轮盘；在扇区上松开执行。' },
  'Settings.WheelLauncher': { en: 'Trigger button', zh: '触发按键' },
  'Settings.WheelEnabled': { en: 'Enable the wheel', zh: '启用轮盘菜单' },
  'Settings.WheelOnlyInPrograms': { en: 'Only in listed programs', zh: '只在指定软件下唤出' },
  'Settings.WheelEnableSystem': { en: 'Enable the system switch', zh: '启用系统总开关' },
  'Settings.WheelSize': { en: 'Wheel size', zh: '轮盘尺寸' },
  'Settings.WheelSizeUnit': { en: 'px', zh: '像素' },
  'Settings.WheelTextSize': { en: 'Label size', zh: '文字大小' },
  'Settings.WheelThird': { en: 'Third circle', zh: '第三圈' },
  'Settings.WheelThird0': { en: 'Off (two circles)', zh: '关闭（两圈）' },
  'Settings.WheelThird8': { en: '8 positions', zh: '8 格' },
  'Settings.WheelThird16': { en: '16 positions', zh: '16 格' },
  'Settings.WheelThird24': { en: '24 positions', zh: '24 格' },
  'Settings.WheelTimeout': { en: 'Cancel after', zh: '超时取消' },
  'Settings.WheelTimeoutUnit': { en: 'ms', zh: '毫秒' },
  'Settings.WheelRepeat': { en: 'Repeat key', zh: '重复触发键' },
  'Settings.WheelWideOuter': { en: '16 positions on the outer circle', zh: '外圈用 16 格' },
  'Settings.WheelHideEmptyOuter': { en: 'Hide empty outer circles', zh: '自动隐藏全空的外圈' },
  'Settings.WheelIconOnly': { en: 'Show icons instead of labels', zh: '有图标时只显示图标' },
  'Settings.WheelCenterName': { en: 'Name the aimed action in the centre', zh: '在中心显示指向的动作名' },
  'Settings.WheelClamp': { en: 'Keep the wheel on screen', zh: '轮盘限制在屏幕内' },
  'Settings.WheelMovePointer': { en: 'Move the pointer to the centre at the screen edge', zh: '贴边时把指针移到轮盘中心' },
  'Settings.WheelGuide': { en: 'Show the line from the centre to the pointer', zh: '显示从中心到指针的提示线' },
  'Settings.WheelExtendedHint': { en: 'Name the action on the swipe circle in the centre', zh: '在中心显示划圈动作的名字' },
  'Settings.WheelFollowTheme': { en: 'Follow the application theme', zh: '跟随应用主题' },
  'Settings.WheelGloss': { en: 'Sector sheen', zh: '扇区光泽' },
  'Settings.WheelHoverBorder': { en: 'Hover rim', zh: '悬浮边框' },
  'Settings.WheelPopAnimation': { en: 'Pop animation', zh: '弹出动效' },
  'Settings.WheelPreview': { en: 'Preview the wheel', zh: '预览轮盘' },
  'Settings.WheelPreviewHint': { en: 'Shows the current global actions in the wheel layout.', zh: '按当前全局动作显示轮盘版面。' },
  'Settings.WheelActions': { en: 'Wheel actions', zh: '轮盘动作管理' },
  'Settings.WheelActionsNote': { en: 'One action per position', zh: '每个位置放一个动作' },
  'Settings.WheelActionsHint': { en: 'The global scene holds the defaults. The first matching program scene overrides only its filled positions.', zh: '全局场景放默认动作。命中第一条程序场景后，只覆盖它填了动作的位置。' },
  'Settings.ThemeHint': { en: 'Dark and light are built in; Custom picks one accent.', zh: '深色与浅色内置；自定义只挑强调色。' },
  'Settings.ColorsNote': { en: 'Dark, light and the accent', zh: '深色、浅色与强调色' },
  'Settings.CustomizeTheme': { en: 'Accent colour', zh: '自定义强调色' },
  'Settings.HotkeysNote': { en: 'Works in any window', zh: '在任何窗口里都能按' },
  'Common.None': { en: 'None', zh: '无' },
  'Common.Save': { en: 'Save', zh: '保存' },
  'Common.Cancel': { en: 'Cancel', zh: '取消' },
  'Common.Ok': { en: 'OK', zh: '确定' },
  'Wheel.Scenes': { en: 'Scenes', zh: '场景' },
  'Wheel.SceneAdd': { en: 'Add', zh: '新增' },
  'Wheel.SceneEdit': { en: 'Edit rule', zh: '编辑规则' },
  'Wheel.SceneRemove': { en: 'Remove', zh: '删除' },
  'Wheel.SceneUp': { en: 'Up', zh: '上移' },
  'Wheel.SceneDown': { en: 'Down', zh: '下移' },
  'Wheel.EditorHint': { en: 'Pick a position to edit its macro, note and icon.', zh: '选一个位置，编辑宏、备注与图标。' },
  'Wheel.SlotHint': { en: 'Click a position to edit it.', zh: '点一个位置开始编辑。' },
  'Wheel.LegendGlobal': { en: 'Global', zh: '全局' },
  'Wheel.LegendOverride': { en: 'Program override', zh: '程序覆盖' },
  'Wheel.LegendExtended': { en: 'Swipe circle', zh: '划动圈' },
  'Wheel.Inherited': { en: '{0} (global)', zh: '{0}（全局）' },
  'Wheel.ActionMacro': { en: 'Macro', zh: '宏' },
  'Wheel.ActionNote': { en: 'Note', zh: '备注' },
  'Wheel.ActionNotePlaceholder': { en: 'Shown in place of the macro name', zh: '显示在扇区上，代替宏名' },
  'Wheel.ActionIcon': { en: 'Icon', zh: '图标' },
  'Wheel.ActionPickIcon': { en: 'Choose icon…', zh: '选择图标…' },
  'Wheel.ActionClearIcon': { en: 'Clear icon', zh: '清除图标' },
  'Wheel.ActionClearSlot': { en: 'Clear this position', zh: '清除该位置' },
  'Wheel.ActionInherited': { en: 'This position follows the global scene; saving writes a program override.', zh: '这个位置当前沿用全局场景；保存后会写成本程序自己的动作。' },
  'Settings.WindowRuleEditor': { en: 'Window rule', zh: '窗口规则' },
  'Settings.WindowRuleRequired': { en: 'Pick a window and the part of it to match.', zh: '请先选择窗口以及要匹配的部分。' },
  'WindowRule.CaptureHint': { en: 'Pick an open window, or point at one on the desktop.', zh: '在左边挑一个窗口，或者到桌面上取一个。' },
  'WindowRule.Pick': { en: 'Which window', zh: '哪个窗口' },
  'WindowRule.Search': { en: 'Filter by title or program', zh: '按标题或程序名筛选' },
  'WindowRule.NoWindows': { en: 'No windows to choose from right now.', zh: '现在没有可选的窗口。' },
  'WindowRule.PickWindow': { en: 'Pick a window', zh: '取窗口' },
  'WindowRule.Refresh': { en: 'Refresh', zh: '刷新' },
  'WindowRule.How': { en: 'Match by', zh: '匹配方式' },
  'WindowRule.MatchText': { en: 'Match text', zh: '匹配内容' },
  'WindowRule.CaseSensitive': { en: 'Case sensitive', zh: '区分大小写' },
  'WindowRule.Trim': { en: 'Ignore spaces at the ends', zh: '去掉两端空格' },
  'WindowRule.Multiline': { en: 'Regex by line', zh: '正则按行' },
  'WindowRule.FieldHint': { en: 'The rule is checked again every time the macro runs.', zh: '每次运行都按这条规则重新找窗口。' },
  'WindowRule.HandleHint': { en: 'A handle identifies only this window instance.', zh: '句柄只认当前这个窗口实例。' },
  'WindowRule.Cancel': { en: 'Cancel', zh: '取消' },
  'WindowRule.Use': { en: 'Use this window', zh: '使用这个窗口' },
  'WindowRule.BadHandle': { en: 'A window handle must be a non-zero whole number.', zh: '窗口句柄必须是不为 0 的整数。' },
  'WindowPick.Untitled': { en: 'A window with no title', zh: '没有标题的窗口' },
  'Theme.Base.Hint': { en: 'Controls follow this base; the accent is yours.', zh: '控件底子跟随这里；强调色由你决定。' },
  'Theme.Base.Light': { en: 'Light controls', zh: '浅色控件' },
  'Theme.Accent': { en: 'Accent colour', zh: '强调色' },
  'Theme.Accent.Hint': { en: 'The button, selected row and accent text are derived from this colour.', zh: '按钮、选中行与强调文字都由这个颜色推出来。' },
  'Theme.Palette.Hint': { en: 'A colour that cannot be made readable is not used.', zh: '推不到可读对比度的颜色不会被采用。' },
  'Theme.Accent.Rejected': { en: 'This colour cannot be made readable; the built-in accent is used.', zh: '这个颜色读不清，已改用内置强调色。' },
  'Theme.Preview.Primary': { en: 'Primary button', zh: '主按钮' },
  'Theme.Preview.Selected': { en: 'Selected row', zh: '选中行' },
  'Theme.Preview.AccentText': { en: 'Accent text', zh: '强调文字' },
  'Theme.ResetAccent': { en: 'Back to the built-in accent', zh: '改回内置强调色' },
  'Main.About': { en: 'About', zh: '关于' },
  'Main.AboutText': { en: 'A little assistant DeepSeek helped write, called WhaleGenie.\n\nVersion {0}', zh: '由 DeepSeek 协助开发的小助手，叫鲸灵。\n\n版本 {0}' },
  'Main.AboutOpenPage': { en: 'Open the project page', zh: '打开项目主页' },
  'Main.AboutNotices': { en: 'Third-party components', zh: '第三方组件' },
  'Main.AboutUnknownVersion': { en: 'unknown', zh: '未知' },
  'About.SupportTitle': { en: 'Support the developer', zh: '支持开发者' },
  'About.SupportText': { en: 'WhaleGenie is a free open-source project.', zh: '鲸灵是完全免费的开源项目。' },
  'About.WeChat': { en: 'WeChat', zh: '微信' },
  'About.Alipay': { en: 'Alipay', zh: '支付宝' },
  'Shell.Field.Version': { en: 'Version', zh: '版本' },
};

const chromeKeys = [
  'Shell.Home', 'Shell.Macros', 'Shell.Editor', 'Shell.Run', 'Shell.Settings',
  'Shell.UntitledProject', 'Shell.Field.Version', 'Shell.Field.Language', 'Shell.Field.Project',
  'Shell.Field.Dirty', 'Shell.Field.Clean', 'Shell.Field.Revision', 'Shell.Field.Macros',
  'Shell.Home.Heading', 'Shell.Home.Intro', 'Shell.Status.Web', 'Shell.Status.Native',
  'Shell.Status.Planned', 'Shell.Placeholder.Body', 'Shell.Navigation', 'Shell.OpenNative',
  'Shell.File', 'Shell.About', 'Shell.Help', 'Shell.Manual',
  'Settings.GeneralCard', 'Settings.LoadLastProject', 'Settings.SystemAtStart',
  'Settings.EnabledFirst', 'Settings.Sounds', 'Settings.SoundSystem', 'Settings.SoundMacro',
  'Settings.Reading', 'Settings.PreloadOcr', 'Settings.FramesCard', 'Settings.FrameRateFollow',
  'Settings.FrameRate', 'Settings.FrameCache', 'Settings.FrameCacheAge', 'Settings.InputCard',
  'Settings.IgnoreOwnInput', 'Settings.InputLimit', 'Settings.LogCount',
  'Settings.Language', 'Settings.Language.English', 'Settings.Language.Chinese', 'Settings.Theme',
  'Settings.ThemeAccent', 'Settings.Hotkeys', 'Settings.SystemHotkey', 'Settings.RecordStopKey', 'Settings.HotkeySet',
  'Settings.HotkeyPress', 'Settings.HotkeyClear', 'Settings.Storage', 'Settings.OpenFolder',
  'Settings.Driver', 'Settings.DriverAttachAtStart', 'Settings.RunAsAdmin',
  'Run.Title', 'Run.Run', 'Run.NativeNote', 'Run.Variables', 'Run.Step', 'Run.Continue',
  'Run.SkipStep', 'Run.Stop', 'Run.NotLoaded', 'Run.AskRetry', 'Run.AskSkip',
  'Run.AskStop', 'Common.Ok', 'Common.Cancel', 'Run.UnknownCommand', 'Run.UnknownStep',
  'Settings.Advanced', 'Settings.WindowRules', 'Settings.CaptureCard',
  'Settings.CaptureWindow', 'Settings.CaptureScreen', 'Theme.Mode.Dark', 'Theme.Mode.Light',
  'Theme.Mode.Custom',
  'Shell.Library', 'Library.Heading', 'Library.Intro', 'Library.Empty', 'Library.Delete',
  'Library.Failed', 'Library.AddFromProject', 'Library.ReplaceFromProject',
  'Shell.Window.Minimize',
  'Shell.Window.Maximize', 'Shell.Window.Restore', 'Shell.Window.Close', 'Shell.Window.Pin',
  'Shell.Window.Unpin', 'Main.Title', 'Main.Dock.All', 'Main.Dock.Keys', 'Main.Dock.Interface',
  'Main.Dock.Hotstring', 'Main.Dock.Menu', 'Main.Dock.Wheel', 'Main.Dock.Events',
  'Main.SearchPlaceholder', 'Main.EmptyTitle', 'Main.EmptyHint', 'Main.NoMatch',
  'Main.AddMacro', 'Main.MacroRunning', 'Main.EnableMacro', 'Main.DisableMacro',
  'Main.SystemEnabled', 'Main.SystemDisabled', 'Main.GroupAll', 'Main.Ungrouped',
  'Main.RunDebug',
  'Main.OpenProject', 'Main.SaveProject', 'Main.SaveProjectAs', 'Main.ReloadCurrent',
  'Main.CloseAll', 'Mcp.Title', 'Mcp.Intro', 'Mcp.Control', 'Mcp.Run',
  'Mcp.SwitchOn', 'Mcp.SwitchOff', 'Mcp.CopyConfig', 'Mcp.GrantsNote', 'Mcp.OpenAudit',
  'Main.CloseAllTitle', 'Main.CloseAllMessage',
  'Main.EditTip', 'Main.DeleteTip', 'Editor.DeleteTitle', 'Editor.DeleteMessage',
  'Editor.NamePlaceholder', 'Editor.GroupPlaceholder', 'Editor.DescriptionPlaceholder', 'Editor.NoSteps',
  'Editor.GateActions', 'Editor.AddStep', 'Editor.AddChildStep', 'Editor.AddRow', 'Editor.Browse',
  'Editor.RemoveRow', 'Editor.StepEnabled', 'Editor.DuplicateStep', 'Editor.Record',
  'Editor.StopRecording', 'Editor.RecordActive', 'Editor.RecordEmpty',
  'Editor.ValueBuilder', 'Editor.Functions', 'Editor.AddGroup', 'Editor.SubMacros',
  'MacroInterface.Parameters', 'MacroInterface.Results', 'MacroInterface.Name',
  'MacroInterface.Type', 'MacroInterface.Required', 'MacroInterface.Default',
  'MacroInterface.Description', 'MacroInterface.Remove', 'MacroInterface.AddParameter',
  'MacroInterface.AddResult', 'MacroInterface.FieldName', 'MacroInterface.AddField',
  'MacroInterface.MoveUp', 'MacroInterface.MoveDown',
  'Condition.All',
  'Condition.Any', 'Condition.None', 'Editor.InsertValue', 'Add.StepComment', 'Add.StepTimeout',
  'Add.StepRetry', 'Add.StepRetryDelay', 'Add.StepOnError', 'Add.OnError.Stop',
  'Add.OnError.Continue', 'Add.OnError.NextIteration', 'Add.OnError.AskUser', 'Editor.OpenMacro',
  'Editor.TriggerEntries', 'Editor.AddTrigger',
  'Editor.RemoveTrigger', 'Editor.BindKey', 'Add.SetCombination', 'Add.PressKey',
  'Palette.Undo', 'Palette.Redo', 'Editor.Loop', 'Editor.TriggerOnce',
  'Editor.TriggerEnabled', 'Editor.Schedule', 'Editor.ScheduleTime', 'Editor.ScheduleTimeHint',
  'Schedule.Interval', 'Schedule.Daily', 'Schedule.Unit.Seconds', 'Schedule.Unit.Minutes',
  'Schedule.Unit.Hours', 'Editor.WatchPath', 'Editor.WatchPathHint', 'Editor.WatchChange',
  'Editor.WatchFilter', 'Editor.WatchSubfolders', 'FileChange.Any', 'FileChange.Created',
  'FileChange.Changed', 'FileChange.Deleted', 'Editor.ProcessName', 'Editor.ProcessNameHint',
  'Editor.ProcessWhen', 'ProcessChange.Any', 'ProcessChange.Started', 'ProcessChange.Stopped',
  'Editor.Hotstring', 'Editor.HotstringHint', 'Editor.ColorPosition', 'Editor.ColorTolerance',
  'Editor.IdleSeconds', 'ColorMatch.Matches', 'ColorMatch.NotMatches', 'WindowPart.Title',
  'WindowRule.Process', 'WindowRule.How', 'WindowRule.How.titleContains',
  'WindowRule.How.titleStartsWith', 'WindowRule.How.titleEndsWith', 'WindowRule.How.titleEquals',
  'WindowRule.How.titleRegex', 'WindowRule.How.process', 'WindowRule.How.processEquals',
  'WindowRule.How.class', 'WindowRule.How.classEquals', 'WindowRule.How.handle',
  'WindowChange.Appeared', 'WindowChange.Disappeared', 'WindowChange.Any',
  'Loop.Press', 'Loop.Hold', 'Loop.Toggle', 'Loop.Release',
  'LocalVariables.Title', 'LocalVariables.New', 'LocalVariables.Initial',
  'LocalVariables.InitialPlaceholder', 'LocalVariables.Delete', 'GlobalVariables.Title',
  'GlobalVariables.New', 'GlobalVariables.Remove', 'GlobalVariables.Definition',
  'GlobalVariables.DefaultPlaceholder', 'Variable.Persist',
  'Run.Steps', 'Common.Save', 'Common.Close',
  'Editor.Delete', 'Common.Cancel', 'Common.Ok', 'Main.Import',
] as const;

const english: Record<string, string> = {
  'Shell.Home': 'Home', 'Shell.Macros': 'Macros', 'Shell.Editor': 'Editor',
  'Shell.Run': 'Run & Debug', 'Shell.Settings': 'Settings',
  'Shell.UntitledProject': 'Untitled project', 'Shell.Field.Version': 'Version',
  'Shell.Field.Language': 'Interface language', 'Shell.Field.Project': 'Project',
  'Shell.Field.Dirty': 'Unsaved changes', 'Shell.Field.Clean': 'Saved',
  'Shell.Field.Revision': 'Revision', 'Shell.Field.Macros': 'Macros',
  'Shell.Home.Heading': 'Welcome to WhaleGenie',
  'Shell.Home.Intro': 'The interface is moving to a web page.',
  'Shell.Status.Web': 'In the web shell', 'Shell.Status.Native': 'Still native',
  'Shell.Status.Planned': 'Planned',
  'Shell.Placeholder.Body': 'This area still lives in its native window.',
  'Shell.Navigation': 'Navigation', 'Shell.OpenNative': 'Open native window',
  'Shell.File': 'File', 'Shell.About': 'About', 'Shell.Help': 'Help', 'Shell.Manual': 'User manual',
  'Main.Dock.All': 'All', 'Main.Dock.Keys': 'Keys', 'Main.Dock.Interface': 'Window',
  'Main.Dock.Hotstring': 'Strings', 'Main.Dock.Menu': 'Menu', 'Main.Dock.Wheel': 'Wheel',
  'Main.Dock.Events': 'Events', 'Main.SearchPlaceholder': 'Search…',
  'Main.EmptyTitle': 'No macros yet.', 'Main.EmptyHint': 'Create your first macro below.',
  'Main.NoMatch': 'No macro matches.', 'Main.AddMacro': 'New macro',
  'Main.MacroRunning': 'Running', 'Main.EnableMacro': 'Enable', 'Main.DisableMacro': 'Disable',
  'Main.SystemEnabled': 'Enabled', 'Main.SystemDisabled': 'Disabled',
  'Main.RunDebug': 'Run and Debug',
  'Main.GroupAll': 'All groups', 'Main.Ungrouped': 'Ungrouped',
  'Main.OpenProject': 'Open Macro Project', 'Main.SaveProject': 'Save Macro Project',
  'Main.Import': 'Import…',
  'Main.SaveProjectAs': 'Save Macro Project As', 'Main.ReloadCurrent': 'Reload Current Macro',
  'Main.CloseAll': 'Close All', 'Main.CloseAllTitle': 'Close all macros',
  'Main.EditTip': 'Edit', 'Main.DeleteTip': 'Delete',
  'Editor.DeleteTitle': 'Delete macro', 'Editor.Delete': 'Delete',
  'Editor.NamePlaceholder': 'Enter a name for your macro..', 'Editor.GroupPlaceholder': 'Group',
  'Editor.DescriptionPlaceholder': 'Describe what this macro does..',
  'Editor.NoSteps': 'No steps yet.', 'Editor.GateActions': '{0} actions in the catalogue',
  'Editor.AddStep': 'Add step', 'Editor.AddChildStep': 'Add inside selected step',
  'Editor.AddRow': 'Add row', 'Editor.RemoveRow': 'Remove row', 'Editor.Browse': 'Browse',
  'Editor.StepEnabled': 'Enabled', 'Editor.DuplicateStep': 'Duplicate step',
  'Editor.Record': 'Record', 'Editor.StopRecording': 'Stop Recording',
  'Editor.ValueBuilder': 'Build value', 'Editor.Functions': 'Functions',
  'Editor.AddGroup': 'Add group', 'Editor.SubMacros': 'Sub-macros',
  'MacroInterface.Parameters': 'Parameters', 'MacroInterface.Results': 'Results',
  'MacroInterface.Name': 'Name', 'MacroInterface.Type': 'Kind',
  'MacroInterface.Required': 'Required', 'MacroInterface.Default': 'Default',
  'MacroInterface.Description': 'Description', 'MacroInterface.Remove': 'Remove',
  'MacroInterface.AddParameter': 'Add parameter', 'MacroInterface.AddResult': 'Add result',
  'MacroInterface.FieldName': 'Field name', 'MacroInterface.AddField': 'Add field',
  'MacroInterface.MoveUp': 'Move up', 'MacroInterface.MoveDown': 'Move down',
  'Editor.RecordActive': 'Recording: {0} captured', 'Editor.RecordEmpty': 'Nothing captured.',
  'Condition.All': 'All of these', 'Condition.Any': 'Any of these', 'Condition.None': 'None of these',
  'Editor.InsertValue': 'Insert value',
  'Add.StepComment': 'Comment', 'Add.StepTimeout': 'Timeout',
  'Add.StepRetry': 'Retries', 'Add.StepRetryDelay': 'Retry delay', 'Add.StepOnError': 'When it fails',
  'Add.OnError.Stop': 'Stop the macro', 'Add.OnError.Continue': 'Carry on with the next step',
  'Add.OnError.NextIteration': 'Skip to the next loop round', 'Add.OnError.AskUser': 'Ask me: retry, skip or stop',
  'Editor.OpenMacro': 'Open macro',
  'Editor.TriggerEntries': 'Trigger entries',
  'Editor.AddTrigger': 'Add entry', 'Editor.RemoveTrigger': 'Remove entry',
  'Editor.BindKey': 'Trigger / Bind Key :', 'Add.SetCombination': 'Set combination',
  'Add.PressKey': 'Press a key…', 'Palette.Undo': 'Undo', 'Palette.Redo': 'Redo',
  'Editor.Loop': 'Loop :', 'Editor.TriggerOnce': 'Trigger Once',
  'Editor.TriggerEnabled': 'Enabled', 'Editor.Schedule': 'Schedule :',
  'Editor.ScheduleTime': 'Time :', 'Editor.ScheduleTimeHint': 'HH:mm, or HH:mm:ss',
  'Schedule.Interval': 'At an Interval', 'Schedule.Daily': 'Every Day at a Time',
  'Schedule.Unit.Seconds': 'seconds', 'Schedule.Unit.Minutes': 'minutes',
  'Schedule.Unit.Hours': 'hours', 'Editor.WatchPath': 'File or Folder :',
  'Editor.WatchPathHint': 'notes.txt, or a folder', 'Editor.WatchChange': 'Change :',
  'Editor.WatchFilter': 'Names in the folder :', 'Editor.WatchSubfolders': 'Include the folders inside it',
  'FileChange.Any': 'Any change', 'FileChange.Created': 'Created',
  'FileChange.Changed': 'Changed', 'FileChange.Deleted': 'Deleted or renamed',
  'Editor.ProcessName': 'Program :', 'Editor.ProcessNameHint': 'notepad, or chrome',
  'Editor.ProcessWhen': 'When :', 'ProcessChange.Any': 'Started or stopped',
  'ProcessChange.Started': 'Started', 'ProcessChange.Stopped': 'Stopped',
  'Editor.Hotstring': 'Typed string', 'Editor.HotstringHint': 'Type a literal keyboard string',
  'Editor.ColorPosition': 'Colour position :', 'Editor.ColorTolerance': 'Colour tolerance :',
  'Editor.IdleSeconds': 'Left alone for :', 'ColorMatch.Matches': 'Colour Matches',
  'ColorMatch.NotMatches': 'Colour Not Matches', 'WindowPart.Title': 'Title',
  'WindowRule.Process': 'Process', 'WindowRule.How': 'Match by',
  'WindowRule.How.titleContains': 'Title contains', 'WindowRule.How.titleStartsWith': 'Title starts with',
  'WindowRule.How.titleEndsWith': 'Title ends with', 'WindowRule.How.titleEquals': 'Title is exactly',
  'WindowRule.How.titleRegex': 'Title matches a pattern', 'WindowRule.How.process': 'Process name contains',
  'WindowRule.How.processEquals': 'Process name is exactly', 'WindowRule.How.class': 'Window class contains',
  'WindowRule.How.classEquals': 'Window class is exactly', 'WindowRule.How.handle': 'Window handle',
  'WindowChange.Appeared': 'Appears', 'WindowChange.Disappeared': 'Goes away', 'WindowChange.Any': 'Either',
  'Loop.Press': 'Once, When Key Pressed (Press)', 'Loop.Hold': 'While Holding Key (Hold)',
  'Loop.Toggle': 'Until Key Pressed Again (Toggle)', 'Loop.Release': 'Once, When Key Released (Release)',
  'LocalVariables.Title': 'Local variables',
  'LocalVariables.New': 'New variable', 'LocalVariables.Initial': 'Starts at',
  'LocalVariables.InitialPlaceholder': 'A value, or an expression such as count + 1',
  'LocalVariables.Delete': 'Delete', 'GlobalVariables.Title': 'Global variables',
  'GlobalVariables.New': 'New variable', 'GlobalVariables.Remove': 'Remove',
  'GlobalVariables.Definition': 'Variable definition',
  'GlobalVariables.DefaultPlaceholder': 'Press + to pick a value',
  'Variable.Persist': 'Keep on this computer',
  'Run.Steps': 'Steps', 'Common.Save': 'Save', 'Common.Close': 'Close',
  'Editor.DeleteMessage': "Delete '{0}'? This cannot be undone.",
  'Common.Cancel': 'Cancel', 'Common.Ok': 'OK',
  'Main.CloseAllMessage': 'Remove every macro from the list? The saved package is not changed.',
  'Mcp.Title': 'MCP', 'Mcp.Control': 'Allow direct control',
  'Mcp.Run': 'Allow the AI to run macros', 'Mcp.SwitchOn': 'On', 'Mcp.SwitchOff': 'Off',
  'Mcp.CopyConfig': 'Copy client config', 'Mcp.GrantsNote': 'Saved as you flip them.',
  'Mcp.OpenAudit': 'Open call log',
  'Shell.Window.Minimize': 'Minimize', 'Shell.Library': 'Library',
  'Settings.GeneralCard': 'General', 'Settings.LoadLastProject': 'Reopen the last project',
  'Settings.SystemAtStart': 'Enable triggers at start', 'Settings.EnabledFirst': 'Enabled macros first',
  'Settings.Sounds': 'Sounds', 'Settings.SoundSystem': 'System sound', 'Settings.SoundMacro': 'Macro sound',
  'Settings.Reading': 'Screen reading', 'Settings.PreloadOcr': 'Preload OCR',
  'Settings.FramesCard': 'Pictures', 'Settings.FrameRateFollow': 'Follow the display',
  'Settings.FrameRate': 'Pictures a second', 'Settings.FrameCache': 'Reuse pictures',
  'Settings.FrameCacheAge': 'Keep for', 'Settings.InputCard': 'Input behaviour',
  'Settings.IgnoreOwnInput': 'Ignore own input', 'Settings.InputLimit': 'Input limit',
  'Settings.LogCount': 'Keep recent logs', 'Settings.Language': 'Language',
  'Settings.Language.English': 'English', 'Settings.Language.Chinese': 'Chinese',
  'Settings.Theme': 'Theme', 'Settings.ThemeAccent': 'Accent colour',
  'Settings.Hotkeys': 'Hotkeys',
  'Settings.SystemHotkey': 'System hotkey', 'Settings.RecordStopKey': 'Record stop key',
  'Settings.HotkeySet': 'Set shortcut', 'Settings.HotkeyPress': 'Press a key…',
  'Settings.HotkeyClear': 'Clear', 'Settings.Storage': 'Storage locations',
  'Settings.OpenFolder': 'Open', 'Settings.Driver': 'Driver-level input',
  'Settings.DriverAttachAtStart': 'Put virtual devices on at startup',
  'Settings.RunAsAdmin': 'Ask for administrator rights at startup',
  'Run.Title': 'Run and Debug', 'Run.Run': 'Run',
  'Run.NativeNote': 'Choose a macro to load into the run panel: controls, steps, log and variables all live here.',
  'Run.Variables': 'Variables',
  'Run.Step': 'Step', 'Run.Continue': 'Continue', 'Run.SkipStep': 'Skip this step once',
  'Run.Stop': 'Stop', 'Run.AskRetry': 'Retry', 'Run.AskSkip': 'Skip it',
  'Run.AskStop': 'Stop',
  'Run.NotLoaded': 'Load a macro into the run panel first.',
  'Run.UnknownCommand': 'That run command is not known.',
  'Run.UnknownStep': 'That step is not in the loaded macro.',
  'Run.UnknownArgument': 'That argument is not declared by the loaded macro.',
  'Settings.Advanced': 'Advanced (native)', 'Settings.WindowRules': 'Window rules',
  'Settings.CaptureCard': 'Picture source', 'Settings.CaptureWindow': 'Window pictures',
  'Settings.CaptureScreen': 'Desktop pictures',
  'Theme.Mode.Dark': 'Dark', 'Theme.Mode.Light': 'Light',
  'Theme.Mode.Custom': 'Custom',

  'Library.Heading': 'Shared macros', 'Library.Intro': 'Macros every project can copy.',
  'Library.Empty': 'The library has no macros yet.', 'Library.Delete': 'Remove from library',
  'Library.Failed': 'The library could not be read.',
  'Library.AddFromProject': 'Add selected project macro',
  'Library.ReplaceFromProject': 'Replace with selected project macro',
  'Shell.Window.Maximize': 'Maximize', 'Shell.Window.Restore': 'Restore',
  'Shell.Window.Close': 'Close', 'Shell.Window.Pin': 'Keep on top',
  'Shell.Window.Unpin': 'Stop keeping on top', 'Main.Title': 'WhaleGenie',
};

const chinese: Record<string, string> = {
  'Shell.Home': '首页', 'Shell.Macros': '宏', 'Shell.Editor': '编辑器',
  'Shell.Run': '运行调试', 'Shell.Settings': '设置',
  'Shell.UntitledProject': '未命名项目', 'Shell.Field.Version': '版本',
  'Shell.Field.Language': '界面语言', 'Shell.Field.Project': '项目',
  'Shell.Field.Dirty': '有未保存修改', 'Shell.Field.Clean': '已保存',
  'Shell.Field.Revision': '修订号', 'Shell.Field.Macros': '宏数量',
  'Shell.Home.Heading': '欢迎使用鲸灵',
  'Shell.Home.Intro': '界面正在迁移到网页。',
  'Shell.Status.Web': '已在网页壳中', 'Shell.Status.Native': '仍是原生界面',
  'Shell.Status.Planned': '规划中',
  'Shell.Placeholder.Body': '这一区域仍由原生窗口承担。',
  'Shell.Navigation': '导航', 'Shell.OpenNative': '打开原生窗口',
  'Shell.File': '文件', 'Shell.About': '关于', 'Shell.Help': '帮助', 'Shell.Manual': '使用手册',
  'Main.Dock.All': '全部', 'Main.Dock.Keys': '按键', 'Main.Dock.Interface': '界面',
  'Main.Dock.Hotstring': '字串', 'Main.Dock.Menu': '菜单', 'Main.Dock.Wheel': '轮盘',
  'Main.Dock.Events': '事件', 'Main.SearchPlaceholder': '输入以搜索..',
  'Main.EmptyTitle': '还没有宏。', 'Main.EmptyHint': '点击下方的“新建宏”创建第一个宏。',
  'Main.NoMatch': '没有宏匹配你输入的内容。', 'Main.AddMacro': '新建宏',
  'Main.MacroRunning': '运行中', 'Main.EnableMacro': '启用宏', 'Main.DisableMacro': '停用宏',
  'Main.SystemEnabled': '已启用', 'Main.SystemDisabled': '已停用',
  'Main.RunDebug': '运行调试',
  'Main.GroupAll': '全部分组', 'Main.Ungrouped': '未分组',
  'Main.OpenProject': '打开宏项目', 'Main.SaveProject': '保存宏项目',
  'Main.Import': '导入…',
  'Main.SaveProjectAs': '宏项目另存为', 'Main.ReloadCurrent': '重新加载当前宏',
  'Main.CloseAll': '全部关闭', 'Main.CloseAllTitle': '关闭全部宏',
  'Main.EditTip': '编辑', 'Main.DeleteTip': '删除',
  'Editor.DeleteTitle': '删除宏', 'Editor.Delete': '删除',
  'Editor.NamePlaceholder': '给这个宏起个名字..', 'Editor.GroupPlaceholder': '分组',
  'Editor.DescriptionPlaceholder': '说明这个宏做什么..',
  'Editor.NoSteps': '还没有步骤。', 'Editor.GateActions': '动作目录共 {0} 项',
  'Editor.AddStep': '添加步骤', 'Editor.AddChildStep': '添加到选中步骤内',
  'Editor.AddRow': '添加行', 'Editor.RemoveRow': '删除行', 'Editor.Browse': '浏览',
  'Editor.StepEnabled': '启用', 'Editor.DuplicateStep': '复制步骤',
  'Editor.Record': '开始录制', 'Editor.StopRecording': '停止录制',
  'Editor.ValueBuilder': '构建取值', 'Editor.Functions': '函数',
  'Editor.AddGroup': '添加组', 'Editor.SubMacros': '子宏',
  'MacroInterface.Parameters': '形参', 'MacroInterface.Results': '返回值',
  'MacroInterface.Name': '名字', 'MacroInterface.Type': '类型',
  'MacroInterface.Required': '必填', 'MacroInterface.Default': '默认值',
  'MacroInterface.Description': '说明', 'MacroInterface.Remove': '删除',
  'MacroInterface.AddParameter': '添加形参', 'MacroInterface.AddResult': '添加结果',
  'MacroInterface.FieldName': '字段名', 'MacroInterface.AddField': '添加字段',
  'MacroInterface.MoveUp': '上移', 'MacroInterface.MoveDown': '下移',
  'Editor.RecordActive': '录制中：已捕获 {0} 个事件', 'Editor.RecordEmpty': '这次录制没有捕获到任何操作。',
  'Condition.All': '全部成立', 'Condition.Any': '任一成立', 'Condition.None': '全部不成立',
  'Editor.InsertValue': '插入取值',
  'Add.StepComment': '备注', 'Add.StepTimeout': '超时',
  'Add.StepRetry': '重试', 'Add.StepRetryDelay': '重试间隔', 'Add.StepOnError': '失败时',
  'Add.OnError.Stop': '停止宏', 'Add.OnError.Continue': '继续下一步',
  'Add.OnError.NextIteration': '跳到下一轮循环', 'Add.OnError.AskUser': '询问：重试、跳过或停止',
  'Editor.OpenMacro': '打开宏',
  'Editor.TriggerEntries': '触发入口',
  'Editor.AddTrigger': '添加入口', 'Editor.RemoveTrigger': '删除入口',
  'Editor.BindKey': '触发 / 绑定按键：', 'Add.SetCombination': '设置组合键',
  'Add.PressKey': '请按一个键…', 'Palette.Undo': '撤销', 'Palette.Redo': '重做',
  'Editor.Loop': '循环：', 'Editor.TriggerOnce': '仅触发一次', 'Editor.TriggerEnabled': '启用',
  'Editor.Schedule': '计划：', 'Editor.ScheduleTime': '时间：',
  'Editor.ScheduleTimeHint': 'HH:mm 或 HH:mm:ss', 'Schedule.Interval': '按间隔重复',
  'Schedule.Daily': '每天固定时间', 'Schedule.Unit.Seconds': '秒',
  'Schedule.Unit.Minutes': '分钟', 'Schedule.Unit.Hours': '小时',
  'Editor.WatchPath': '文件或文件夹：', 'Editor.WatchPathHint': 'notes.txt，或一个文件夹',
  'Editor.WatchChange': '变化：', 'Editor.WatchFilter': '文件夹里匹配的名字：',
  'Editor.WatchSubfolders': '包含里面的子文件夹', 'FileChange.Any': '任何变化',
  'FileChange.Created': '创建', 'FileChange.Changed': '修改', 'FileChange.Deleted': '删除或重命名',
  'Editor.ProcessName': '程序：', 'Editor.ProcessNameHint': 'notepad、chrome，不用写 .exe',
  'Editor.ProcessWhen': '时机：', 'ProcessChange.Any': '启动或退出',
  'ProcessChange.Started': '启动', 'ProcessChange.Stopped': '退出',
  'Editor.Hotstring': '键盘字串', 'Editor.HotstringHint': '最多 40 个键盘字面字符',
  'Editor.ColorPosition': '屏幕取色位置：', 'Editor.ColorTolerance': '颜色容差：',
  'Editor.IdleSeconds': '空闲时间：', 'ColorMatch.Matches': '颜色匹配',
  'ColorMatch.NotMatches': '颜色不匹配', 'WindowPart.Title': '标题',
  'WindowRule.Process': '进程名', 'WindowRule.How': '匹配方式',
  'WindowRule.How.titleContains': '标题包含', 'WindowRule.How.titleStartsWith': '标题开头',
  'WindowRule.How.titleEndsWith': '标题结尾', 'WindowRule.How.titleEquals': '标题等于',
  'WindowRule.How.titleRegex': '标题正则', 'WindowRule.How.process': '进程名包含',
  'WindowRule.How.processEquals': '进程名等于', 'WindowRule.How.class': '窗口类包含',
  'WindowRule.How.classEquals': '窗口类等于', 'WindowRule.How.handle': '窗口句柄',
  'WindowChange.Appeared': '出现', 'WindowChange.Disappeared': '消失', 'WindowChange.Any': '出现或消失',
  'Loop.Press': '按下时执行一次（按下）', 'Loop.Hold': '按住期间循环（长按）',
  'Loop.Toggle': '再次按键时停止（切换）', 'Loop.Release': '松开时执行一次（松开）',
  'LocalVariables.Title': '局部变量',
  'LocalVariables.New': '新建变量', 'LocalVariables.Initial': '初始值',
  'LocalVariables.InitialPlaceholder': '直接填值，或写表达式，例如 count + 1',
  'LocalVariables.Delete': '删除', 'GlobalVariables.Title': '全局变量',
  'GlobalVariables.New': '新建变量', 'GlobalVariables.Remove': '删除',
  'GlobalVariables.Definition': '变量定义',
  'GlobalVariables.DefaultPlaceholder': '按「+」选一个值',
  'Variable.Persist': '持久化保存',
  'Run.Steps': '步骤', 'Common.Save': '保存', 'Common.Close': '关闭',
  'Editor.DeleteMessage': '确定删除“{0}”？此操作无法撤销。',
  'Common.Cancel': '取消', 'Common.Ok': '确定',
  'Main.CloseAllMessage': '从列表中移除所有宏？已保存的宏包不会被修改。',
  'Mcp.Title': 'MCP', 'Mcp.Control': '允许直接控制',
  'Mcp.Run': '允许 AI 运行宏', 'Mcp.SwitchOn': '开', 'Mcp.SwitchOff': '关',
  'Mcp.CopyConfig': '复制客户端配置', 'Mcp.GrantsNote': '改一下就存下来了。',
  'Mcp.OpenAudit': '查看调用记录',
  'Shell.Window.Minimize': '最小化', 'Shell.Library': '宏库',
  'Settings.GeneralCard': '常规', 'Settings.LoadLastProject': '启动时重开上次工程',
  'Settings.SystemAtStart': '启动时启用触发', 'Settings.EnabledFirst': '启用的宏排在前面',
  'Settings.Sounds': '声音', 'Settings.SoundSystem': '系统声音', 'Settings.SoundMacro': '宏声音',
  'Settings.Reading': '屏幕读取', 'Settings.PreloadOcr': '预加载 OCR',
  'Settings.FramesCard': '画面', 'Settings.FrameRateFollow': '跟随屏幕刷新率',
  'Settings.FrameRate': '每秒画面数', 'Settings.FrameCache': '复用画面',
  'Settings.FrameCacheAge': '保留时长', 'Settings.InputCard': '输入行为',
  'Settings.IgnoreOwnInput': '忽略本程序发出的输入', 'Settings.InputLimit': '输入限制',
  'Settings.LogCount': '保留最近日志', 'Settings.Language': '语言',
  'Settings.Language.English': 'English', 'Settings.Language.Chinese': '中文',
  'Settings.Theme': '主题', 'Settings.ThemeAccent': '强调色',
  'Settings.Hotkeys': '热键',
  'Settings.SystemHotkey': '系统热键', 'Settings.RecordStopKey': '录制停止键',
  'Settings.HotkeySet': '设置快捷键', 'Settings.HotkeyPress': '请按一个键…',
  'Settings.HotkeyClear': '清除', 'Settings.Storage': '存储位置',
  'Settings.OpenFolder': '打开', 'Settings.Driver': '驱动级输入',
  'Settings.DriverAttachAtStart': '启动时预装虚拟键鼠',
  'Settings.RunAsAdmin': '启动时请求管理员权限',
  'Run.Title': '运行与调试', 'Run.Run': '运行',
  'Run.NativeNote': '选一个宏载入运行面板；运行控制、步骤、日志与变量都在这里。',
  'Run.Variables': '变量监视',
  'Run.Step': '单步', 'Run.Continue': '继续', 'Run.SkipStep': '跳过这一步（这一次）',
  'Run.Stop': '停止', 'Run.AskRetry': '重试', 'Run.AskSkip': '跳过这一步',
  'Run.AskStop': '停止宏',
  'Run.NotLoaded': '请先把宏载入运行面板。',
  'Run.UnknownCommand': '不认识这条运行命令。',
  'Run.UnknownStep': '载入的宏里没有这一步。',
  'Run.UnknownArgument': '载入的宏没有声明这个参数。',
  'Settings.Advanced': '高级设置（原生）', 'Settings.WindowRules': '窗口规则',
  'Settings.CaptureCard': '画面来源', 'Settings.CaptureWindow': '窗口画面',
  'Settings.CaptureScreen': '桌面画面',
  'Theme.Mode.Dark': '深色', 'Theme.Mode.Light': '浅色',
  'Theme.Mode.Custom': '自定义',

  'Library.Heading': '共享宏', 'Library.Intro': '所有工程都能复制的可调用宏。',
  'Library.Empty': '宏库还没有宏。', 'Library.Delete': '从宏库删除',
  'Library.Failed': '无法读取宏库。',
  'Library.AddFromProject': '添加选中的工程宏',
  'Library.ReplaceFromProject': '用选中的工程宏覆盖',
  'Shell.Window.Maximize': '最大化', 'Shell.Window.Restore': '还原',
  'Shell.Window.Close': '关闭', 'Shell.Window.Pin': '窗口置顶',
  'Shell.Window.Unpin': '取消置顶', 'Main.Title': '鲸灵',
};

export class MockTransport implements BridgeTransport {
  private receive?: (message: unknown) => void;

  /** 页面发来的请求按序记录，调试与 e2e 断言用。 */
  readonly seen: string[] = [];

  private info: AppInfo = {
    name: 'WhaleGenie',
    version: '0.0.0-mock',
    language: 'zh-Hans',
    debug: true,
    project: { path: null, name: null, untitled: true, dirty: false, revision: 0, macros: 0 },
  };

  private theme: ThemeState = { mode: 'dark', baseLight: false, accentHex: null };

  private windowState: WindowState = { state: 'normal', topmost: false };

  /** 等待删除确认的宏名；dialog.respond 带着答案回来时用。 */
  private pendingDelete: string | null = null;

  private editor: EditorSnapshot | null = null;

  private runState = {
    loaded: '', status: '', statusKind: 'Ready', busy: false, paused: false,
    canRun: false, canStep: false, canStop: false,
    question: '', wantsTypedAnswer: false, answerText: '', isAskingUser: false, isAsking: false,
    arguments: [] as { name: string; label: string; hint: string; required: boolean;
      value: string; given: boolean; missing: boolean }[],
    look: null as { stepId: string; type: string; looking: string; image: string } | null,
    globals: [] as { name: string; type: string; value: string; defaultValue: string }[],
    locals: [] as { name: string; macro: string; type: string }[],
    steps: [] as { id: string; label: string; detail: string; depth: number; shown: boolean;
      container: boolean; current: boolean; breakpoint: boolean; canBreak: boolean;
      time: string; failure: string; condition: string; hitTimes: number; expression: string }[],
    log: [] as { time: string; level: string; text: string }[],
  };

  private settingsState = {
    loadLastProjectAtStart: true, systemAtStart: false, enabledFirst: true,
    ignoreOwnInput: true, soundSystem: true, soundMacro: false, preloadOcr: false,
    inputRate: 20, frameRateFollow: true, frameRate: 60, frameRateMax: 60,
    frameCache: false, frameCacheAge: 100, logCount: 100,
    language: 'zh-Hans',
    systemHotkey: 'Ctrl+F12', recordStopKey: 'Ctrl+F10',
    systemHotkeyProblem: '', recordStopProblem: '',
    storage: [
      { name: 'State', what: 'Settings and logs', where: 'C:\\mock\\state', folder: 'C:\\mock\\state' },
    ],
    driverStatus: 'Ready', driverReady: true, attachDriverAtStart: false,
    runAsAdmin: false, adminStatus: 'Running normally',
    windowWay: 'Auto', screenWay: 'Auto',
    windowWays: [{ value: 'Auto', label: 'Automatic' }, { value: 'PrintWindow', label: 'PrintWindow' }],
    screenWays: [{ value: 'Auto', label: 'Automatic' }, { value: 'Gdi', label: 'GDI' }],
    menu: demoMenu,
    wheel: demoWheel,
  };

  private libraryEntries: { id: string; name: string; version: number; description: string }[] = [
    { id: 'library-1', name: '打开记事本', version: 1, description: '公共示例宏' },
  ];

  private recording = false;

  private capturedCount = 0;

  private mcp: McpState = {
    allowControl: false,
    allowRun: false,
    config: '{\n  "mcpServers": {\n    "whalegenie": {\n      "command": "WhaleGenie.exe",\n      "args": ["--mcp"]\n    }\n  }\n}',
    auditFile: '/mock/logs/mcp-2026-11-21.log',
  };

  private projectState: ProjectState = {
    path: null,
    name: null,
    untitled: true,
    dirty: false,
    revision: 0,
    systemEnabled: false,
    views: ['All', 'Keys', 'Interface', 'Hotstring', 'Menu', 'Wheel', 'Events'],
    groups: ['办公'],
    macros: [
      {
        name: '自动保存', group: '办公', description: '每分钟存一次', enabled: true, running: false,
        triggerSummary: '定时：每 1 分钟', triggerActions: '', triggerPreview: '每 1 分钟',
        views: ['All', 'Events'],
      },
      {
        name: '游戏连招', group: '', description: '', enabled: false, running: false,
        triggerSummary: '键：F8', triggerActions: '', triggerPreview: 'F8',
        views: ['All', 'Keys'],
      },
    ],
  };

  constructor() {
    const asked = new URLSearchParams(window.location.search).get('lang');
    if (asked === 'en') {
      this.info.language = 'en';
    }
  }

  onMessage(handler: (message: unknown) => void): void {
    this.receive = handler;
  }

  /** 测试与演练用：假装宿主推来一个事件。 */
  push(event: string, payload: unknown): void {
    this.answer({ event, payload });
  }

  send(envelope: unknown): void {
    const request = envelope as { id: number; method: string; params?: Record<string, unknown> };
    const method = request.method;
    this.seen.push(method);
    const params = request.params ?? {};

    let result: unknown;
    switch (method) {
      case 'bridge.ready':
        break;
      case 'app.info':
        result = {
          ...this.info,
          project: { ...this.info.project, macros: this.projectState.macros.length },
        };
        break;
      case 'strings.bundle':
        result = this.bundle();
        break;
      case 'theme.get':
        result = this.theme;
        break;
      case 'theme.set': {
        const mode = String(params['mode'] ?? 'dark') as ThemeState['mode'];
        const accentHex = (params['accentHex'] as string | null) ?? null;
        this.theme = {
          mode,
          baseLight: params['baseLight'] === true,
          accentHex,
          accentUsable: true,
          fill: accentHex ?? '#c8401c',
          surface: accentHex ?? '#f5e6e2',
          text: accentHex ?? '#c8401c',
          defaultAccent: '#c8401c',
        };
        this.push('theme.changed', this.theme);
        result = this.theme;
        break;
      }
      case 'window.minimize':
        this.windowState = { ...this.windowState, state: 'minimized' };
        this.push('window.changed', this.windowState);
        break;
      case 'window.maximize':
        this.windowState = {
          ...this.windowState,
          state: this.windowState.state === 'maximized' ? 'normal' : 'maximized',
        };
        this.push('window.changed', this.windowState);
        break;
      case 'window.close':
        break;
      case 'window.pin':
        this.windowState = { ...this.windowState, topmost: !this.windowState.topmost };
        this.push('window.changed', this.windowState);
        break;
      case 'window.drag':
      case 'window.focus':
      case 'app.quit':
      case 'app.openExternal':
        break;
      case 'app.openRun':
        this.push('run.openRequested', { name: String(params['name'] ?? '') });
        break;
      case 'run.state':
        result = this.runState;
        break;
      case 'run.load':
        this.runState = {
          ...this.runState,
          loaded: String(params['name'] ?? ''),
          status: 'Loaded', canRun: true, canStep: true, busy: false, paused: false,
          arguments: [{ name: 'target', label: 'target', hint: 'Text', required: true,
            value: '', given: false, missing: true }],
          globals: [{ name: 'count', type: 'Number', value: '3', defaultValue: '0' }],
          locals: [{ name: 'found', macro: 'Demo', type: 'Boolean' }],
          steps: [
            { id: 's1', label: 'Step one', detail: 'action.key', depth: 0, shown: true,
              container: false, current: false, breakpoint: false, canBreak: true,
              time: '', failure: '', condition: '', hitTimes: 1, expression: '' },
            { id: 's2', label: 'Step two', detail: 'action.key', depth: 0, shown: true,
              container: false, current: false, breakpoint: false, canBreak: true,
              time: '', failure: '', condition: '', hitTimes: 1, expression: '' },
          ],
        };
        result = this.runState;
        break;
      case 'run.command':
        this.runState = {
          ...this.runState,
          status: String(params['command'] ?? ''),
          busy: params['command'] === 'run',
          canStop: params['command'] === 'run',
          canRun: params['command'] !== 'run',
          canStep: params['command'] !== 'run',
        };
        result = this.runState;
        break;
      case 'run.answer':
        result = this.runState;
        break;
      case 'run.argument':
        this.runState = {
          ...this.runState,
          arguments: this.runState.arguments.map((argument) => argument.name === params['name']
            ? { ...argument, value: String(params['value'] ?? ''),
                given: String(params['value'] ?? '').length > 0,
                missing: false }
            : argument),
        };
        result = this.runState;
        break;
      case 'run.breakpointRule':
        this.runState = {
          ...this.runState,
          steps: this.runState.steps.map((step) => step.id === params['id']
            ? { ...step, hitTimes: Number(params['hitTimes'] ?? 1),
                expression: String(params['expression'] ?? '') }
            : step),
        };
        result = this.runState;
        break;
      case 'run.breakpoint':
        this.runState = {
          ...this.runState,
          steps: this.runState.steps.map((step) => step.id === params['id']
            ? { ...step, breakpoint: !step.breakpoint }
            : step),
        };
        result = this.runState;
        break;
      case 'settings.state':
        result = this.settingsState;
        break;
      case 'settings.update': {
        const field = String(params['field'] ?? '');
        const value = String(params['value'] ?? '');
        this.settingsState = {
          ...this.settingsState,
          [field]: field === 'language'
            || field === 'systemHotkey' || field === 'recordStopKey'
            || field === 'windowWay' || field === 'screenWay'
            ? value
            : value === 'true' ? true : value === 'false' ? false : Number(value),
        };
        result = this.settingsState;
        if (field === 'language') {
          this.info = {
            ...this.info,
            language: value,
          };
          this.push('strings.changed', this.bundle());
        }
        break;
      }
      case 'settings.windows.list':
        result = {
          windows: demoWindows,
          hows: [
            { value: 'titleContains', label: '标题包含' },
            { value: 'titleEquals', label: '标题等于' },
            { value: 'process', label: '进程名包含' },
            { value: 'class', label: '窗口类包含' },
            { value: 'handle', label: '窗口句柄（当前实例）' },
          ],
        };
        break;
      case 'settings.windows.pick':
        result = demoWindows[0];
        break;
      case 'settings.menu.capture': {
        const binding = String(params['binding'] ?? '');
        this.settingsState = {
          ...this.settingsState,
          menu: {
            ...this.settingsState.menu,
            launcher: binding,
            display: binding,
            isMiddle: binding === 'Mouse Middle',
            isCustom: binding !== 'Mouse Middle',
            status: binding ? '已设置' : '未设置',
          },
        };
        result = this.settingsState;
        break;
      }
      case 'settings.menu.clear':
        this.settingsState = {
          ...this.settingsState,
          menu: {
            ...this.settingsState.menu,
            launcher: '', display: '无', isMiddle: false, isCustom: false, status: '未设置',
          },
        };
        result = this.settingsState;
        break;
      case 'settings.menu.rule.add': {
        const rule = params['rule'] as WindowRuleState;
        this.settingsState = {
          ...this.settingsState,
          menu: {
            ...this.settingsState.menu,
            rules: [...this.settingsState.menu.rules, {
              key: 'rule-' + Date.now(), summary: rule.title || rule.process || '窗口规则',
              pageCountLabel: '1 页', isDefault: false, isBlocked: false, canAddPage: true, rule,
            }],
          },
        };
        result = this.settingsState;
        break;
      }
      case 'settings.menu.rule.edit': {
        const rule = params['rule'] as WindowRuleState;
        this.settingsState = {
          ...this.settingsState,
          menu: {
            ...this.settingsState.menu,
            rules: this.settingsState.menu.rules.map((row) => row.key === params['key']
              ? { ...row, rule, summary: rule.title || rule.process || '窗口规则' }
              : row),
          },
        };
        result = this.settingsState;
        break;
      }
      case 'settings.menu.rule.remove':
        this.settingsState = {
          ...this.settingsState,
          menu: {
            ...this.settingsState.menu,
            rules: this.settingsState.menu.rules.filter((row) => row.key !== params['key']),
          },
        };
        result = this.settingsState;
        break;
      case 'settings.menu.rule.addPage':
        result = this.settingsState;
        break;
      case 'settings.menu.rule.move': {
        const step = Number(params['step'] ?? 0);
        const rules = [...this.settingsState.menu.rules];
        const at = rules.findIndex((row) => row.key === params['key']);
        const to = Math.max(0, Math.min(rules.length - 1, at + step));
        if (at >= 0 && to !== at) {
          const [row] = rules.splice(at, 1);
          if (row) {
            rules.splice(to, 0, row);
          }
        }
        this.settingsState = { ...this.settingsState, menu: { ...this.settingsState.menu, rules } };
        result = this.settingsState;
        break;
      }
      case 'settings.menu.rule.moveTo': {
        const rules = [...this.settingsState.menu.rules];
        const from = rules.findIndex((row) => row.key === params['key']);
        const to = rules.findIndex((row) => row.key === params['target']);
        if (from >= 0 && to >= 0) {
          const [row] = rules.splice(from, 1);
          if (row) {
            rules.splice(to, 0, row);
          }
        }
        this.settingsState = { ...this.settingsState, menu: { ...this.settingsState.menu, rules } };
        result = this.settingsState;
        break;
      }
      case 'settings.wheel.update': {
        const field = String(params['field'] ?? '');
        const value = String(params['value'] ?? '');
        const next: Record<string, unknown> = { ...this.settingsState.wheel };
        if (['diameter', 'fontSize', 'thirdCapacity', 'timeoutMs'].includes(field)) {
          next[field] = Number(value);
        } else if (field === 'enabled') {
          next[field] = value === 'true';
        } else {
          next[field] = value === 'true' ? true : value === 'false' ? false : value;
        }
        this.settingsState = {
          ...this.settingsState,
          wheel: next as unknown as SettingsWheelState,
        };
        result = this.settingsState;
        break;
      }
      case 'settings.wheel.launcher': {
        const launcher = String(params['value'] ?? '');
        this.settingsState = {
          ...this.settingsState,
          wheel: {
            ...this.settingsState.wheel,
            launcher,
            enabled: launcher.length > 0,
            launcherStatus: launcher ? '就绪' : '未设置',
          },
        };
        result = this.settingsState;
        break;
      }
      case 'settings.wheel.enabled': {
        const enabled = params['value'] === true;
        this.settingsState = {
          ...this.settingsState,
          wheel: {
            ...this.settingsState.wheel,
            enabled,
            launcher: enabled ? (this.settingsState.wheel.launcher || 'Mouse Right') : '',
          },
        };
        result = this.settingsState;
        break;
      }
      case 'settings.wheel.enableSystem':
        this.settingsState = {
          ...this.settingsState,
          wheel: { ...this.settingsState.wheel, isSystemOff: false },
        };
        result = this.settingsState;
        break;
      case 'settings.wheel.repeat': {
        const key = String(params['binding'] ?? '');
        this.settingsState = {
          ...this.settingsState,
          wheel: { ...this.settingsState.wheel, repeatKey: key, repeatDisplay: key },
        };
        result = this.settingsState;
        break;
      }
      case 'settings.wheel.scene.select':
        this.settingsState = {
          ...this.settingsState,
          wheel: { ...this.settingsState.wheel, selectedScene: String(params['key'] ?? 'global') },
        };
        result = this.settingsState;
        break;
      case 'settings.wheel.scene.add': {
        const rule = params['rule'] as WindowRuleState;
        const key = 'scene-' + Date.now();
        this.settingsState = {
          ...this.settingsState,
          wheel: {
            ...this.settingsState.wheel,
            selectedScene: key,
            scenes: [...this.settingsState.wheel.scenes, {
              key, summary: rule.title || rule.process || '窗口规则', isGlobal: false, rule,
            }],
          },
        };
        result = this.settingsState;
        break;
      }
      case 'settings.wheel.scene.edit': {
        const rule = params['rule'] as WindowRuleState;
        this.settingsState = {
          ...this.settingsState,
          wheel: {
            ...this.settingsState.wheel,
            scenes: this.settingsState.wheel.scenes.map((scene) => scene.key === params['key']
              ? { ...scene, rule, summary: rule.title || rule.process || '窗口规则' }
              : scene),
          },
        };
        result = this.settingsState;
        break;
      }
      case 'settings.wheel.scene.remove':
        this.settingsState = {
          ...this.settingsState,
          wheel: {
            ...this.settingsState.wheel,
            selectedScene: 'global',
            scenes: this.settingsState.wheel.scenes.filter((scene) => scene.key !== params['key']),
          },
        };
        result = this.settingsState;
        break;
      case 'settings.wheel.scene.move': {
        const scenes = [...this.settingsState.wheel.scenes];
        const at = scenes.findIndex((scene) => scene.key === this.settingsState.wheel.selectedScene);
        const to = Math.max(1, Math.min(scenes.length - 1, at + Number(params['step'] ?? 0)));
        if (at > 0 && to !== at) {
          const [scene] = scenes.splice(at, 1);
          if (scene) {
            scenes.splice(to, 0, scene);
          }
        }
        this.settingsState = {
          ...this.settingsState,
          wheel: { ...this.settingsState.wheel, scenes },
        };
        result = this.settingsState;
        break;
      }
      case 'settings.wheel.slot.set': {
        const ring = String(params['ring'] ?? '');
        const index = Number(params['index'] ?? 0);
        const macro = String(params['macro'] ?? '');
        const note = String(params['note'] ?? '');
        const icon = params['icon'] as { kind: string; value: string } | null;
        const update = (slot: SettingsWheelSlot): SettingsWheelSlot => ({
          ...slot,
          macro,
          note,
          label: note || macro,
          icon,
          inherited: false,
          overridden: macro.length > 0,
          empty: macro.length === 0,
          tone: macro.length === 0 ? 'empty' : 'override',
        });
        this.settingsState = {
          ...this.settingsState,
          wheel: {
            ...this.settingsState.wheel,
            slots: this.settingsState.wheel.slots.map((slot) =>
              slot.ring === ring && slot.index === index ? update(slot) : slot),
          },
        };
        result = this.settingsState;
        break;
      }
      case 'settings.wheel.pickIcon':
        result = { kind: 'builtin', value: 'circle' };
        break;
      case 'app.about':
        result = {
          projectUrl: 'https://github.com/ilpoint/WhaleGenie',
          notices: '第三方组件\n\n- Avalonia — MIT\n- Lit — BSD-3-Clause',
        };
        break;
      case 'settings.openFolder':
        result = null;
        break;
      case 'library.state':
        result = { entries: this.libraryEntries, error: null };
        break;
      case 'library.delete':
        this.libraryEntries = this.libraryEntries.filter((entry) => entry.id !== String(params['id']));
        result = { entries: this.libraryEntries, error: null };
        break;
      case 'library.add': {
        const macro = this.projectState.macros.find((entry) => entry.name === params['name']);
        this.libraryEntries = [...this.libraryEntries, {
          id: 'library-' + String(this.libraryEntries.length + 1),
          name: String(params['name'] ?? macro?.name ?? ''),
          version: 1,
          description: macro?.description ?? '',
        }];
        result = { entries: this.libraryEntries, error: null };
        break;
      }
      case 'library.replace':
        this.libraryEntries = this.libraryEntries.map((entry) => entry.id === params['id']
          ? { ...entry, name: String(params['name'] ?? entry.name), version: entry.version + 1 }
          : entry);
        result = { entries: this.libraryEntries, error: null };
        break;
      case 'editor.sessionNew':
        this.editor = {
          name: '未命名宏',
          description: '',
          group: '',
          subMacros: [],
          callableParameters: [],
          callableResults: [],
          followInputLimit: true,
          inputLimit: 20,
          toggleKey: '',
          dirty: false,
          stepCount: 0,
          steps: [],
          triggers: [],
          variables: [],
          globals: [],
          valueChoices: [
            { name: 'found', type: 'text', origin: 'Local', source: 'find.text', description: '' },
          ],
          valueFunctions: [
            { name: 'upper', signature: 'upper(text)', description: 'Capitals', group: 'Text' },
            { name: 'length', signature: 'length(value)', description: 'How many', group: 'Text' },
          ],
          conditionCompares: [
            { value: 'Equal', label: '=' },
            { value: 'NotEqual', label: '≠' },
            { value: 'Greater', label: '>' },
            { value: 'Less', label: '<' },
            { value: 'Holds', label: 'holds' },
          ],
          variableTypes: [
            { value: 'text', label: '文本' },
            { value: 'number', label: '数字' },
            { value: 'bool', label: '布尔' },
            { value: 'record', label: '记录' },
          ],
          triggerKinds: [
            { value: 'Key', label: '按键' },
            { value: 'Timer', label: '定时' },
            { value: 'Color', label: '颜色' },
            { value: 'File', label: '文件' },
            { value: 'Process', label: '程序' },
            { value: 'Hotstring', label: '字串' },
            { value: 'Idle', label: '空闲' },
            { value: 'Window', label: '窗口' },
            { value: 'Interface', label: '窗口内接口' },
          ],
          canUndo: false,
          canRedo: false,
        };
        result = this.editor;
        break;
      case 'editor.sessionOpen': {
        const name = params['name'] ? String(params['name']) : this.projectState.macros[0]?.name;
        const macro = this.projectState.macros.find((entry) => entry.name === name);
        this.editor = {
          name: macro?.name ?? '未命名宏',
          description: macro?.description ?? '',
          group: macro?.group ?? '',
          subMacros: ['辅助宏'],
          callableParameters: [],
          callableResults: [],
          followInputLimit: true,
          inputLimit: 20,
          toggleKey: '',
          dirty: false,
          stepCount: 0,
          steps: [],
          triggers: [],
          variables: [],
          globals: [],
          valueChoices: [
            { name: 'found', type: 'text', origin: 'Local', source: 'find.text', description: '' },
          ],
          valueFunctions: [
            { name: 'upper', signature: 'upper(text)', description: 'Capitals', group: 'Text' },
            { name: 'length', signature: 'length(value)', description: 'How many', group: 'Text' },
          ],
          conditionCompares: [
            { value: 'Equal', label: '=' },
            { value: 'NotEqual', label: '≠' },
            { value: 'Greater', label: '>' },
            { value: 'Less', label: '<' },
            { value: 'Holds', label: 'holds' },
          ],
          variableTypes: [
            { value: 'text', label: '文本' },
            { value: 'number', label: '数字' },
            { value: 'bool', label: '布尔' },
            { value: 'record', label: '记录' },
          ],
          triggerKinds: [
            { value: 'Key', label: '按键' },
            { value: 'Timer', label: '定时' },
            { value: 'Color', label: '颜色' },
            { value: 'File', label: '文件' },
            { value: 'Process', label: '程序' },
            { value: 'Hotstring', label: '字串' },
            { value: 'Idle', label: '空闲' },
            { value: 'Window', label: '窗口' },
            { value: 'Interface', label: '窗口内接口' },
          ],
          canUndo: false,
          canRedo: false,
        };
        result = this.editor;
        break;
      }
      case 'editor.sessionState':
        result = this.editor;
        break;
      case 'editor.sessionTabs': {
        const tabs = this.projectState.macros.map((macro) => ({
          key: macro.name,
          name: macro.name,
          dirty: false,
        }));
        if (this.editor && !tabs.some((tab) => tab.name === this.editor!.name)) {
          tabs.push({ key: this.editor.name, name: this.editor.name, dirty: this.editor.dirty });
        }
        result = { activeKey: this.editor?.name ?? null, tabs };
        break;
      }
      case 'editor.sessionParameterFieldAdd':
        if (this.editor) {
          this.editor = {
            ...this.editor,
            dirty: true,
            callableParameters: this.editor.callableParameters.map((parameter, at) => at !== Number(params['index'])
              ? parameter
              : { ...parameter, fields: [...parameter.fields, { name: '', type: 'text' }] }),
          };
        }
        result = this.editor;
        break;
      case 'editor.sessionParameterFieldPatch':
        if (this.editor) {
          this.editor = {
            ...this.editor,
            dirty: true,
            callableParameters: this.editor.callableParameters.map((parameter, at) => at !== Number(params['index'])
              ? parameter
              : {
                  ...parameter,
                  fields: parameter.fields.map((field, fieldAt) => fieldAt !== Number(params['fieldIndex'])
                    ? field
                    : {
                        ...field,
                        name: params['field'] === 'name' ? String(params['value'] ?? '') : field.name,
                        type: params['field'] === 'type' ? String(params['value'] ?? '') : field.type,
                      }),
                }),
          };
        }
        result = this.editor;
        break;
      case 'editor.sessionParameterFieldRemove':
        if (this.editor) {
          this.editor = {
            ...this.editor,
            dirty: true,
            callableParameters: this.editor.callableParameters.map((parameter, at) => at !== Number(params['index'])
              ? parameter
              : {
                  ...parameter,
                  fields: parameter.fields.filter((_, fieldAt) => fieldAt !== Number(params['fieldIndex'])),
                }),
          };
        }
        result = this.editor;
        break;
      case 'editor.sessionResultFieldAdd':
        if (this.editor) {
          this.editor = {
            ...this.editor,
            dirty: true,
            callableResults: this.editor.callableResults.map((resultItem, at) => at !== Number(params['index'])
              ? resultItem
              : { ...resultItem, fields: [...resultItem.fields, { name: '', type: 'text' }] }),
          };
        }
        result = this.editor;
        break;
      case 'editor.sessionResultFieldPatch':
        if (this.editor) {
          this.editor = {
            ...this.editor,
            dirty: true,
            callableResults: this.editor.callableResults.map((resultItem, at) => at !== Number(params['index'])
              ? resultItem
              : {
                  ...resultItem,
                  fields: resultItem.fields.map((field, fieldAt) => fieldAt !== Number(params['fieldIndex'])
                    ? field
                    : {
                        ...field,
                        name: params['field'] === 'name' ? String(params['value'] ?? '') : field.name,
                        type: params['field'] === 'type' ? String(params['value'] ?? '') : field.type,
                      }),
                }),
          };
        }
        result = this.editor;
        break;
      case 'editor.sessionResultFieldRemove':
        if (this.editor) {
          this.editor = {
            ...this.editor,
            dirty: true,
            callableResults: this.editor.callableResults.map((resultItem, at) => at !== Number(params['index'])
              ? resultItem
              : {
                  ...resultItem,
                  fields: resultItem.fields.filter((_, fieldAt) => fieldAt !== Number(params['fieldIndex'])),
                }),
          };
        }
        result = this.editor;
        break;
      case 'editor.sessionParameterDeclAdd':
        if (this.editor) {
          this.editor = {
            ...this.editor,
            dirty: true,
            callableParameters: [...this.editor.callableParameters, {
              name: 'parameter', type: 'text', required: false, default: '', description: '',
              fields: [],
            }],
          };
        }
        result = this.editor;
        break;
      case 'editor.sessionParameterDeclPatch':
        if (this.editor) {
          const index = Number(params['index']);
          this.editor = {
            ...this.editor,
            dirty: true,
            callableParameters: this.editor.callableParameters.map((parameter, at) => at !== index
              ? parameter
              : {
                  ...parameter,
                  name: params['field'] === 'name' ? String(params['value'] ?? '') : parameter.name,
                  type: params['field'] === 'type' ? String(params['value'] ?? '') : parameter.type,
                  required: params['field'] === 'required'
                    ? params['value'] === 'true'
                    : parameter.required,
                  default: params['field'] === 'default'
                    ? String(params['value'] ?? '')
                    : parameter.default,
                  description: params['field'] === 'description'
                    ? String(params['value'] ?? '')
                    : parameter.description,
                }),
          };
        }
        result = this.editor;
        break;
      case 'editor.sessionParameterDeclMove': {
        const index = Number(params['index']);
        const target = index + Number(params['offset']);
        if (this.editor && target >= 0 && target < this.editor.callableParameters.length) {
          const parameters = [...this.editor.callableParameters];
          [parameters[index]!, parameters[target]!] = [parameters[target]!, parameters[index]!];
          this.editor = { ...this.editor, dirty: true, callableParameters: parameters };
        }
        result = this.editor;
        break;
      }
      case 'editor.sessionResultDeclMove': {
        const index = Number(params['index']);
        const target = index + Number(params['offset']);
        if (this.editor && target >= 0 && target < this.editor.callableResults.length) {
          const results = [...this.editor.callableResults];
          [results[index]!, results[target]!] = [results[target]!, results[index]!];
          this.editor = { ...this.editor, dirty: true, callableResults: results };
        }
        result = this.editor;
        break;
      }
      case 'editor.sessionParameterDeclRemove':
        if (this.editor) {
          this.editor = {
            ...this.editor,
            dirty: true,
            callableParameters: this.editor.callableParameters.filter(
              (_, at) => at !== Number(params['index'])),
          };
        }
        result = this.editor;
        break;
      case 'editor.sessionResultDeclAdd':
        if (this.editor) {
          this.editor = {
            ...this.editor,
            dirty: true,
            callableResults: [...this.editor.callableResults, {
              name: 'result', type: 'text', description: '', fields: [],
            }],
          };
        }
        result = this.editor;
        break;
      case 'editor.sessionResultDeclPatch':
        if (this.editor) {
          const index = Number(params['index']);
          this.editor = {
            ...this.editor,
            dirty: true,
            callableResults: this.editor.callableResults.map((resultItem, at) => at !== index
              ? resultItem
              : {
                  ...resultItem,
                  name: params['field'] === 'name' ? String(params['value'] ?? '') : resultItem.name,
                  type: params['field'] === 'type' ? String(params['value'] ?? '') : resultItem.type,
                  description: params['field'] === 'description'
                    ? String(params['value'] ?? '')
                    : resultItem.description,
                }),
          };
        }
        result = this.editor;
        break;
      case 'editor.sessionResultDeclRemove':
        if (this.editor) {
          this.editor = {
            ...this.editor,
            dirty: true,
            callableResults: this.editor.callableResults.filter(
              (_, at) => at !== Number(params['index'])),
          };
        }
        result = this.editor;
        break;
      case 'editor.sessionMacroSetting':
        if (this.editor) {
          this.editor = {
            ...this.editor,
            dirty: true,
            followInputLimit: params['field'] === 'followInputLimit'
              ? params['value'] === 'true'
              : this.editor.followInputLimit,
            inputLimit: params['field'] === 'inputLimit'
              ? Number(params['value'])
              : this.editor.inputLimit,
            toggleKey: params['field'] === 'toggleKey'
              ? String(params['value'] ?? '')
              : this.editor.toggleKey,
          };
        }
        result = this.editor;
        break;
      case 'editor.sessionSubOpen':
        if (this.editor) {
          this.editor = {
            ...this.editor,
            name: String(params['name'] ?? ''),
            subMacros: [],
            callableParameters: [],
          callableResults: [],
          followInputLimit: true,
            inputLimit: 20,
            toggleKey: '',
            dirty: false,
            stepCount: 0,
            steps: [],
          };
        }
        result = this.editor;
        break;
      case 'editor.sessionActivate': {
        const macro = this.projectState.macros.find((entry) => entry.name === params['key']);
        if (macro) {
          this.editor = {
            name: macro.name,
            description: macro.description,
            group: macro.group,
            subMacros: [],
            callableParameters: [],
          callableResults: [],
          followInputLimit: true,
            inputLimit: 20,
            toggleKey: '',
            dirty: false,
            stepCount: 0,
            steps: [],
            triggers: [],
          variables: [],
          globals: [],
          valueChoices: [
            { name: 'found', type: 'text', origin: 'Local', source: 'find.text', description: '' },
          ],
          valueFunctions: [
            { name: 'upper', signature: 'upper(text)', description: 'Capitals', group: 'Text' },
            { name: 'length', signature: 'length(value)', description: 'How many', group: 'Text' },
          ],
          conditionCompares: [
            { value: 'Equal', label: '=' },
            { value: 'NotEqual', label: '≠' },
            { value: 'Greater', label: '>' },
            { value: 'Less', label: '<' },
            { value: 'Holds', label: 'holds' },
          ],
          variableTypes: [
            { value: 'text', label: '文本' },
            { value: 'number', label: '数字' },
            { value: 'bool', label: '布尔' },
            { value: 'record', label: '记录' },
          ],
          triggerKinds: [
            { value: 'Key', label: '按键' },
            { value: 'Timer', label: '定时' },
            { value: 'Color', label: '颜色' },
            { value: 'File', label: '文件' },
            { value: 'Process', label: '程序' },
            { value: 'Hotstring', label: '字串' },
            { value: 'Idle', label: '空闲' },
            { value: 'Window', label: '窗口' },
            { value: 'Interface', label: '窗口内接口' },
          ],
          canUndo: false,
          canRedo: false,
          };
        }
        result = this.editor;
        break;
      }
      case 'editor.sessionPatch':
        if (this.editor) {
          this.editor = {
            ...this.editor,
            name: params['name'] === undefined ? this.editor.name : String(params['name']),
            description: params['description'] === undefined
              ? this.editor.description
              : String(params['description']),
            group: params['group'] === undefined ? this.editor.group : String(params['group']),
            dirty: true,
          };
        }
        result = this.editor;
        break;
      case 'editor.sessionConditionRowAdd':
        if (this.editor) {
          const path = this.conditionPath(params);
          this.editor = {
            ...this.editor,
            dirty: true,
            steps: this.changeCondition(this.editor.steps, String(params['stepId'] ?? ''),
              String(params['name'] ?? ''),
              (condition) => this.addConditionNode(condition, path, 'row')),
          };
        }
        result = this.editor;
        break;
      case 'editor.sessionConditionGroupAdd':
        if (this.editor) {
          const path = this.conditionPath(params);
          this.editor = {
            ...this.editor,
            dirty: true,
            steps: this.changeCondition(this.editor.steps, String(params['stepId'] ?? ''),
              String(params['name'] ?? ''),
              (condition) => this.addConditionNode(condition, path, 'group')),
          };
        }
        result = this.editor;
        break;
      case 'editor.sessionConditionRowRemove':
        if (this.editor) {
          const path = this.conditionPath(params);
          this.editor = {
            ...this.editor,
            dirty: true,
            steps: this.changeCondition(this.editor.steps, String(params['stepId'] ?? ''),
              String(params['name'] ?? ''),
              (condition) => this.removeConditionNode(condition, path)),
          };
        }
        result = this.editor;
        break;
      case 'editor.sessionConditionRow':
        if (this.editor) {
          const path = this.conditionPath(params);
          this.editor = {
            ...this.editor,
            dirty: true,
            steps: this.changeCondition(this.editor.steps, String(params['stepId'] ?? ''),
              String(params['name'] ?? ''),
              (condition) => this.setConditionCell(condition, path,
                String(params['field'] ?? ''), String(params['value'] ?? ''))),
          };
        }
        result = this.editor;
        break;
      case 'editor.sessionConditionJoin':
        if (this.editor) {
          const path = this.conditionPath(params);
          this.editor = {
            ...this.editor,
            dirty: true,
            steps: this.changeCondition(this.editor.steps, String(params['stepId'] ?? ''),
              String(params['name'] ?? ''),
              (condition) => this.setConditionJoinAt(condition, path,
                String(params['join'] ?? 'All'))),
          };
        }
        result = this.editor;
        break;
      case 'editor.sessionStepMeta':
        if (this.editor) {
          const field = String(params['field'] ?? '');
          const value = String(params['value'] ?? '');
          this.editor = {
            ...this.editor,
            dirty: true,
            steps: this.changeStepMeta(this.editor.steps, String(params['stepId'] ?? ''), (step) => ({
              ...step,
              comment: field === 'comment' ? value : step.comment,
              timeoutMs: field === 'timeoutMs' ? Number(value) : step.timeoutMs,
              retryCount: field === 'retryCount' ? Number(value) : step.retryCount,
              retryDelayMs: field === 'retryDelayMs' ? Number(value) : step.retryDelayMs,
              onError: field === 'onError' ? value : step.onError,
            })),
          };
        }
        result = this.editor;
        break;
      case 'editor.sessionStepDuplicate':
        if (this.editor) {
          this.editor = {
            ...this.editor,
            dirty: true,
            canUndo: true,
            canRedo: false,
            stepCount: this.editor.stepCount + 1,
            steps: this.duplicateStep(this.editor.steps, String(params['stepId'] ?? '')),
          };
        }
        result = this.editor;
        break;
      case 'editor.sessionStepEnabled':
        if (this.editor) {
          this.editor = {
            ...this.editor,
            dirty: true,
            steps: this.setStepEnabled(
              this.editor.steps,
              String(params['stepId'] ?? ''),
              params['enabled'] === true),
          };
        }
        result = this.editor;
        break;
      case 'editor.sessionStep':
        if (this.editor) {
          const type = String(params['type'] ?? '');
          const parentId = String(params['parentId'] ?? '');
          const step: EditorStep = {
            id: 'mock-' + String(this.editor.stepCount + 1),
            type,
            name: type === 'control.delay'
              ? '延迟'
              : type === 'control.repeat'
                ? '重复'
                : type === 'input.keyPress'
                  ? '按键'
                  : type === 'screen.color'
                    ? '颜色'
                    : type === 'image.find'
                      ? '找图'
                      : type === 'control.waitUntil'
                        ? '等待条件'
                        : type,
            enabled: true,
            comment: '',
            timeoutMs: 0,
            retryCount: 0,
            retryDelayMs: 0,
            onError: 'Stop',
            acceptsSteps: type === 'control.repeat',
            parameters: type === 'control.delay'
              ? [{
                  name: 'ms', label: '等待时长', kind: 'Number', value: '',
                  options: [], columns: [], rows: [], condition: null,
                }]
              : type === 'input.keyPress'
                ? [{
                    name: 'key', label: '按键', kind: 'Key', value: '',
                    options: [], columns: [], rows: [], condition: null,
                  }]
                : type === 'screen.color'
                  ? [{
                      name: 'hex', label: '颜色', kind: 'Color', value: '',
                      options: [], columns: [], rows: [], condition: null,
                    }]
                  : type === 'control.repeat'
                    ? [{
                        name: 'returns', label: '返回值', kind: 'Rows', value: '',
                        options: [],
                        columns: [{ name: 'key', required: true }, { name: 'name', required: true }],
                        rows: [],
                        condition: null,
                      }]
                    : type === 'image.find'
                      ? [{
                          name: 'path', label: '图片', kind: 'Image', value: '',
                          options: [], columns: [], rows: [], condition: null,
                        }]
                      : type === 'control.waitUntil'
                        ? [{
                            name: 'condition', label: '条件', kind: 'Condition', value: '',
                            options: [], columns: [], rows: [],
                            condition: {
                              join: 'All',
                              editable: true,
                              nodes: [{
                                kind: 'row', join: '', left: '', compare: 'Equal', right: '',
                                children: [],
                              }],
                            },
                          }]
                        : [],
            steps: [],
          };
          this.editor = {
            ...this.editor,
            dirty: true,
            canUndo: true,
            canRedo: false,
            stepCount: this.editor.stepCount + 1,
            steps: parentId
              ? this.addChild(this.editor.steps, parentId, step)
              : [...this.editor.steps, step],
          };
        }
        result = this.editor;
        break;
      case 'editor.sessionUndo':
        if (this.editor) {
          this.editor = { ...this.editor, canUndo: false, canRedo: true };
        }
        result = this.editor;
        break;
      case 'editor.sessionRedo':
        if (this.editor) {
          this.editor = { ...this.editor, canUndo: true, canRedo: false };
        }
        result = this.editor;
        break;
      case 'editor.sessionParameterRowAdd':
        if (this.editor) {
          this.editor = {
            ...this.editor,
            dirty: true,
            steps: this.changeRows(
              this.editor.steps,
              String(params['stepId'] ?? ''),
              String(params['name'] ?? ''),
              (rows) => [...rows, { cells: ['', ''] }]),
          };
        }
        result = this.editor;
        break;
      case 'editor.sessionParameterRowRemove':
        if (this.editor) {
          this.editor = {
            ...this.editor,
            dirty: true,
            steps: this.changeRows(
              this.editor.steps,
              String(params['stepId'] ?? ''),
              String(params['name'] ?? ''),
              (rows) => rows.filter((_, index) => index !== Number(params['index']))),
          };
        }
        result = this.editor;
        break;
      case 'editor.sessionParameterRowCell':
        if (this.editor) {
          this.editor = {
            ...this.editor,
            dirty: true,
            steps: this.changeRows(
              this.editor.steps,
              String(params['stepId'] ?? ''),
              String(params['name'] ?? ''),
              (rows) => rows.map((row, index) => index !== Number(params['index'])
                ? row
                : {
                    cells: row.cells.map((cell, cellIndex) =>
                      cellIndex === this.columnIndex(String(params['column'] ?? ''))
                        ? String(params['value'] ?? '')
                        : cell),
                  })),
          };
        }
        result = this.editor;
        break;
      case 'editor.sessionParameter':
        if (this.editor) {
          this.editor = {
            ...this.editor,
            dirty: true,
            steps: this.setParameter(
              this.editor.steps,
              String(params['stepId'] ?? ''),
              String(params['name'] ?? ''),
              String(params['value'] ?? '')),
          };
        }
        result = this.editor;
        break;
      case 'editor.sessionRecordStart':
        this.recording = true;
        this.capturedCount = 0;
        result = { recording: true, captured: 0 };
        break;
      case 'editor.sessionRecordState':
        if (this.recording) {
          this.capturedCount += 1;
        }
        result = { recording: this.recording, captured: this.capturedCount };
        break;
      case 'editor.sessionRecordStop':
        this.recording = false;
        result = {
          captured: this.capturedCount,
          steps: this.capturedCount > 0 ? 1 : 0,
          snapshot: this.editor,
        };
        this.capturedCount = 0;
        break;
      case 'editor.sessionSave':
        if (this.editor) {
          this.editor = { ...this.editor, dirty: false };
        }
        result = this.editor;
        break;
      case 'editor.sessionTriggerAdd': {
        const kind = String(params['kind'] ?? 'Key');
        if (this.editor) {
          this.editor = {
            ...this.editor,
            dirty: true,
            triggers: [...this.editor.triggers, {
              id: 'trigger',
              kind,
              kindLabel: kind === 'Key' ? '按键' : kind,
              summary: '',
              enabled: true,
              binding: '',
              loop: 'Press',
              once: false,
              timerMode: 'Interval',
              time: '08:00',
              interval: '5',
              unit: 'Seconds',
              filePath: '',
              fileChange: 'Any',
              fileFilter: '*',
              fileSubfolders: false,
              processName: '',
              processChange: 'Started',
              hotstringText: '',
              colorMatch: 'ColorMatches',
              colorHex: '#000000',
              colorTolerance: '5',
              colorX: '0',
              colorY: '0',
              followCursor: false,
              idleSeconds: '60',
              windowTitle: '',
              windowProcess: '',
              windowClass: '',
              windowHow: 'TitleContains',
              windowChange: 'Appeared',
              interfaceTitle: '',
              interfaceProcess: '',
              interfaceHow: 'TitleContains',
            }],
          };
        }
        result = this.editor;
        break;
      }
      case 'editor.sessionTriggerRemove':
        if (this.editor) {
          this.editor = {
            ...this.editor,
            dirty: true,
            triggers: this.editor.triggers.filter((trigger) => trigger.id !== params['triggerId']),
          };
        }
        result = this.editor;
        break;
      case 'editor.sessionTriggerInterface':
        if (this.editor) {
          this.editor = {
            ...this.editor,
            dirty: true,
            triggers: this.editor.triggers.map((trigger) => {
              if (trigger.id !== params['triggerId']) {
                return trigger;
              }
              const field = String(params['field'] ?? '');
              const value = String(params['value'] ?? '');
              return {
                ...trigger,
                interfaceTitle: field === 'title' ? value : trigger.interfaceTitle,
                interfaceProcess: field === 'process' ? value : trigger.interfaceProcess,
                interfaceHow: field === 'how' ? value : trigger.interfaceHow,
              };
            }),
          };
        }
        result = this.editor;
        break;
      case 'editor.sessionTriggerWindow':
        if (this.editor) {
          this.editor = {
            ...this.editor,
            dirty: true,
            triggers: this.editor.triggers.map((trigger) => {
              if (trigger.id !== params['triggerId']) {
                return trigger;
              }
              const field = String(params['field'] ?? '');
              const value = String(params['value'] ?? '');
              return {
                ...trigger,
                windowTitle: field === 'title' ? value : trigger.windowTitle,
                windowProcess: field === 'process' ? value : trigger.windowProcess,
                windowClass: field === 'class' ? value : trigger.windowClass,
                windowHow: field === 'how' ? value : trigger.windowHow,
                windowChange: field === 'change' ? value : trigger.windowChange,
              };
            }),
          };
        }
        result = this.editor;
        break;
      case 'editor.sessionTriggerColor':
        if (this.editor) {
          this.editor = {
            ...this.editor,
            dirty: true,
            triggers: this.editor.triggers.map((trigger) => {
              if (trigger.id !== params['triggerId']) {
                return trigger;
              }
              const field = String(params['field'] ?? '');
              return {
                ...trigger,
                colorMatch: field === 'match' ? String(params['value'] ?? '') : trigger.colorMatch,
                colorHex: field === 'hex' ? String(params['value'] ?? '') : trigger.colorHex,
                colorTolerance: field === 'tolerance'
                  ? String(params['value'] ?? '')
                  : trigger.colorTolerance,
                colorX: field === 'x' ? String(params['value'] ?? '') : trigger.colorX,
                colorY: field === 'y' ? String(params['value'] ?? '') : trigger.colorY,
                followCursor: field === 'follow'
                  ? params['value'] === 'true'
                  : trigger.followCursor,
              };
            }),
          };
        }
        result = this.editor;
        break;
      case 'editor.sessionTriggerIdle':
        if (this.editor) {
          this.editor = {
            ...this.editor,
            dirty: true,
            triggers: this.editor.triggers.map((trigger) => trigger.id === params['triggerId']
              ? { ...trigger, idleSeconds: String(params['value'] ?? '') }
              : trigger),
          };
        }
        result = this.editor;
        break;
      case 'editor.sessionTriggerProcess':
        if (this.editor) {
          this.editor = {
            ...this.editor,
            dirty: true,
            triggers: this.editor.triggers.map((trigger) => trigger.id === params['triggerId']
              ? {
                  ...trigger,
                  processName: params['field'] === 'name'
                    ? String(params['value'] ?? '')
                    : trigger.processName,
                  processChange: params['field'] === 'change'
                    ? String(params['value'] ?? '')
                    : trigger.processChange,
                }
              : trigger),
          };
        }
        result = this.editor;
        break;
      case 'editor.sessionTriggerHotstring':
        if (this.editor) {
          this.editor = {
            ...this.editor,
            dirty: true,
            triggers: this.editor.triggers.map((trigger) => trigger.id === params['triggerId']
              ? { ...trigger, hotstringText: String(params['value'] ?? '') }
              : trigger),
          };
        }
        result = this.editor;
        break;
      case 'editor.sessionTriggerFile':
        if (this.editor) {
          this.editor = {
            ...this.editor,
            dirty: true,
            triggers: this.editor.triggers.map((trigger) => {
              if (trigger.id !== params['triggerId']) {
                return trigger;
              }
              const field = String(params['field'] ?? '');
              const value = String(params['value'] ?? '');
              return {
                ...trigger,
                filePath: field === 'path' ? value : trigger.filePath,
                fileChange: field === 'change' ? value : trigger.fileChange,
                fileFilter: field === 'filter' ? value : trigger.fileFilter,
                fileSubfolders: field === 'subfolders' ? value === 'true' : trigger.fileSubfolders,
              };
            }),
          };
        }
        result = this.editor;
        break;
      case 'editor.sessionTriggerTimer':
        if (this.editor) {
          this.editor = {
            ...this.editor,
            dirty: true,
            triggers: this.editor.triggers.map((trigger) => {
              if (trigger.id !== params['triggerId']) {
                return trigger;
              }
              const field = String(params['field'] ?? '');
              const value = String(params['value'] ?? '');
              return {
                ...trigger,
                timerMode: field === 'mode' ? value : trigger.timerMode,
                time: field === 'time' ? value : trigger.time,
                interval: field === 'interval' ? value : trigger.interval,
                unit: field === 'unit' ? value : trigger.unit,
              };
            }),
          };
        }
        result = this.editor;
        break;
      case 'editor.sessionTriggerEnabled':
        if (this.editor) {
          this.editor = {
            ...this.editor,
            dirty: true,
            triggers: this.editor.triggers.map((trigger) => trigger.id === params['triggerId']
              ? { ...trigger, enabled: params['enabled'] === true }
              : trigger),
          };
        }
        result = this.editor;
        break;
      case 'editor.sessionTriggerLoop':
        if (this.editor) {
          this.editor = {
            ...this.editor,
            dirty: true,
            triggers: this.editor.triggers.map((trigger) => trigger.id === params['triggerId']
              ? { ...trigger, loop: String(params['loop'] ?? 'Press') }
              : trigger),
          };
        }
        result = this.editor;
        break;
      case 'editor.sessionTriggerOnce':
        if (this.editor) {
          this.editor = {
            ...this.editor,
            dirty: true,
            triggers: this.editor.triggers.map((trigger) => trigger.id === params['triggerId']
              ? { ...trigger, once: params['once'] === true }
              : trigger),
          };
        }
        result = this.editor;
        break;
      case 'editor.sessionTriggerBinding':
        if (this.editor) {
          this.editor = {
            ...this.editor,
            dirty: true,
            triggers: this.editor.triggers.map((trigger) => trigger.id === params['triggerId']
              ? { ...trigger, binding: String(params['binding'] ?? '') }
              : trigger),
          };
        }
        result = this.editor;
        break;
      case 'editor.sessionGlobalAdd':
        if (this.editor) {
          this.editor = {
            ...this.editor,
            globals: [...this.editor.globals, {
              name: 'variable',
              type: 'text',
              default: '',
              description: '',
              persisted: false,
            }],
          };
        }
        result = this.editor;
        break;
      case 'editor.sessionGlobalPatch':
        if (this.editor) {
          this.editor = {
            ...this.editor,
            globals: this.editor.globals.map((global) =>
              global.name === params['originalName']
                ? {
                    ...global,
                    name: params['field'] === 'name' ? String(params['value'] ?? '') : global.name,
                    type: params['field'] === 'type' ? String(params['value'] ?? '') : global.type,
                    default: params['field'] === 'default'
                      ? String(params['value'] ?? '')
                      : global.default,
                    description: params['field'] === 'description'
                      ? String(params['value'] ?? '')
                      : global.description,
                    persisted: params['field'] === 'persist'
                      ? params['value'] === 'true'
                      : global.persisted,
                  }
                : global),
          };
        }
        result = this.editor;
        break;
      case 'editor.sessionGlobalRemove':
        if (this.editor) {
          this.editor = {
            ...this.editor,
            globals: this.editor.globals.filter((global) => global.name !== params['name']),
          };
        }
        result = this.editor;
        break;
      case 'editor.sessionVariableAdd':
        if (this.editor) {
          this.editor = {
            ...this.editor,
            dirty: true,
            variables: [...this.editor.variables, {
              name: '',
              type: 'text',
              initial: '',
              description: '',
              source: '声明',
              isDeclared: true,
            }],
          };
        }
        result = this.editor;
        break;
      case 'editor.sessionVariablePatch':
        if (this.editor) {
          this.editor = {
            ...this.editor,
            dirty: true,
            variables: this.editor.variables.map((variable) =>
              variable.name === params['originalName']
                ? {
                    ...variable,
                    name: params['field'] === 'name' ? String(params['value'] ?? '') : variable.name,
                    type: params['field'] === 'type' ? String(params['value'] ?? '') : variable.type,
                    initial: params['field'] === 'initial'
                      ? String(params['value'] ?? '')
                      : variable.initial,
                    description: params['field'] === 'description'
                      ? String(params['value'] ?? '')
                      : variable.description,
                  }
                : variable),
          };
        }
        result = this.editor;
        break;
      case 'editor.sessionVariableRemove':
        if (this.editor) {
          this.editor = {
            ...this.editor,
            dirty: true,
            variables: this.editor.variables.filter((variable) => variable.name !== params['name']),
          };
        }
        result = this.editor;
        break;
      case 'editor.sessionClose':
        if (this.editor?.dirty) {
          throw new Error('Save this document before closing it.');
        }
        this.editor = null;
        result = null;
        break;
      case 'editor.sessionActions':
        result = {
          count: 177,
          actions: [
            {
              key: 'control.delay',
              name: '延迟',
              category: 'Control',
              categoryLabel: 'Flow',
              description: '',
              hidden: false,
              parameters: [
                { name: 'ms', label: '等待时长', kind: 'Number', required: false, placeholder: '', options: [] },
              ],
            },
            {
              key: 'control.repeat',
              name: '重复',
              category: 'Control',
              categoryLabel: 'Flow',
              description: '',
              hidden: false,
              parameters: [],
            },
            {
              key: 'input.keyPress',
              name: '按键',
              category: 'Input',
              categoryLabel: 'Input',
              description: '',
              hidden: false,
              parameters: [
                { name: 'key', label: '按键', kind: 'Key', required: false, placeholder: '', options: [] },
              ],
            },
            {
              key: 'screen.color',
              name: '颜色',
              category: 'Screen',
              categoryLabel: 'Vision',
              description: '',
              hidden: false,
              parameters: [
                { name: 'hex', label: '颜色', kind: 'Color', required: false, placeholder: '', options: [] },
              ],
            },
            {
              key: 'control.waitUntil',
              name: '等待条件',
              category: 'Control',
              categoryLabel: 'Flow',
              description: '',
              hidden: false,
              parameters: [
                { name: 'condition', label: '条件', kind: 'Condition', required: false, placeholder: '', options: [] },
              ],
            },
            {
              key: 'image.find',
              name: '找图',
              category: 'Screen',
              categoryLabel: 'Vision',
              description: '',
              hidden: false,
              parameters: [
                { name: 'path', label: '图片', kind: 'Image', required: false, placeholder: '', options: [] },
              ],
            },
          ],
        };
        break;

      case 'project.delete':
        this.pendingDelete = String(params['name']);
        this.push('dialog.request', {
          requestId: 2,
          title: chinese['Editor.DeleteTitle']!,
          message: `确定删除“${String(params['name'])}”？此操作无法撤销。`,
          confirmLabel: chinese['Editor.Delete']!,
          cancelLabel: chinese['Common.Cancel']!,
        });
        break;
      case 'project.export':
        result = { exported: String(params['name']) };
        break;
      case 'project.import':
        result = { imported: '/mock/SomeProject.wgmacro' };
        break;
      case 'project.state':
        result = this.projectState;
        break;
      case 'project.toggle':
        for (const entry of this.projectState.macros) {
          if (entry.name === params['name']) {
            entry.enabled = !entry.enabled;
          }
        }
        // 启停一次宏就是一次修改：真机上 ToggleMacro 会把项目标脏，推送里带着这个标记。
        this.projectState = { ...this.projectState, dirty: true };
        this.push('project.changed', this.projectState);
        break;
      case 'project.system':
        this.projectState = {
          ...this.projectState,
          systemEnabled: params['enabled'] === true,
        };
        this.push('project.changed', this.projectState);
        break;
      case 'dialog.pickImage':
        result = { path: 'C:\\shot.png' };
        break;
      case 'dialog.pickOpenPackage':
        result = '/mock/opened-project.wgmacro';
        break;
      case 'dialog.pickSavePackage':
        result = `/mock/${String(params['suggestedName'] ?? 'macros.wgmacro')}`;
        break;
      case 'project.open': {
        const path = String(params['path'] ?? '/mock/opened-project.wgmacro');
        const name = path.split('/').pop()?.replace(/\.wgmacro$/, '') ?? null;
        this.projectState = {
          ...this.projectState,
          path,
          name,
          untitled: false,
          dirty: false,
          revision: this.projectState.revision + 1,
          macros: [
            {
              name: '包里的宏', group: '', description: '从包里打开的', enabled: true, running: false,
              triggerSummary: '键：Ctrl+Alt+O', triggerActions: '', triggerPreview: 'Ctrl+Alt+O',
              views: ['All', 'Keys'],
            },
          ],
          groups: [],
        };
        this.push('project.changed', this.projectState);
        break;
      }
      case 'project.save':
        this.projectState = { ...this.projectState, dirty: false };
        this.push('project.changed', this.projectState);
        break;
      case 'project.saveAs': {
        const path = String(params['path'] ?? `/mock/${String(params['suggestedName'] ?? 'macros.wgmacro')}`);
        const name = path.split('/').pop()?.replace(/\.wgmacro$/, '') ?? null;
        this.projectState = { ...this.projectState, path, name, untitled: false, dirty: false };
        this.push('project.changed', this.projectState);
        break;
      }
      case 'project.reload':
        this.projectState = { ...this.projectState, dirty: false };
        this.push('project.changed', this.projectState);
        break;
      case 'project.closeAll':
        // 关全部要页面确认；mock 的文案与真宿主同源（同一组词表键），页面上有人点确定，
        // dialog.respond 才真的清。
        this.push('dialog.request', {
          requestId: 1,
          title: chinese['Main.CloseAllTitle']!,
          message: chinese['Main.CloseAllMessage']!,
          confirmLabel: chinese['Main.CloseAll']!,
          cancelLabel: null,
        });
        break;
      case 'dialog.respond': {
        if (params['accepted'] !== true) {
          this.pendingDelete = null;
          break;
        }

        if (this.pendingDelete !== null) {
          // 删除确认被接受：那一个宏离开列表。
          this.projectState = {
            ...this.projectState,
            dirty: true,
            macros: this.projectState.macros.filter(entry => entry.name !== this.pendingDelete),
          };
          this.pendingDelete = null;
          this.push('project.changed', this.projectState);
          break;
        }

        this.projectState = {
          ...this.projectState,
          path: null,
          name: null,
          untitled: true,
          dirty: false,
          macros: [],
          groups: [],
        };
        this.push('project.changed', this.projectState);
        break;
      }
      case 'mcp.state':
        result = this.mcp;
        break;
      case 'mcp.setGrants':
        this.mcp = {
          ...this.mcp,
          allowControl: params['control'] === true,
          allowRun: params['run'] === true,
        };
        this.push('mcp.changed', this.mcp);
        break;
      case 'mcp.copyConfig':
      case 'mcp.openAudit':
      case 'app.openManual':
        break;
      default:
        this.answer({ id: request.id, ok: false, error: { code: 'NoSuchMethod', message: method } });
        return;
    }

    this.answer({ id: request.id, ok: true, result });
  }

  private changeCondition(
    steps: EditorStep[],
    stepId: string,
    name: string,
    change: (condition: NonNullable<EditorStep['parameters'][number]['condition']>) =>
      NonNullable<EditorStep['parameters'][number]['condition']>,
  ): EditorStep[] {
    return steps.map((step) => {
      if (step.id === stepId) {
        return {
          ...step,
          parameters: step.parameters.map((parameter) =>
            parameter.name === name && parameter.condition
              ? { ...parameter, condition: change(parameter.condition) }
              : parameter),
        };
      }

      return { ...step, steps: this.changeCondition(step.steps, stepId, name, change) };
    });
  }

  private conditionPath(params: Record<string, unknown>): number[] {
    const path = params['path'];
    if (Array.isArray(path)) {
      return path.map((item) => Number(item));
    }

    return params['index'] === undefined ? [] : [Number(params['index'])];
  }

  private addConditionNode(
    condition: NonNullable<EditorStep['parameters'][number]['condition']>,
    path: number[],
    kind: 'row' | 'group',
  ): NonNullable<EditorStep['parameters'][number]['condition']> {
    const root = this.conditionRoot(condition);
    const next = this.changeConditionGroup(root, path, (group) => ({
      ...group,
      children: [...group.children, this.conditionNode(kind)],
    }));
    return this.conditionFromRoot(next);
  }

  private removeConditionNode(
    condition: NonNullable<EditorStep['parameters'][number]['condition']>,
    path: number[],
  ): NonNullable<EditorStep['parameters'][number]['condition']> {
    if (path.length === 0) {
      return condition;
    }

    const parentPath = path.slice(0, -1);
    const index = path[path.length - 1];
    const root = this.conditionRoot(condition);
    const next = this.changeConditionGroup(root, parentPath, (group) => ({
      ...group,
      children: group.children.filter((_, at) => at !== index),
    }));
    return this.conditionFromRoot(next);
  }

  private setConditionCell(
    condition: NonNullable<EditorStep['parameters'][number]['condition']>,
    path: number[],
    field: string,
    value: string,
  ): NonNullable<EditorStep['parameters'][number]['condition']> {
    if (path.length === 0) {
      return condition;
    }

    const root = this.conditionRoot(condition);
    const next = this.changeConditionNode(root, path, (node) => ({
      ...node,
      left: field === 'left' ? value : node.left,
      compare: field === 'compare' ? value : node.compare,
      right: field === 'right' ? value : node.right,
    }));
    return this.conditionFromRoot(next);
  }

  private setConditionJoinAt(
    condition: NonNullable<EditorStep['parameters'][number]['condition']>,
    path: number[],
    join: string,
  ): NonNullable<EditorStep['parameters'][number]['condition']> {
    const root = this.conditionRoot(condition);
    const next = this.changeConditionGroup(root, path, (group) => ({ ...group, join }));
    return this.conditionFromRoot(next);
  }

  private conditionRoot(
    condition: NonNullable<EditorStep['parameters'][number]['condition']>,
  ): EditorConditionNode {
    return {
      kind: 'group',
      join: condition.join,
      left: '',
      compare: '',
      right: '',
      children: condition.nodes,
    };
  }

  private conditionFromRoot(
    root: EditorConditionNode,
  ): NonNullable<EditorStep['parameters'][number]['condition']> {
    return { join: root.join, nodes: root.children, editable: true };
  }

  private conditionNode(kind: 'row' | 'group'): EditorConditionNode {
    return kind === 'row'
      ? { kind: 'row', join: '', left: '', compare: 'Equal', right: '', children: [] }
      : { kind: 'group', join: 'All', left: '', compare: '', right: '', children: [] };
  }

  private changeConditionGroup(
    group: EditorConditionNode,
    path: number[],
    change: (group: EditorConditionNode) => EditorConditionNode,
  ): EditorConditionNode {
    if (path.length === 0) {
      return change(group);
    }

    const [index, ...rest] = path;
    return {
      ...group,
      children: group.children.map((node, at) => at === index
        ? this.changeConditionGroup(node, rest, change)
        : node),
    };
  }

  private changeConditionNode(
    group: EditorConditionNode,
    path: number[],
    change: (node: EditorConditionNode) => EditorConditionNode,
  ): EditorConditionNode {
    const [index, ...rest] = path;
    return {
      ...group,
      children: group.children.map((node, at) => {
        if (at !== index) {
          return node;
        }

        return rest.length === 0
          ? change(node)
          : this.changeConditionNode(node, rest, change);
      }),
    };
  }

  private changeStepMeta(
    steps: EditorStep[],
    stepId: string,
    change: (step: EditorStep) => EditorStep,
  ): EditorStep[] {
    return steps.map((step) => step.id === stepId
      ? change(step)
      : { ...step, steps: this.changeStepMeta(step.steps, stepId, change) });
  }

  private duplicateStep(steps: EditorStep[], stepId: string): EditorStep[] {
    const copy: EditorStep[] = [];
    for (const step of steps) {
      copy.push(step);
      if (step.id === stepId) {
        copy.push(structuredClone(step));
      } else {
        copy.push({ ...step, steps: this.duplicateStep(step.steps, stepId) });
      }
    }
    return copy;
  }

  private setStepEnabled(steps: EditorStep[], stepId: string, enabled: boolean): EditorStep[] {
    return steps.map((step) => step.id === stepId
      ? { ...step, enabled }
      : { ...step, steps: this.setStepEnabled(step.steps, stepId, enabled) });
  }

  private changeRows(
    steps: EditorStep[],
    stepId: string,
    name: string,
    change: (rows: { cells: string[] }[]) => { cells: string[] }[],
  ): EditorStep[] {
    return steps.map((step) => step.id === stepId
      ? {
          ...step,
          parameters: step.parameters.map((parameter) =>
            parameter.name === name ? { ...parameter, rows: change(parameter.rows) } : parameter),
        }
      : { ...step, steps: this.changeRows(step.steps, stepId, name, change) });
  }

  private columnIndex(column: string): number {
    return column === 'name' ? 1 : 0;
  }

  private setParameter(
    steps: EditorStep[],
    stepId: string,
    name: string,
    value: string,
  ): EditorStep[] {
    return steps.map((step) => step.id === stepId
      ? {
          ...step,
          parameters: step.parameters.map((parameter) =>
            parameter.name === name ? { ...parameter, value } : parameter),
        }
      : { ...step, steps: this.setParameter(step.steps, stepId, name, value) });
  }

  private addChild(steps: EditorStep[], parentId: string, child: EditorStep): EditorStep[] {
    return steps.map((step) => step.id === parentId
      ? { ...step, steps: [...step.steps, child] }
      : { ...step, steps: this.addChild(step.steps, parentId, child) });
  }

  private bundle(): StringsBundle {
    const entries: Record<string, string> = {};
    for (const key of [...chromeKeys, ...Object.keys(mockExtra)]) {
      entries[key] = this.info.language === 'en'
        ? (english[key] ?? mockExtra[key]?.en ?? key)
        : (chinese[key] ?? mockExtra[key]?.zh ?? key);
    }
    return { language: this.info.language, entries };
  }

  private answer(message: unknown): void {
    // 下一轮才送，且做一次 JSON 往返：真实宿主的每个信封都是序列化过来的新对象，页面组件
    // 对"引用没变"会当作没变化跳过重画——mock 复用内部对象就会把这类 bug 漏到真机上才炸。
    const copy = JSON.parse(JSON.stringify(message)) as unknown;
    queueMicrotask(() => this.receive?.(copy));
  }
}


