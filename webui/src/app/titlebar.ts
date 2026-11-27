import { LitElement, css, html, nothing } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import { appInfo } from '../shared/app/app.js';
import { bridge } from '../shared/bridge/bridge.js';
import { describeError } from '../shared/errors.js';
import { project } from '../shared/project/store.js';
import { icon } from '../shared/icons.js';
import { go } from '../shared/navigation/router.js';
import { strings, t } from '../shared/strings/strings.js';
import { StoreController } from '../shared/lit/store-controller.js';
import { WindowEventController } from '../shared/lit/window-event-controller.js';
import { windowCommand } from '../shared/window/window-kind.js';
import { windowMirror } from '../shared/window/window.js';
import { startExport, startImport, startOpen, transfer } from '../features/transfer/index.js';
import { editor } from '../features/editor/editor-store.js';
import type { BridgeMethod } from '../shared/bridge/protocol.js';
import whale from '../assets/whale.png';

/**
 * 网页标题栏：鲸鱼、名字与当前项目在左，置顶与窗口钮在右。拖动、双击最大化与关闭都是
 * 标题栏的职责；按钮只发桥命令，窗口的真实状态由宿主推回来。
 */
@customElement('wg-titlebar')
export class WgTitlebar extends LitElement {
  @state()
  private project: { name: string | null; untitled: boolean; autoSave: boolean } | null = null;

  @state()
  private placement = 'normal';

  @state()
  private topmost = false;

  @state()
  private menu: 'file' | 'help' | null = null;

  @state()
  private version = '';

  static styles = css`
    :host {
      display: block;
      user-select: none;
    }

    .bar {
      display: grid;
      grid-template-columns: auto auto 1fr auto;
      align-items: center;
      gap: var(--spacingHorizontalS);
      height: 40px;
      padding-left: var(--spacingHorizontalM);
      background: var(--colorNeutralBackground1);
      -webkit-app-region: no-drag;
      position: relative;
      z-index: 10;
    }

    .menus {
      display: flex;
      gap: var(--spacingHorizontalXS);
    }

    .menu {
      position: relative;
    }

    .menu > button {
      border: none;
      border-radius: var(--borderRadiusMedium);
      background: transparent;
      color: var(--colorNeutralForeground1);
      font: inherit;
      font-size: var(--fontSizeBase200);
      padding: var(--spacingVerticalXS) var(--spacingHorizontalS);
      cursor: default;
    }

    .menu > button:hover,
    .menu > button[aria-expanded='true'] {
      background: var(--colorNeutralBackground1Hover);
    }

    .menu > button:focus-visible {
      outline: var(--strokeWidthThicker) solid var(--colorStrokeFocus2);
      outline-offset: -2px;
    }

    .dropdown {
      position: absolute;
      top: calc(100% + 2px);
      left: 0;
      min-width: 220px;
      padding: var(--spacingVerticalXS);
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke1);
      border-radius: var(--borderRadiusMedium);
      background: var(--colorNeutralBackground1);
      box-shadow: var(--shadow16);
      z-index: 20;
    }

    .dropdown button {
      display: block;
      width: 100%;
      border: none;
      border-radius: var(--borderRadiusSmall);
      background: transparent;
      color: var(--colorNeutralForeground1);
      font: inherit;
      text-align: left;
      padding: var(--spacingVerticalSNudge) var(--spacingHorizontalM);
      cursor: default;
    }

    .dropdown button:hover {
      background: var(--colorNeutralBackground1Hover);
    }

    .dropdown .separator {
      margin: var(--spacingVerticalXS) 0;
      border-top: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
    }

    .dropdown .version {
      padding: var(--spacingVerticalSNudge) var(--spacingHorizontalM);
      color: var(--colorNeutralForeground3);
      font-size: var(--fontSizeBase200);
      border-bottom: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
      margin-bottom: var(--spacingVerticalXS);
    }

    .whale {
      width: 20px;
      height: 20px;
    }

    .title {
      font-size: var(--fontSizeBase200);
      color: var(--colorNeutralForeground1);
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
      min-width: 0;
    }

    .title .project {
      color: var(--colorNeutralForeground3);
    }

    .buttons {
      display: flex;
      align-self: stretch;
    }

    .buttons button {
      width: 46px;
      border: none;
      background: transparent;
      color: var(--colorNeutralForeground1);
      display: grid;
      place-items: center;
      cursor: default;
      padding: 0;
    }

    .buttons button:hover {
      background: var(--colorNeutralBackground1Hover);
    }

    .buttons button:active {
      background: var(--colorNeutralBackground1Pressed);
    }

    .buttons button:focus-visible {
      outline: var(--strokeWidthThicker) solid var(--colorStrokeFocus2);
      outline-offset: -2px;
    }

    .buttons .close:hover {
      background: var(--colorPaletteRedBackground3);
      color: var(--colorNeutralForegroundOnBrand);
    }

    .pin[data-active='true'] {
      color: var(--colorBrandForeground1);
    }

    svg {
      width: 16px;
      height: 16px;
    }
  `;

  protected readonly stringsController = new StoreController(this, strings);

  protected readonly projectController = new StoreController(this, project, (state) => {
    this.project = state
      ? { name: state.name, untitled: state.untitled, autoSave: state.autoSave }
      : null;
  });

  protected readonly windowController = new StoreController(this, windowMirror, (state) => {
    if (state) {
      this.placement = state.state;
      this.topmost = state.topmost;
    }
  });

  protected readonly appInfoController = new StoreController(this, appInfo, (info) => {
    this.version = info?.version ?? '';
  });

  protected readonly pointerController = new WindowEventController(this, 'pointerdown', (event) => {
    this.onDocumentPointerDown(event);
  }, { capture: true });

  render() {
    return html`
      <header
        class="bar"
        @pointerdown=${this.onDragPointerDown}
        @dblclick=${this.onToggleMaximize}
      >
        <img class="whale" src=${whale} alt="" />
        <div class="menus">
          <div class="menu">
            <button
              type="button"
              aria-haspopup="menu"
              aria-expanded=${this.menu === 'file'}
              @click=${() => this.toggleMenu('file')}
            >
              ${t('Shell.File')}
            </button>
            ${this.menu === 'file'
              ? html`<div class="dropdown" role="menu">
                  <button role="menuitem" @click=${() => void this.onOpen()}>${t('Main.OpenProject')}</button>
                  <button role="menuitem" @click=${() => this.run('project.save')}>${t('Main.SaveProject')}</button>
                  <button role="menuitem" @click=${() => this.run('project.saveAs')}>${t('Main.SaveProjectAs')}</button>
                  <button role="menuitem" @click=${() => void this.onImport()}>${t('Main.Import')}</button>
                  <button role="menuitem" @click=${() => void this.onExport()}>${t('Main.ExportMacro')}</button>
                  <button role="menuitem" @click=${() => this.run('project.closeAll')}>${t('Main.CloseAll')}</button>
                  <div class="separator" role="separator"></div>
                  <button role="menuitem" @click=${() => this.run('app.quit')}>${t('Main.Exit')}</button>
                </div>`
              : nothing}
          </div>
          <div class="menu">
            <button
              type="button"
              aria-haspopup="menu"
              aria-expanded=${this.menu === 'help'}
              @click=${() => this.toggleMenu('help')}
            >
              ${t('Shell.Help')}
            </button>
            ${this.menu === 'help'
              ? html`<div class="dropdown" role="menu">
                  <div class="version">${t('Shell.Field.Version')} ${this.version}</div>
                  <button role="menuitem" @click=${() => this.run('app.openManual')}>${t('Shell.Manual')}</button>
                  <button role="menuitem" @click=${this.openAbout}>${t('Shell.About')}</button>
                </div>`
              : nothing}
          </div>
        </div>
        <span class="title">
          ${t('Main.Title')}
          <span class="project">· ${this.projectLabel()}</span>
        </span>
        <div class="buttons">
          <button
            class="pin"
            data-active=${String(this.topmost)}
            title=${this.topmost ? t('Shell.Window.Unpin') : t('Shell.Window.Pin')}
            aria-label=${this.topmost ? t('Shell.Window.Unpin') : t('Shell.Window.Pin')}
            @click=${this.onPin}
          >
            ${icon('pin')}
          </button>
          <button title=${t('Shell.Window.Minimize')} aria-label=${t('Shell.Window.Minimize')} @click=${this.onMinimize}>
            ${icon('minimize')}
          </button>
          <button
            title=${this.placement === 'maximized' ? t('Shell.Window.Restore') : t('Shell.Window.Maximize')}
            aria-label=${this.placement === 'maximized' ? t('Shell.Window.Restore') : t('Shell.Window.Maximize')}
            @click=${this.onToggleMaximize}
          >
            ${icon(this.placement === 'maximized' ? 'restore' : 'maximize')}
          </button>
          <button class="close" title=${t('Shell.Window.Close')} aria-label=${t('Shell.Window.Close')} @click=${this.onClose}>
            ${icon('close')}
          </button>
        </div>
      </header>
    `;
  }

  private projectLabel(): string {
    const name = this.projectName();
    return this.project?.autoSave === true
      ? t('Shell.ProjectNameAutoSave').replace('{0}', name)
      : name;
  }

  private projectName(): string {
    if (!this.project || this.project.untitled) {
      return t('Shell.UntitledProject');
    }
    return this.project.name ?? '';
  }

  private readonly onDocumentPointerDown = (event: PointerEvent): void => {
    if (this.menu === null) {
      return;
    }

    const inside = event.composedPath().some((node) =>
      node instanceof HTMLElement && node.classList.contains('menu'));
    if (!inside) {
      this.menu = null;
    }
  };

  private toggleMenu(menu: 'file' | 'help'): void {
    this.menu = this.menu === menu ? null : menu;
  }

  private openAbout(): void {
    this.menu = null;
    void go('about');
  }

  private run(command: BridgeMethod): void {
    this.menu = null;
    void bridge.value.call(command);
  }

  /** Opening shows the same package preview as importing; the primary action differs. */
  private async onOpen(): Promise<void> {
    this.menu = null;
    try {
      const session = await startOpen(bridge.value);
      if (session !== null) {
        transfer.show(session);
      }
    } catch (error) {
      transfer.fail('open', describeError(error));
    }
  }

  /** Import asks for the file first, then hands the plan to the wizard. */
  private async onImport(): Promise<void> {
    this.menu = null;
    try {
      const session = await startImport(bridge.value);
      if (session !== null) {
        transfer.show(session);
      }
    } catch (error) {
      transfer.fail('import', describeError(error));
    }
  }

  private async onExport(): Promise<void> {
    this.menu = null;
    try {
      const session = await startExport(bridge.value);
      const active = editor.get().snapshot?.name ?? '';
      transfer.show(session, active.length > 0 ? [active] : []);
    } catch (error) {
      transfer.fail('export', describeError(error));
    }
  }

  private onDragPointerDown(event: PointerEvent): void {
    if ((event.target as HTMLElement).closest('button')) {
      return;
    }
    if (event.button !== 0) {
      return;
    }
    void bridge.value.call(windowCommand('drag'));
  }

  private onToggleMaximize(): void {
    void bridge.value.call(windowCommand('maximize'));
  }

  private onMinimize(): void {
    void bridge.value.call(windowCommand('minimize'));
  }

  private onPin(): void {
    void bridge.value.call(windowCommand('pin'));
  }

  private onClose(): void {
    // Closing is the window's job: the tray hides the main shell, and the workbench has its own
    // close question for the macro documents it carries. The title bar itself does not prompt.
    void bridge.value.call(windowCommand('close'));
  }
}

declare global {
  interface HTMLElementTagNameMap {
    'wg-titlebar': WgTitlebar;
  }
}