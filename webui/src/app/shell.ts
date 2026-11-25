import { LitElement, css, html, nothing } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import { currentRoute, go, type Route } from '../shared/navigation/router.js';
import './titlebar.js';
import './nav.js';
import '../features/dialogs/confirm.js';
import '../features/transfer/transfer-dialog.js';
import '../features/editor/editor-unsaved.js';
import '../features/macros/macros-page.js';
import '../features/editor/editor-page.js';
import '../features/library/library-page.js';
import '../features/settings/settings-page.js';
import '../features/run/run-page.js';
import '../features/about/about-page.js';
import { project } from '../shared/project/store.js';
import { systemSwitch } from '../shared/system/system.js';
import { strings, t } from '../shared/strings/strings.js';
import { StoreController } from '../shared/lit/store-controller.js';
import { WindowEventController } from '../shared/lit/window-event-controller.js';
import type { ProjectState, SystemSwitchState } from '../shared/bridge/protocol.js';

/**
 * 应用壳：网页标题栏 + 左侧导航 + 内容区 + 底部状态条。窗口按钮、置顶与拖动都在标题栏里；
 * 这里管当前路由、项目保存状态与系统开关的两行只读摘录。系统开关本身在左侧栏，这里只把
 * 完整文字和开关键再写一遍，方便被侧栏省略的长组合键仍能读到。
 */
@customElement('wg-shell')
export class WgShell extends LitElement {
  @state()
  private route: Route = currentRoute();

  @state()
  private document: ProjectState | null = null;

  @state()
  private system: SystemSwitchState | null = null;

  protected readonly stringsController = new StoreController(this, strings);

  protected readonly projectController = new StoreController(this, project, (value) => {
    this.document = value;
  });

  protected readonly systemController = new StoreController(this, systemSwitch, (value) => {
    this.system = value;
  });

  protected readonly popstateController = new WindowEventController(this, 'popstate', () => {
    this.route = currentRoute();
  });

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
      <div class="content ${this.route === 'editor' ? 'editor' : ''}">${this.renderRoute()}</div>
      <footer class="status" role="status">
        <span data-status="project">${this.projectLabel()}</span>
        <span data-status="dirty" title=${this.saveError()}>${this.saveLabel()}</span>
        <span data-status="system">${this.systemLabel()}</span>
      </footer>
      <wg-confirm></wg-confirm>
      <wg-transfer></wg-transfer>
      <wg-editor-unsaved></wg-editor-unsaved>
    `;
  }

  private projectLabel(): string {
    return this.document?.name || t('Shell.UntitledProject');
  }

  private saveError(): string {
    return this.document?.saveError ?? '';
  }

  private saveLabel(): string {
    if (this.document?.saving) {
      return t('Shell.Saving');
    }

    if (this.document?.saveError) {
      return t('Shell.SaveFailed');
    }

    return t(this.document?.dirty ? 'Shell.Field.Dirty' : 'Shell.Field.Clean');
  }

  private systemLabel(): string {
    const enabled = this.system?.enabled ?? false;
    const state = t(enabled ? 'Main.SystemEnabled' : 'Main.SystemDisabled');
    const key = this.system?.hotkeyDisplay ?? '';
    return t('Shell.SystemStatus')
      .replace('{0}', state)
      .replace('{1}', key.length > 0 ? key : t('Common.None'));
  }

  private renderRoute() {
    switch (this.route) {
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
