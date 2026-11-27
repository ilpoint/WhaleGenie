import { LitElement, css, html, nothing } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import { currentRoute, go, type Route } from '../shared/navigation/router.js';
import './titlebar.js';
import './nav.js';
import '../features/dialogs/confirm.js';
import '../features/transfer/transfer-dialog.js';
import '../features/macros/macros-page.js';
import '../features/library/library-page.js';
import '../features/settings/settings-page.js';
import '../features/about/about-page.js';
import { strings } from '../shared/strings/strings.js';
import { StoreController } from '../shared/lit/store-controller.js';
import { WindowEventController } from '../shared/lit/window-event-controller.js';

/**
 * 应用壳：网页标题栏 + 左侧导航 + 内容区 + 底部状态条。窗口按钮、置顶与拖动都在标题栏里；
 * 这里管当前路由与当前编辑文档的保存状态。项目本身的状态归标题栏，系统开关归左侧栏。
 */
@customElement('wg-shell')
export class WgShell extends LitElement {
  @state()
  private route: Route = currentRoute();

  protected readonly stringsController = new StoreController(this, strings);

  protected readonly popstateController = new WindowEventController(this, 'popstate', () => {
    this.route = currentRoute();
  });

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
      font-family: var(--fontFamilyBase);
      font-size: var(--fontSizeBase300);
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
      min-height: 0;
    }

    .content.editor {
      overflow: hidden;
    }
  `;

  private onNavigate(event: CustomEvent<{ route: Route }>): void {
    void this.navigate(event.detail.route);
  }

  private async navigate(route: Route): Promise<void> {
    await go(route);
    this.route = currentRoute();
  }

  render() {
    return html`
      <wg-titlebar></wg-titlebar>
      <wg-nav .active=${this.route} @wg-navigate=${this.onNavigate}></wg-nav>
      <div class="content">${this.renderRoute()}</div>
      <wg-confirm></wg-confirm>
      <wg-transfer></wg-transfer>
    `;
  }

  private renderRoute() {
    switch (this.route) {
      case 'macros':
        return html`<wg-macros></wg-macros>`;
      case 'library':
        return html`<wg-library></wg-library>`;
      case 'settings':
        return html`<wg-settings></wg-settings>`;
      case 'about':
        return html`<wg-about></wg-about>`;
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
