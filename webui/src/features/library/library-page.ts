import { LitElement, css, html, nothing } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import { bridge } from '../../shared/bridge/bridge.js';
import type { LibraryEntry } from '../../shared/bridge/protocol.js';
import { icon } from '../../shared/icons.js';
import { strings, t } from '../../shared/strings/strings.js';

/** The public macro library: what every project may copy, and the way to remove an entry. */
@customElement('wg-library')
export class WgLibrary extends LitElement {
  @state()
  private entries: LibraryEntry[] = [];

  @state()
  private error = '';

  private unsubscribe?: () => void;

  static styles = css`
    :host {
      display: block;
      padding: var(--spacingVerticalL) var(--spacingHorizontalXXL);
      max-width: 900px;
    }

    h1 {
      margin: 0 0 var(--spacingVerticalS);
      font-size: var(--font-size-base-500);
      font-weight: var(--font-weight-semibold);
    }

    .intro {
      color: var(--colorNeutralForeground2);
      margin: 0 0 var(--spacingVerticalL);
    }

    .cards {
      display: grid;
      gap: var(--spacingVerticalS);
    }

    .card {
      display: grid;
      grid-template-columns: 1fr auto;
      gap: var(--spacingHorizontalM);
      align-items: center;
      padding: var(--spacingVerticalM) var(--spacingHorizontalM);
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
      border-radius: var(--borderRadiusLarge);
      background: var(--colorNeutralBackground1);
    }

    .name {
      font-weight: var(--font-weight-semibold);
    }

    .description,
    .version {
      color: var(--colorNeutralForeground2);
      font-size: var(--font-size-base-200);
    }

    .empty,
    .error {
      color: var(--colorNeutralForeground2);
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
    return html`
      <h1>${t('Library.Heading')}</h1>
      <p class="intro">${t('Library.Intro')}</p>
      ${this.error ? html`<p class="error">${this.error}</p>` : nothing}
      ${this.entries.length === 0
        ? html`<p class="empty">${t('Library.Empty')}</p>`
        : html`<div class="cards">
            ${this.entries.map((entry) => html`
              <div class="card">
                <div>
                  <div class="name">${entry.name}</div>
                  <div class="description">${entry.description}</div>
                  <div class="version">v${entry.version}</div>
                </div>
                <fluent-button
                  appearance="subtle"
                  title=${t('Library.Delete')}
                  aria-label=${t('Library.Delete')}
                  @click=${() => void this.deleteEntry(entry.id)}
                >${icon('delete')}</fluent-button>
              </div>
            `)}
          </div>`}
    `;
  }

  private async load(): Promise<void> {
    try {
      const state = await bridge.value.call<{ entries: LibraryEntry[]; error: string | null }>(
        'library.state');
      this.entries = state.entries;
      this.error = state.error ?? '';
    } catch (error) {
      this.error = String(error);
    }
  }

  private async deleteEntry(id: string): Promise<void> {
    try {
      const state = await bridge.value.call<{ entries: LibraryEntry[]; error: string | null }>(
        'library.delete', { id });
      this.entries = state.entries;
      this.error = state.error ?? '';
    } catch (error) {
      this.error = String(error);
    }
  }
}

declare global {
  interface HTMLElementTagNameMap {
    'wg-library': WgLibrary;
  }
}
