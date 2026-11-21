import { LitElement, css, html, nothing } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import { appInfo } from '../shared/app/app.js';
import { bridge } from '../shared/bridge/bridge.js';
import { project } from '../shared/project/store.js';
import { icon } from '../shared/icons.js';
import { strings, t } from '../shared/strings/strings.js';
import { windowMirror } from '../shared/window/window.js';
import whale from '../assets/whale.png';

/**
 * 网页标题栏：鲸鱼、名字与当前项目在左，置顶与窗口钮在右。拖动、双击最大化与关闭都是
 * 标题栏的职责；按钮只发桥命令，窗口的真实状态由宿主推回来。
 */
@customElement('wg-titlebar')
export class WgTitlebar extends LitElement {
  @state()
  private project: { name: string | null; untitled: boolean } | null = null;

  @state()
  private placement: 'normal' | 'maximized' | 'minimized' = 'normal';

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
      font-size: var(--font-size-base-200);
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
      padding: var(--spacingVerticalSN) var(--spacingHorizontalM);
      cursor: default;
    }

    .dropdown button:hover {
      background: var(--colorNeutralBackground1Hover);
    }

    .dropdown .version {
      padding: var(--spacingVerticalSN) var(--spacingHorizontalM);
      color: var(--colorNeutralForeground3);
      font-size: var(--font-size-base-200);
      border-bottom: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
      margin-bottom: var(--spacingVerticalXS);
    }

    .whale {
      width: 20px;
      height: 20px;
    }

    .title {
      font-size: var(--font-size-base-200);
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

  connectedCallback(): void {
    super.connectedCallback();
    this.unsubscribers = [
      strings.subscribe(() => this.requestUpdate()),
      // 标题上的项目名跟着文档走：打开、另存、MCP 改名都会推 project.changed。
      project.subscribe((state) => {
        this.project = state ? { name: state.name, untitled: state.untitled } : null;
      }),
      windowMirror.subscribe((state) => {
        if (state) {
          this.placement = state.state;
          this.topmost = state.topmost;
        }
      }),
      appInfo.subscribe((info) => {
        this.version = info?.version ?? '';
      }),
    ];
    window.addEventListener('pointerdown', this.onDocumentPointerDown, true);
  }

  private unsubscribers: Array<() => void> = [];

  disconnectedCallback(): void {
    for (const unsubscribe of this.unsubscribers) {
      unsubscribe();
    }
    this.unsubscribers = [];
    window.removeEventListener('pointerdown', this.onDocumentPointerDown, true);
    super.disconnectedCallback();
  }

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
                  <button role="menuitem" @click=${() => this.run('project.open')}>${t('Main.OpenProject')}</button>
                  <button role="menuitem" @click=${() => this.run('project.save')}>${t('Main.SaveProject')}</button>
                  <button role="menuitem" @click=${() => this.run('project.saveAs')}>${t('Main.SaveProjectAs')}</button>
                  <button role="menuitem" @click=${() => this.run('project.import')}>${t('Main.Import')}</button>
                  <button role="menuitem" @click=${() => this.run('project.closeAll')}>${t('Main.CloseAll')}</button>
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
                  <button role="menuitem" @click=${() => this.run('app.openAbout')}>${t('Shell.About')}</button>
                </div>`
              : nothing}
          </div>
        </div>
        <span class="title">
          ${t('Main.Title')}
          <span class="project">· ${this.projectName()}</span>
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

  private run(command: string): void {
    this.menu = null;
    void bridge.value.call(command);
  }

  private onDragPointerDown(event: PointerEvent): void {
    if ((event.target as HTMLElement).closest('button')) {
      return;
    }
    if (event.button !== 0) {
      return;
    }
    void bridge.value.call('window.drag');
  }

  private onToggleMaximize(): void {
    void bridge.value.call('window.maximize');
  }

  private onMinimize(): void {
    void bridge.value.call('window.minimize');
  }

  private onPin(): void {
    void bridge.value.call('window.pin');
  }

  private onClose(): void {
    // 关闭交给窗口本身：托盘把它拦下来藏起，真正的退出在托盘菜单与 app.quit。
    void bridge.value.call('window.close');
  }
}

declare global {
  interface HTMLElementTagNameMap {
    'wg-titlebar': WgTitlebar;
  }
}
