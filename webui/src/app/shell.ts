import { LitElement, css, html, nothing } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import { currentRoute, go, type Route } from './router.js';
import './titlebar.js';
import './nav.js';
import '../features/dialogs/confirm.js';
import '../features/home/home-page.js';
import '../features/macros/macros-page.js';
import '../features/placeholder/placeholder-page.js';

/**
 * 应用壳：网页标题栏 + 左侧导航 + 内容区。窗口按钮、置顶与拖动都在标题栏里；这里只管
 * 当前路由是哪一页。仍由原生窗口承担的区域显示占位页，按钮经桥把对应窗口请出来。
 */
@customElement('wg-shell')
export class WgShell extends LitElement {
  @state()
  private route: Route = currentRoute();

  static styles = css`
    :host {
      display: grid;
      grid-template-rows: auto 1fr;
      grid-template-columns: auto 1fr;
      grid-template-areas:
        'titlebar titlebar'
        'nav content';
      height: 100vh;
      background: var(--colorNeutralBackground2);
      color: var(--colorNeutralForeground1);
      font-family: var(--font-family-base);
      font-size: var(--font-size-base-300);
    }

    wg-titlebar {
      grid-area: titlebar;
    }

    wg-nav {
      grid-area: nav;
    }

    .content {
      grid-area: content;
      overflow: auto;
      min-width: 0;
    }
  `;

  connectedCallback(): void {
    super.connectedCallback();
    window.addEventListener('popstate', this.onPopState);
  }

  disconnectedCallback(): void {
    window.removeEventListener('popstate', this.onPopState);
    super.disconnectedCallback();
  }

  private readonly onPopState = (): void => {
    this.route = currentRoute();
  };

  private onNavigate(event: CustomEvent<{ route: Route }>): void {
    go(event.detail.route);
    this.route = currentRoute();
  }

  render() {
    return html`
      <wg-titlebar></wg-titlebar>
      <wg-nav .active=${this.route} @wg-navigate=${this.onNavigate}></wg-nav>
      <div class="content">${this.renderRoute()}</div>
      <wg-confirm></wg-confirm>
    `;
  }

  private renderRoute() {
    switch (this.route) {
      case 'home':
        return html`<wg-home></wg-home>`;
      case 'macros':
        return html`<wg-macros></wg-macros>`;
      case 'editor':
        return html`<wg-placeholder area="editor"></wg-placeholder>`;
      case 'run':
        return html`<wg-placeholder area="run"></wg-placeholder>`;
      case 'settings':
        return html`<wg-placeholder area="settings"></wg-placeholder>`;
      default:
        return nothing;
    }
  }
}

declare global {
  interface HTMLElementTagNameMap {
    'wg-shell': WgShell;
  }
}
