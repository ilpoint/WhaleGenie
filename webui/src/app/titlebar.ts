import { LitElement, css, html } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import { bridge } from '../shared/bridge/bridge.js';
import { project } from '../shared/project/store.js';
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

  static styles = css`
    :host {
      display: block;
      user-select: none;
    }

    .bar {
      display: grid;
      grid-template-columns: auto 1fr auto;
      align-items: center;
      gap: var(--spacingHorizontalS);
      height: 40px;
      padding-left: var(--spacingHorizontalM);
      background: var(--colorNeutralBackground1);
      -webkit-app-region: no-drag;
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
    ];
  }

  private unsubscribers: Array<() => void> = [];

  disconnectedCallback(): void {
    for (const unsubscribe of this.unsubscribers) {
      unsubscribe();
    }
    this.unsubscribers = [];
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
            <svg viewBox="0 0 16 16" aria-hidden="true">
              <path
                d="M9.5 1.5 14.5 6.5 12.2 7.2 10.6 8.8 10 12 8.2 13.8 5.6 11.2 2 14.7 1.3 14 4.8 10.4 2.2 7.8 4 6 7.2 5.4 8.8 3.8 Z"
                fill="none"
                stroke="currentColor"
                stroke-width="1.1"
                stroke-linejoin="round"
              />
            </svg>
          </button>
          <button title=${t('Shell.Window.Minimize')} aria-label=${t('Shell.Window.Minimize')} @click=${this.onMinimize}>
            <svg viewBox="0 0 16 16" aria-hidden="true">
              <path d="M2.5 8h11" stroke="currentColor" stroke-width="1" />
            </svg>
          </button>
          <button
            title=${this.placement === 'maximized' ? t('Shell.Window.Restore') : t('Shell.Window.Maximize')}
            aria-label=${this.placement === 'maximized' ? t('Shell.Window.Restore') : t('Shell.Window.Maximize')}
            @click=${this.onToggleMaximize}
          >
            ${this.placement === 'maximized'
              ? html`<svg viewBox="0 0 16 16" aria-hidden="true">
                  <path d="M5.5 3.5h7v7" fill="none" stroke="currentColor" stroke-width="1" />
                  <path d="M3.5 5.5h7v7h-7z" fill="none" stroke="currentColor" stroke-width="1" />
                </svg>`
              : html`<svg viewBox="0 0 16 16" aria-hidden="true">
                  <path d="M3.5 3.5h9v9h-9z" fill="none" stroke="currentColor" stroke-width="1" />
                </svg>`}
          </button>
          <button class="close" title=${t('Shell.Window.Close')} aria-label=${t('Shell.Window.Close')} @click=${this.onClose}>
            <svg viewBox="0 0 16 16" aria-hidden="true">
              <path d="M3 3l10 10M13 3L3 13" stroke="currentColor" stroke-width="1" />
            </svg>
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
