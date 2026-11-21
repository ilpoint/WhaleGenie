import { LitElement, css, html, nothing } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import { bridge } from '../../shared/bridge/bridge.js';
import type { SettingsState } from '../../shared/bridge/protocol.js';
import { strings, t } from '../../shared/strings/strings.js';

/** The general settings the page owns; hotkeys, menus and wheel editors stay native for now. */
@customElement('wg-settings')
export class WgSettings extends LitElement {
  @state()
  private settings: SettingsState | null = null;

  @state()
  private error = '';

  @state()
  private capturing: 'systemHotkey' | 'recordStopKey' | '' = '';

  private unsubscribe?: () => void;

  static styles = css`
    :host {
      display: block;
      padding: var(--spacingVerticalL) var(--spacingHorizontalXXL);
      max-width: 980px;
    }

    h1 {
      margin: 0 0 var(--spacingVerticalL);
      font-size: var(--font-size-base-500);
      font-weight: var(--font-weight-semibold);
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

    .card h2 {
      margin: 0;
      font-size: var(--font-size-base-400);
    }

    .row {
      display: grid;
      grid-template-columns: minmax(0, 1fr) 180px;
      gap: var(--spacingHorizontalM);
      align-items: center;
    }

    .row label {
      color: var(--colorNeutralForeground1);
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

    .hotkey {
      display: grid;
      grid-template-columns: minmax(0, 1fr) minmax(0, 220px) auto auto;
      gap: var(--spacingHorizontalS);
      align-items: center;
    }

    .row input[type="number"],
    .row select {
      width: 100%;
      font: inherit;
    }

    .error {
      color: var(--colorPaletteRedForeground1);
    }
  `;

  connectedCallback(): void {
    super.connectedCallback();
    this.unsubscribe = strings.subscribe(() => this.requestUpdate());
    void this.load();
  }

  disconnectedCallback(): void {
    this.unsubscribe?.();
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
          <h2>${t('Settings.GeneralCard')}</h2>
          <div class="row">
            <label>${t('Settings.Language')}</label>
            <select .value=${this.settings.language}
              @change=${(event: Event) => void this.changeSetting('language', event)}>
              <option value="zh-Hans">${t('Settings.Language.Chinese')}</option>
              <option value="en">${t('Settings.Language.English')}</option>
            </select>
          </div>
          <div class="row">
            <label>${t('Settings.Theme')}</label>
            <select .value=${this.settings.theme}
              @change=${(event: Event) => void this.changeSetting('theme', event)}>
              <option value="Dark">${t('Theme.Mode.Dark')}</option>
              <option value="Light">${t('Theme.Mode.Light')}</option>
              <option value="Custom">${t('Theme.Mode.Custom')}</option>
            </select>
          </div>
          ${this.toggle('loadLastProjectAtStart', 'Settings.LoadLastProject')}
          ${this.toggle('systemAtStart', 'Settings.SystemAtStart')}
          ${this.toggle('enabledFirst', 'Settings.EnabledFirst')}
        </section>
        <section class="card">
          <h2>${t('Settings.Hotkeys')}</h2>
          ${this.hotkey('systemHotkey', 'Settings.SystemHotkey', this.settings.systemHotkey,
            this.settings.systemHotkeyProblem)}
          ${this.hotkey('recordStopKey', 'Settings.RecordStopKey', this.settings.recordStopKey,
            this.settings.recordStopProblem)}
        </section>
        <section class="card">
          <h2>${t('Settings.Sounds')}</h2>
          ${this.toggle('soundSystem', 'Settings.SoundSystem')}
          ${this.toggle('soundMacro', 'Settings.SoundMacro')}
        </section>
        <section class="card">
          <h2>${t('Settings.Reading')}</h2>
          ${this.toggle('preloadOcr', 'Settings.PreloadOcr')}
        </section>
        <section class="card">
          <h2>${t('Settings.FramesCard')}</h2>
          ${this.toggle('frameRateFollow', 'Settings.FrameRateFollow')}
          ${this.number('frameRate', 'Settings.FrameRate', 1, this.settings.frameRateMax,
            this.settings.frameRateFollow)}
          ${this.toggle('frameCache', 'Settings.FrameCache')}
          ${this.number('frameCacheAge', 'Settings.FrameCacheAge', 0, 60000,
            !this.settings.frameCache)}
        </section>
        <section class="card">
          <h2>${t('Settings.InputCard')}</h2>
          ${this.toggle('ignoreOwnInput', 'Settings.IgnoreOwnInput')}
          ${this.number('inputRate', 'Settings.InputLimit', 0, 999, false)}
          ${this.number('logCount', 'Settings.LogCount', 1, 1000, false)}
        </section>
        <section class="card">
          <h2>${t('Settings.Storage')}</h2>
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

  private async openFolder(folder: string): Promise<void> {
    try {
      await bridge.value.call('settings.openFolder', { folder });
    } catch (error) {
      this.error = String(error);
    }
  }

  private async load(): Promise<void> {
    try {
      this.settings = await bridge.value.call<SettingsState>('settings.state');
    } catch (error) {
      this.error = String(error);
    }
  }

  private hotkey(
    field: 'systemHotkey' | 'recordStopKey',
    label: string,
    value: string,
    problem: string,
  ) {
    return html`
      <div class="hotkey">
        <label>${t(label)}</label>
        <input readonly data-hotkey=${field} .value=${value}
          @keydown=${(event: KeyboardEvent) => void this.captureHotkey(field, event)} />
        <fluent-button appearance="subtle" @click=${() => void this.startCapture(field)}>
          ${t(this.capturing === field ? 'Settings.HotkeyPress' : 'Settings.HotkeySet')}
        </fluent-button>
        <fluent-button appearance="subtle" @click=${() => void this.setSettingValue(field, '')}>
          ${t('Settings.HotkeyClear')}
        </fluent-button>
      </div>
      ${problem ? html`<p class="error">${problem}</p>` : nothing}
    `;
  }

  private async startCapture(field: 'systemHotkey' | 'recordStopKey'): Promise<void> {
    this.capturing = field;
    await this.updateComplete;
    this.renderRoot
      .querySelector<HTMLInputElement>(`input[data-hotkey="${field}"]`)
      ?.focus();
  }

  private async captureHotkey(
    field: 'systemHotkey' | 'recordStopKey',
    event: KeyboardEvent,
  ): Promise<void> {
    if (this.capturing !== field) {
      return;
    }

    if (['Control', 'Shift', 'Alt', 'Meta'].includes(event.key)) {
      return;
    }

    event.preventDefault();
    event.stopPropagation();
    let key = event.key === ' ' ? 'Space' : event.key;
    if (key.length === 1) {
      key = key.toUpperCase();
    }

    const parts: string[] = [];
    if (event.ctrlKey) {
      parts.push('Ctrl');
    }
    if (event.altKey) {
      parts.push('Alt');
    }
    if (event.shiftKey) {
      parts.push('Shift');
    }
    if (event.metaKey) {
      parts.push('Win');
    }
    parts.push(key);
    this.capturing = '';
    await this.setSettingValue(field, parts.join('+'));
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
}

declare global {
  interface HTMLElementTagNameMap {
    'wg-settings': WgSettings;
  }
}
