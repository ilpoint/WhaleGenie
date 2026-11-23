import { LitElement, html, nothing } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import { bridgeApi } from '../../shared/bridge/bridge.js';
import type { SurfaceMenuCell, SurfaceMenuPage, SurfaceMenuSnapshot } from '../../shared/bridge/protocol.js';
import { strings, t } from '../../shared/strings/strings.js';
import { StoreController } from '../../shared/lit/store-controller.js';
import { menuOverlayStyles } from './menu-overlay.styles.js';
import '../../shared/icons/icon.js';
import { renderHeader, renderDots, renderSearch } from './menu-overlay.chrome.js';
import type { ChromeActions } from './menu-overlay.chrome.js';
import { renderGrid } from './menu-overlay.grid.js';
import type { GridActions } from './menu-overlay.grid.js';
import { renderCellMenu, renderEmptyCellMenu, renderPageMenu } from './menu-overlay.menus.js';
import type { CellMenuState, EmptyCellMenuState, PageMenuState } from './menu-overlay.menus.js';

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
  private emptyMenu: EmptyCellMenuState | null = null;

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
      } else if (this.emptyMenu) {
        this.emptyMenu = null;
      } else if (this.searchOpen) {
        this.closeSearch();
      } else {
        void bridgeApi.call('surface.menu.close');
      }
    });
    window.addEventListener('pointerdown', (event) => {
      this.menuFor = null;
      this.pageMenu = null;
      this.emptyMenu = null;
      if (!this.searchOpen) {
        return;
      }

      // Shadow DOM retargets the event to the host; the composed path still names the input.
      const insideSearch = event.composedPath().some((node) =>
        node instanceof HTMLElement
        && (node.classList.contains('search-slot')
          || (node.classList.contains('tool') && node.classList.contains('search'))));
      if (!insideSearch) {
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

  private openPageMenu(event: MouseEvent, pageId: string, pageCount: number): void {
    event.preventDefault();
    event.stopPropagation();
    if (pageCount <= 1) {
      return;
    }

    this.menuFor = null;
    this.emptyMenu = null;
    this.pageMenu = { pageId, x: event.clientX, y: event.clientY };
  }

  private openEmptyMenu(event: MouseEvent, page: SurfaceMenuPage, row: number,
    column: number): void {
    event.preventDefault();
    event.stopPropagation();
    this.menuFor = null;
    this.pageMenu = null;
    this.emptyMenu = { pageId: page.pageId, row, column, x: event.clientX, y: event.clientY };
  }

  /** 空格子：先选宏，再按需要打开原生图标选择器。 */
  private async pickCell(page: SurfaceMenuPage, row: number, column: number,
    withIcon: boolean): Promise<void> {
    this.emptyMenu = null;
    this.snapshot = await bridgeApi.call<SurfaceMenuSnapshot>('surface.menu.cell.pick',
      { pageId: page.pageId, row, column });
    if (!withIcon) {
      return;
    }

    const cell = this.snapshot.common.concat(this.snapshot.context)
      .flatMap((candidate) => candidate.cells)
      .find((candidate) => candidate.row === row && candidate.column === column);
    if (cell) {
      this.snapshot = await bridgeApi.call<SurfaceMenuSnapshot>('surface.menu.icon',
        { entryId: cell.entryId });
    }
  }

  /** 滚轮在哪个半区上，就翻哪一组页；上半区与下半区互不影响。 */
  private pageWheel(event: WheelEvent, common: boolean): void {
    if (this.filtering) {
      return;
    }

    const pages = common ? this.commonPages : this.contextPages;
    if (pages.length <= 1 || event.deltaY === 0) {
      return;
    }

    event.preventDefault();
    const current = common ? this.common : this.context;
    const next = Math.max(0, Math.min(pages.length - 1, current + (event.deltaY > 0 ? -1 : 1)));
    if (common) {
      this.common = next;
    } else {
      this.context = next;
    }
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

  private chromeActions(): ChromeActions {
    return {
      drag: (event) => this.startDrag(event),
      settings: (event) => { this.stop(event); this.openSettings(); },
      toggleSearch: (event) => { this.stop(event); void this.toggleSearch(); },
      togglePin: (event) => { this.stop(event); void this.togglePin(); },
      close: (event) => { this.stop(event); this.close(); },
      setQuery: (value) => { this.query = value; },
      closeSearch: () => this.closeSearch(),
    };
  }

  private gridActions(): GridActions {
    return {
      filtering: this.filtering,
      matches: (cell) => this.matchesQuery(cell),
      drop: (event, page, row, column) => this.dropOn(event, page, row, column),
      add: (page, row, column) => void this.pickCell(page, row, column, false),
      emptyMenu: (event, page, row, column) => this.openEmptyMenu(event, page, row, column),
      dragStart: (event, cell) => {
        this.dragging = cell.entryId;
        event.dataTransfer?.setData('text/plain', cell.entryId);
      },
      activate: (cell) => void this.activate(cell),
      cellMenu: (event, cell) => this.openCellMenu(event, cell),
    };
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
        ${renderHeader({ searchOpen: this.searchOpen, pinned: this.pinned, query: this.query }, this.chromeActions())}
        ${renderSearch({ searchOpen: this.searchOpen, pinned: this.pinned, query: this.query }, this.chromeActions())}
        ${commonPage ? renderGrid(commonPage, (event) => this.pageWheel(event, true), this.gridActions()) : nothing}
        <div class="divider">
          <span class="rule">${contextPage?.ruleSummary ?? ''}</span>
          <div class="dot-rows">
            ${renderDots(this.commonPages, this.common, {
            select: (index) => { this.menuFor = null; this.common = index; },
            add: () => void this.addPage(),
            pageMenu: (event, pageId, count) => this.openPageMenu(event, pageId, count),
          })}
            ${this.contextPages.length > 0
              ? renderDots(this.contextPages, this.context, {
                select: (index) => { this.menuFor = null; this.context = index; },
                add: () => void this.addPage(contextPage?.pageId),
                pageMenu: (event, pageId, count) => this.openPageMenu(event, pageId, count),
              }) : nothing}
          </div>
        </div>
        ${contextPage ? renderGrid(contextPage, (event) => this.pageWheel(event, false), this.gridActions()) : nothing}
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
        ${renderEmptyCellMenu(this.emptyMenu, (withIcon) => {
          const state = this.emptyMenu;
          const page = [...this.commonPages, ...this.contextPages]
            .find((candidate) => candidate.pageId === state?.pageId);
          if (state && page) {
            void this.pickCell(page, state.row, state.column, withIcon);
          }
        })}
      </div>
    `;
  }
}
