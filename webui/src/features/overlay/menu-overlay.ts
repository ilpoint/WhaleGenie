import { LitElement, html, nothing } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import { bridgeApi } from '../../shared/bridge/bridge.js';
import type { SurfaceMenuCell, SurfaceMenuPage, SurfaceMenuSnapshot } from '../../shared/bridge/protocol.js';
import { strings, t } from '../../shared/strings/strings.js';
import { StoreController } from '../../shared/lit/store-controller.js';
import { menuOverlayStyles } from './menu-overlay.styles.js';

const Columns = 4;
const Rows = 3;

/**
 * 菜单浮层：把 C# 给的 `surface.menu.state` 画成一张 4×3 的格子菜单。
 *
 * 分页、搜索、悬停与拖动都是这里自己的状态；程序只回答“这一页有哪些宏”。点一个有宏的格子
 * 就把 `entryId` 交回去运行——用 id 而不是名字，改名不会点到别人。
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
  private menuFor: SurfaceMenuCell | null = null;

  @state()
  private menuX = 0;

  @state()
  private menuY = 0;

  @state()
  private dragging: string | null = null;

  protected readonly stringsController = new StoreController(this, strings);



  protected override async firstUpdated(): Promise<void> {
    await this.reload();
    window.addEventListener('focus', () => void this.reload());
    window.addEventListener('keydown', (event) => {
      if (event.key === 'Escape') {
        event.preventDefault();
        if (this.menuFor) {
          this.menuFor = null;
          return;
        }

        void bridgeApi.call('surface.menu.close');
      }
    });
    window.addEventListener('click', () => {
      this.menuFor = null;
    });
  }

  private async reload(): Promise<void> {
    try {
      this.snapshot = await bridgeApi.call<SurfaceMenuSnapshot>('surface.menu.state');
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
    return wanted.length === 0 || cell.macroName.toLowerCase().includes(wanted);
  }

  private openMenu(event: MouseEvent, cell: SurfaceMenuCell): void {
    event.preventDefault();
    event.stopPropagation();
    this.menuFor = cell;
    this.menuX = event.clientX;
    this.menuY = event.clientY;
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

  private async addRule(): Promise<void> {
    this.menuFor = null;
    this.snapshot = await bridgeApi.call<SurfaceMenuSnapshot>('surface.menu.rule.add');
  }

  private async blockForeground(): Promise<void> {
    this.menuFor = null;
    this.snapshot = await bridgeApi.call<SurfaceMenuSnapshot>('surface.menu.block');
  }

  private async edit(cell: SurfaceMenuCell): Promise<void> {
    this.menuFor = null;
    await bridgeApi.call('surface.menu.edit', { entryId: cell.entryId });
  }

  private async debug(cell: SurfaceMenuCell): Promise<void> {
    this.menuFor = null;
    await bridgeApi.call('surface.menu.debug', { entryId: cell.entryId });
  }

  private async replace(cell: SurfaceMenuCell): Promise<void> {
    this.menuFor = null;
    this.snapshot = await bridgeApi.call<SurfaceMenuSnapshot>('surface.menu.replace',
      { entryId: cell.entryId });
  }

  private async chooseIcon(cell: SurfaceMenuCell): Promise<void> {
    this.menuFor = null;
    this.snapshot = await bridgeApi.call<SurfaceMenuSnapshot>('surface.menu.icon',
      { entryId: cell.entryId });
  }

  private renderMenu() {
    if (!this.menuFor) {
      return nothing;
    }

    const cell = this.menuFor;
    return html`
      <div class="context" style=${'left:' + this.menuX + 'px;top:' + this.menuY + 'px'}>
        <button @click=${() => void this.edit(cell)}>${t('Menu.EditMacro')}</button>
        <button @click=${() => void this.debug(cell)}>${t('Menu.Debug')}</button>
        <button @click=${() => void this.chooseIcon(cell)}>${t('Menu.ChooseIcon')}</button>
        <button @click=${() => void this.replace(cell)}>${t('Menu.ReplaceMacro')}</button>
        <button @click=${() => void this.addRule()}>${t('Menu.AddRule')}</button>
        <button @click=${() => void this.blockForeground()}>${t('Menu.Block')}</button>
      </div>
    `;
  }

  private async activate(cell: SurfaceMenuCell): Promise<void> {
    try {
      await bridgeApi.call('surface.menu.activate', { entryId: cell.entryId });
    } catch {
      // 宏在快照与点击之间被删掉时，点一下什么也不发生。
    }
  }

  private renderCell(page: SurfaceMenuPage, row: number, column: number) {
    const cell = page.cells.find((candidate) => candidate.row === row && candidate.column === column);
    if (!cell) {
      return html`<button class="cell empty" tabindex="-1" aria-hidden="true"
        @dragover=${(event: DragEvent) => event.preventDefault()}
        @drop=${(event: DragEvent) => this.dropOn(event, page, row, column)}></button>`;
    }

    return html`
      <button class="cell" title=${cell.macroName}
        draggable="true"
        @dragstart=${(event: DragEvent) => {
          this.dragging = cell.entryId;
          event.dataTransfer?.setData('text/plain', cell.entryId);
        }}
        @dragover=${(event: DragEvent) => event.preventDefault()}
        @drop=${(event: DragEvent) => this.dropOn(event, page, row, column)}
        @click=${() => void this.activate(cell)}
        @contextmenu=${(event: MouseEvent) => this.openMenu(event, cell)}>
        ${cell.iconKind === 'image' && cell.iconValue
          ? html`<img src=${cell.iconValue} alt="" />`
          : html`<span class="glyph">${cell.macroName.slice(0, 1)}</span>`}
        <span class="name">${cell.macroName}</span>
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

  private renderDots(
    count: number,
    active: number,
    pick: (index: number) => void,
    onAdd: () => void,
    onRemove: () => void,
  ) {
    return html`
      <div class="pages">
        ${Array.from({ length: count }, (_, index) => html`
          <button
            aria-current=${index === active ? 'true' : 'false'}
            aria-label=${String(index + 1)}
            @click=${() => pick(index)}
          ></button>
        `)}
        <button class="add" aria-label=${t('Menu.AddPage')} title=${t('Menu.AddPage')}
          @click=${onAdd}>＋</button>
        ${count > 1
          ? html`<button class="remove" aria-label=${t('Menu.RemovePage')}
              title=${t('Menu.RemovePage')} @click=${onRemove}>×</button>`
          : nothing}
      </div>
    `;
  }

  private async addPage(pageId?: string): Promise<void> {
    this.snapshot = await bridgeApi.call<SurfaceMenuSnapshot>('surface.menu.page.add',
      pageId === undefined ? {} : { pageId });
  }

  private async removePage(pageId: string): Promise<void> {
    this.snapshot = await bridgeApi.call<SurfaceMenuSnapshot>('surface.menu.page.remove',
      { pageId });
    this.common = Math.min(this.common, Math.max(0, this.commonPages.length - 1));
    this.context = Math.min(this.context, Math.max(0, this.contextPages.length - 1));
  }

  private async togglePin(): Promise<void> {
    this.pinned = !this.pinned;
    try {
      await bridgeApi.call('surface.menu.pin', { pinned: this.pinned });
    } catch {
      this.pinned = !this.pinned;
    }
  }

  private renderHeader() {
    return html`
      <div class="header">
        <span class="title">${t('Menu.Search')}</span>
        <button
          class="pin ${this.pinned ? 'on' : ''}"
          aria-pressed=${this.pinned ? 'true' : 'false'}
          title=${this.pinned ? t('Menu.Unpin') : t('Menu.Pin')}
          @click=${() => void this.togglePin()}
        >${this.pinned ? '📌' : '📍'}</button>
      </div>
    `;
  }

  private renderSearch() {
    return html`
      <div class="search">
        <input
          type="search"
          placeholder=${t('Menu.Search')}
          .value=${this.query}
          @input=${(event: Event) => {
            this.query = (event.target as HTMLInputElement).value;
          }}
          @focus=${() => void bridgeApi.call('surface.menu.focus', { focused: true })}
          @blur=${() => void bridgeApi.call('surface.menu.focus', { focused: false })}
        />
      </div>
    `;
  }

  private renderMatches() {
    const pages = [...this.commonPages, ...this.contextPages];
    const matches = pages.flatMap((page) => page.cells).filter((cell) => this.matchesQuery(cell));

    if (matches.length === 0) {
      return html`<p class="note">${t('Menu.PickMacroSearch')}</p>`;
    }

    return html`
      <div class="grid">
        ${matches.slice(0, Columns * Rows).map((cell) => this.renderCell(
          { pageId: '', isDefault: false, ruleSummary: '', cells: [cell] },
          cell.row,
          cell.column,
        ))}
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
        ${this.renderMenu()}
        ${this.filtering
          ? this.renderMatches()
          : html`
              ${commonPage ? this.renderPage(commonPage) : nothing}
              ${this.renderDots(this.commonPages.length, this.common,
                (index) => {
                  this.common = index;
                },
                () => void this.addPage(commonPage?.pageId),
                () => {
                  if (commonPage) {
                    void this.removePage(commonPage.pageId);
                  }
                })}
              ${contextPage
                ? html`
                    <p class="section-title">${contextPage.ruleSummary}</p>
                    ${this.renderPage(contextPage)}
                    ${this.renderDots(this.contextPages.length, this.context,
                      (index) => {
                        this.context = index;
                      },
                      () => void this.addPage(contextPage?.pageId),
                      () => {
                        if (contextPage) {
                          void this.removePage(contextPage.pageId);
                        }
                      })}
                  `
                : nothing}
            `}
      </div>
    `;
  }
}