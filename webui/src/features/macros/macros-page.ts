import { LitElement, html, nothing } from 'lit';
import { macrosPageCss } from './macros-page-css.js';
import { customElement, query, state } from 'lit/decorators.js';
import { go } from '../../shared/navigation/router.js';
import { bridge } from '../../shared/bridge/bridge.js';
import { describeError } from '../../shared/errors.js';
import { icon } from '../../shared/icons.js';
import { project } from '../../shared/project/store.js';
import { strings, t } from '../../shared/strings/strings.js';
import { StoreController } from '../../shared/lit/store-controller.js';
import type {
  BridgeFailurePayload,
  BridgeMethod,
  MacroEntry,
  ProjectState,
} from '../../shared/bridge/protocol.js';
import type { WgMcpPanel } from '../mcp/index.js';
import '../mcp/index.js';
import { startExport, transfer } from '../transfer/index.js';
import { filterMacros } from './macros-model.js';

/**
 * 视图到文案键的显式映射：程序给的是 MacroView 的枚举名，页面按字面量取词。生成器的 mock 词表
 * 与对账测试都按字面量扫键，动态拼出来的键两边都扫不到，于是 mock 下只剩原始键名。
 */
const dockKeys: Record<string, string> = {
  All: 'Main.Dock.All',
  Keys: 'Main.Dock.Keys',
  Interface: 'Main.Dock.Interface',
  Hotstring: 'Main.Dock.Hotstring',
  Menu: 'Main.Dock.Menu',
  Wheel: 'Main.Dock.Wheel',
  Events: 'Main.Dock.Events',
};

/**
 * 宏列表页：视图 dock、分组、搜索与「已启用优先」的排序都归页面，程序只给宏属于哪些视图和
 * 这个开关的当前值；启停与系统开关经桥命令程序。空状态有两种——没有宏、这一格视图里没有
 * 匹配的宏——与旧主窗的用户体感一致。
 */
@customElement('wg-macros')
export class WgMacros extends LitElement {
  @state()
  private state: ProjectState | null = null;

  @state()
  private view = 'All';

  /** 分组筛选：'' 是"全部"，null 是"未分组"，其余是组名——与程序侧的过滤同形。 */
  @state()
  private group: string | null = '';

  @state()
  private search = '';

  @state()
  private note = '';

  @query('wg-mcp-panel')
  private mcpPanel!: WgMcpPanel;

  static styles = macrosPageCss;

  protected readonly stringsController = new StoreController(this, strings);

  protected readonly projectController = new StoreController(this, project, (state) => {
    this.state = state;
  });

  render() {
    if (this.state === null) {
      return nothing;
    }

    const visible = this.visibleMacros();

    return html`
      <div class="filebar">
        ${this.state.dirty
          ? html`<fluent-badge appearance="tint" color="warning">${t('Shell.Field.Dirty')}</fluent-badge>`
          : nothing}
        <span class="path">${this.state.path ?? ''}</span>
        ${this.state.path ? html`
          <fluent-button appearance="subtle" @click=${this.onReload}>${t('Main.ReloadCurrent')}</fluent-button>
        ` : nothing}
        <fluent-button appearance="subtle" @click=${this.onMcp}>${t('Mcp.Title')}</fluent-button>
      </div>
      <div class="filters">
        <div class="dock" role="tablist">
          ${this.state.views.map((view) => html`
            <button
              role="tab"
              aria-current=${String(this.view === view)}
              @click=${() => this.selectView(view)}
            >
              ${t(dockKeys[view] ?? view)}
            </button>
          `)}
        </div>
        <div class="tools">
          ${this.groupChips()}
          <input
            type="search"
            placeholder=${t('Main.SearchPlaceholder')}
            aria-label=${t('Main.SearchPlaceholder')}
            @input=${this.onSearchInput}
            .value=${this.search}
          />
        </div>
        ${this.note.length > 0
          ? html`<fluent-message-bar layout="multiline" @dismiss=${this.onNoteDismiss}>${this.note}</fluent-message-bar>`
          : nothing}
      </div>
      <div class="list">
        ${this.state.macros.length === 0
          ? this.renderEmpty()
          : visible.length === 0
            ? this.renderNoMatch()
            : visible.map((entry) => this.renderCard(entry))}
      </div>
      <div class="footer">
        <div class="system">
          <fluent-switch
            ?checked=${this.state.systemEnabled}
            aria-label=${t(this.state.systemEnabled ? 'Main.SystemEnabled' : 'Main.SystemDisabled')}
            @change=${this.onSystemToggle}
          ></fluent-switch>
          <span>${t(this.state.systemEnabled ? 'Main.SystemEnabled' : 'Main.SystemDisabled')}</span>
        </div>
        <fluent-button appearance="accent" @click=${this.onAddMacro}>${t('Main.AddMacro')}</fluent-button>
      </div>
      <wg-mcp-panel></wg-mcp-panel>
    `;
  }

  private groupChips() {
    if (this.state === null || this.state.groups.length === 0) {
      return nothing;
    }

    const chip = (value: string | null, label: string) => html`
      <button
        aria-current=${String(this.group === value)}
        @click=${() => this.selectGroup(value)}
      >
        ${label}
      </button>
    `;

    return html`
      <div class="dock">
        ${chip('', t('Main.GroupAll'))}
        ${this.state.groups.map((name) => chip(name, name))}
        ${chip(null, t('Main.Ungrouped'))}
      </div>
    `;
  }

  private visibleMacros(): MacroEntry[] {
    if (this.state === null) {
      return [];
    }

    return filterMacros(this.state.macros, {
      view: this.view,
      group: this.group,
      search: this.search,
      enabledFirst: this.state.enabledFirst,
    });
  }

  private renderEmpty() {
    return html`
      <div class="empty">
        <fluent-text size="400" weight="semibold">${t('Main.EmptyTitle')}</fluent-text>
        <span>${t('Main.EmptyHint')}</span>
      </div>
    `;
  }

  private renderNoMatch() {
    return html`
      <div class="empty">
        <span>${t('Main.NoMatch')}</span>
      </div>
    `;
  }

  private renderCard(entry: MacroEntry) {
    return html`
      <div class="card" @dblclick=${() => this.onEdit(entry)}>
        <span class="dot" data-enabled=${String(entry.enabled)}></span>
        <div class="body">
          <span class="name">
            ${entry.name}
            ${entry.running ? html`<fluent-badge color="success">${t('Main.MacroRunning')}</fluent-badge>` : nothing}
            ${entry.group.trim().length > 0
              ? html`<span class="group">· ${entry.group}</span>`
              : nothing}
          </span>
          <span class="triggers">${entry.triggerSummary}</span>
        </div>
        <div class="iconbuttons">
          <button
            title=${t('Main.RunDebug')}
            aria-label=${t('Main.RunDebug')}
            @click=${() => this.onRun(entry)}
          >
            ${icon('run')}
          </button>
          <button
            title=${t('Main.EditTip')}
            aria-label=${t('Main.EditTip')}
            @click=${() => this.onEdit(entry)}
          >
            ${icon('edit')}
          </button>
          <button
            title=${t('Main.ExportMacro')}
            aria-label=${t('Main.ExportMacro')}
            @click=${() => this.onExport(entry)}
          >
            ${icon('export')}
          </button>
          <button
            class="danger"
            title=${t('Main.DeleteTip')}
            aria-label=${t('Main.DeleteTip')}
            @click=${() => this.onDelete(entry)}
          >
            ${icon('delete')}
          </button>
        </div>
        <fluent-switch
          ?checked=${entry.enabled}
          aria-label=${t(entry.enabled ? 'Main.DisableMacro' : 'Main.EnableMacro')}
          @change=${() => this.onToggle(entry)}
        ></fluent-switch>
      </div>
    `;
  }

  private selectView(view: string): void {
    this.view = view;
  }

  private selectGroup(value: string | null): void {
    this.group = value;
  }

  private onSearchInput(event: Event): void {
    this.search = (event.target as HTMLInputElement).value;
  }

  private onToggle(entry: MacroEntry): void {
    void this.call('project.toggle', { name: entry.name });
  }

  private onSystemToggle(): void {
    if (this.state === null) {
      return;
    }

    // 页面不翻转自己的状态：真相在程序侧，project.changed 会带着新值回来。
    void this.call('project.system', { enabled: !this.state.systemEnabled });
  }

  private async onExport(entry: MacroEntry): Promise<void> {
    // The plan is the page's call, so a project it cannot read is said here; the wizard asks the
    // rest — which macros travel and where the package goes.
    try {
      const session = await startExport(bridge.value);
      transfer.show(session, [entry.name]);
    } catch (error) {
      this.note = describeError(error);
    }
  }

  private onDelete(entry: MacroEntry): void {
    void this.call('project.delete', { name: entry.name });
  }

  private onReload(): void {
    void this.call('project.reload', {});
  }

  private onMcp(): void {
    void this.mcpPanel.show();
  }

  /** 发命令，把程序的拒绝（坏包、没这个宏）拿到提示条上说，而不是静默失败。 */
  private async call(method: BridgeMethod, params?: unknown): Promise<boolean> {
    try
    {
      await bridge.value.call(method, params);
      return true;
    }
    catch (error)
    {
      const failure = error as BridgeFailurePayload;
      this.note = failure?.message ?? String(error);
      return false;
    }
  }

  private async onAddMacro(): Promise<void> {
    // 网页编辑器是默认编辑器：先开一份 C# 会话，再切到编辑器路由。
    if (await this.call('editor.session', { kind: 'new' })) {
      go('editor');
    }
  }

  private async onEdit(entry: MacroEntry): Promise<void> {
    if (await this.call('editor.session', { kind: 'open', name: entry.name })) {
      go('editor');
    }
  }

  private onRun(entry: MacroEntry): void {
    // The run panel is a page too; the name goes across the bridge and the shell switches route.
    void this.call('app.openRun', { name: entry.name });
  }

  private onNoteDismiss(): void {
    this.note = '';
  }
}

declare global {
  interface HTMLElementTagNameMap {
    'wg-macros': WgMacros;
  }
}
