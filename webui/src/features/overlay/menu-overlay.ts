import { LitElement, html, nothing } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import { bridgeApi } from '../../shared/bridge/bridge.js';
import type { SurfaceMenuCell, SurfaceMenuPage, SurfaceMenuSnapshot } from '../../shared/bridge/protocol.js';
import { strings, t } from '../../shared/strings/strings.js';
import { StoreController } from '../../shared/lit/store-controller.js';
import { fileUrl } from '../../shared/files/file-url.js';
import { menuOverlayStyles } from './menu-overlay.styles.js';
import logoUrl from '../../assets/whale.png';
import '../../shared/icons/icon.js';
import { renderCellMenu, renderPageMenu } from './menu-overlay.menus.js';
import type { CellMenuState, PageMenuState } from './menu-overlay.menus.js';
const Columns = 4; const Rows = 3;

/**
 * 菜单浮层：把 C# 给的 `surface.menu.state` 画成原生尺寸的 4×3 两段菜单。
 *
 * 窗口 350×667、格子 80×80、标题 40、搜索区 34、分隔条 44、底栏 28；这些数与原生
 * `MacroMenuWindow` 的常量一致。搜索框默认不可见也不可聚焦，点放大镜才把键盘交给页面。
 */
@customElement('wg-overlay-menu')
export class WgOverlayMenu extends LitElement {
  static styles = menuOverlayStyles;

  @state()
  private snapshot: SurfaceMenuSnapshot | null = null;

  @state()
  private common = 0;

  @state()
  private context = 0;

  @state()
  private query = '';

  @state()
  private error = '';

  @state()
  private pinned = false;

  @state()
  private searchOpen = false;

  @state()
  private menuFor: CellMenuState | null = null;

  @state()
  private pageMenu: PageMenuState | null = null;

  @state()
  private dragging: string | null = null;

  protected readonly stringsController = new StoreController(this, strings);

  protected override async firstUpdated(): Promise<void> {
    await this.reload();
    window.addEventListener('focus', () => void this.reload());
    bridgeApi.on('surface.menu.closed', () => {
      this.searchOpen = false;
      this.query = '';
      this.menuFor = null;
      this.pageMenu = null;
      void this.reload();
    });
    window.addEventListener('keydown', (event) => {
      if (event.key !== 'Escape') {
        return;
      }

      event.preventDefault();
      if (this.menuFor) {
        this.menuFor = null;
      } else if (this.pageMenu) {
        this.pageMenu = null;
      } else if (this.searchOpen) {
        this.closeSearch();
      } else {
        void bridgeApi.call('surface.menu.close');
      }
    });
    window.addEventListener('pointerdown', (event) => {
      this.menuFor = null;
      this.pageMenu = null;
      if (!this.searchOpen) {
        return;
      }

      const target = event.target as HTMLElement;
      if (!target.closest('.search-slot') && !target.closest('.tool.search')) {
        this.closeSearch();
      }
    });
  }

  private async reload(): Promise<void> {
    try {
      this.snapshot = await bridgeApi.call<SurfaceMenuSnapshot>('surface.menu.state');
      this.pinned = this.snapshot.pinned;
      this.error = '';
    } catch (failure) {
      this.error = failure instanceof Error ? failure.message : String(failure);
    }
  }

  private get commonPages(): SurfaceMenuPage[] {
    return this.snapshot?.common ?? [];
  }

  private get contextPages(): SurfaceMenuPage[] {
    return this.snapshot?.context ?? [];
  }

  private get filtering(): boolean {
    return this.query.trim().length > 0;
  }

  private matchesQuery(cell: SurfaceMenuCell): boolean {
    const wanted = this.query.trim().toLowerCase();
    return wanted.length === 0
      || cell.macroName.toLowerCase().includes(wanted)
      || cell.description.toLowerCase().includes(wanted);
  }

  private stop(event: Event): void {
    event.stopPropagation();
  }

  private async toggleSearch(): Promise<void> {
    if (this.searchOpen) {
      this.closeSearch();
      return;
    }

    this.searchOpen = true;
    try {
      await bridgeApi.call('surface.menu.focus', { focused: true });
    } finally {
      await this.updateComplete;
      // The button's own click is still settling when the await returns; one task later the
      // input is the element that keeps the keyboard.
      setTimeout(() => this.searchField()?.focus(), 0);
    }
  }

  private closeSearch(): void {
    if (!this.searchOpen) {
      return;
    }

    this.searchOpen = false;
    this.query = '';
    this.searchField()?.blur();
    void bridgeApi.call('surface.menu.focus', { focused: false });
  }

  private searchField(): HTMLInputElement | null {
    return this.renderRoot.querySelector<HTMLInputElement>('.search-slot input');
  }

  private async togglePin(): Promise<void> {
    const wanted = !this.pinned;
    try {
      const answer = await bridgeApi.call<{ pinned: boolean }>('surface.menu.pin', { pinned: wanted });
      this.pinned = answer.pinned;
    } catch {
      // 宿主没接住时保持原状态，按钮不能假装已钉住。
    }
  }

  private startDrag(event: PointerEvent): void {
    if (event.button !== 0 || (event.target as HTMLElement).closest('button')) {
      return;
    }

    void bridgeApi.call('surface.menu.drag');
  }

  private openSettings(): void {
    void bridgeApi.call('surface.menu.settings');
  }

  private close(): void {
    void bridgeApi.call('surface.menu.close');
  }

  private openCellMenu(event: MouseEvent, cell: SurfaceMenuCell): void {
    event.preventDefault();
    event.stopPropagation();
    this.pageMenu = null;
    this.menuFor = { cell, x: event.clientX, y: event.clientY };
  }

  private openPageMenu(event: MouseEvent, pageId: string): void {
    event.preventDefault();
    event.stopPropagation();
    if (this.commonPages.length + this.contextPages.length <= 2) {
      return;
    }

    this.menuFor = null;
    this.pageMenu = { pageId, x: event.clientX, y: event.clientY };
  }

  private async activate(cell: SurfaceMenuCell): Promise<void> {
    this.menuFor = null;
    try {
      await bridgeApi.call('surface.menu.activate', { entryId: cell.entryId });
    } catch {
      // 快照与点击之间宏被删掉时，点一下什么也不发生。
    }
  }

  private async edit(cell: SurfaceMenuCell): Promise<void> {
    this.menuFor = null;
    await bridgeApi.call('surface.menu.edit', { entryId: cell.entryId });
  }

  private async debug(cell: SurfaceMenuCell): Promise<void> {
    this.menuFor = null;
    await bridgeApi.call('surface.menu.debug', { entryId: cell.entryId });
  }

  private async chooseIcon(cell: SurfaceMenuCell): Promise<void> {
    this.menuFor = null;
    this.snapshot = await bridgeApi.call<SurfaceMenuSnapshot>('surface.menu.icon',
      { entryId: cell.entryId });
  }

  private async replace(cell: SurfaceMenuCell): Promise<void> {
    this.menuFor = null;
    this.snapshot = await bridgeApi.call<SurfaceMenuSnapshot>('surface.menu.replace',
      { entryId: cell.entryId });
  }

  private async addRule(): Promise<void> {
    this.menuFor = null;
    this.snapshot = await bridgeApi.call<SurfaceMenuSnapshot>('surface.menu.rule.add');
  }

  private async blockForeground(): Promise<void> {
    this.menuFor = null;
    this.snapshot = await bridgeApi.call<SurfaceMenuSnapshot>('surface.menu.block');
  }

  private async addPage(pageId?: string): Promise<void> {
    this.snapshot = await bridgeApi.call<SurfaceMenuSnapshot>('surface.menu.page.add',
      pageId === undefined ? {} : { pageId });
    this.clampPages();
  }

  private async removePage(pageId: string): Promise<void> {
    this.pageMenu = null;
    this.snapshot = await bridgeApi.call<SurfaceMenuSnapshot>('surface.menu.page.remove', { pageId });
    this.clampPages();
  }

  private clampPages(): void {
    this.common = Math.min(this.common, Math.max(0, this.commonPages.length - 1));
    this.context = Math.min(this.context, Math.max(0, this.contextPages.length - 1));
  }

  private dropOn(event: DragEvent, page: SurfaceMenuPage, row: number, column: number): void {
    event.preventDefault();
    const entryId = event.dataTransfer?.getData('text/plain') || this.dragging;
    this.dragging = null;
    if (!entryId) {
      return;
    }

    void bridgeApi.call<SurfaceMenuSnapshot>('surface.menu.move',
      { entryId, pageId: page.pageId, row, column }).then((snapshot) => {
      this.snapshot = snapshot;
    });
  }

  private renderCell(page: SurfaceMenuPage, row: number, column: number) {
    const cell = page.cells.find((candidate) => candidate.row === row && candidate.column === column);
    if (!cell) {
      return html`<button class="cell empty" tabindex="-1" aria-hidden="true"
        @dragover=${(event: DragEvent) => event.preventDefault()}
        @drop=${(event: DragEvent) => this.dropOn(event, page, row, column)}>
        <wg-icon name="SquarePlus" .size=${24} .strokeWidth=${1.4}></wg-icon>
      </button>`;
    }

    const dimmed = this.filtering && !this.matchesQuery(cell);
    return html`
      <button class="cell ${dimmed ? 'dimmed' : ''}" title=${cell.description.length > 0
          ? cell.macroName + '\n' + cell.description : cell.macroName}
        draggable="true"
        @dragstart=${(event: DragEvent) => {
          this.dragging = cell.entryId;
          event.dataTransfer?.setData('text/plain', cell.entryId);
        }}
        @dragover=${(event: DragEvent) => event.preventDefault()}
        @drop=${(event: DragEvent) => this.dropOn(event, page, row, column)}
        @click=${() => void this.activate(cell)}
        @contextmenu=${(event: MouseEvent) => this.openCellMenu(event, cell)}>
        ${cell.iconKind === 'image' && cell.iconValue
          ? html`<img src=${fileUrl(cell.iconValue)} alt="" />`
          : cell.iconKind === 'builtin' && cell.iconValue
            ? html`<wg-icon name=${cell.iconValue} .size=${44} .strokeWidth=${1.5}></wg-icon>`
            : html`<span class="name">${cell.macroName}</span>`}
      </button>
    `;
  }

  private renderPage(page: SurfaceMenuPage) {
    const slots = [];
    for (let row = 0; row < Rows; row += 1) {
      for (let column = 0; column < Columns; column += 1) {
        slots.push(this.renderCell(page, row, column));
      }
    }

    return html`<div class="grid">${slots}</div>`;
  }

  private renderDots(pages: SurfaceMenuPage[], active: number, common: boolean) {
    return html`
      <div class="dots">
        ${pages.map((page, index) => html`
          <button class="dot ${index === active ? 'on' : ''}" aria-label=${String(index + 1)}
            @click=${() => {
              this.menuFor = null;
              if (common) {
                this.common = index;
              } else {
                this.context = index;
              }
            }}
            @contextmenu=${(event: MouseEvent) => this.openPageMenu(event, page.pageId)}>
            <span></span>
          </button>
        `)}
        <button class="dot add" aria-label=${t('Menu.AddPage')} title=${t('Menu.AddPage')}
          @click=${() => void this.addPage(pages[active]?.pageId)}>
          <wg-icon name="Plus" .size=${11}></wg-icon>
        </button>
      </div>
    `;
  }


  private renderHeader() {
    return html`
      <div class="titlebar" @pointerdown=${(event: PointerEvent) => this.startDrag(event)}>
        <img class="logo" src=${logoUrl} alt="" />
        <span class="app-title">${t('Main.Title')}</span>
        <button class="tool" title=${t('Menu.Settings')} @click=${(event: Event) => {
          this.stop(event);
          this.openSettings();
        }}><wg-icon name="Settings" .size=${14}></wg-icon></button>
        <button class="tool search ${this.searchOpen ? 'on' : ''}" title=${t('Menu.SearchOpen')}
          @click=${(event: Event) => {
            this.stop(event);
            void this.toggleSearch();
          }}><wg-icon name="Search" .size=${14}></wg-icon></button>
        <button class="tool ${this.pinned ? 'on' : ''}" title=${this.pinned ? t('Menu.Unpin') : t('Menu.Pin')}
          @click=${(event: Event) => {
            this.stop(event);
            void this.togglePin();
          }}><wg-icon name=${this.pinned ? 'PinOff' : 'Pin'} .size=${13}></wg-icon></button>
        <button class="tool close" title=${t('Menu.Close')} @click=${(event: Event) => {
          this.stop(event);
          this.close();
        }}><wg-icon name="X" .size=${14}></wg-icon></button>
      </div>
    `;
  }

  private renderSearch() {
    return html`
      <div class="search-slot ${this.searchOpen ? 'open' : ''}">
        <input type="search" placeholder=${t('Menu.Search')} .value=${this.query}
          ?disabled=${!this.searchOpen}
          @input=${(event: Event) => {
            this.query = (event.target as HTMLInputElement).value;
          }}
          @keydown=${(event: KeyboardEvent) => {
            if (event.key === 'Escape') {
              event.preventDefault();
              event.stopPropagation();
              this.closeSearch();
            }
          }} />
      </div>
    `;
  }

  protected override render() {
    if (this.error.length > 0) {
      return html`<div class="panel"><p class="note">${this.error}</p></div>`;
    }

    if (!this.snapshot) {
      return html`<div class="panel"><p class="note">…</p></div>`;
    }

    const commonPage = this.commonPages[this.common] ?? this.commonPages[0];
    const contextPage = this.contextPages[this.context] ?? this.contextPages[0];

    return html`
      <div class="panel">
        ${this.renderHeader()}
        ${this.renderSearch()}
        ${commonPage ? this.renderPage(commonPage) : nothing}
        <div class="divider">
          <span class="rule">${contextPage?.ruleSummary ?? ''}</span>
          <div class="dot-rows">
            ${this.renderDots(this.commonPages, this.common, true)}
            ${this.contextPages.length > 0
              ? this.renderDots(this.contextPages, this.context, false) : nothing}
          </div>
        </div>
        ${contextPage ? this.renderPage(contextPage) : nothing}
        <div class="footer">
          <button title=${t('Menu.AddRule')} ?disabled=${!this.snapshot.canUseForeground}
            @click=${() => void this.addRule()}>
            <wg-icon name="SquarePlus" .size=${15}></wg-icon>
          </button>
          <button title=${t('Menu.Block')} ?disabled=${!this.snapshot.canUseForeground}
            @click=${() => void this.blockForeground()}>
            <wg-icon name="Ban" .size=${15}></wg-icon>
          </button>
        </div>
        ${renderCellMenu(this.menuFor, {
          edit: (cell) => void this.edit(cell),
          debug: (cell) => void this.debug(cell),
          chooseIcon: (cell) => void this.chooseIcon(cell),
          replace: (cell) => void this.replace(cell),
          addRule: () => void this.addRule(),
          block: () => void this.blockForeground(),
        })}
        ${renderPageMenu(this.pageMenu, (pageId) => void this.removePage(pageId))}
      </div>
    `;
  }
}
