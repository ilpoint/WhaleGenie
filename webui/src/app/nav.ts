import { LitElement, css, html } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import { strings, t } from '../shared/strings/strings.js';
import { routes, type Route } from './router.js';

/**
 * 左侧导航。条目从词表键推导：`Shell.<Route>` 存在就是一项，所以阶段加页面只需要加键与
 * 一条路由，不用改导航的代码。当前选中的项铺强调色底。
 */
@customElement('wg-nav')
export class WgNav extends LitElement {
  @property()
  active: Route = 'home';

  static styles = css`
    :host {
      display: grid;
      grid-template-columns: 48px 220px;
      height: 100%;
      background: var(--colorNeutralBackground3);
    }

    button {
      display: flex;
      align-items: center;
      gap: var(--spacingHorizontalS);
      width: 100%;
      border: none;
      border-radius: var(--borderRadiusMedium);
      background: transparent;
      color: var(--colorNeutralForeground1);
      font: inherit;
      text-align: left;
      padding: var(--spacingVerticalSN) var(--spacingHorizontalM);
      cursor: pointer;
    }

    button:hover {
      background: var(--colorNeutralBackground3Hover);
    }

    button:focus-visible {
      outline: var(--strokeWidthThicker) solid var(--colorStrokeFocus2);
      outline-offset: -1px;
    }

    button[aria-current='page'] {
      background: var(--colorBrandBackground);
      color: var(--colorNeutralForegroundOnBrand);
    }

    button[aria-current='page']:hover {
      background: var(--colorBrandBackgroundHover);
    }

    .activity {
      display: flex;
      flex-direction: column;
      align-items: stretch;
      padding-top: var(--spacingVerticalS);
      border-right: var(--strokeWidthThin) solid var(--colorNeutralStroke1);
    }

    .activity button {
      position: relative;
      display: grid;
      place-items: center;
      height: 48px;
      padding: 0;
      border-radius: 0;
      color: var(--colorNeutralForeground3);
    }

    .activity button:hover {
      color: var(--colorNeutralForeground1);
    }

    .activity button[aria-current='page'] {
      color: var(--colorNeutralForeground1);
      background: var(--colorNeutralBackground2);
    }

    .activity button[aria-current='page']::before {
      content: '';
      position: absolute;
      left: 0;
      top: 6px;
      bottom: 6px;
      width: 2px;
      background: var(--colorBrandBackground);
    }

    .activity svg {
      width: 20px;
      height: 20px;
    }

    .side {
      padding: var(--spacingVerticalS) var(--spacingHorizontalS);
    }
  `;

  private labels: Record<string, string> = {};

  private unsubscribe?: () => void;

  /** 路由到词表键的显式映射：对账测试按字面量扫键，动态拼出来的键扫不到。 */
  private static readonly keys: Record<Route, string> = {
    home: 'Shell.Home',
    macros: 'Shell.Macros',
    editor: 'Shell.Editor',
    run: 'Shell.Run',
    settings: 'Shell.Settings',
  };

  connectedCallback(): void {
    super.connectedCallback();
    // 页面元素在词表到达之前就挂载了（wg-shell 写在 index.html 里），先按键名显示，词表
    // 到了再重画。
    this.unsubscribe = strings.subscribe(() => this.refreshLabels());
    this.refreshLabels();
  }

  disconnectedCallback(): void {
    this.unsubscribe?.();
    super.disconnectedCallback();
  }

  /** 只在词表换包时重建标签；active 变化走 property。 */
  private refreshLabels(): void {
    this.labels = {};
    for (const route of routes) {
      this.labels[route] = t(WgNav.keys[route]);
    }
    this.requestUpdate();
  }

  render() {
    return html`
      <nav class="activity" aria-label=${t('Shell.Window.Menu')}>
        ${routes.map(
          (route) => html`
            <button
              type="button"
              title=${this.labels[route] ?? route}
              aria-label=${this.labels[route] ?? route}
              aria-current=${this.active === route ? 'page' : 'false'}
              @click=${() => this.select(route)}
            >
              ${this.icon(route)}
            </button>
          `,
        )}
      </nav>
      <nav class="side">
        ${routes.map(
          (route) => html`
            <button
              type="button"
              aria-current=${this.active === route ? 'page' : 'false'}
              @click=${() => this.select(route)}
            >
              ${this.labels[route] ?? route}
            </button>
          `,
        )}
      </nav>
    `;
  }

  private icon(route: Route) {
    switch (route) {
      case 'home':
        return html`<svg viewBox="0 0 24 24" aria-hidden="true" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linejoin="round">
          <path d="M4 10.5 12 4l8 6.5V20h-5v-6H9v6H4Z" />
        </svg>`;
      case 'macros':
        return html`<svg viewBox="0 0 24 24" aria-hidden="true" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round">
          <path d="M5 6h14M5 12h14M5 18h9" />
        </svg>`;
      case 'editor':
        return html`<svg viewBox="0 0 24 24" aria-hidden="true" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linejoin="round">
          <path d="M6 3h8l4 4v14H6Z" />
          <path d="M14 3v4h4M9 12h6M9 16h6" />
        </svg>`;
      case 'run':
        return html`<svg viewBox="0 0 24 24" aria-hidden="true" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linejoin="round">
          <path d="M8 5.5 19 12 8 18.5Z" />
        </svg>`;
      case 'settings':
        return html`<svg viewBox="0 0 24 24" aria-hidden="true" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linejoin="round">
          <circle cx="12" cy="12" r="3" />
          <path d="M12 3v2M12 19v2M3 12h2M19 12h2M5.6 5.6 7 7M17 17l1.4 1.4M18.4 5.6 17 7M7 17l-1.4 1.4" />
        </svg>`;
      default:
        return html``;
    }
  }

  private select(route: Route): void {
    this.dispatchEvent(new CustomEvent('wg-navigate', { detail: { route }, bubbles: true, composed: true }));
  }
}

declare global {
  interface HTMLElementTagNameMap {
    'wg-nav': WgNav;
  }
}
