import { LitElement, css, html, nothing } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import { currentRoute, go, type Route } from './router.js';
import './titlebar.js';
import './nav.js';
import '../features/dialogs/confirm.js';
import '../features/home/home-page.js';
import '../features/macros/macros-page.js';
import '../features/editor/editor-page.js';
import '../features/library/library-page.js';
import '../features/settings/settings-page.js';
import '../features/run/run-page.js';
import '../features/about/about-page.js';
import { project } from '../shared/project/store.js';
import { strings, t } from '../shared/strings/strings.js';
import type { ProjectState } from '../shared/bridge/protocol.js';

/**
 * 应用壳：网页标题栏 + 左侧导航 + 内容区 + 底部状态条。窗口按钮、置顶与拖动都在标题栏里；
 * 这里管当前路由与项目状态的两行只读摘录。仍由原生窗口承担的区域显示占位页，按钮经桥把对应
 * 窗口请出来。
 */
@customElement('wg-shell')
export class WgShell extends LitElement {
  @state()
  private route: Route = currentRoute();

  @state()
  private document: ProjectState | null = null;

  private unsubscribeProject?: () => void;

  private unsubscribeStrings?: () => void;

  static styles = css`
    :host {
      display: grid;
      grid-template-rows: auto 1fr 24px;
      grid-template-columns: auto 1fr;
      grid-template-areas:
        'titlebar titlebar'
        'nav content'
        'status status';
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

    .status {
      grid-area: status;
      display: flex;
      align-items: center;
      justify-content: flex-end;
      gap: var(--spacingHorizontalL);
      padding: 0 var(--spacingHorizontalM);
      border-top: var(--strokeWidthThin) solid var(--colorNeutralStroke1);
      background: var(--colorNeutralBackground3);
      color: var(--colorNeutralForeground2);
      font-size: var(--fontSizeBase200);
    }
  `;

  connectedCallback(): void {
    super.connectedCallback();
    window.addEventListener('popstate', this.onPopState);
    this.unsubscribeProject = project.subscribe((value) => {
      this.document = value;
    });
    this.unsubscribeStrings = strings.subscribe(() => this.requestUpdate());
  }

  disconnectedCallback(): void {
    window.removeEventListener('popstate', this.onPopState);
    this.unsubscribeProject?.();
    this.unsubscribeStrings?.();
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
      <div class="content ${this.route === 'editor' ? 'editor' : ''}">${this.renderRoute()}</div>
      <footer class="status" role="status">
        <span data-status="project">${this.projectLabel()}</span>
        <span data-status="revision">${this.revisionLabel()}</span>
        <span data-status="dirty">${this.dirtyLabel()}</span>
        <span data-status="system">${this.systemLabel()}</span>
      </footer>
      <wg-confirm></wg-confirm>
    `;
  }

  private projectLabel(): string {
    return this.document?.name || t('Shell.UntitledProject');
  }

  private revisionLabel(): string {
    return 'R' + (this.document?.revision ?? 0);
  }

  private dirtyLabel(): string {
    return t(this.document?.dirty ? 'Shell.Field.Dirty' : 'Shell.Field.Clean');
  }

  private systemLabel(): string {
    return t(this.document?.systemEnabled ? 'Main.SystemEnabled' : 'Main.SystemDisabled');
  }

  private renderRoute() {
    switch (this.route) {
      case 'home':
        return html`<wg-home></wg-home>`;
      case 'macros':
        return html`<wg-macros></wg-macros>`;
      case 'editor':
        return html`<wg-editor></wg-editor>`;
      case 'run':
        return html`<wg-run></wg-run>`;
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