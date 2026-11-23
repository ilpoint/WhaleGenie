import { html, nothing, type TemplateResult } from 'lit';
import type { CaptureKind, EditableSetting } from './settings-model.js';
import type { SettingsState, ThemeState } from '../../shared/bridge/protocol.js';
import { t } from '../../shared/strings/strings.js';
import type { SettingsCaptureController } from './settings-capture-controller.js';
import { renderMenu, type MenuActions } from './settings-menu.js';
import { renderTheme, type ThemeActions } from './settings-theme.js';
import {
  renderWheelActions,
  renderWheelSettings,
  type WheelActions,
  type WheelSlotDraft,
} from './settings-wheel.js';

export interface SettingsSectionsHost {
  settings: SettingsState | null;
  theme: ThemeState;
  capture: SettingsCaptureController;
  selectedSlot: { ring: string; index: number } | null;
  slotDraft: WheelSlotDraft | null;
  changeSetting(field: EditableSetting, event: Event): Promise<void>;
  openFolder(folder: string): Promise<void>;
  beginCapture(kind: CaptureKind): Promise<void>;
  setSettingValue(field: EditableSetting, value: string): Promise<void>;
  restartElevated(): Promise<void>;
  themeActions(): ThemeActions;
  menuActions(): MenuActions;
  wheelActions(): WheelActions;
}

/** Every settings card below the page header. Kept out of the page so the layout file stays small. */
export function renderSettingsCards(host: SettingsSectionsHost): TemplateResult | typeof nothing {
  const settings = host.settings;
  if (!settings) {
    return nothing;
  }

  return html`
      <div class="cards">
        <section class="card" data-settings-section="general">
          <div class="card-head">
            <h2>${t('Settings.GeneralCard')}</h2>
            <span class="note">${t('Settings.GeneralCardNote')}</span>
          </div>
          <div class="row">
            <label>${t('Settings.Language')}</label>
            <select @change=${(event: Event) => void host.changeSetting('language', event)}>
              <option value="zh-Hans" ?selected=${settings.language === 'zh-Hans'}>
                ${t('Settings.Language.Chinese')}
              </option>
              <option value="en" ?selected=${settings.language === 'en'}>
                ${t('Settings.Language.English')}
              </option>
            </select>
          </div>
          ${toggle(host, 'loadLastProjectAtStart', 'Settings.LoadLastProject')}
          ${toggle(host, 'systemAtStart', 'Settings.SystemAtStart')}
          ${toggle(host, 'enabledFirst', 'Settings.EnabledFirst')}
        </section>

        ${renderTheme(host.theme, host.themeActions())}

        <section class="card" data-settings-section="input">
          <div class="card-head">
            <h2>${t('Settings.Hotkeys')}</h2>
            <span class="note">${t('Settings.HotkeysNote')}</span>
          </div>
          ${hotkey(host, 'systemHotkey', 'Settings.SystemHotkey')}
          ${hotkey(host, 'recordStopKey', 'Settings.RecordStopKey')}
        </section>

        ${renderMenu(settings.menu, host.capture.active === 'menu', host.menuActions())}

        ${renderWheelSettings(settings.wheel, host.capture.active === 'wheelRepeat',
          host.wheelActions())}
        ${renderWheelActions(settings.wheel, host.selectedSlot, host.slotDraft,
          host.wheelActions())}

        <section class="card" data-settings-section="input">
          <div class="card-head">
            <h2>${t('Settings.Sounds')}</h2>
            <span class="note">${t('Settings.SoundsNote')}</span>
          </div>
          ${toggle(host, 'soundSystem', 'Settings.SoundSystem')}
          ${toggle(host, 'soundMacro', 'Settings.SoundMacro')}
        </section>

        <section class="card" data-settings-section="input">
          <div class="card-head">
            <h2>${t('Settings.Reading')}</h2>
            <span class="note">${t('Settings.ReadingNote')}</span>
          </div>
          ${toggle(host, 'preloadOcr', 'Settings.PreloadOcr')}
        </section>

        <section class="card" data-settings-section="input">
          <div class="card-head">
            <h2>${t('Settings.FramesCard')}</h2>
            <span class="note">${t('Settings.FramesCardNote')}</span>
          </div>
          ${toggle(host, 'frameRateFollow', 'Settings.FrameRateFollow')}
          ${number(host, 'frameRate', 'Settings.FrameRate', 1, settings.frameRateMax,
            settings.frameRateFollow)}
          <p class="status">${settings.frameRateNote}</p>
          ${toggle(host, 'frameCache', 'Settings.FrameCache')}
          ${number(host, 'frameCacheAge', 'Settings.FrameCacheAge', 0,
            settings.limits.frameCacheAgeMax, !settings.frameCache)}
        </section>

        <section class="card" data-settings-section="input">
          <div class="card-head">
            <h2>${t('Settings.InputCard')}</h2>
            <span class="note">${t('Settings.InputCardNote')}</span>
          </div>
          ${toggle(host, 'ignoreOwnInput', 'Settings.IgnoreOwnInput')}
          ${number(host, 'inputRate', 'Settings.InputLimit',
            settings.limits.inputRateMin, settings.limits.inputRateMax, false)}
          ${number(host, 'logCount', 'Settings.LogCount',
            settings.limits.logCountMin, settings.limits.logCountMax, false)}
        </section>

        <section class="card" data-settings-section="input">
          <div class="card-head">
            <h2>${t('Settings.Driver')}</h2>
            <span class="note">${t('Settings.DriverNote')}</span>
          </div>
          <p class="status">${settings.driverStatus}</p>
          ${toggle(host, 'attachDriverAtStart', 'Settings.DriverAttachAtStart')}
          ${toggle(host, 'runAsAdmin', 'Settings.RunAsAdmin')}
          <p class="status">${settings.adminStatus}</p>
          ${settings.canRestartElevated ? html`
            <p class="status">${settings.restartNote}</p>
            <fluent-button appearance="subtle" @click=${() => void host.restartElevated()}>
              ${t('Settings.AdminRestart')}
            </fluent-button>
          ` : nothing}
        </section>

        <section class="card" data-settings-section="input">
          <div class="card-head">
            <h2>${t('Settings.CaptureCard')}</h2>
          </div>
          <div class="row">
            <label>${t('Settings.CaptureWindow')}</label>
            <select @change=${(event: Event) => void host.changeSetting('windowWay', event)}>
              ${settings.windowWays.map((way) => html`
                <option value=${way.value} ?selected=${way.value === settings!.windowWay}>
                  ${way.label}
                </option>
              `)}
            </select>
          </div>
          <div class="row">
            <label>${t('Settings.CaptureScreen')}</label>
            <select @change=${(event: Event) => void host.changeSetting('screenWay', event)}>
              ${settings.screenWays.map((way) => html`
                <option value=${way.value} ?selected=${way.value === settings!.screenWay}>
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
          ${settings.storage.map((row) => html`
            <div class="storage-row">
              <div>
                <strong>${row.name}</strong>
                <div>${row.what}</div>
                <div class="where">${row.where}</div>
              </div>
              <fluent-button appearance="subtle" @click=${() => void host.openFolder(row.folder)}>
                ${t('Settings.OpenFolder')}
              </fluent-button>
            </div>
          `)}
        </section>
      </div>
  `;
}

function toggle(host: SettingsSectionsHost, field: EditableSetting, label: string): TemplateResult {
    return html`
      <div class="row">
        <label>${t(label)}</label>
        <input type="checkbox" .checked=${Boolean(host.settings![field])}
          @change=${(event: Event) => void host.changeSetting(field, event)} />
      </div>
    `;
  }

function number(host: SettingsSectionsHost, field: EditableSetting, label: string, min: number, max: number,
  disabled: boolean): TemplateResult {
    return html`
      <div class="row">
        <label>${t(label)}</label>
        <input type="number" min=${min} max=${max} ?disabled=${disabled}
          .value=${String(host.settings![field])}
          @change=${(event: Event) => void host.changeSetting(field, event)} />
      </div>
    `;
  }

function hotkey(host: SettingsSectionsHost, field: 'systemHotkey' | 'recordStopKey', label: string): TemplateResult {
    const value = host.settings![field];
    const problem = field === 'systemHotkey'
      ? host.settings!.systemHotkeyProblem
      : host.settings!.recordStopProblem;
    return html`
      <div class="hotkey hotkey-row">
        <label>${t(label)}</label>
        <input readonly data-hotkey=${field} .value=${value}
          @keydown=${(event: KeyboardEvent) => host.capture.key(field, event)} />
        <fluent-button appearance="subtle" @click=${() => void host.beginCapture(field)}>
          ${t(host.capture.active === field ? 'Settings.HotkeyPress' : 'Settings.HotkeySet')}
        </fluent-button>
        <fluent-button appearance="subtle" @click=${() => void host.setSettingValue(field, '')}>
          ${t('Settings.HotkeyClear')}
        </fluent-button>
      </div>
      ${problem ? html`<p class="error">${problem}</p>` : nothing}
    `;
  }
