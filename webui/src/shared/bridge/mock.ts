import { demoMenu, demoSurfacePanel, demoSurfaceMenu, demoSurfaceWheel, demoWheel } from './mock-fixtures.js';
import wordTables from './generated/strings.json';
import { handleSettings } from './mock-settings.js';
import { handleRun } from './mock-run.js';
import { handleShell } from './mock-shell.js';
import { handleSurfaceMenu } from './mock-menu.js';
import type { BridgeTransport } from './transport.js';
import { MockEditor } from './mock-editor.js';
import type {
  AppInfo,
  McpState,
  ProjectState,
  SettingsState,
  RunState,
  StringsBundle,
  SurfaceMenuSnapshot,
  SurfacePanelSnapshot,
  SurfaceWheelSnapshot,
  ThemeState,
  WindowState,
} from './protocol.js';

/**
 * 浏览器里的假宿主：不依赖程序就能开发壳、跑 Playwright。它回答壳用到的每个方法，并留出
 * `push()` 让测试往页面里灌事件。真实词表以程序下发的为准——这里只放壳需要的键，与真表
 * 同步更新（有测试对账）。
 */

export class MockTransport implements BridgeTransport {
  private receive?: (message: unknown) => void;

  /** 页面发来的请求按序记录，调试与 e2e 断言用。 */
  readonly seen: string[] = [];

  info: AppInfo = {
    name: 'WhaleGenie',
    version: '0.0.0-mock',
    language: 'zh-Hans',
    debug: true,
  };

  theme: ThemeState = {
    mode: 'dark',
    baseLight: false,
    accentHex: null,
    accentUsable: true,
    fill: '#c8401c',
    surface: '#f5e6e2',
    text: '#c8401c',
    defaultAccent: '#c8401c',
  };

  windowState: WindowState = { state: 'normal', topmost: false };

  /** 等待删除确认的宏名；dialog.respond 带着答案回来时用。 */
  private pendingDelete: string | null = null;

  runState: RunState = {
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
    logFrom: 0,
  };

  settingsState: SettingsState = {
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

  private mcp: McpState = {
    allowControl: false,
    allowRun: false,
    config: '{\n  "mcpServers": {\n    "whalegenie": {\n      "command": "WhaleGenie.exe",\n      "args": ["--mcp"]\n    }\n  }\n}',
    auditFile: '/mock/logs/mcp-2026-11-21.log',
  };

  surfaceMenu: SurfaceMenuSnapshot = demoSurfaceMenu;

  surfaceWheel: SurfaceWheelSnapshot = demoSurfaceWheel;

  surfacePanel: SurfacePanelSnapshot = demoSurfacePanel;

  projectState: ProjectState = {
    path: null,
    name: null,
    untitled: true,
    dirty: false,
    revision: 0,
    systemEnabled: false,
    enabledFirst: true,
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

  /** 编辑器域的假宿主：多文档、步骤树与录制都在 MockEditor 里。 */
  private readonly mockEditor = new MockEditor(
    () => this.projectState.macros,
    () => this.info.language,
  );

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
    try {
      if (method.startsWith('settings.')) {
        result = handleSettings(this, method, params);
      } else if (method.startsWith('run.')) {
        result = handleRun(this, method, params);
      } else if (method === 'surface.panel.view') {
        this.surfacePanel = {
          ...this.surfacePanel,
          collapsed: params['collapsed'] === true,
          horizontal: params['horizontal'] === true,
        };
        result = this.surfacePanel;
      } else if (method === 'surface.menu.drag') {
        result = undefined;
      } else if (method === 'surface.panel.drag') {
        result = undefined;
      } else if (method === 'surface.panel.run') {
        result = { started: true };
      } else if (method === 'surface.panel.state') {
        result = this.surfacePanel;
      } else if (method === 'surface.wheel.state') {
        result = this.surfaceWheel;
      } else if (method.startsWith('surface.')) {
        result = handleSurfaceMenu(this, method, params);
      } else if (method.startsWith('app.') || method.startsWith('strings.')
          || method.startsWith('theme.') || method.startsWith('window.')) {
        result = handleShell(this, method, params);
      } else {
      switch (method) {
        case 'bridge.ready':
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
        case 'project.delete':
          this.pendingDelete = String(params['name']);
          this.push('dialog.request', {
            requestId: 2,
            title: wordTables.zh['Editor.DeleteTitle']!,
            message: `确定删除“${String(params['name'])}”？此操作无法撤销。`,
            confirmLabel: wordTables.zh['Editor.Delete']!,
            cancelLabel: wordTables.zh['Common.Cancel']!,
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
            title: wordTables.zh['Main.CloseAllTitle']!,
            message: wordTables.zh['Main.CloseAllMessage']!,
            confirmLabel: wordTables.zh['Main.CloseAll']!,
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
          break;
        default:
          if (method.startsWith('editor.')) {
            result = this.mockEditor.run(method, params);
            break;
          }

          this.answer({ id: request.id, ok: false, error: { code: 'NoSuchMethod', message: method } });
          return;
      }
      }
    } catch (error) {
      this.answer({
        id: request.id,
        ok: false,
        error: {
          code: 'BadArgument',
          message: error instanceof Error ? error.message : String(error),
        },
      });
      return;
    }

    this.answer({ id: request.id, ok: true, result });
  }

  bundle(): StringsBundle {
    // The words come from the program's own table, generated by the contracts tool: a copy here
    // would drift away from what the real host sends the page.
    const entries = this.info.language === 'en' ? wordTables.en : wordTables.zh;
    return { language: this.info.language, entries };
  }

  private answer(message: unknown): void {
    // 下一轮才送，且做一次 JSON 往返：真实宿主的每个信封都是序列化过来的新对象，页面组件
    // 对"引用没变"会当作没变化跳过重画——mock 复用内部对象就会把这类 bug 漏到真机上才炸。
    const copy = JSON.parse(JSON.stringify(message)) as unknown;
    queueMicrotask(() => this.receive?.(copy));
  }
}


