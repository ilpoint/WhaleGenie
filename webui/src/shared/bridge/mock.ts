import { demoMenu, demoSurfacePanel, demoSurfaceMenu, demoSurfaceWheel, demoWheel } from './mock-fixtures.js';
import wordTables from './generated/strings.json';
import { handleSettings } from './mock-settings.js';
import { handleWindow } from './mock-window.js';
import { handleRun } from './mock-run.js';
import { handleShell } from './mock-shell.js';
import { handleSurfaceMenu } from './mock-menu.js';
import type { BridgeTransport } from './transport.js';
import { MockEditor } from './mock-editor.js';
import { MockTransfer } from './mock-transfer.js';
import type {
  AppInfo,
  EditorSnapshot,
  LibraryEntry,
  MacroEntry,
  McpState,
  ProjectState,
  SettingsState,
  RunState,
  SystemSwitchState,
  StringsBundle,
  SurfaceMenuSnapshot,
  SurfacePanelSnapshot,
  SurfaceWheelSnapshot,
  ThemeState,
  WindowState,
} from './protocol.js';
import raw from './data/host-state.json';

/**
 * 浏览器里的假宿主：不依赖程序就能开发壳、跑 Playwright。它回答壳用到的每个方法，并留出
 * `push()` 让测试往页面里灌事件。真实词表以程序下发的为准——这里只放壳需要的键，与真表
 * 同步更新（有测试对账）。
 */

interface HostStateData {
  info: AppInfo;
  theme: ThemeState;
  window: WindowState;
  run: RunState;
  settings: Omit<SettingsState, 'menu' | 'wheel'>;
  library: LibraryEntry[];
  mcp: McpState;
  project: ProjectState;
  openedMacro: MacroEntry;
  icons: { names: string[]; path: string };
}

const STATE = raw as unknown as HostStateData;

export class MockTransport implements BridgeTransport {
  private receive?: (message: unknown) => void;

  /** 页面发来的请求按序记录，调试与 e2e 断言用。 */
  readonly seen: string[] = [];

  /** 每次请求的方法与参数：同一方法下有多个命令时，e2e 靠 params 区分种类。 */
  readonly calls: { method: string; params: Record<string, unknown> }[] = [];

  info: AppInfo = structuredClone(STATE.info);

  theme: ThemeState = structuredClone(STATE.theme);

  windowState: WindowState = structuredClone(STATE.window);

  /** 等待删除确认的宏名；dialog.respond 带着答案回来时用。 */
  private pendingDelete: string | null = null;

  /** 导入导出向导的假应答：包的形状、勾选与落盘都按真桥来。 */
  private readonly mockTransfer = new MockTransfer(
    () => this.projectState,
    (state) => { this.projectState = state; },
    (event, payload) => this.push(event, payload),
  );

  runState: RunState = structuredClone(STATE.run);

  settingsState: SettingsState = {
    ...structuredClone(STATE.settings),
    menu: demoMenu,
    wheel: demoWheel,
  };

  private libraryEntries: LibraryEntry[] = structuredClone(STATE.library);

  private mcp: McpState = structuredClone(STATE.mcp);

  surfaceMenu: SurfaceMenuSnapshot = demoSurfaceMenu;

  surfaceWheel: SurfaceWheelSnapshot = demoSurfaceWheel;

  surfacePanel: SurfacePanelSnapshot = demoSurfacePanel;

  projectState: ProjectState = structuredClone(STATE.project);

  /** 编辑器域的假宿主：多文档、步骤树与录制都在 MockEditor 里。 */
  private readonly mockEditor = new MockEditor(
    () => this.projectState.macros,
    () => this.info.language,
    (document, previousName) => this.macroSaved(document, previousName),
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

  /** The switch and its key as the side bar reads them. The project owns the switch. */
  system(): SystemSwitchState {
    return {
      enabled: this.projectState.systemEnabled,
      hotkeyDisplay: this.settingsState.systemHotkey.length > 0
        ? this.settingsState.systemHotkey
        : wordTables.zh['Common.None']!,
    };
  }

  /** The editor saved a macro: the mock project really takes the new name and contents. */
  private macroSaved(document: EditorSnapshot, previousName: string | null): void {
    if (document.isSubMacro) {
      return;
    }

    const macros = [...this.projectState.macros];
    const at = macros.findIndex((entry) => entry.name === previousName
      || entry.name === document.name);
    const existing = at >= 0 ? macros[at] : undefined;
    const entry: MacroEntry = {
      name: document.name,
      group: document.group,
      description: document.description,
      enabled: existing?.enabled ?? true,
      running: false,
      triggerSummary: existing?.triggerSummary ?? '',
      triggerActions: existing?.triggerActions ?? '',
      triggerPreview: existing?.triggerPreview ?? '',
      views: existing?.views ?? ['All'],
    };
    if (at >= 0) {
      macros[at] = entry;
    } else {
      macros.push(entry);
    }

    this.projectState = {
      ...this.projectState,
      macros,
      dirty: !this.projectState.autoSave,
      revision: this.projectState.revision + 1,
    };
    this.push('project.changed', this.projectState);
  }

  /** 测试与演练用：假装宿主推来一个事件。 */
  push(event: string, payload: unknown): void {
    this.answer({ event, payload });
  }

  send(envelope: unknown): void {
    const request = envelope as { id: number; method: string; params?: Record<string, unknown> };
    const method = request.method;
    const params = request.params ?? {};
    this.seen.push(method);
    // 浅拷贝：调用方之后改写自己的 params 时，记录不会跟着变。
    this.calls.push({ method, params: { ...params } });

    let result: unknown;
    try {
      if (MockTransfer.handles(method)) {
        result = this.mockTransfer.answer(method, params);
      } else if (method.startsWith('settings.')) {
        result = handleSettings(this, method, params);
      } else if (method === 'window.list' || method === 'window.pick') {
        result = handleWindow(method);
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
      } else if (method === 'icon.names') {
        result = STATE.icons.names;
      } else if (method === 'icon.path') {
        // 开发页没有 Lucide 表，用一个圆角方框代替；路径仍是 SVG 能读的一条。
        result = { path: STATE.icons.path };
      } else if (method === 'surface.wheel.ready') {
        result = null;
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
        case 'library.create':
          this.libraryEntries = [...this.libraryEntries, {
            id: 'library-' + String(this.libraryEntries.length + 1),
            name: '公共宏 ' + String(this.libraryEntries.length + 1),
            version: 1,
            description: '',
          }];
          result = { entries: this.libraryEntries, error: null };
          break;
        case 'library.import':
          this.libraryEntries = [...this.libraryEntries, {
            id: 'library-' + String(this.libraryEntries.length + 1),
            name: '包里的宏',
            version: 1,
            description: '',
          }];
          result = { entries: this.libraryEntries, error: null };
          break;
        case 'library.publish': {
          const name = String(params['name'] ?? '');
          const macro = this.projectState.macros.find((entry) => entry.name === name);
          const existing = this.libraryEntries.find((entry) => entry.name === name);
          this.libraryEntries = existing
            ? this.libraryEntries.map((entry) => entry.id === existing.id
              ? { ...entry, version: entry.version + 1 }
              : entry)
            : [...this.libraryEntries, {
              id: 'library-' + String(this.libraryEntries.length + 1),
              name,
              version: 1,
              description: macro?.description ?? '',
            }];
          result = { entries: this.libraryEntries, error: null };
          break;
        }
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
        case 'project.state':
          result = this.projectState;
          break;
        case 'system.state':
          result = this.system();
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
          this.push('system.changed', this.system());
          break;
        case 'dialog.pickImage':
          result = { path: 'C:\\shot.png' };
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
            macros: [structuredClone(STATE.openedMacro)],
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
          // A handler may carry a specific code (an editor close that needs the person's answer);
          // everything else keeps the generic refusal.
          code: typeof (error as { code?: unknown })?.code === 'string'
            ? (error as { code: string }).code
            : 'BadArgument',
          message: error instanceof Error ? error.message : String(error),
        },
      });
      return;
    }

    if (method.startsWith('library.')
        && result !== null && typeof result === 'object' && 'entries' in result) {
      this.push('library.changed', result);
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

