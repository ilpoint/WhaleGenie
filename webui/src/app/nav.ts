import { LitElement, css, html } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import { icon } from '../shared/icons.js';
import { strings, t } from '../shared/strings/strings.js';
import { routes, type Route } from '../shared/navigation/router.js';

/**
 * 左侧活动栏。条目从词表键推导：`Shell.<Route>` 存在就是一项，所以阶段加页面只需要加键与
 * 一条路由，不用改导航的代码。当前选中的项铺强调色底。侧栏内容属于各页面自己的
 * workbench（编辑器动作库、运行面板），这里只留一条 48px 的活动栏，避免同一份导航画两遍。
 */
@customElement('wg-nav')
export class WgNav extends LitElement {
  @property()
  active: Route = 'home';



  static styles = css`
    :host {
      display: grid;
      grid-template-columns: 48px;
      height: 100%;
      min-height: 0;
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
      padding: var(--spacingVerticalS) var(--spacingHorizontalM);
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

    .grow {
      flex: 1;
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

  `;

  private labels: Record<string, string> = {};

  private unsubscribe?: () => void;

  /** 路由到词表键的显式映射：对账测试按字面量扫键，动态拼出来的键扫不到。 */
  private static readonly keys: Record<Route, string> = {
    home: 'Shell.Home',
    macros: 'Shell.Macros',
    editor: 'Shell.Editor',
    run: 'Shell.Run',
    library: 'Shell.Library',
    settings: 'Shell.Settings',
    about: 'Shell.About',
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
    const mainRoutes = routes.filter((route) => route !== 'settings' && route !== 'about');
    return html`
      <nav class="activity" aria-label=${t('Shell.Navigation')}>
        ${mainRoutes.map((route) => this.activityButton(route))}
        <span class="grow"></span>
        ${this.activityButton('settings')}
      </nav>
    `;
  }

  private activityButton(route: Route) {
    return html`
      <button
        type="button"
        title=${this.labels[route] ?? route}
        aria-label=${this.labels[route] ?? route}
        aria-current=${this.active === route ? 'page' : 'false'}
        @click=${() => this.select(route)}
      >
        ${icon(route)}
      </button>
    `;
  }

  private select(route: Route): void {
    if (route === this.active) {
      return;
    }

    this.dispatchEvent(new CustomEvent('wg-navigate', { detail: { route }, bubbles: true, composed: true }));
  }
}

declare global {
  interface HTMLElementTagNameMap {
    'wg-nav': WgNav;
  }
}