import { LitElement, css, html, nothing } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import { bridge } from '../../shared/bridge/bridge.js';
import type {
  SettingsChoice,
  WindowEntryState,
  WindowRulePickerState,
  WindowRuleState,
} from '../../shared/bridge/protocol.js';
import { t } from '../../shared/strings/strings.js';

export function emptyRule(): WindowRuleState {
  return {
    title: '', process: '', className: '', handle: 0,
    how: 'titleContains', caseSensitive: false, trim: true, multiline: false,
  };
}

/**
 * The window rule form shared by the menu's lower region and a wheel scene. It lists the windows
 * that are open, or takes one from the desktop through the native overlay; the page owns the form
 * and only the pointing needs a window of its own.
 */
@customElement('wg-window-rule')
export class WgWindowRule extends LitElement {
  @property({ attribute: false })
  public rule: WindowRuleState = emptyRule();

  @property()
  public title = '';

  @state()
  private windows: WindowEntryState[] = [];

  @state()
  private hows: SettingsChoice[] = [];

  @state()
  private filter = '';

  @state()
  private selectedHandle = 0;

  @state()
  private error = '';

  @state()
  private busy = false;

  static styles = css`
    :host {
      display: block;
      position: fixed;
      inset: 0;
      z-index: 40;
    }

    .backdrop {
      position: fixed;
      inset: 0;
      z-index: 40;
      display: grid;
      place-items: center;
      padding: var(--spacingVerticalXL);
      background: rgba(0, 0, 0, 0.35);
    }

    .dialog {
      width: min(760px, 100%);
      max-height: min(640px, 100%);
      display: grid;
      grid-template-rows: auto 1fr auto;
      gap: var(--spacingVerticalM);
      padding: var(--spacingVerticalL);
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke1);
      border-radius: var(--borderRadiusLarge);
      background: var(--colorNeutralBackground1);
      box-shadow: var(--shadow16);
    }

    h2 {
      margin: 0;
      font-size: var(--font-size-base-400);
    }

    .hint {
      margin: 0;
      color: var(--colorNeutralForeground3);
      font-size: var(--font-size-base-200);
    }

    .body {
      display: grid;
      grid-template-columns: minmax(220px, 0.9fr) minmax(280px, 1.1fr);
      gap: var(--spacingHorizontalL);
      min-height: 0;
    }

    .panel {
      display: grid;
      gap: var(--spacingVerticalS);
      min-height: 0;
    }

    .windows {
      overflow: auto;
      min-height: 0;
      display: grid;
      align-content: start;
      gap: var(--spacingVerticalXS);
    }

    .window {
      display: grid;
      gap: 2px;
      width: 100%;
      text-align: left;
      padding: var(--spacingVerticalS) var(--spacingHorizontalS);
      border: var(--strokeWidthThin) solid transparent;
      border-radius: var(--borderRadiusMedium);
      background: transparent;
      color: inherit;
      font: inherit;
      cursor: default;
    }

    .window:hover {
      background: var(--colorNeutralBackground1Hover);
    }

    .window[data-selected='true'] {
      border-color: var(--colorBrandStroke1);
      background: var(--colorBrandBackground2);
    }

    .window strong {
      font-size: var(--font-size-base-300);
      font-weight: var(--font-weight-semibold);
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .window span {
      color: var(--colorNeutralForeground3);
      font-size: var(--font-size-base-200);
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .row {
      display: grid;
      grid-template-columns: 130px minmax(0, 1fr);
      align-items: center;
      gap: var(--spacingHorizontalM);
    }

    .row label {
      color: var(--colorNeutralForeground2);
    }

    .checks {
      display: flex;
      flex-wrap: wrap;
      gap: var(--spacingHorizontalM);
    }

    .actions {
      display: flex;
      flex-wrap: wrap;
      justify-content: flex-end;
      gap: var(--spacingHorizontalS);
    }

    .actions .left {
      margin-right: auto;
    }

    .error {
      color: var(--colorPaletteRedForeground1);
      margin: 0;
    }

    input,
    select {
      font: inherit;
      width: 100%;
    }
  `;

  connectedCallback(): void {
    super.connectedCallback();
    this.rule = { ...this.rule };
    this.selectedHandle = this.rule.handle;
    void this.loadWindows();
  }

  render() {
    return html`
      <div class="backdrop" @pointerdown=${this.onBackdrop}>
        <div class="dialog" role="dialog" aria-modal="true" aria-label=${this.title}>
          <div>
            <h2>${this.title}</h2>
            <p class="hint">${t('WindowRule.CaptureHint')}</p>
          </div>
          <div class="body">
            <section class="panel">
              <label>
                <span class="hint">${t('WindowRule.Pick')}</span>
                <input type="search" .value=${this.filter} placeholder=${t('WindowRule.Search')}
                  @input=${(event: Event) => { this.filter = (event.target as HTMLInputElement).value; }} />
              </label>
              <div class="windows">
                ${this.filteredWindows().map((window) => html`
                  <button type="button" class="window"
                    data-selected=${String(window.handle === this.selectedHandle)}
                    @click=${() => this.chooseWindow(window)}>
                    <strong>${window.title || t('WindowPick.Untitled')}</strong>
                    <span>${window.summary}</span>
                  </button>
                `)}
                ${this.filteredWindows().length === 0
                  ? html`<p class="hint">${t('WindowRule.NoWindows')}</p>`
                  : nothing}
              </div>
              <div class="actions">
                <fluent-button class="left" appearance="subtle" ?disabled=${this.busy}
                  @click=${() => void this.pickWindow()}>
                  ${t('WindowRule.PickWindow')}
                </fluent-button>
                <fluent-button appearance="subtle" ?disabled=${this.busy}
                  @click=${() => void this.loadWindows()}>
                  ${t('WindowRule.Refresh')}
                </fluent-button>
              </div>
            </section>
            <section class="panel">
              <div class="row">
                <label>${t('WindowRule.How')}</label>
                <select
                  @change=${(event: Event) => { this.rule = {
                    ...this.rule, how: (event.target as HTMLSelectElement).value }; }}>
                  ${this.hows.map((how) => html`
                    <option value=${how.value} ?selected=${how.value === this.rule.how}>
                      ${how.label}
                    </option>
                  `)}
                </select>
              </div>
              <div class="row">
                <label>${t('WindowRule.MatchText')}</label>
                ${this.rule.how === 'handle'
                  ? html`<input type="number" min="1" .value=${String(this.rule.handle || '')}
                      @input=${(event: Event) => { this.rule = {
                        ...this.rule,
                        handle: Number((event.target as HTMLInputElement).value) || 0 }; }} />`
                  : html`<input .value=${this.matchValue()}
                      @input=${(event: Event) => this.setMatchValue(
                        (event.target as HTMLInputElement).value)} />`}
              </div>
              <div class="checks">
                <label><input type="checkbox" .checked=${this.rule.caseSensitive}
                  @change=${(event: Event) => { this.rule = {
                    ...this.rule, caseSensitive: (event.target as HTMLInputElement).checked }; }} />
                  ${t('WindowRule.CaseSensitive')}</label>
                <label><input type="checkbox" .checked=${this.rule.trim}
                  @change=${(event: Event) => { this.rule = {
                    ...this.rule, trim: (event.target as HTMLInputElement).checked }; }} />
                  ${t('WindowRule.Trim')}</label>
                <label><input type="checkbox" .checked=${this.rule.multiline}
                  @change=${(event: Event) => { this.rule = {
                    ...this.rule, multiline: (event.target as HTMLInputElement).checked }; }} />
                  ${t('WindowRule.Multiline')}</label>
              </div>
              <p class="hint">${t(this.rule.how === 'handle'
                ? 'WindowRule.HandleHint'
                : 'WindowRule.FieldHint')}</p>
              ${this.error ? html`<p class="error">${this.error}</p>` : nothing}
            </section>
          </div>
          <div class="actions">
            <fluent-button class="left" appearance="subtle" @click=${this.cancel}>
              ${t('WindowRule.Cancel')}
            </fluent-button>
            <fluent-button appearance="accent" ?disabled=${this.busy} @click=${this.use}>
              ${t('WindowRule.Use')}
            </fluent-button>
          </div>
        </div>
      </div>
    `;
  }

  private filteredWindows(): WindowEntryState[] {
    const wanted = this.filter.trim().toLowerCase();
    if (!wanted) {
      return this.windows;
    }
    return this.windows.filter((window) =>
      window.title.toLowerCase().includes(wanted)
      || window.process.toLowerCase().includes(wanted)
      || window.className.toLowerCase().includes(wanted));
  }

  private matchValue(): string {
    switch (this.rule.how) {
      case 'process':
      case 'processEquals':
        return this.rule.process;
      case 'class':
      case 'classEquals':
        return this.rule.className;
      case 'handle':
        return String(this.rule.handle);
      default:
        return this.rule.title;
    }
  }

  private setMatchValue(value: string): void {
    switch (this.rule.how) {
      case 'process':
      case 'processEquals':
        this.rule = { ...this.rule, process: value };
        break;
      case 'class':
      case 'classEquals':
        this.rule = { ...this.rule, className: value };
        break;
      case 'handle':
        this.rule = { ...this.rule, handle: Number(value) || 0 };
        break;
      default:
        this.rule = { ...this.rule, title: value };
        break;
    }
  }

  private chooseWindow(window: WindowEntryState): void {
    this.selectedHandle = window.handle;
    this.rule = {
      ...this.rule,
      title: window.title,
      process: window.process,
      className: window.className,
      handle: window.handle,
    };
  }

  private async loadWindows(): Promise<void> {
    this.busy = true;
    try {
      const state = await bridge.value.call<WindowRulePickerState>('settings.windows.list');
      this.windows = state.windows;
      this.hows = state.hows;
      this.error = '';
    } catch (error) {
      this.error = String(error);
    } finally {
      this.busy = false;
    }
  }

  private async pickWindow(): Promise<void> {
    this.busy = true;
    try {
      const window = await bridge.value.call<WindowEntryState | null>('settings.windows.pick');
      if (window) {
        this.chooseWindow(window);
        this.windows = [window, ...this.windows.filter((entry) => entry.handle !== window.handle)];
      }
      this.error = '';
    } catch (error) {
      this.error = String(error);
    } finally {
      this.busy = false;
    }
  }

  private use = (): void => {
    if (this.rule.how === 'handle') {
      if (this.rule.handle === 0) {
        this.error = t('WindowRule.BadHandle');
        return;
      }
    } else if (!this.matchValue().trim()) {
      this.error = t('Settings.WindowRuleRequired');
      return;
    }

    this.dispatchEvent(new CustomEvent<WindowRuleState>('wg-rule-submit', {
      detail: { ...this.rule },
      bubbles: true,
      composed: true,
    }));
  };

  private cancel = (): void => {
    this.dispatchEvent(new CustomEvent('wg-rule-close', { bubbles: true, composed: true }));
  };

  private onBackdrop = (event: PointerEvent): void => {
    if (event.target === event.currentTarget) {
      this.cancel();
    }
  };
}

declare global {
  interface HTMLElementTagNameMap {
    'wg-window-rule': WgWindowRule;
  }
}