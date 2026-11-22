import { demoMenu, demoWheel, demoWindows } from './mock-fixtures.js';
import { mockExtra } from './mock-strings-extra.js';
import { chromeKeys, english } from './mock-strings-en.js';
import { chinese } from './mock-strings-zh.js';
import type { BridgeTransport } from './transport.js';
import { MockEditor } from './mock-editor.js';
import type {
  AppInfo,
  McpState,
  ProjectState,
  SettingsWheelSlot,
  SettingsWheelState,
  StringsBundle,
  ThemeState,
  WindowRuleState,
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

  private info: AppInfo = {
    name: 'WhaleGenie',
    version: '0.0.0-mock',
    language: 'zh-Hans',
    debug: true,
    project: { path: null, name: null, untitled: true, dirty: false, revision: 0, macros: 0 },
  };

  private theme: ThemeState = {
    mode: 'dark', baseLight: false, accentHex: null, defaultAccent: '#c8401c',
  };

  private windowState: WindowState = { state: 'normal', topmost: false };

  /** 等待删除确认的宏名；dialog.respond 带着答案回来时用。 */
  private pendingDelete: string | null = null;

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
          if (method.startsWith('editor.')) {
            result = this.mockEditor.run(method, params);
            break;
          }

          this.answer({ id: request.id, ok: false, error: { code: 'NoSuchMethod', message: method } });
          return;
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


