import { LitElement, html, nothing } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import { bridgeApi } from '../../shared/bridge/bridge.js';
import type {
  SurfaceMenuCell,
  SurfaceMenuPage,
  SurfaceMenuSnapshot,
} from '../../shared/bridge/protocol.js';
import { strings, t } from '../../shared/strings/strings.js';
import { StoreController } from '../../shared/lit/store-controller.js';
import { menuOverlayStyles } from './menu-overlay.styles.js';
import '../../shared/icons/icon.js';
import { renderHeader, renderDots, renderSearch } from './menu-overlay.chrome.js';
import type { ChromeActions } from './menu-overlay.chrome.js';
import { findCell, matchesCell, renderGrid } from './menu-overlay.grid.js';
import type { GridActions } from './menu-overlay.grid.js';
import { renderCellMenu, renderEmptyCellMenu, renderPageMenu } from './menu-overlay.menus.js';
import { renderMacroMenu } from './menu-overlay.macros.js';
import { dropCell } from './menu-overlay.moves.js';
import { MenuPickerState } from './menu-overlay.pickers.js';
import { CellMenuHeight, ContextWidth, EmptyMenuHeight, PageMenuHeight, clampMenuPoint } from './menu-overlay.position.js';
import '../../shared/icons/icon-picker.js';
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

  private readonly picker = new MenuPickerState({
    snapshot: () => this.snapshot,
    setSnapshot: (snapshot) => { this.snapshot = snapshot; },
    setError: (message) => { this.error = message; },
    searchOpen: () => this.searchOpen,
    focusKeyboard: (active) => { void bridgeApi.call('surface.menu.focus', { focused: active }); },
    focusMacroInput: () => this.renderRoot.querySelector<HTMLInputElement>('.macro-search')?.focus(),
    refresh: () => this.requestUpdate(),
  });

  @state()
  private dragging: string | null = null;

  protected readonly stringsController = new StoreController(this, strings);

  protected override async firstUpdated(): Promise<void> {
    await this.reload();
    window.addEventListener('focus', () => void this.reload());
    bridgeApi.on('surface.menu.pinned', (payload) => {
      this.pinned = payload.pinned;
    });
    bridgeApi.on('surface.menu.refreshed', () => void this.reload());
    bridgeApi.on('surface.menu.closed', () => {
      this.searchOpen = false;
      this.query = '';
      this.menuFor = null;
      this.pageMenu = null;
      this.emptyMenu = null;
      this.picker.reset();
      void this.reload();
    });
    window.addEventListener('keydown', (event) => {
      if (event.key !== 'Escape') {
        return;
      }

      event.preventDefault();
      if (this.picker.iconTarget) {
        this.picker.closeIcon();
      } else if (this.picker.macroMenu) {
        this.picker.closeMacro();
      } else if (this.menuFor) {
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
      const path = event.composedPath();
      const inside = (className: string) => path.some((node) =>
        node instanceof HTMLElement && node.classList.contains(className));
      if (!inside('context')) {
        this.menuFor = null;
        this.pageMenu = null;
        this.emptyMenu = null;
      }
      if (!inside('macro-menu') && !inside('context')) {
        this.picker.closeMacro();
      }
      if (!inside('icon-backdrop')) {
        this.picker.closeIcon();
      }
      if (!this.searchOpen) {
        return;
      }

      const insideSearch = inside('search-slot')
        || path.some((node) => node instanceof HTMLElement
          && node.classList.contains('tool') && node.classList.contains('search'));
      if (!insideSearch) {
        this.closeSearch();
      }
    });
  }

  private async reload(): Promise<void> {
    try {
      this.snapshot = await bridgeApi.call<SurfaceMenuSnapshot>('surface.menu.state');
      this.pinned = this.snapshot.pinned;
      this.clampPages();
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

  private matchesQuery(cell: SurfaceMenuCell): boolean {
    return matchesCell(cell, this.query);
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

  private openCellMenu(event: MouseEvent, cell: SurfaceMenuCell, page: SurfaceMenuPage,
    row: number, column: number): void {
    event.preventDefault();
    event.stopPropagation();
    this.pageMenu = null;
    this.emptyMenu = null;
    this.picker.macroMenu = null;
    const point = clampMenuPoint(event.clientX, event.clientY, ContextWidth, CellMenuHeight,
      window.innerWidth, window.innerHeight);
    this.menuFor = { cell, pageId: page.pageId, row, column, ...point };
  }

  private openPageMenu(event: MouseEvent, pageId: string, pageCount: number): void {
    event.preventDefault();
    event.stopPropagation();
    if (pageCount <= 1) {
      return;
    }

    this.menuFor = null;
    this.emptyMenu = null;
    const point = clampMenuPoint(event.clientX, event.clientY, ContextWidth, PageMenuHeight,
      window.innerWidth, window.innerHeight);
    this.pageMenu = { pageId, ...point };
  }

  private openEmptyMenu(event: MouseEvent, page: SurfaceMenuPage, row: number,
    column: number): void {
    event.preventDefault();
    event.stopPropagation();
    this.menuFor = null;
    this.pageMenu = null;
    this.picker.macroMenu = null;
    const point = clampMenuPoint(event.clientX, event.clientY, ContextWidth, EmptyMenuHeight,
      window.innerWidth, window.innerHeight);
    this.emptyMenu = { pageId: page.pageId, row, column, ...point };
  }

  /** 滚轮在哪个半区上，就翻哪一组页；上半区与下半区互不影响。 */
  private pageWheel(event: WheelEvent, common: boolean): void {
    if (this.query.trim().length > 0) {
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

  private chooseIcon(cell: SurfaceMenuCell): void {
    this.menuFor = null;
    this.picker.openIcon(cell.entryId);
  }

  private replace(): void {
    const state = this.menuFor;
    if (!state) {
      return;
    }

    this.menuFor = null;
    void this.picker.openMacro(state.pageId, state.row, state.column, state.x, state.y, false);
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
    const dragging = this.dragging;
    this.dragging = null;
    dropCell(event, dragging, page.pageId, row, column, (snapshot) => { this.snapshot = snapshot; });
  }

  private iconCell(): SurfaceMenuCell | undefined {
    return findCell(this.snapshot, this.picker.iconTarget);
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
      filtering: this.query.trim().length > 0,
      matches: (cell) => this.matchesQuery(cell),
      drop: (event, page, row, column) => this.dropOn(event, page, row, column),
      add: (page, row, column) => void this.picker.openMacro(page.pageId, row, column,
        window.innerWidth / 2 - 150, window.innerHeight / 2, false),
      emptyMenu: (event, page, row, column) => this.openEmptyMenu(event, page, row, column),
      dragStart: (event, cell) => {
        this.dragging = cell.entryId;
        event.dataTransfer?.setData('text/plain', cell.entryId);
      },
      activate: (cell) => void this.activate(cell),
      cellMenu: (event, cell, page, row, column) => this.openCellMenu(event, cell, page, row, column),
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

    const iconCell = this.iconCell();
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
      </div>
      <div class="menu-context-layer">
        ${renderCellMenu(this.menuFor, {
          edit: (cell) => void this.edit(cell),
          debug: (cell) => void this.debug(cell),
          chooseIcon: (cell) => this.chooseIcon(cell),
          replace: () => this.replace(),
        })}
        ${renderPageMenu(this.pageMenu, (pageId) => void this.removePage(pageId))}
        ${renderEmptyCellMenu(this.emptyMenu, (withIcon) => {
          const state = this.emptyMenu;
          if (state) {
            this.emptyMenu = null;
            void this.picker.openMacro(state.pageId, state.row, state.column,
              state.x, state.y, withIcon);
          }
        })}
        ${renderMacroMenu(this.picker.macroMenu, this.picker.macros, this.picker.macroQuery, this.picker.macroLoading, {
          query: (value) => { this.picker.macroQuery = value; this.requestUpdate(); },
          choose: (macro) => void this.picker.chooseMacro(macro),
          cancel: () => this.picker.closeMacro(),
        })}
        <wg-icon-picker
          .open=${this.picker.iconTarget !== null}
          .selectedKind=${iconCell?.iconKind ?? ''}
          .selectedValue=${iconCell?.iconValue ?? ''}
          .imagePicker=${() => this.picker.pickImage()}
          @icon-picked=${(event: CustomEvent<{ kind: string; value: string }>) =>
            void this.picker.pickIcon(event.detail)}
          @icon-cancel=${() => this.picker.closeIcon()}></wg-icon-picker>
      </div>
    `;
  }
}