import { LitElement, html, nothing } from 'lit';
import { settingsPageCss } from './settings-page-css.js';
import { customElement, state } from 'lit/decorators.js';
import { bridge } from '../../shared/bridge/bridge.js';
import type {
  BridgeMethod,
  SettingsState,
  ThemeState,
  WindowRuleState,
} from '../../shared/bridge/protocol.js';
import { theme as themeStore } from '../../shared/theme/theme.js';
import { strings, t } from '../../shared/strings/strings.js';
import { StoreController } from '../../shared/lit/store-controller.js';
import { emptyRule, type WgWindowRule } from '../../shared/window-rule/window-rule-dialog.js';
import type { MenuActions } from './settings-menu.js';
import type { WheelActions, WheelSlotDraft } from './settings-wheel.js';
import type { ThemeActions } from './settings-theme.js';
import {
  draftOf,
  type CaptureKind,
  type EditableSetting,
  type RuleEditor,
} from './settings-model.js';
import { patchFor } from './settings-patch.js';
import { SettingsCaptureController } from './settings-capture-controller.js';
import { renderSettingsCards } from './settings-sections.js';
import '../../shared/icons/icon-picker.js';

const emptyTheme: ThemeState = {
  mode: 'dark',
  baseLight: false,
  accentHex: null,
  accentUsable: true,
  fill: '#c8401c',
  surface: '#f5e6e2',
  text: '#c8401c',
  defaultAccent: '#c8401c',
};

/** The settings page: general choices, theme, hotkeys, menu rules, wheel and storage. */
@customElement('wg-settings')
export class WgSettings extends LitElement {
  @state()
  settings: SettingsState | null = null;

  @state()
  theme: ThemeState = emptyTheme;

  @state()
  private error = '';

  @state()
  private ruleEditor: RuleEditor | null = null;

  @state()
  selectedSlot: { ring: string; index: number } | null = null;

  @state()
  slotDraft: WheelSlotDraft | null = null;

  @state()
  private iconPickerOpen = false;

  @state()
  private search = '';

  static styles = settingsPageCss;

  readonly capture = new SettingsCaptureController(this);

  protected readonly stringsController = new StoreController(this, strings);

  protected readonly themeController = new StoreController(this, themeStore, (theme) => {
    this.theme = theme;
  });

  connectedCallback(): void {
    super.connectedCallback();
    void this.load();
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
${renderSettingsCards(this)}
      </div>
      <wg-icon-picker
        .open=${this.iconPickerOpen}
        .selectedKind=${this.slotDraft?.icon?.kind ?? ''}
        .selectedValue=${this.slotDraft?.icon?.value ?? ''}
        .imagePicker=${() => this.pickSettingsImage()}
        @icon-picked=${(event: CustomEvent<{ kind: string; value: string }>) =>
          this.chooseSlotIcon(event.detail)}
        @icon-cancel=${() => { this.iconPickerOpen = false; }}></wg-icon-picker>
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

  menuActions(): MenuActions {
    return {
      beginCapture: () => void this.beginCapture('menu'),
      captureKey: (event) => this.capture.key('menu', event),
      capturePointer: (event) => this.capture.pointer('menu', event),
      setMiddle: () => void this.applyCapture('menu', 'Mouse Middle'),
      clear: () => void this.menuState('settings.menu.clear'),
      addRule: () => this.openRuleEditor('menu-add'),
      editRule: (key) => this.openRuleEditor('menu-edit', key),
      removeRule: (key) => void this.menuState('settings.menu.rule.remove', { key }),
      addPage: (key) => void this.menuState('settings.menu.rule.addPage', { key }),
      moveRule: (key, step) => void this.menuState('settings.menu.rule.move', { key, step }),
      moveRuleTo: (key, target) =>
        void this.menuState('settings.menu.rule.moveTo', { key, target }),
      setRememberPinned: (value) =>
        void this.menuState('settings.menu.rememberPin', { remember: value }),
    };
  }

  wheelActions(): WheelActions {
    return {
      update: (field, value) => void this.wheelState('settings.wheel.update', { field, value }),
      setLauncher: (value) => void this.wheelState('settings.wheel.launcher', { value }),
      setEnabled: (value) => void this.wheelState('settings.wheel.enabled', { value }),
      enableSystem: () => void this.wheelState('settings.wheel.enableSystem'),
      beginRepeatCapture: () => void this.beginCapture('wheelRepeat'),
      captureRepeatKey: (event) => this.capture.key('wheelRepeat', event),
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

  themeActions(): ThemeActions {
    return {
      setMode: (mode) => void this.applyTheme(mode),
      setBaseLight: (baseLight) => void this.applyTheme('custom', baseLight),
      setAccent: (accentHex) => void this.applyTheme('custom', undefined, accentHex),
      resetAccent: () => void this.applyTheme('custom', undefined,
        this.theme.defaultAccent ?? '#c8401c'),
    };
  }

  async beginCapture(kind: CaptureKind): Promise<void> {
    this.error = '';
    await this.capture.begin(kind);
  }

  async focusCapture(kind: CaptureKind): Promise<void> {
    const selector = kind === 'menu'
      ? '[data-menu-capture]'
      : kind === 'wheelRepeat'
        ? '[data-wheel-repeat]'
        : `input[data-hotkey="${kind}"]`;
    this.renderRoot.querySelector<HTMLElement>(selector)?.focus();
  }



  async applyCapture(kind: CaptureKind, binding: string): Promise<void> {
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

  async changeSetting(field: EditableSetting, event: Event): Promise<void> {
    const target = event.target as HTMLInputElement;
    const value = target.type === 'checkbox' ? String(target.checked) : target.value;
    await this.setSettingValue(field, value);
  }

  async setSettingValue(field: EditableSetting, value: string): Promise<void> {
    try {
      this.settings = await bridge.value.call<SettingsState>('settings.update',
        { patch: patchFor(field, value) });
      this.error = '';
    } catch (error) {
      this.error = String(error);
    }
  }

  async openFolder(folder: string): Promise<void> {
    try {
      await bridge.value.call('settings.openFolder', { folder });
    } catch (error) {
      this.error = String(error);
    }
  }

  private async menuState(method: BridgeMethod, params: Record<string, unknown> = {}): Promise<void> {
    try {
      this.settings = await bridge.value.call<SettingsState>(method, params);
      this.error = '';
    } catch (error) {
      this.error = String(error);
    }
  }

  private async wheelState(method: BridgeMethod, params: Record<string, unknown> = {}): Promise<void> {
    try {
      this.settings = await bridge.value.call<SettingsState>(method, params);
      this.error = '';
      this.refreshDraft();
    } catch (error) {
      this.error = String(error);
    }
  }

  private async sceneState(method: BridgeMethod, params: Record<string, unknown> = {}): Promise<void> {
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
    const method: BridgeMethod = editor.kind === 'menu-add'
      ? 'settings.menu.rule.add'
      : editor.kind === 'menu-edit'
        ? 'settings.menu.rule.edit'
        : editor.kind === 'wheel-add'
          ? 'settings.wheel.scene.add'
          : 'settings.wheel.scene.edit';
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

  private pickIcon(): void {
    if (this.slotDraft) {
      this.iconPickerOpen = true;
    }
  }

  private chooseSlotIcon(icon: { kind: string; value: string }): void {
    if (this.slotDraft && icon.value.length > 0) {
      this.slotDraft = {
        ...this.slotDraft,
        icon: { kind: icon.kind, value: icon.value },
      };
    }
    this.iconPickerOpen = false;
  }

  private async pickSettingsImage(): Promise<string | null> {
    const answer = await bridge.value.call<{ path: string | null }>('dialog.pickImage');
    return answer.path;
  }
}

declare global {
  interface HTMLElementTagNameMap {
    'wg-settings': WgSettings;
    'wg-window-rule': WgWindowRule;
  }
}