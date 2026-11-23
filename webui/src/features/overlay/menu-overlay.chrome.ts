import { html, type TemplateResult } from 'lit';
import type { SurfaceMenuPage } from '../../shared/bridge/protocol.js';
import { t } from '../../shared/strings/strings.js';
import logoUrl from '../../assets/whale.png';
import '../../shared/icons/icon.js';

/** 标题栏、搜索区与分页点的状态；页面只把回调交给这三块。 */
export interface ChromeState {
  searchOpen: boolean;
  pinned: boolean;
  query: string;
}

export interface ChromeActions {
  drag: (event: PointerEvent) => void;
  settings: (event: Event) => void;
  toggleSearch: (event: Event) => void;
  togglePin: (event: Event) => void;
  close: (event: Event) => void;
  setQuery: (value: string) => void;
  closeSearch: () => void;
}

export function renderHeader(state: ChromeState, actions: ChromeActions): TemplateResult {
  return html`
    <div class="titlebar" @pointerdown=${actions.drag}>
      <img class="logo" src=${logoUrl} alt="" />
      <span class="app-title">${t('Main.Title')}</span>
      <button class="tool" title=${t('Menu.Settings')} @click=${actions.settings}>
        <wg-icon name="Settings" .size=${14}></wg-icon></button>
      <button class="tool search ${state.searchOpen ? 'on' : ''}" title=${t('Menu.SearchOpen')}
        @click=${actions.toggleSearch}><wg-icon name="Search" .size=${14}></wg-icon></button>
      <button class="tool pin ${state.pinned ? 'pinned' : ''}"
        aria-pressed=${String(state.pinned)}
        title=${state.pinned ? t('Menu.Unpin') : t('Menu.Pin')}
        @click=${actions.togglePin}>
        <wg-icon name=${state.pinned ? 'PinOff' : 'Pin'} .size=${13}></wg-icon></button>
      <button class="tool close" title=${t('Menu.Close')} @click=${actions.close}>
        <wg-icon name="X" .size=${14}></wg-icon></button>
    </div>
  `;
}

export function renderSearch(state: ChromeState, actions: ChromeActions): TemplateResult {
  return html`
    <div class="search-slot ${state.searchOpen ? 'open' : ''}">
      <input type="search" placeholder=${t('Menu.Search')} .value=${state.query}
        ?disabled=${!state.searchOpen}
        @input=${(event: Event) => actions.setQuery((event.target as HTMLInputElement).value)}
        @keydown=${(event: KeyboardEvent) => {
          if (event.key === 'Escape') {
            event.preventDefault();
            event.stopPropagation();
            actions.closeSearch();
          }
        }} />
    </div>
  `;
}

export function renderDots(pages: SurfaceMenuPage[], active: number,
  actions: {
    select: (index: number) => void;
    add: () => void;
    pageMenu: (event: MouseEvent, pageId: string, pageCount: number) => void;
  }): TemplateResult {
  return html`
    <div class="dots">
      ${pages.map((page, index) => html`
        <button class="dot ${index === active ? 'on' : ''}" aria-label=${String(index + 1)}
          @click=${() => actions.select(index)}
          @contextmenu=${(event: MouseEvent) => actions.pageMenu(event, page.pageId, pages.length)}>
          <span></span>
        </button>
      `)}
      <button class="dot add" aria-label=${t('Menu.AddPage')} title=${t('Menu.AddPage')}
        @click=${() => actions.add()}>
        <wg-icon name="Plus" .size=${11}></wg-icon>
      </button>
    </div>
  `;
}
