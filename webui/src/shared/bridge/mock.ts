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
  'Editor.NamePlaceholder', 'Editor.DescriptionPlaceholder', 'Editor.NoSteps',
  'Editor.GateActions', 'Editor.AddStep', 'Editor.AddChildStep', 'Editor.OpenMacro',
  'Editor.TriggerEntries', 'Editor.AddTrigger',
  'Editor.RemoveTrigger', 'Editor.BindKey', 'Add.SetCombination', 'Add.PressKey',
  'Palette.Undo', 'Palette.Redo', 'Editor.Loop', 'Editor.TriggerOnce',
  'Loop.Press', 'Loop.Hold', 'Loop.Toggle', 'Loop.Release',
  'LocalVariables.Title', 'LocalVariables.New', 'LocalVariables.Initial',
  'LocalVariables.InitialPlaceholder', 'LocalVariables.Delete',
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
  'Editor.NamePlaceholder': 'Enter a name for your macro..',
  'Editor.DescriptionPlaceholder': 'Describe what this macro does..',
  'Editor.NoSteps': 'No steps yet.', 'Editor.GateActions': '{0} actions in the catalogue',
  'Editor.AddStep': 'Add step', 'Editor.AddChildStep': 'Add inside selected step',
  'Editor.OpenMacro': 'Open macro',
  'Editor.TriggerEntries': 'Trigger entries',
  'Editor.AddTrigger': 'Add entry', 'Editor.RemoveTrigger': 'Remove entry',
  'Editor.BindKey': 'Trigger / Bind Key :', 'Add.SetCombination': 'Set combination',
  'Add.PressKey': 'Press a key…', 'Palette.Undo': 'Undo', 'Palette.Redo': 'Redo',
  'Editor.Loop': 'Loop :', 'Editor.TriggerOnce': 'Trigger Once',
  'Loop.Press': 'Once, When Key Pressed (Press)', 'Loop.Hold': 'While Holding Key (Hold)',
  'Loop.Toggle': 'Until Key Pressed Again (Toggle)', 'Loop.Release': 'Once, When Key Released (Release)',
  'LocalVariables.Title': 'Local variables',
  'LocalVariables.New': 'New variable', 'LocalVariables.Initial': 'Starts at',
  'LocalVariables.InitialPlaceholder': 'A value, or an expression such as count + 1',
  'LocalVariables.Delete': 'Delete',
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
  'Editor.NamePlaceholder': '给这个宏起个名字..',
  'Editor.DescriptionPlaceholder': '说明这个宏做什么..',
  'Editor.NoSteps': '还没有步骤。', 'Editor.GateActions': '动作目录共 {0} 项',
  'Editor.AddStep': '添加步骤', 'Editor.AddChildStep': '添加到选中步骤内',
  'Editor.OpenMacro': '打开宏',
  'Editor.TriggerEntries': '触发入口',
  'Editor.AddTrigger': '添加入口', 'Editor.RemoveTrigger': '删除入口',
  'Editor.BindKey': '触发 / 绑定按键：', 'Add.SetCombination': '设置组合键',
  'Add.PressKey': '请按一个键…', 'Palette.Undo': '撤销', 'Palette.Redo': '重做',
  'Editor.Loop': '循环：', 'Editor.TriggerOnce': '仅触发一次',
  'Loop.Press': '按下时执行一次（按下）', 'Loop.Hold': '按住期间循环（长按）',
  'Loop.Toggle': '再次按键时停止（切换）', 'Loop.Release': '松开时执行一次（松开）',
  'LocalVariables.Title': '局部变量',
  'LocalVariables.New': '新建变量', 'LocalVariables.Initial': '初始值',
  'LocalVariables.InitialPlaceholder': '直接填值，或写表达式，例如 count + 1',
  'LocalVariables.Delete': '删除',
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
          dirty: false,
          stepCount: 0,
          steps: [],
          triggers: [],
          variables: [],
          variableTypes: [
            { value: 'text', label: '文本' },
            { value: 'number', label: '数字' },
            { value: 'bool', label: '布尔' },
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
          dirty: false,
          stepCount: 0,
          steps: [],
          triggers: [],
          variables: [],
          variableTypes: [
            { value: 'text', label: '文本' },
            { value: 'number', label: '数字' },
            { value: 'bool', label: '布尔' },
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
            dirty: false,
            stepCount: 0,
            steps: [],
            triggers: [],
          variables: [],
          variableTypes: [
            { value: 'text', label: '文本' },
            { value: 'number', label: '数字' },
            { value: 'bool', label: '布尔' },
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
                  : type,
            enabled: true,
            acceptsSteps: type === 'control.repeat',
            parameters: type === 'control.delay'
              ? [{ name: 'ms', label: '等待时长', kind: 'Number', value: '', options: [] }]
              : type === 'input.keyPress'
                ? [{ name: 'key', label: '按键', kind: 'Key', value: '', options: [] }]
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
      case 'editor.sessionTriggerAdd':
        if (this.editor) {
          this.editor = {
            ...this.editor,
            dirty: true,
            triggers: [...this.editor.triggers, {
              id: 'trigger',
              kind: 'Key',
              kindLabel: '按键',
              summary: '',
              enabled: true,
              binding: '',
              loop: 'Press',
              once: false,
            }],
          };
        }
        result = this.editor;
        break;
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
