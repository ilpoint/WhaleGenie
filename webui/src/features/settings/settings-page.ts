import { LitElement, html, nothing } from 'lit';
import { settingsPageCss } from './settings-page-css.js';
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
import { keyboardChord, pointerBinding } from '../../shared/input/capture.js';
import { emptyRule, type WgWindowRule } from '../../shared/window-rule/window-rule-dialog.js';
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

  @state()
  private search = '';

  static styles = settingsPageCss;

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
    const header = html`
      <header class="header">
        <h1>${t('Shell.Settings')}</h1>
        <input type="search" .value=${this.search} placeholder=${t('Settings.Search')}
          aria-label=${t('Settings.Search')}
          @input=${(event: Event) => this.setSearch((event.target as HTMLInputElement).value)} />
      </header>
      <nav class="rail" aria-label=${t('Shell.Settings')}>
        ${this.categories().map((category) => html`
          <button type="button" @click=${() => this.jump(category.key)}>${t(category.label)}</button>
        `)}
      </nav>
    `;

    if (!this.settings) {
      return html`${header}<div class="content">
        ${this.error ? html`<p class="error">${this.error}</p>` : nothing}
      </div>`;
    }

    return html`
      ${header}
      <div class="content">
      ${this.error ? html`<p class="error">${this.error}</p>` : nothing}
      <div class="cards">
        <section class="card" data-settings-section="general">
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

        <section class="card" data-settings-section="input">
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

        <section class="card" data-settings-section="input">
          <div class="card-head">
            <h2>${t('Settings.Sounds')}</h2>
            <span class="note">${t('Settings.SoundsNote')}</span>
          </div>
          ${this.toggle('soundSystem', 'Settings.SoundSystem')}
          ${this.toggle('soundMacro', 'Settings.SoundMacro')}
        </section>

        <section class="card" data-settings-section="input">
          <div class="card-head">
            <h2>${t('Settings.Reading')}</h2>
            <span class="note">${t('Settings.ReadingNote')}</span>
          </div>
          ${this.toggle('preloadOcr', 'Settings.PreloadOcr')}
        </section>

        <section class="card" data-settings-section="input">
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

        <section class="card" data-settings-section="input">
          <div class="card-head">
            <h2>${t('Settings.InputCard')}</h2>
            <span class="note">${t('Settings.InputCardNote')}</span>
          </div>
          ${this.toggle('ignoreOwnInput', 'Settings.IgnoreOwnInput')}
          ${this.number('inputRate', 'Settings.InputLimit', 0, 999, false)}
          ${this.number('logCount', 'Settings.LogCount', 1, 1000, false)}
        </section>

        <section class="card" data-settings-section="input">
          <div class="card-head">
            <h2>${t('Settings.Driver')}</h2>
            <span class="note">${t('Settings.DriverNote')}</span>
          </div>
          <p class="status">${this.settings.driverStatus}</p>
          ${this.toggle('attachDriverAtStart', 'Settings.DriverAttachAtStart')}
          ${this.toggle('runAsAdmin', 'Settings.RunAsAdmin')}
          <p class="status">${this.settings.adminStatus}</p>
        </section>

        <section class="card" data-settings-section="input">
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

        <section class="card" data-settings-section="storage">
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
      </div>
      ${this.ruleEditor ? html`
        <wg-window-rule .rule=${this.ruleEditor.rule} .title=${this.ruleEditor.title}
          @wg-rule-submit=${(event: CustomEvent<WindowRuleState>) => void this.submitRule(event.detail)}
          @wg-rule-close=${() => { this.ruleEditor = null; }}>
        </wg-window-rule>
      ` : nothing}
    `;
  }

  private categories(): { key: string; label: string }[] {
    return [
      { key: 'general', label: 'Settings.GeneralCard' },
      { key: 'theme', label: 'Settings.Theme' },
      { key: 'input', label: 'Settings.InputCard' },
      { key: 'menu', label: 'Settings.WheelTitle' },
      { key: 'storage', label: 'Settings.Storage' },
    ];
  }

  private jump(key: string): void {
    const node = this.renderRoot.querySelector<HTMLElement>(
      `[data-settings-section="${key}"]`);
    node?.scrollIntoView({ block: 'start', behavior: 'smooth' });
  }

  private setSearch(value: string): void {
    this.search = value;
    void this.updateComplete.then(() => this.applySearch());
  }

  private applySearch(): void {
    const wanted = this.search.trim().toLocaleLowerCase();
    this.renderRoot.querySelectorAll<HTMLElement>('.cards > .card').forEach((card) => {
      card.hidden = wanted.length > 0
        && !(card.textContent ?? '').toLocaleLowerCase().includes(wanted);
    });
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
      params['baseLight'] = baseLight ?? current.baseLight;
      params['accentHex'] = accentHex ?? current.accentHex ?? current.defaultAccent;
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