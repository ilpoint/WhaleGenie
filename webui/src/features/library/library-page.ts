import { LitElement, css, html, nothing } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import { bridge } from '../../shared/bridge/bridge.js';
import type { LibraryEntry } from '../../shared/bridge/protocol.js';
import { icon } from '../../shared/icons.js';
import { project } from '../../shared/project/store.js';
import { strings, t } from '../../shared/strings/strings.js';

/** The public macro library: what every project may copy, and the way to remove an entry. */
@customElement('wg-library')
export class WgLibrary extends LitElement {
  @state()
  private entries: LibraryEntry[] = [];

  @state()
  private error = '';

  @state()
  private macros: string[] = [];

  @state()
  private selectedMacro = '';

  private unsubscribe?: () => void;

  private unsubscribeProject?: () => void;

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

    .source {
      display: flex;
      gap: var(--spacingHorizontalS);
      align-items: center;
      margin-bottom: var(--spacingVerticalM);
    }

    .source select {
      min-width: 220px;
      font: inherit;
      padding: var(--spacingVerticalSN) var(--spacingHorizontalM);
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke1);
      border-radius: var(--borderRadiusMedium);
      background: var(--colorNeutralBackground1);
      color: var(--colorNeutralForeground1);
    }

    .entry-actions {
      display: flex;
      gap: var(--spacingHorizontalXS);
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
    this.unsubscribeProject = project.subscribe((state) => {
      this.macros = state?.macros.map((macro) => macro.name) ?? [];
      if (this.selectedMacro.length === 0) {
        this.selectedMacro = this.macros[0] ?? '';
      }
    });
    void this.load();
  }

  disconnectedCallback(): void {
    this.unsubscribe?.();
    this.unsubscribeProject?.();
    super.disconnectedCallback();
  }

  render() {
    return html`
      <h1>${t('Library.Heading')}</h1>
      <p class="intro">${t('Library.Intro')}</p>
      <div class="source">
        <select
          .value=${this.selectedMacro}
          @change=${(event: Event) => { this.selectedMacro = (event.target as HTMLSelectElement).value; }}
        >
          ${this.macros.map((name) => html`<option value=${name}>${name}</option>`)}
        </select>
        <fluent-button appearance="accent" @click=${() => void this.addFromProject()}>
          ${t('Library.AddFromProject')}
        </fluent-button>
      </div>
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
                <div class="entry-actions">
                  <fluent-button
                    appearance="subtle"
                    title=${t('Library.ReplaceFromProject')}
                    aria-label=${t('Library.ReplaceFromProject')}
                    @click=${() => void this.replaceFromProject(entry.id)}
                  >${icon('edit')}</fluent-button>
                  <fluent-button
                    appearance="subtle"
                    title=${t('Library.Delete')}
                    aria-label=${t('Library.Delete')}
                    @click=${() => void this.deleteEntry(entry.id)}
                  >${icon('delete')}</fluent-button>
                </div>
              </div>
            `)}
          </div>`}
    `;
  }

  private async addFromProject(): Promise<void> {
    if (!this.selectedMacro) {
      return;
    }

    await this.change('library.add', { name: this.selectedMacro });
  }

  private async replaceFromProject(id: string): Promise<void> {
    if (!this.selectedMacro) {
      return;
    }

    await this.change('library.replace', { id, name: this.selectedMacro });
  }

  private async change(method: string, params: Record<string, unknown>): Promise<void> {
    try {
      const state = await bridge.value.call<{ entries: LibraryEntry[]; error: string | null }>(
        method, params);
      this.entries = state.entries;
      this.error = state.error ?? '';
    } catch (error) {
      this.error = String(error);
    }
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
