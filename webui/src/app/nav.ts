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
      display: block;
      background: var(--colorNeutralBackground3);
      padding: var(--spacingVerticalS) var(--spacingHorizontalS);
      width: 148px;
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
      <nav>
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

  private select(route: Route): void {
    this.dispatchEvent(new CustomEvent('wg-navigate', { detail: { route }, bubbles: true, composed: true }));
  }
}

declare global {
  interface HTMLElementTagNameMap {
    'wg-nav': WgNav;
  }
}
