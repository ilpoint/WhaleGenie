import { LitElement, css, html, nothing } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import { bridge } from '../../shared/bridge/bridge.js';
import type {
  SettingsState,
  SettingsWheelSlot,
  ThemeState,
  WindowRuleState,
} from '../../shared/bridge/protocol.js';
import { theme as themeStore } from '../../shared/theme/theme.js';
import { strings, t } from '../../shared/strings/strings.js';
import { keyboardChord, pointerBinding } from './capture.js';
import { emptyRule, type WgWindowRule } from './window-rule-dialog.js';
import { renderMenu, type MenuActions } from './settings-menu.js';
import {
  renderWheelActions,
  renderWheelSettings,
  type WheelActions,
  type WheelSlotDraft,
} from './settings-wheel.js';
import { renderTheme, type ThemeActions } from './settings-theme.js';

type CaptureKind = 'systemHotkey' | 'recordStopKey' | 'menu' | 'wheelRepeat' | '';

interface RuleEditor {
  kind: 'menu-add' | 'menu-edit' | 'wheel-add' | 'wheel-edit';
  key: string;
  title: string;
  rule: WindowRuleState;
}

const emptyTheme: ThemeState = { mode: 'dark', baseLight: false, accentHex: null };

/** The settings page: general choices, theme, hotkeys, menu rules, wheel and storage. */
@customElement('wg-settings')
export class WgSettings extends LitElement {
  @state()
  private settings: SettingsState | null = null;

  @state()
  private theme: ThemeState = emptyTheme;

  @state()
  private error = '';

  @state()
  private capturing: CaptureKind = '';

  @state()
  private ruleEditor: RuleEditor | null = null;

  @state()
  private selectedSlot: { ring: string; index: number } | null = null;

  @state()
  private slotDraft: WheelSlotDraft | null = null;

  static styles = css`
    :host {
      display: block;
      padding: var(--spacingVerticalL) var(--spacingHorizontalXXL);
      max-width: 1240px;
    }

    h1 {
      margin: 0 0 var(--spacingVerticalL);
      font-size: var(--font-size-base-500);
      font-weight: var(--font-weight-semibold);
    }

    h3 {
      margin: var(--spacingVerticalM) 0 0;
      font-size: var(--font-size-base-300);
    }

    .cards {
      display: grid;
      gap: var(--spacingVerticalM);
    }

    .card {
      display: grid;
      gap: var(--spacingVerticalS);
      padding: var(--spacingVerticalM) var(--spacingHorizontalM);
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
      border-radius: var(--borderRadiusLarge);
      background: var(--colorNeutralBackground1);
    }

    .card-head {
      display: flex;
      flex-wrap: wrap;
      align-items: baseline;
      gap: var(--spacingHorizontalS);
    }

    .card h2,
    .slot-editor h3 {
      margin: 0;
      font-size: var(--font-size-base-400);
    }

    .note {
      color: var(--colorNeutralForeground3);
      font-size: var(--font-size-base-200);
    }

    .row {
      display: grid;
      grid-template-columns: minmax(0, 1fr) 180px;
      gap: var(--spacingHorizontalM);
      align-items: center;
    }

    .row label,
    .field > span:first-child {
      color: var(--colorNeutralForeground1);
    }

    .settings-grid {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(240px, 1fr));
      gap: var(--spacingHorizontalM) var(--spacingHorizontalL);
      align-items: end;
    }

    .settings-grid.compact {
      grid-template-columns: repeat(auto-fit, minmax(210px, 1fr));
      gap: var(--spacingVerticalS) var(--spacingHorizontalM);
      align-items: center;
    }

    .field {
      display: grid;
      gap: var(--spacingVerticalXS);
      min-width: 0;
    }

    .field select,
    .field input {
      width: 100%;
      max-width: 280px;
      font: inherit;
    }

    .field .inline-unit {
      max-width: 220px;
    }

    .inline-unit {
      display: flex;
      align-items: center;
      gap: var(--spacingHorizontalXS);
    }

    .inline-unit input {
      width: 100%;
    }

    .inline-unit em {
      color: var(--colorNeutralForeground3);
      font-size: var(--font-size-base-200);
      font-style: normal;
      white-space: nowrap;
    }

    .check-row {
      display: flex;
      align-items: center;
      gap: var(--spacingHorizontalS);
      min-height: 28px;
      color: var(--colorNeutralForeground1);
    }

    .check-row input {
      width: auto;
    }

    .hotkey-row {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--spacingHorizontalS);
    }

    .binding {
      display: inline-flex;
      align-items: center;
      min-height: 28px;
      padding: 0 var(--spacingHorizontalS);
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
      border-radius: var(--borderRadiusSmall);
      background: var(--colorNeutralBackground2);
      font-family: var(--fontFamilyMonospace);
      font-size: var(--font-size-base-200);
    }

    .capture-target {
      min-height: 30px;
      padding: 0 var(--spacingHorizontalM);
      border: var(--strokeWidthThick) solid var(--colorBrandStroke1);
      border-radius: var(--borderRadiusMedium);
      background: var(--colorBrandBackground2);
      color: var(--colorNeutralForeground1);
      font: inherit;
      cursor: default;
    }

    .hint,
    .status {
      margin: 0;
      color: var(--colorNeutralForeground2);
      font-size: var(--font-size-base-200);
      line-height: 1.45;
    }

    .status {
      color: var(--colorNeutralForeground3);
    }

    .error {
      color: var(--colorPaletteRedForeground1);
      margin: 0;
    }

    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--spacingHorizontalS);
    }

    .rule-list {
      display: grid;
      gap: var(--spacingVerticalXS);
    }

    .rule {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--spacingHorizontalM);
      padding: var(--spacingVerticalS) var(--spacingHorizontalS);
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
      border-radius: var(--borderRadiusMedium);
      background: var(--colorNeutralBackground2);
    }

    .rule[draggable='true'] {
      cursor: grab;
    }

    .rule-text {
      display: grid;
      gap: 2px;
      min-width: 0;
    }

    .rule-text strong {
      font-size: var(--font-size-base-300);
      font-weight: var(--font-weight-semibold);
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .rule-text span {
      color: var(--colorNeutralForeground3);
      font-size: var(--font-size-base-200);
    }

    .rule-buttons {
      display: flex;
      flex-wrap: wrap;
      justify-content: flex-end;
      gap: 2px;
    }

    .storage-row {
      display: grid;
      grid-template-columns: minmax(0, 1fr) auto;
      gap: var(--spacingHorizontalM);
      align-items: center;
      padding: var(--spacingVerticalXS) 0;
      border-top: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
    }

    .storage-row .where {
      color: var(--colorNeutralForeground3);
      font-size: var(--font-size-base-200);
      word-break: break-all;
    }

    .wheel-layout {
      display: grid;
      grid-template-columns: minmax(220px, 0.7fr) minmax(320px, 1.3fr);
      gap: var(--spacingHorizontalL);
      align-items: start;
    }

    .scene-panel,
    .wheel-stage {
      display: grid;
      gap: var(--spacingVerticalS);
      min-width: 0;
    }

    .wheel {
      position: relative;
      justify-self: center;
      flex: none;
    }

    .wheel-rings {
      position: absolute;
      inset: 0;
      width: 100%;
      height: 100%;
      overflow: visible;
    }

    .wheel-rings circle {
      fill: none;
      stroke: var(--colorNeutralStroke1);
      stroke-width: 1;
    }

    .wheel-slot {
      position: absolute;
      transform: translate(-50%, -50%);
      min-width: 34px;
      max-width: 84px;
      min-height: 24px;
      padding: 2px 4px;
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
      border-radius: var(--borderRadiusSmall);
      background: var(--colorNeutralBackground2);
      color: var(--colorNeutralForeground1);
      font: inherit;
      line-height: 1.15;
      text-align: center;
      overflow: hidden;
      cursor: default;
    }

    .wheel-slot[data-tone='override'] {
      border-color: var(--colorBrandStroke1);
      background: var(--colorBrandBackground2);
    }

    .wheel-slot[data-tone='extended'] {
      background: var(--colorNeutralBackground4);
    }

    .wheel-slot[data-tone='empty'] {
      border-color: transparent;
      background: transparent;
      color: var(--colorNeutralForeground4);
      opacity: 0.16;
    }

    .wheel-slot[data-tone='empty']:hover,
    .wheel-slot[data-tone='empty'][data-selected='true'] {
      border-color: var(--colorNeutralStroke2);
      background: var(--colorNeutralBackground2);
      opacity: 1;
    }

    .wheel-slot[data-selected='true'] {
      outline: var(--strokeWidthThicker) solid var(--colorBrandStroke1);
      outline-offset: 1px;
    }

    .wheel-slot:disabled {
      cursor: default;
    }

    .wheel-slot .slot-label {
      display: block;
      max-width: 76px;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .wheel-slot .empty {
      font-size: var(--font-size-base-400);
      line-height: 1;
    }

    .wheel-centre {
      position: absolute;
      left: 50%;
      top: 50%;
      width: 30%;
      transform: translate(-50%, -50%);
      text-align: center;
      color: var(--colorNeutralForeground3);
      font-size: var(--font-size-base-100);
      overflow: hidden;
      text-overflow: ellipsis;
      pointer-events: none;
    }

    .legend {
      display: flex;
      flex-wrap: wrap;
      gap: var(--spacingHorizontalM);
      font-size: var(--font-size-base-200);
      color: var(--colorNeutralForeground2);
    }

    .legend span::before {
      content: '';
      display: inline-block;
      width: 10px;
      height: 10px;
      margin-right: 5px;
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
      background: var(--colorNeutralBackground2);
      vertical-align: -1px;
    }

    .legend span[data-tone='override']::before {
      border-color: var(--colorBrandStroke1);
      background: var(--colorBrandBackground2);
    }

    .legend span[data-tone='extended']::before {
      background: var(--colorNeutralBackground4);
    }

    .slot-editor {
      display: grid;
      gap: var(--spacingVerticalS);
      padding: var(--spacingVerticalM);
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
      border-radius: var(--borderRadiusLarge);
      background: var(--colorNeutralBackground2);
    }

    .icon-row,
    .accent-row {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--spacingHorizontalS);
    }

    .icon-row .binding,
    .accent-row .binding {
      flex: 1;
      min-width: 0;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    .accent-row input[type='color'] {
      width: 52px;
      height: 30px;
      padding: 2px;
    }

    .theme-previews {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(140px, 1fr));
      gap: var(--spacingHorizontalS);
    }

    .preview {
      min-height: 44px;
      display: grid;
      place-items: center;
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
      border-radius: var(--borderRadiusMedium);
      font-size: var(--font-size-base-200);
    }
  `;

  private unsubscribeStrings?: () => void;

  private unsubscribeTheme?: () => void;

  connectedCallback(): void {
    super.connectedCallback();
    this.unsubscribeStrings = strings.subscribe(() => this.requestUpdate());
    this.unsubscribeTheme = themeStore.subscribe((theme) => {
      this.theme = theme;
    });
    void this.load();
  }

  disconnectedCallback(): void {
    this.unsubscribeStrings?.();
    this.unsubscribeTheme?.();
    super.disconnectedCallback();
  }

  render() {
    if (!this.settings) {
      return html`<h1>${t('Shell.Settings')}</h1>
        ${this.error ? html`<p class="error">${this.error}</p>` : nothing}`;
    }

    return html`
      <h1>${t('Shell.Settings')}</h1>
      ${this.error ? html`<p class="error">${this.error}</p>` : nothing}
      <div class="cards">
        <section class="card">
          <div class="card-head">
            <h2>${t('Settings.GeneralCard')}</h2>
            <span class="note">${t('Settings.GeneralCardNote')}</span>
          </div>
          <div class="row">
            <label>${t('Settings.Language')}</label>
            <select @change=${(event: Event) => void this.changeSetting('language', event)}>
              <option value="zh-Hans" ?selected=${this.settings.language === 'zh-Hans'}>
                ${t('Settings.Language.Chinese')}
              </option>
              <option value="en" ?selected=${this.settings.language === 'en'}>
                ${t('Settings.Language.English')}
              </option>
            </select>
          </div>
          ${this.toggle('loadLastProjectAtStart', 'Settings.LoadLastProject')}
          ${this.toggle('systemAtStart', 'Settings.SystemAtStart')}
          ${this.toggle('enabledFirst', 'Settings.EnabledFirst')}
        </section>

        ${renderTheme(this.theme, this.themeActions())}

        <section class="card">
          <div class="card-head">
            <h2>${t('Settings.Hotkeys')}</h2>
            <span class="note">${t('Settings.HotkeysNote')}</span>
          </div>
          ${this.hotkey('systemHotkey', 'Settings.SystemHotkey')}
          ${this.hotkey('recordStopKey', 'Settings.RecordStopKey')}
        </section>

        ${renderMenu(this.settings.menu, this.capturing === 'menu', this.menuActions())}

        ${renderWheelSettings(this.settings.wheel, this.capturing === 'wheelRepeat',
          this.wheelActions())}
        ${renderWheelActions(this.settings.wheel, this.selectedSlot, this.slotDraft,
          this.wheelActions())}

        <section class="card">
          <div class="card-head">
            <h2>${t('Settings.Sounds')}</h2>
            <span class="note">${t('Settings.SoundsNote')}</span>
          </div>
          ${this.toggle('soundSystem', 'Settings.SoundSystem')}
          ${this.toggle('soundMacro', 'Settings.SoundMacro')}
        </section>

        <section class="card">
          <div class="card-head">
            <h2>${t('Settings.Reading')}</h2>
            <span class="note">${t('Settings.ReadingNote')}</span>
          </div>
          ${this.toggle('preloadOcr', 'Settings.PreloadOcr')}
        </section>

        <section class="card">
          <div class="card-head">
            <h2>${t('Settings.FramesCard')}</h2>
            <span class="note">${t('Settings.FramesCardNote')}</span>
          </div>
          ${this.toggle('frameRateFollow', 'Settings.FrameRateFollow')}
          ${this.number('frameRate', 'Settings.FrameRate', 1, this.settings.frameRateMax,
            this.settings.frameRateFollow)}
          ${this.toggle('frameCache', 'Settings.FrameCache')}
          ${this.number('frameCacheAge', 'Settings.FrameCacheAge', 0, 60000,
            !this.settings.frameCache)}
        </section>

        <section class="card">
          <div class="card-head">
            <h2>${t('Settings.InputCard')}</h2>
            <span class="note">${t('Settings.InputCardNote')}</span>
          </div>
          ${this.toggle('ignoreOwnInput', 'Settings.IgnoreOwnInput')}
          ${this.number('inputRate', 'Settings.InputLimit', 0, 999, false)}
          ${this.number('logCount', 'Settings.LogCount', 1, 1000, false)}
        </section>

        <section class="card">
          <div class="card-head">
            <h2>${t('Settings.Driver')}</h2>
            <span class="note">${t('Settings.DriverNote')}</span>
          </div>
          <p class="status">${this.settings.driverStatus}</p>
          ${this.toggle('attachDriverAtStart', 'Settings.DriverAttachAtStart')}
          ${this.toggle('runAsAdmin', 'Settings.RunAsAdmin')}
          <p class="status">${this.settings.adminStatus}</p>
        </section>

        <section class="card">
          <div class="card-head">
            <h2>${t('Settings.CaptureCard')}</h2>
          </div>
          <div class="row">
            <label>${t('Settings.CaptureWindow')}</label>
            <select @change=${(event: Event) => void this.changeSetting('windowWay', event)}>
              ${this.settings.windowWays.map((way) => html`
                <option value=${way.value} ?selected=${way.value === this.settings!.windowWay}>
                  ${way.label}
                </option>
              `)}
            </select>
          </div>
          <div class="row">
            <label>${t('Settings.CaptureScreen')}</label>
            <select @change=${(event: Event) => void this.changeSetting('screenWay', event)}>
              ${this.settings.screenWays.map((way) => html`
                <option value=${way.value} ?selected=${way.value === this.settings!.screenWay}>
                  ${way.label}
                </option>
              `)}
            </select>
          </div>
        </section>

        <section class="card">
          <div class="card-head">
            <h2>${t('Settings.Storage')}</h2>
            <span class="note">${t('Settings.StorageNote')}</span>
          </div>
          ${this.settings.storage.map((row) => html`
            <div class="storage-row">
              <div>
                <strong>${row.name}</strong>
                <div>${row.what}</div>
                <div class="where">${row.where}</div>
              </div>
              <fluent-button appearance="subtle" @click=${() => void this.openFolder(row.folder)}>
                ${t('Settings.OpenFolder')}
              </fluent-button>
            </div>
          `)}
        </section>
      </div>
      ${this.ruleEditor ? html`
        <wg-window-rule .rule=${this.ruleEditor.rule} .title=${this.ruleEditor.title}
          @wg-rule-submit=${(event: CustomEvent<WindowRuleState>) => void this.submitRule(event.detail)}
          @wg-rule-close=${() => { this.ruleEditor = null; }}>
        </wg-window-rule>
      ` : nothing}
    `;
  }

  private toggle(field: keyof SettingsState, label: string) {
    return html`
      <div class="row">
        <label>${t(label)}</label>
        <input type="checkbox" .checked=${Boolean(this.settings![field])}
          @change=${(event: Event) => void this.changeSetting(field, event)} />
      </div>
    `;
  }

  private number(field: keyof SettingsState, label: string, min: number, max: number,
    disabled: boolean) {
    return html`
      <div class="row">
        <label>${t(label)}</label>
        <input type="number" min=${min} max=${max} ?disabled=${disabled}
          .value=${String(this.settings![field])}
          @change=${(event: Event) => void this.changeSetting(field, event)} />
      </div>
    `;
  }

  private hotkey(field: 'systemHotkey' | 'recordStopKey', label: string) {
    const value = this.settings![field];
    const problem = field === 'systemHotkey'
      ? this.settings!.systemHotkeyProblem
      : this.settings!.recordStopProblem;
    return html`
      <div class="hotkey hotkey-row">
        <label>${t(label)}</label>
        <input readonly data-hotkey=${field} .value=${value}
          @keydown=${(event: KeyboardEvent) => this.captureKey(field, event)} />
        <fluent-button appearance="subtle" @click=${() => void this.beginCapture(field)}>
          ${t(this.capturing === field ? 'Settings.HotkeyPress' : 'Settings.HotkeySet')}
        </fluent-button>
        <fluent-button appearance="subtle" @click=${() => void this.setSettingValue(field, '')}>
          ${t('Settings.HotkeyClear')}
        </fluent-button>
      </div>
      ${problem ? html`<p class="error">${problem}</p>` : nothing}
    `;
  }

  private menuActions(): MenuActions {
    return {
      beginCapture: () => void this.beginCapture('menu'),
      captureKey: (event) => this.captureKey('menu', event),
      capturePointer: (event) => this.capturePointer('menu', event),
      setMiddle: () => void this.applyCapture('menu', 'Mouse Middle'),
      clear: () => void this.menuState('settings.menu.clear'),
      addRule: () => this.openRuleEditor('menu-add'),
      editRule: (key) => this.openRuleEditor('menu-edit', key),
      removeRule: (key) => void this.menuState('settings.menu.rule.remove', { key }),
      addPage: (key) => void this.menuState('settings.menu.rule.addPage', { key }),
      moveRule: (key, step) => void this.menuState('settings.menu.rule.move', { key, step }),
      moveRuleTo: (key, target) =>
        void this.menuState('settings.menu.rule.moveTo', { key, target }),
    };
  }

  private wheelActions(): WheelActions {
    return {
      update: (field, value) => void this.wheelState('settings.wheel.update', { field, value }),
      setLauncher: (value) => void this.wheelState('settings.wheel.launcher', { value }),
      setEnabled: (value) => void this.wheelState('settings.wheel.enabled', { value }),
      enableSystem: () => void this.wheelState('settings.wheel.enableSystem'),
      beginRepeatCapture: () => void this.beginCapture('wheelRepeat'),
      captureRepeatKey: (event) => this.captureKey('wheelRepeat', event),
      selectScene: (key) => void this.selectScene(key),
      addScene: () => this.openRuleEditor('wheel-add'),
      editScene: (key) => this.openRuleEditor('wheel-edit', key),
      removeScene: (key) => void this.sceneState('settings.wheel.scene.remove', { key }),
      moveScene: (step) => void this.sceneState('settings.wheel.scene.move', { step }),
      selectSlot: (slot) => {
        this.selectedSlot = { ring: slot.ring, index: slot.index };
        this.slotDraft = draftOf(slot);
      },
      editDraft: (field, value) => {
        if (this.slotDraft) {
          this.slotDraft = { ...this.slotDraft, [field]: value };
        }
      },
      pickIcon: () => void this.pickIcon(),
      clearIcon: () => {
        if (this.slotDraft) {
          this.slotDraft = { ...this.slotDraft, icon: null };
        }
      },
      saveSlot: () => void this.saveSlot(),
      clearSlot: () => void this.saveSlot(true),
    };
  }

  private themeActions(): ThemeActions {
    return {
      setMode: (mode) => void this.applyTheme(mode),
      setBaseLight: (baseLight) => void this.applyTheme('custom', baseLight),
      setAccent: (accentHex) => void this.applyTheme('custom', undefined, accentHex),
      resetAccent: () => void this.applyTheme('custom', undefined,
        this.theme.defaultAccent ?? '#c8401c'),
    };
  }

  private async beginCapture(kind: CaptureKind): Promise<void> {
    this.capturing = kind;
    this.error = '';
    await this.updateComplete;
    const selector = kind === 'menu'
      ? '[data-menu-capture]'
      : kind === 'wheelRepeat'
        ? '[data-wheel-repeat]'
        : `input[data-hotkey="${kind}"]`;
    this.renderRoot.querySelector<HTMLElement>(selector)?.focus();
  }

  private captureKey(kind: CaptureKind, event: KeyboardEvent): void {
    if (this.capturing !== kind) {
      return;
    }
    if (event.key === 'Escape') {
      event.preventDefault();
      this.capturing = '';
      return;
    }
    const binding = keyboardChord(event);
    if (!binding) {
      return;
    }
    event.preventDefault();
    event.stopPropagation();
    this.capturing = '';
    void this.applyCapture(kind, binding);
  }

  private capturePointer(kind: CaptureKind, event: PointerEvent): void {
    if (this.capturing !== kind) {
      return;
    }
    const binding = pointerBinding(event);
    if (!binding) {
      return;
    }
    event.preventDefault();
    event.stopPropagation();
    this.capturing = '';
    void this.applyCapture(kind, binding);
  }

  private async applyCapture(kind: CaptureKind, binding: string): Promise<void> {
    try {
      if (kind === 'systemHotkey' || kind === 'recordStopKey') {
        await this.setSettingValue(kind, binding);
      } else if (kind === 'menu') {
        this.settings = await bridge.value.call<SettingsState>('settings.menu.capture',
          { binding });
        this.error = '';
      } else if (kind === 'wheelRepeat') {
        this.settings = await bridge.value.call<SettingsState>('settings.wheel.repeat',
          { binding });
        this.error = '';
      }
    } catch (error) {
      this.error = String(error);
    }
  }

  private async load(): Promise<void> {
    try {
      this.settings = await bridge.value.call<SettingsState>('settings.state');
      this.theme = themeStore.get();
      this.error = '';
    } catch (error) {
      this.error = String(error);
    }
  }

  private async changeSetting(field: keyof SettingsState, event: Event): Promise<void> {
    const target = event.target as HTMLInputElement;
    const value = target.type === 'checkbox' ? String(target.checked) : target.value;
    await this.setSettingValue(field, value);
  }

  private async setSettingValue(field: keyof SettingsState, value: string): Promise<void> {
    try {
      this.settings = await bridge.value.call<SettingsState>('settings.update', { field, value });
      this.error = '';
    } catch (error) {
      this.error = String(error);
    }
  }

  private async openFolder(folder: string): Promise<void> {
    try {
      await bridge.value.call('settings.openFolder', { folder });
    } catch (error) {
      this.error = String(error);
    }
  }

  private async menuState(method: string, params: Record<string, unknown> = {}): Promise<void> {
    try {
      this.settings = await bridge.value.call<SettingsState>(method, params);
      this.error = '';
    } catch (error) {
      this.error = String(error);
    }
  }

  private async wheelState(method: string, params: Record<string, unknown> = {}): Promise<void> {
    try {
      this.settings = await bridge.value.call<SettingsState>(method, params);
      this.error = '';
      this.refreshDraft();
    } catch (error) {
      this.error = String(error);
    }
  }

  private async sceneState(method: string, params: Record<string, unknown> = {}): Promise<void> {
    this.selectedSlot = null;
    this.slotDraft = null;
    await this.wheelState(method, params);
  }

  private async selectScene(key: string): Promise<void> {
    this.selectedSlot = null;
    this.slotDraft = null;
    await this.wheelState('settings.wheel.scene.select', { key });
  }

  private async applyTheme(mode: string, baseLight?: boolean, accentHex?: string): Promise<void> {
    const current = this.theme;
    const params: Record<string, unknown> = { mode };
    if (mode === 'custom') {
      params.baseLight = baseLight ?? current.baseLight;
      params.accentHex = accentHex ?? current.accentHex ?? current.defaultAccent;
    }
    try {
      this.theme = await bridge.value.call<ThemeState>('theme.set', params);
      this.error = '';
    } catch (error) {
      this.error = String(error);
    }
  }

  private openRuleEditor(kind: RuleEditor['kind'], key = ''): void {
    if (!this.settings) {
      return;
    }
    let rule = emptyRule();
    if (kind === 'menu-edit') {
      rule = this.settings.menu.rules.find((row) => row.key === key)?.rule ?? rule;
    } else if (kind === 'wheel-edit') {
      rule = this.settings.wheel.scenes.find((scene) => scene.key === key)?.rule ?? rule;
    }
    this.ruleEditor = {
      kind,
      key,
      title: kind === 'menu-add' || kind === 'menu-edit'
        ? t('Settings.MenuWindowRules')
        : t('Settings.WheelTitle'),
      rule: { ...rule },
    };
  }

  private async submitRule(rule: WindowRuleState): Promise<void> {
    if (!this.ruleEditor) {
      return;
    }
    const editor = this.ruleEditor;
    const method = editor.kind === 'menu-add' || editor.kind === 'menu-edit'
      ? `settings.menu.rule.${editor.kind === 'menu-add' ? 'add' : 'edit'}`
      : `settings.wheel.scene.${editor.kind === 'wheel-add' ? 'add' : 'edit'}`;
    try {
      this.settings = await bridge.value.call<SettingsState>(method,
        editor.key ? { key: editor.key, rule } : { rule });
      this.ruleEditor = null;
      this.error = '';
      this.refreshDraft();
    } catch (error) {
      this.error = String(error);
    }
  }

  private refreshDraft(): void {
    if (!this.settings || !this.selectedSlot) {
      return;
    }
    const slot = this.settings.wheel.slots.find((candidate) =>
      candidate.ring === this.selectedSlot!.ring && candidate.index === this.selectedSlot!.index);
    if (slot) {
      this.slotDraft = draftOf(slot);
    }
  }

  private async saveSlot(clear = false): Promise<void> {
    if (!this.settings || !this.slotDraft) {
      return;
    }
    const draft = this.slotDraft;
    const params = {
      ring: draft.ring,
      index: draft.index,
      macro: clear ? '' : draft.macro,
      note: clear ? '' : draft.note,
      icon: clear ? null : draft.icon,
    };
    try {
      this.settings = await bridge.value.call<SettingsState>('settings.wheel.slot.set', params);
      this.error = '';
      this.refreshDraft();
    } catch (error) {
      this.error = String(error);
    }
  }

  private async pickIcon(): Promise<void> {
    if (!this.slotDraft) {
      return;
    }
    try {
      const icon = await bridge.value.call<{ kind: string; value: string } | null>(
        'settings.wheel.pickIcon');
      if (icon) {
        this.slotDraft = {
          ...this.slotDraft,
          icon: icon.value.length > 0 ? icon : null,
        };
      }
      this.error = '';
    } catch (error) {
      this.error = String(error);
    }
  }
}

function draftOf(slot: SettingsWheelSlot): WheelSlotDraft {
  return {
    ring: slot.ring,
    index: slot.index,
    position: slot.position,
    macro: slot.macro,
    note: slot.note,
    icon: slot.icon,
    inherited: slot.inherited,
    overridden: slot.overridden,
  };
}

declare global {
  interface HTMLElementTagNameMap {
    'wg-settings': WgSettings;
    'wg-window-rule': WgWindowRule;
  }
}