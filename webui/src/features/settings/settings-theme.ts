import { html, nothing } from 'lit';
import type { ThemeState } from '../../shared/bridge/protocol.js';
import { PROJECT_ACCENT } from '../../shared/theme/brand.js';
import { t } from '../../shared/strings/strings.js';

export interface ThemeActions {
  setMode(mode: string): void;
  setBaseLight(value: boolean): void;
  setAccent(value: string): void;
  resetAccent(): void;
}

export function renderTheme(theme: ThemeState, actions: ThemeActions) {
  const custom = theme.mode === 'custom';
  const fill = theme.fill ?? theme.accentHex ?? theme.defaultAccent ?? PROJECT_ACCENT;
  const surface = theme.surface ?? 'transparent';
  const text = theme.text ?? fill;
  return html`
    <section class="card">
      <div class="card-head">
        <h2>${t('Settings.Theme')}</h2>
        <span class="note">${t('Settings.ColorsNote')}</span>
      </div>
      <p class="hint">${t('Settings.ThemeHint')}</p>
      <div class="settings-grid compact">
        <label class="field">
          <span>${t('Settings.Theme')}</span>
          <select
            @change=${(event: Event) => actions.setMode(
              (event.target as HTMLSelectElement).value)}>
            <option value="dark" ?selected=${theme.mode === 'dark'}>${t('Theme.Mode.Dark')}</option>
            <option value="light" ?selected=${theme.mode === 'light'}>${t('Theme.Mode.Light')}</option>
            <option value="custom" ?selected=${theme.mode === 'custom'}>${t('Theme.Mode.Custom')}</option>
          </select>
        </label>
        ${custom
          ? html`
            <label class="check-row">
              <input type="checkbox" .checked=${theme.baseLight}
                @change=${(event: Event) => actions.setBaseLight(
                  (event.target as HTMLInputElement).checked)} />
              <span>${t('Theme.Base.Light')}</span>
            </label>
            <label class="field">
              <span>${t('Theme.Accent')}</span>
              <span class="accent-row">
                <input type="color" .value=${theme.accentHex ?? PROJECT_ACCENT}
                  @change=${(event: Event) => actions.setAccent(
                    (event.target as HTMLInputElement).value)} />
                <code class="binding">${theme.accentHex ?? ''}</code>
              </span>
            </label>
            <div class="field">
              <span>${t('Settings.CustomizeTheme')}</span>
              <div class="actions">
                <fluent-button appearance="subtle" @click=${actions.resetAccent}>
                  ${t('Theme.ResetAccent')}
                </fluent-button>
              </div>
            </div>
          `
          : nothing}
      </div>
      ${custom
        ? html`
          <p class="hint">${t('Theme.Accent.Hint')}</p>
          <p class="hint">${t('Theme.Palette.Hint')}</p>
          ${theme.accentUsable === false
            ? html`<p class="error">${t('Theme.Accent.Rejected')}</p>`
            : nothing}
          <div class="theme-previews">
            <div class="preview" style="background: ${fill}; color: var(--colorNeutralForegroundOnBrand)">
              ${t('Theme.Preview.Primary')}
            </div>
            <div class="preview" style="background: ${surface}">
              ${t('Theme.Preview.Selected')}
            </div>
            <div class="preview" style="color: ${text}">
              ${t('Theme.Preview.AccentText')}
            </div>
          </div>
        `
        : html`<p class="hint">${t('Theme.Base.Hint')}</p>`}
    </section>
  `;
}