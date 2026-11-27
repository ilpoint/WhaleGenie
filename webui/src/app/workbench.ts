import { LitElement, css, html } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import { bridge } from '../shared/bridge/bridge.js';
import { icon } from '../shared/icons.js';
import { currentRoute, go } from '../shared/navigation/router.js';
import { strings, t } from '../shared/strings/strings.js';
import { StoreController } from '../shared/lit/store-controller.js';
import { WindowEventController } from '../shared/lit/window-event-controller.js';
import { windowCommand } from '../shared/window/window-kind.js';
import { windowMirror } from '../shared/window/window.js';
import '../features/editor/editor-page.js';
import '../features/editor/editor-unsaved.js';
import '../features/dialogs/confirm.js';
import '../features/run/run-page.js';
import whale from '../assets/whale.png';

/**
 * 宏工作台：编辑器与运行调试的独立窗口。两个页面同时挂载，页签只切显示，运行中的会话不会
 * 因为切到编辑器而被卸载；主窗口的卡片、导航和系统开关不在这里重复。
 */
@customElement('wg-workbench')
export class WgWorkbench extends LitElement {
  @state()
  private route: 'editor' | 'run' = currentRoute() === 'run' ? 'run' : 'editor';

  @state()
  private placement = 'normal';

  @state()
  private topmost = false;

  protected readonly stringsController = new StoreController(this, strings);

  protected readonly windowController = new StoreController(this, windowMirror, (state) => {
    if (state) {
      this.placement = state.state;
      this.topmost = state.topmost;
    }
  });

  protected readonly routeController = new WindowEventController(this, 'hashchange', () => {
    this.route = currentRoute() === 'run' ? 'run' : 'editor';
  });

  static styles = css`
    :host {
      display: grid;
      grid-template-rows: 40px 36px minmax(0, 1fr);
      height: 100vh;
      background: var(--colorNeutralBackground2);
      color: var(--colorNeutralForeground1);
      font-family: var(--fontFamilyBase);
      font-size: var(--fontSizeBase300);
    }

    .bar {
      display: flex;
      align-items: center;
      gap: var(--spacingHorizontalS);
      padding-left: var(--spacingHorizontalM);
      background: var(--colorNeutralBackground1);
      user-select: none;
    }

    .whale {
      width: 20px;
      height: 20px;
      object-fit: contain;
    }

    .title {
      flex: 1;
      min-width: 0;
      color: var(--colorNeutralForeground2);
      font-size: var(--fontSizeBase200);
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    button {
      border: none;
      border-radius: var(--borderRadiusMedium);
      background: transparent;
      color: var(--colorNeutralForeground1);
      font: inherit;
      height: 30px;
      padding: 0 var(--spacingHorizontalS);
      display: inline-flex;
      align-items: center;
      gap: var(--spacingHorizontalXS);
      cursor: default;
    }

    button:hover {
      background: var(--colorNeutralBackground1Hover);
    }

    .buttons {
      display: flex;
      align-items: center;
    }

    .buttons button {
      width: 36px;
      justify-content: center;
    }

    .buttons .close:hover {
      background: var(--colorStatusDangerBackground1);
    }

    .tabs {
      display: flex;
      align-items: stretch;
      gap: 2px;
      padding: 0 var(--spacingHorizontalM);
      border-bottom: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
      background: var(--colorNeutralBackground1);
    }

    .tabs button {
      border-radius: 0;
      border-bottom: 2px solid transparent;
      padding: 0 var(--spacingHorizontalM);
    }

    .tabs button[aria-selected='true'] {
      border-bottom-color: var(--colorBrandStroke1);
      color: var(--colorBrandForeground1);
    }

    main {
      min-width: 0;
      min-height: 0;
      overflow: hidden;
    }

    .pane {
      width: 100%;
      height: 100%;
      min-width: 0;
      min-height: 0;
    }

    .pane[hidden] {
      display: none;
    }
  `;

  protected firstUpdated(): void {
    if (currentRoute() !== 'editor' && currentRoute() !== 'run') {
      window.location.hash = 'editor';
    }
  }

  render() {
    const route = this.route;
    return html`
      <header class="bar" @pointerdown=${this.onDragPointerDown} @dblclick=${this.onToggleMaximize}>
        <img class="whale" src=${whale} alt="" />
        <span class="title">${t('Shell.WorkbenchTitle')}</span>
        <div class="buttons">
          <button title=${this.topmost ? t('Shell.Window.Unpin') : t('Shell.Window.Pin')}
            aria-label=${this.topmost ? t('Shell.Window.Unpin') : t('Shell.Window.Pin')}
            @click=${this.onPin}>${icon('pin')}</button>
          <button title=${t('Shell.Window.Minimize')} aria-label=${t('Shell.Window.Minimize')}
            @click=${() => this.windowAction('minimize')}>${icon('minimize')}</button>
          <button title=${this.placement === 'maximized' ? t('Shell.Window.Restore') : t('Shell.Window.Maximize')}
            aria-label=${this.placement === 'maximized' ? t('Shell.Window.Restore') : t('Shell.Window.Maximize')}
            @click=${this.onToggleMaximize}>
            ${icon(this.placement === 'maximized' ? 'restore' : 'maximize')}
          </button>
          <button class="close" title=${t('Shell.Window.Close')} aria-label=${t('Shell.Window.Close')}
            @click=${() => this.windowAction('close')}>${icon('close')}</button>
        </div>
      </header>
      <nav class="tabs" role="tablist" aria-label=${t('Shell.WorkbenchTitle')}>
        <button role="tab" aria-selected=${String(route === 'editor')}
          @click=${() => void go('editor')}>${icon('editor')}${t('Shell.Editor')}</button>
        <button role="tab" aria-selected=${String(route === 'run')}
          @click=${() => void go('run')}>${icon('run')}${t('Shell.Run')}</button>
      </nav>
      <main>
        <section class="pane" ?hidden=${route !== 'editor'}><wg-editor></wg-editor></section>
        <section class="pane" ?hidden=${route !== 'run'}><wg-run></wg-run></section>
      </main>
      <wg-confirm></wg-confirm>
      <wg-editor-unsaved></wg-editor-unsaved>
    `;
  }

  private onDragPointerDown(event: PointerEvent): void {
    if ((event.target as HTMLElement).closest('button')) {
      return;
    }

    if (event.button === 0) {
      this.windowAction('drag');
    }
  }

  private onToggleMaximize(): void {
    this.windowAction('maximize');
  }

  private onPin(): void {
    this.windowAction('pin');
  }

  private windowAction(command: 'minimize' | 'maximize' | 'close' | 'pin' | 'drag'): void {
    void bridge.value.call(windowCommand(command));
  }
}

declare global {
  interface HTMLElementTagNameMap {
    'wg-workbench': WgWorkbench;
  }
}
