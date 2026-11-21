import type { BridgeTransport } from './transport.js';
import type { AppInfo, McpState, ProjectState, StringsBundle, ThemeState, WindowState } from './protocol.js';

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
  'Shell.Status.Planned', 'Shell.Placeholder.Body', 'Shell.Window.Minimize',
  'Shell.Window.Maximize', 'Shell.Window.Restore', 'Shell.Window.Close', 'Shell.Window.Pin',
  'Shell.Window.Unpin', 'Main.Title', 'Main.Dock.All', 'Main.Dock.Keys', 'Main.Dock.Interface',
  'Main.Dock.Hotstring', 'Main.Dock.Menu', 'Main.Dock.Wheel', 'Main.Dock.Events',
  'Main.SearchPlaceholder', 'Main.EmptyTitle', 'Main.EmptyHint', 'Main.NoMatch',
  'Main.AddMacro', 'Main.MacroRunning', 'Main.EnableMacro', 'Main.DisableMacro',
  'Main.SystemEnabled', 'Main.SystemDisabled', 'Main.GroupAll', 'Main.Ungrouped',
  'Main.OpenProject', 'Main.SaveProject', 'Main.SaveProjectAs', 'Main.ReloadCurrent',
  'Main.CloseAll', 'Mcp.Title', 'Mcp.Intro', 'Mcp.Control', 'Mcp.Run',
  'Mcp.SwitchOn', 'Mcp.SwitchOff', 'Mcp.CopyConfig', 'Mcp.GrantsNote', 'Mcp.OpenAudit',
  'Main.CloseAllTitle', 'Main.CloseAllMessage',
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
  'Main.Dock.All': 'All', 'Main.Dock.Keys': 'Keys', 'Main.Dock.Interface': 'Window',
  'Main.Dock.Hotstring': 'Strings', 'Main.Dock.Menu': 'Menu', 'Main.Dock.Wheel': 'Wheel',
  'Main.Dock.Events': 'Events', 'Main.SearchPlaceholder': 'Search…',
  'Main.EmptyTitle': 'No macros yet.', 'Main.EmptyHint': 'Create your first macro below.',
  'Main.NoMatch': 'No macro matches.', 'Main.AddMacro': 'New macro',
  'Main.MacroRunning': 'Running', 'Main.EnableMacro': 'Enable', 'Main.DisableMacro': 'Disable',
  'Main.SystemEnabled': 'Enabled', 'Main.SystemDisabled': 'Disabled',
  'Main.GroupAll': 'All groups', 'Main.Ungrouped': 'Ungrouped',
  'Main.OpenProject': 'Open Macro Project', 'Main.SaveProject': 'Save Macro Project',
  'Main.SaveProjectAs': 'Save Macro Project As', 'Main.ReloadCurrent': 'Reload Current Macro',
  'Main.CloseAll': 'Close All', 'Main.CloseAllTitle': 'Close all macros',
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
  'Main.Dock.All': '全部', 'Main.Dock.Keys': '按键', 'Main.Dock.Interface': '界面',
  'Main.Dock.Hotstring': '字串', 'Main.Dock.Menu': '菜单', 'Main.Dock.Wheel': '轮盘',
  'Main.Dock.Events': '事件', 'Main.SearchPlaceholder': '输入以搜索..',
  'Main.EmptyTitle': '还没有宏。', 'Main.EmptyHint': '点击下方的“新建宏”创建第一个宏。',
  'Main.NoMatch': '没有宏匹配你输入的内容。', 'Main.AddMacro': '新建宏',
  'Main.MacroRunning': '运行中', 'Main.EnableMacro': '启用宏', 'Main.DisableMacro': '停用宏',
  'Main.SystemEnabled': '已启用', 'Main.SystemDisabled': '已停用',
  'Main.GroupAll': '全部分组', 'Main.Ungrouped': '未分组',
  'Main.OpenProject': '打开宏项目', 'Main.SaveProject': '保存宏项目',
  'Main.SaveProjectAs': '宏项目另存为', 'Main.ReloadCurrent': '重新加载当前宏',
  'Main.CloseAll': '全部关闭', 'Main.CloseAllTitle': '关闭全部宏',
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
      case 'app.quit':
      case 'app.openExternal':
        break;
      case 'editor.newMacro':
      case 'editor.open':
        result = { opened: false };
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
        if (params['accepted'] === true) {
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
        }
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
        break;
      default:
        this.answer({ id: request.id, ok: false, error: { code: 'NoSuchMethod', message: method } });
        return;
    }

    this.answer({ id: request.id, ok: true, result });
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
