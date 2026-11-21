import type { BridgeTransport } from './transport.js';
import type {
  AppInfo,
  EditorSnapshot,
  EditorStep,
  McpState,
  ProjectState,
  StringsBundle,
  ThemeState,
  WindowState,
} from './protocol.js';

/**
 * 浏览器里的假宿主：不依赖程序就能开发壳、跑 Playwright。它回答壳用到的每个方法，并留出
 * `push()` 让测试往页面里灌事件。真实词表以程序下发的为准——这里只放壳需要的键，与真表
 * 同步更新（有测试对账）。
 */

const chromeKeys = [
  'Shell.Home', 'Shell.Macros', 'Shell.Editor', 'Shell.Run', 'Shell.Settings',
  'Shell.UntitledProject', 'Shell.Field.Version', 'Shell.Field.Language', 'Shell.Field.Project',
  'Shell.Field.Dirty', 'Shell.Field.Clean', 'Shell.Field.Revision', 'Shell.Field.Macros',
  'Shell.Home.Heading', 'Shell.Home.Intro', 'Shell.Status.Web', 'Shell.Status.Native',
  'Shell.Status.Planned', 'Shell.Placeholder.Body', 'Shell.Navigation', 'Shell.OpenNative',
  'Shell.File', 'Shell.About', 'Shell.Help', 'Shell.Manual',
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
  'Editor.RemoveRow', 'Editor.OpenMacro',
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
  'Editor.Delete', 'Common.Cancel', 'Main.Import',
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
  'Common.Cancel': 'Cancel',
  'Main.CloseAllMessage': 'Remove every macro from the list? The saved package is not changed.',
  'Mcp.Title': 'MCP', 'Mcp.Control': 'Allow direct control',
  'Mcp.Run': 'Allow the AI to run macros', 'Mcp.SwitchOn': 'On', 'Mcp.SwitchOff': 'Off',
  'Mcp.CopyConfig': 'Copy client config', 'Mcp.GrantsNote': 'Saved as you flip them.',
  'Mcp.OpenAudit': 'Open call log',
  'Shell.Window.Minimize': 'Minimize',
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
  'Common.Cancel': '取消',
  'Main.CloseAllMessage': '从列表中移除所有宏？已保存的宏包不会被修改。',
  'Mcp.Title': 'MCP', 'Mcp.Control': '允许直接控制',
  'Mcp.Run': '允许 AI 运行宏', 'Mcp.SwitchOn': '开', 'Mcp.SwitchOff': '关',
  'Mcp.CopyConfig': '复制客户端配置', 'Mcp.GrantsNote': '改一下就存下来了。',
  'Mcp.OpenAudit': '查看调用记录',
  'Shell.Window.Minimize': '最小化',
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
      case 'theme.set':
        this.theme = {
          mode: String(params['mode'] ?? 'dark') as ThemeState['mode'],
          baseLight: params['baseLight'] === true,
          accentHex: (params['accentHex'] as string | null) ?? null,
        };
        this.push('theme.changed', this.theme);
        break;
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
      case 'editor.newMacro':
      case 'editor.open':
        result = { opened: false };
        break;
      case 'editor.sessionNew':
        this.editor = {
          name: '未命名宏',
          description: '',
          group: '',
          dirty: false,
          stepCount: 0,
          steps: [],
          triggers: [],
          variables: [],
          globals: [],
          variableTypes: [
            { value: 'text', label: '文本' },
            { value: 'number', label: '数字' },
            { value: 'bool', label: '布尔' },
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
          dirty: false,
          stepCount: 0,
          steps: [],
          triggers: [],
          variables: [],
          globals: [],
          variableTypes: [
            { value: 'text', label: '文本' },
            { value: 'number', label: '数字' },
            { value: 'bool', label: '布尔' },
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
      case 'editor.sessionActivate': {
        const macro = this.projectState.macros.find((entry) => entry.name === params['key']);
        if (macro) {
          this.editor = {
            name: macro.name,
            description: macro.description,
            group: macro.group,
            dirty: false,
            stepCount: 0,
            steps: [],
            triggers: [],
          variables: [],
          globals: [],
          variableTypes: [
            { value: 'text', label: '文本' },
            { value: 'number', label: '数字' },
            { value: 'bool', label: '布尔' },
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
                      : type,
            enabled: true,
            acceptsSteps: type === 'control.repeat',
            parameters: type === 'control.delay'
              ? [{
                  name: 'ms', label: '等待时长', kind: 'Number', value: '',
                  options: [], columns: [], rows: [],
                }]
              : type === 'input.keyPress'
                ? [{
                    name: 'key', label: '按键', kind: 'Key', value: '',
                    options: [], columns: [], rows: [],
                  }]
                : type === 'screen.color'
                  ? [{
                      name: 'hex', label: '颜色', kind: 'Color', value: '',
                      options: [], columns: [], rows: [],
                    }]
                  : type === 'control.repeat'
                    ? [{
                        name: 'returns', label: '返回值', kind: 'Rows', value: '',
                        options: [],
                        columns: [{ name: 'key', required: true }, { name: 'name', required: true }],
                        rows: [],
                      }]
                    : type === 'image.find'
                      ? [{
                          name: 'path', label: '图片', kind: 'Image', value: '',
                          options: [], columns: [], rows: [],
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
              description: '',
              hidden: false,
              parameters: [],
            },
            {
              key: 'input.keyPress',
              name: '按键',
              category: 'Input',
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
              description: '',
              hidden: false,
              parameters: [
                { name: 'hex', label: '颜色', kind: 'Color', required: false, placeholder: '', options: [] },
              ],
            },
            {
              key: 'image.find',
              name: '找图',
              category: 'Screen',
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
      case 'app.openSettings':
      case 'app.openRun':
      case 'app.openAbout':
      case 'app.openManual':
        break;
      default:
        this.answer({ id: request.id, ok: false, error: { code: 'NoSuchMethod', message: method } });
        return;
    }

    this.answer({ id: request.id, ok: true, result });
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
    for (const key of chromeKeys) {
      entries[key] = this.info.language === 'en' ? english[key]! : chinese[key]!;
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
