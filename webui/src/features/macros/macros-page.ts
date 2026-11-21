import { LitElement, css, html, nothing } from 'lit';
import { customElement, query, state } from 'lit/decorators.js';
import { bridge } from '../../shared/bridge/bridge.js';
import { project } from '../../shared/project/store.js';
import { strings, t } from '../../shared/strings/strings.js';
import type { BridgeFailurePayload, MacroEntry, ProjectState } from '../../shared/bridge/protocol.js';
import type { WgMcpPanel } from '../mcp/mcp-panel.js';
import '../mcp/mcp-panel.js';

/**
 * 宏列表页：视图 dock、分组与搜索的过滤是页面自己的事（状态整包在手），启停与系统开关经桥
 * 命令程序。空状态有三种——没有宏、有宏但这一格视图没东西、搜不到——与旧主窗一致。
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

  static styles = css`
    :host {
      display: flex;
      flex-direction: column;
      height: 100%;
    }

    .filters {
      display: flex;
      flex-direction: column;
      gap: var(--spacingVerticalS);
      padding: var(--spacingVerticalM) var(--spacingHorizontalXL);
      border-bottom: var(--strokeWidthThin) solid var(--colorNeutralStroke1);
    }

    .filebar {
      display: flex;
      align-items: center;
      gap: var(--spacingHorizontalSN);
      padding: var(--spacingVerticalS) var(--spacingHorizontalXL) 0;
    }

    .filebar .path {
      color: var(--colorNeutralForeground4);
      font-size: var(--font-size-base-200);
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
      min-width: 0;
      flex: 1;
      text-align: right;
    }

    .dock {
      display: flex;
      flex-wrap: wrap;
      gap: var(--spacingHorizontalSN);
    }

    .dock button {
      border: none;
      border-radius: var(--borderRadiusMedium);
      background: transparent;
      color: var(--colorNeutralForeground1);
      font: inherit;
      padding: var(--spacingVerticalSN) var(--spacingHorizontalM);
      cursor: pointer;
    }

    .dock button:hover {
      background: var(--colorNeutralBackground1Hover);
    }

    .dock button[aria-current='true'] {
      background: var(--colorBrandBackground);
      color: var(--colorNeutralForegroundOnBrand);
    }

    .dock button:focus-visible,
    .tools input:focus-visible,
    .footer fluent-button:focus-visible,
    .footer fluent-switch:focus-visible {
      outline: var(--strokeWidthThicker) solid var(--colorStrokeFocus2);
      outline-offset: -1px;
    }

    .tools {
      display: flex;
      align-items: center;
      gap: var(--spacingHorizontalM);
      flex-wrap: wrap;
    }

    .tools input {
      flex: 1;
      min-width: 160px;
      max-width: 320px;
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke1);
      border-bottom: var(--strokeWidthThicker) solid var(--colorNeutralStrokeAccessible);
      border-radius: var(--borderRadiusMedium);
      background: var(--colorNeutralBackground1);
      color: var(--colorNeutralForeground1);
      font: inherit;
      padding: var(--spacingVerticalSN) var(--spacingHorizontalM);
    }

    .tools input::placeholder {
      color: var(--colorNeutralForeground4);
    }

    .list {
      flex: 1;
      overflow: auto;
      display: flex;
      flex-direction: column;
      gap: var(--spacingVerticalSN);
      padding: var(--spacingVerticalM) var(--spacingHorizontalXL);
    }

    .card {
      display: grid;
      grid-template-columns: auto 1fr auto;
      align-items: center;
      gap: var(--spacingHorizontalM);
      background: var(--colorNeutralBackground1);
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke1);
      border-radius: var(--borderRadiusMedium);
      padding: var(--spacingVerticalMN) var(--spacingHorizontalL);
    }

    .dot {
      width: 10px;
      height: 10px;
      border-radius: 50%;
      background: var(--colorNeutralStrokeAccessible);
    }

    .dot[data-enabled='true'] {
      background: var(--colorBrandForeground1);
    }

    .card .body {
      display: flex;
      flex-direction: column;
      gap: var(--spacingVerticalXS);
      min-width: 0;
    }

    .card .name {
      font-weight: var(--font-weight-semibold);
      display: flex;
      align-items: center;
      gap: var(--spacingHorizontalS);
    }

    .card .name .group {
      color: var(--colorNeutralForeground3);
      font-weight: var(--font-weight-regular);
    }

    .card .triggers {
      color: var(--colorNeutralForeground2);
      font-size: var(--font-size-base-200);
    }

    .card fluent-switch {
      justify-self: end;
    }

    .iconbuttons {
      display: flex;
      gap: var(--spacingHorizontalXS);
    }

    .iconbuttons button {
      width: 32px;
      height: 32px;
      display: grid;
      place-items: center;
      border: none;
      border-radius: var(--borderRadiusMedium);
      background: transparent;
      color: var(--colorNeutralForeground2);
      cursor: pointer;
      padding: 0;
    }

    .iconbuttons button:hover {
      background: var(--colorNeutralBackground1Hover);
      color: var(--colorNeutralForeground1);
    }

    .iconbuttons button:focus-visible {
      outline: var(--strokeWidthThicker) solid var(--colorStrokeFocus2);
      outline-offset: -1px;
    }

    .iconbuttons button.danger:hover {
      background: var(--colorPaletteRedBackground1);
      color: var(--colorPaletteRedForeground1);
    }

    .iconbuttons svg {
      width: 16px;
      height: 16px;
    }

    .empty {
      flex: 1;
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      gap: var(--spacingVerticalS);
      color: var(--colorNeutralForeground3);
      text-align: center;
      padding: var(--spacingVerticalXXL);
    }

    .footer {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--spacingHorizontalL);
      padding: var(--spacingVerticalM) var(--spacingHorizontalXL);
      border-top: var(--strokeWidthThin) solid var(--colorNeutralStroke1);
    }

    .footer .system {
      display: flex;
      align-items: center;
      gap: var(--spacingHorizontalM);
    }
  `;

  private unsubscribers: Array<() => void> = [];

  connectedCallback(): void {
    super.connectedCallback();
    this.unsubscribers = [
      strings.subscribe(() => this.requestUpdate()),
      project.subscribe((state) => {
        this.state = state;
      }),
    ];
  }

  disconnectedCallback(): void {
    for (const unsubscribe of this.unsubscribers) {
      unsubscribe();
    }
    this.unsubscribers = [];
    super.disconnectedCallback();
  }

  render() {
    if (this.state === null) {
      return nothing;
    }

    const visible = this.visibleMacros();

    return html`
      <div class="filebar">
        <fluent-button appearance="subtle" @click=${this.onOpen}>${t('Main.OpenProject')}</fluent-button>
        <fluent-button appearance="subtle" @click=${this.onSave}>${t('Main.SaveProject')}</fluent-button>
        <fluent-button appearance="subtle" @click=${this.onSaveAs}>${t('Main.SaveProjectAs')}</fluent-button>
        ${this.state.path ? html`
          <fluent-button appearance="subtle" @click=${this.onReload}>${t('Main.ReloadCurrent')}</fluent-button>
        ` : nothing}
        ${this.state.macros.length > 0 ? html`
          <fluent-button appearance="subtle" @click=${this.onCloseAll}>${t('Main.CloseAll')}</fluent-button>
        ` : nothing}
        ${this.state.dirty ? html`<fluent-badge appearance="warning">${t('Shell.Field.Dirty')}</fluent-badge>` : nothing}
        <span class="path">${this.state.path ?? ''}</span>
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
              ${t(`Main.Dock.${view}`)}
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

    const wanted = this.search.trim().toLowerCase();
    return this.state.macros
      .filter((entry) => entry.views.includes(this.view))
      .filter((entry) => this.group === ''
        || (this.group === null ? entry.group.trim().length === 0
          : entry.group.trim().toLowerCase() === this.group.toLowerCase()))
      .filter((entry) => wanted.length === 0
        || entry.name.toLowerCase().includes(wanted))
      .sort((left, right) => Number(right.enabled) - Number(left.enabled));
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
            title=${t('Main.EditTip')}
            aria-label=${t('Main.EditTip')}
            @click=${() => this.onEdit(entry)}
          >
            <svg viewBox="0 0 24 24" aria-hidden="true">
              <path d="M17 3a2.83 2.83 0 1 1 4 4L7.5 20.5 2 22l1.5-5.5L17 3z" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linejoin="round" />
            </svg>
          </button>
          <button
            title=${t('Main.ExportMacro')}
            aria-label=${t('Main.ExportMacro')}
            @click=${() => this.onExport(entry)}
          >
            <svg viewBox="0 0 24 24" aria-hidden="true">
              <path d="M4 12v8a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-8" fill="none" stroke="currentColor" stroke-width="1.6" />
              <path d="M16 6l-4-4-4 4M12 2v13" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" />
            </svg>
          </button>
          <button
            class="danger"
            title=${t('Main.DeleteTip')}
            aria-label=${t('Main.DeleteTip')}
            @click=${() => this.onDelete(entry)}
          >
            <svg viewBox="0 0 24 24" aria-hidden="true">
              <path d="M3 6h18" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" />
              <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linejoin="round" />
            </svg>
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

  private onOpen(): void {
    void this.call('project.open', {});
  }

  private onExport(entry: MacroEntry): void {
    void this.call('project.export', { name: entry.name });
  }

  private onDelete(entry: MacroEntry): void {
    void this.call('project.delete', { name: entry.name });
  }

  private onSave(): void {
    void this.call('project.save', {});
  }

  private onSaveAs(): void {
    void this.call('project.saveAs', {});
  }

  private onReload(): void {
    void this.call('project.reload', {});
  }

  private onCloseAll(): void {
    void this.call('project.closeAll', {});
  }

  private onMcp(): void {
    void this.mcpPanel.show();
  }

  /** 发命令，把程序的拒绝（坏包、没这个宏）拿到提示条上说，而不是静默失败。 */
  private async call(method: string, params?: unknown): Promise<void> {
    try
    {
      await bridge.value.call(method, params);
    }
    catch (error)
    {
      const failure = error as BridgeFailurePayload;
      this.note = failure?.message ?? String(error);
    }
  }

  private onAddMacro(): void {
    // 编辑器还是原生窗口：命令过桥打开它，保存时宏落进项目、列表经 project.changed 刷新。
    void this.call('editor.newMacro', { view: this.view });
  }

  private onEdit(entry: MacroEntry): void {
    void this.call('editor.open', { name: entry.name });
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
