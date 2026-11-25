import { LitElement, css, html, nothing } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import { bridge } from '../shared/bridge/bridge.js';
import { icon } from '../shared/icons.js';
import { go, routes, type Route } from '../shared/navigation/router.js';
import { systemSwitch } from '../shared/system/system.js';
import { strings, t } from '../shared/strings/strings.js';
import { StoreController } from '../shared/lit/store-controller.js';
import { editor } from '../features/editor/editor-store.js';
import { hotkeyKeys } from './nav-model.js';
import type { SystemSwitchState } from '../shared/bridge/protocol.js';

/**
 * 左侧活动栏。条目从词表键推导：`Shell.<Route>` 存在就是一项，所以阶段加页面只需要加键与
 * 一条路由，不用改导航的代码。底部常驻两件事：系统开关与它的开关键，以及设置。系统这一格
 * 在任何页面都能按，不必先切到某个工作区再去找。
 */
@customElement('wg-nav')
export class WgNav extends LitElement {
  @property()
  active: Route = 'macros';

  @state()
  private system: SystemSwitchState | null = null;

  @state()
  private editorDirty = false;

  static styles = css`
    :host {
      display: grid;
      grid-template-columns: 64px;
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

    .activity > button {
      position: relative;
      display: grid;
      place-items: center;
      height: 48px;
      padding: 0;
      border-radius: 0;
      color: var(--colorNeutralForeground3);
    }

    .activity > button:hover {
      color: var(--colorNeutralForeground1);
    }

    .activity > button[aria-current='page'] {
      color: var(--colorNeutralForeground1);
      background: var(--colorNeutralBackground2);
    }

    .activity > button[aria-current='page']::before {
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

    .badge {
      position: absolute;
      top: 10px;
      right: 14px;
      width: 7px;
      height: 7px;
      border-radius: 50%;
      background: var(--colorStatusWarningBackground3);
      outline: var(--strokeWidthThin) solid var(--colorNeutralBackground3);
    }

    /* The system switch and its toggle key live together at the bottom, so both are in
       reach from every page. The key column is the one thing that tells a person which
       chord they are about to press without opening Settings. */
    .system {
      display: grid;
      justify-items: center;
      gap: 2px;
      margin: var(--spacingVerticalS) var(--spacingHorizontalXS);
      padding: var(--spacingVerticalXS) 0;
      border-top: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
    }

    .power {
      position: relative;
      display: grid;
      place-items: center;
      width: 100%;
      height: 40px;
      padding: 0;
      border-radius: var(--borderRadiusMedium);
      color: var(--colorNeutralForeground3);
    }

    .power:hover {
      background: var(--colorSubtleBackgroundHover);
      color: var(--colorNeutralForeground1);
    }

    .system[data-enabled='true'] .power {
      color: var(--colorBrandForeground1);
    }

    .system[data-enabled='true'] .power::after {
      content: '';
      position: absolute;
      bottom: 4px;
      width: 4px;
      height: 4px;
      border-radius: 50%;
      background: var(--colorBrandBackground);
    }

    .power svg {
      width: 19px;
      height: 19px;
    }

    .hotkey {
      display: grid;
      justify-items: center;
      gap: 1px;
      width: 100%;
      min-height: 30px;
      padding: 4px 2px;
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke1);
      border-radius: var(--borderRadiusMedium);
      background: var(--colorNeutralBackground1);
      color: var(--colorNeutralForeground2);
      font-size: var(--fontSizeBase100);
      line-height: 1.1;
    }

    .hotkey:hover {
      background: var(--colorNeutralBackground1Hover);
      color: var(--colorNeutralForeground1);
    }

    .hotkey .key {
      max-width: 54px;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
      font-family: var(--fontFamilyMonospace);
      font-size: 10px;
      line-height: 1.15;
    }

    .hotkey .plus {
      color: var(--colorNeutralForeground3);
      font-size: 10px;
      line-height: 1;
    }

  `;

  private labels: Record<string, string> = {};

  protected readonly stringsController = new StoreController(this, strings, () => this.refreshLabels());

  protected readonly systemController = new StoreController(this, systemSwitch, (value) => {
    this.system = value;
  });

  protected readonly editorController = new StoreController<void>(this, editor, () => {
    this.editorDirty = editor.get().tabs.some((tab) => tab.dirty);
  });

  /** 路由到词表键的显式映射：对账测试按字面量扫键，动态拼出来的键扫不到。 */
  private static readonly keys: Record<Route, string> = {
    macros: 'Shell.Macros',
    editor: 'Shell.Editor',
    run: 'Shell.Run',
    library: 'Shell.Library',
    settings: 'Shell.Settings',
    about: 'Shell.About',
  };

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
        ${this.systemBlock()}
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
        ${route === 'editor' && this.editorDirty
          ? html`<span class="badge" aria-hidden="true"></span>`
          : nothing}
      </button>
    `;
  }

  private systemBlock() {
    const enabled = this.system?.enabled ?? false;
    const stateWord = t(enabled ? 'Main.SystemEnabled' : 'Main.SystemDisabled');
    const key = this.system?.hotkeyDisplay ?? '';
    const label = t('Shell.SystemButton')
      .replace('{0}', stateWord)
      .replace('{1}', key.length > 0 ? key : t('Common.None'));
    const title = t('Shell.SystemHotkeyTitle').replace('{0}', key.length > 0 ? key : t('Common.None'));
    return html`
      <div class="system" data-enabled=${String(enabled)}>
        <button
          type="button"
          class="power"
          aria-pressed=${String(enabled)}
          aria-label=${label}
          title=${label}
          @click=${() => void this.toggleSystem()}
        >${icon('power')}</button>
        <button
          type="button"
          class="hotkey"
          aria-label=${title}
          title=${title}
          @click=${() => void go('settings')}
        >
          ${this.hotkeyLines()}
        </button>
      </div>
    `;
  }

  private hotkeyLines() {
    const keys = hotkeyKeys(this.system?.hotkeyDisplay ?? '');
    if (keys.length === 0) {
      return html`<span class="key">${t('Common.None')}</span>`;
    }

    return keys.map((key, index) => html`
      ${index > 0 ? html`<span class="plus">+</span>` : nothing}
      <span class="key">${key}</span>
    `);
  }

  private async toggleSystem(): Promise<void> {
    const enabled = this.system?.enabled ?? false;
    try {
      await bridge.value.call('project.system', { enabled: !enabled });
    } catch {
      // The host pushes the real state; a refused write leaves the switch where it was.
    }
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
