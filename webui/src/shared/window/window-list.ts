import { LitElement, css, html, nothing } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import { bridge } from '../bridge/bridge.js';
import type { WindowEntryState, WindowRulePickerState } from '../bridge/protocol.js';
import { t } from '../strings/strings.js';

/** The windows whose title, program or class holds what was typed, in the order the desktop gave. */
export function filterWindows(windows: WindowEntryState[], filter: string): WindowEntryState[] {
  const wanted = filter.trim().toLowerCase();
  if (!wanted) {
    return windows;
  }

  return windows.filter((window) =>
    window.title.toLowerCase().includes(wanted)
    || window.process.toLowerCase().includes(wanted)
    || window.className.toLowerCase().includes(wanted));
}

/**
 * The short list of windows that are open right now, for a field that names a window by its title.
 * It reads the same bridge list the rule form reads and hands the chosen title back; the field stays
 * editable, because a title that changes with the document usually has to be shortened by hand.
 */
@customElement('wg-window-list')
export class WgWindowList extends LitElement {
  @state()
  private windows: WindowEntryState[] = [];

  @state()
  private filter = '';

  @state()
  private chosen = 0;

  @state()
  private error = '';

  @state()
  private busy = false;

  static styles = css`
    :host { display: block; position: fixed; inset: 0; z-index: 80; }
    .backdrop { position: fixed; inset: 0; display: grid; place-items: center; padding: var(--spacingVerticalXL); background: var(--shell-scrim); }
    .dialog { width: min(560px, 100%); max-height: min(620px, 100%); display: grid; grid-template-rows: auto auto 1fr auto; gap: var(--spacingVerticalM); padding: var(--spacingVerticalL); border: var(--strokeWidthThin) solid var(--colorNeutralStroke1); border-radius: var(--borderRadiusLarge); background: var(--colorNeutralBackground1); box-shadow: var(--shadow16); }
    h2 { margin: 0; font-size: var(--fontSizeBase400); }
    input { font: inherit; width: 100%; }
    .windows { overflow: auto; min-height: 160px; display: grid; align-content: start; gap: var(--spacingVerticalXS); }
    .window { display: grid; gap: 2px; width: 100%; text-align: left; padding: var(--spacingVerticalS) var(--spacingHorizontalS); border: var(--strokeWidthThin) solid transparent; border-radius: var(--borderRadiusMedium); background: transparent; color: inherit; font: inherit; cursor: default; }
    .window:hover { background: var(--colorNeutralBackground1Hover); }
    .window[data-selected='true'] { border-color: var(--colorBrandStroke1); background: var(--colorBrandBackground2); }
    .window strong { font-size: var(--fontSizeBase300); font-weight: var(--fontWeightSemibold); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .window span { color: var(--colorNeutralForeground3); font-size: var(--fontSizeBase200); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .hint { margin: 0; color: var(--colorNeutralForeground3); font-size: var(--fontSizeBase200); }
    .error { margin: 0; color: var(--colorPaletteRedForeground1); }
    .actions { display: flex; justify-content: flex-end; gap: var(--spacingHorizontalS); }
  `;

  connectedCallback(): void {
    super.connectedCallback();
    void this.load();
  }

  render() {
    const shown = filterWindows(this.windows, this.filter);
    return html`
      <div class="backdrop" @pointerdown=${this.onBackdrop}>
        <div class="dialog" role="dialog" aria-modal="true" aria-label=${t('WindowPicker.Title')}>
          <h2>${t('WindowPicker.Title')}</h2>
          <input type="search" .value=${this.filter}
            placeholder=${t('WindowPicker.Filter')}
            aria-label=${t('WindowPicker.Filter')}
            @input=${(event: Event) => {
              this.filter = (event.target as HTMLInputElement).value;
            }} />
          <div class="windows">
            ${shown.map((window) => html`
              <button type="button" class="window" data-handle=${String(window.handle)}
                data-selected=${String(window.handle === this.chosen)}
                @click=${() => { this.chosen = window.handle; }}>
                <strong>${window.title || t('WindowPick.Untitled')}</strong>
                <span>${window.summary}</span>
              </button>`)}
            ${shown.length === 0
              ? html`<p class="hint">${t(this.windows.length === 0
                ? 'WindowPicker.Empty'
                : 'WindowPicker.None')}</p>`
              : nothing}
          </div>
          ${this.error ? html`<p class="error">${this.error}</p>` : nothing}
          <div class="actions">
            <fluent-button appearance="subtle" @click=${this.cancel}>${t('Common.Cancel')}</fluent-button>
            <fluent-button appearance="accent" ?disabled=${this.busy || this.chosenWindow() === null}
              @click=${this.confirm}>${t('Common.Ok')}</fluent-button>
          </div>
        </div>
      </div>
    `;
  }

  private chosenWindow(): WindowEntryState | null {
    return this.windows.find((window) => window.handle === this.chosen) ?? null;
  }

  private async load(): Promise<void> {
    this.busy = true;
    try {
      const state = await bridge.value.call<WindowRulePickerState>('window.list');
      this.windows = state.windows;
      this.chosen = state.windows[0]?.handle ?? 0;
      this.error = '';
    } catch (error) {
      this.error = String(error);
    } finally {
      this.busy = false;
    }
  }

  private confirm = (): void => {
    const window = this.chosenWindow();
    if (!window) {
      return;
    }

    this.dispatchEvent(new CustomEvent<WindowEntryState>('wg-window-list-choose', {
      detail: window,
      bubbles: true,
      composed: true,
    }));
  };

  private cancel = (): void => {
    this.dispatchEvent(new CustomEvent('wg-window-list-cancel', { bubbles: true, composed: true }));
  };

  private onBackdrop = (event: PointerEvent): void => {
    if (event.target === event.currentTarget) {
      this.cancel();
    }
  };
}

declare global {
  interface HTMLElementTagNameMap {
    'wg-window-list': WgWindowList;
  }
}
