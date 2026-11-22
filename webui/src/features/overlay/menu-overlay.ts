import { LitElement, css, html, nothing } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import { bridgeApi } from '../../shared/bridge/bridge.js';
import type { SurfaceMenuCell, SurfaceMenuPage, SurfaceMenuSnapshot } from '../../shared/bridge/protocol.js';
import { strings, t } from '../../shared/strings/strings.js';
import { StoreController } from '../../shared/lit/store-controller.js';

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

  protected readonly stringsController = new StoreController(this, strings);

  static styles = css`
    :host {
      display: block;
      width: max-content;
      background: transparent;
      font-family: var(--fontFamilyBase);
      color: var(--colorNeutralForeground1);
      user-select: none;
    }

    .panel {
      margin: 10px;
      padding: 10px;
      border-radius: 12px;
      background: var(--colorNeutralBackground1);
      box-shadow: var(--shadow16);
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
    }

    .header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      margin-bottom: 6px;
    }

    .header .title {
      font-size: 12px;
      font-weight: var(--fontWeightSemibold);
      color: var(--colorNeutralForeground2);
    }

    .header .pin {
      width: 24px;
      height: 22px;
      padding: 0;
      border: none;
      border-radius: 5px;
      background: transparent;
      font-size: 12px;
      cursor: pointer;
      opacity: 0.65;
    }

    .header .pin.on {
      background: var(--colorBrandBackground2);
      opacity: 1;
    }

    .search {
      margin-bottom: 8px;
    }

    .search input {
      width: 100%;
      box-sizing: border-box;
      height: 28px;
      padding: 0 8px;
      border-radius: 6px;
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke1);
      background: var(--colorNeutralBackground3);
      color: inherit;
      font: inherit;
      outline: none;
    }

    .search input:focus {
      border-color: var(--colorBrandStroke1);
    }

    .grid {
      display: grid;
      grid-template-columns: repeat(4, 78px);
      grid-auto-rows: 70px;
      gap: 4px;
    }

    button.cell {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      gap: 4px;
      padding: 4px;
      border-radius: 8px;
      border: var(--strokeWidthThin) solid transparent;
      background: var(--colorNeutralBackground3);
      color: inherit;
      font: inherit;
      cursor: pointer;
      overflow: hidden;
    }

    button.cell:hover {
      background: var(--colorNeutralBackground3Hover);
      border-color: var(--colorNeutralStroke2);
    }

    button.cell:active {
      background: var(--colorNeutralBackground3Pressed);
    }

    button.cell.empty {
      background: transparent;
      border: var(--strokeWidthThin) dashed var(--colorNeutralStroke3);
      color: var(--colorNeutralForeground4);
      cursor: default;
    }

    button.cell img {
      width: 26px;
      height: 26px;
      object-fit: contain;
    }

    .glyph {
      width: 26px;
      height: 26px;
      border-radius: 6px;
      display: grid;
      place-items: center;
      background: var(--colorNeutralBackground1);
      font-size: 12px;
      font-weight: var(--fontWeightSemibold);
    }

    .name {
      max-width: 70px;
      font-size: 11px;
      line-height: 1.2;
      text-align: center;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .pages {
      display: flex;
      gap: 6px;
      justify-content: center;
      margin: 8px 0;
    }

    .pages button {
      width: 8px;
      height: 8px;
      padding: 0;
      border: none;
      border-radius: 50%;
      background: var(--colorNeutralStroke3);
      cursor: pointer;
    }

    .pages button[aria-current='true'] {
      background: var(--colorBrandBackground);
    }

    .pages button.add,
    .pages button.remove {
      width: 16px;
      height: 16px;
      border-radius: 4px;
      background: var(--colorNeutralBackground3);
      color: var(--colorNeutralForeground2);
      font-size: 11px;
      line-height: 1;
    }

    .pages button.add:hover,
    .pages button.remove:hover {
      background: var(--colorNeutralBackground3Hover);
    }

    .section-title {
      margin: 6px 0 4px;
      font-size: 11px;
      color: var(--colorNeutralForeground3);
    }

    .note {
      padding: 10px;
      font-size: 12px;
      color: var(--colorNeutralForeground3);
    }
  `;

  protected override async firstUpdated(): Promise<void> {
    await this.reload();
    window.addEventListener('focus', () => void this.reload());
    window.addEventListener('keydown', (event) => {
      if (event.key === 'Escape') {
        event.preventDefault();
        void bridgeApi.call('surface.menu.close');
      }
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
      return html`<button class="cell empty" tabindex="-1" aria-hidden="true"></button>`;
    }

    return html`
      <button class="cell" title=${cell.macroName} @click=${() => void this.activate(cell)}>
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
