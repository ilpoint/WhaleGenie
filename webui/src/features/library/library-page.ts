import { LitElement, css, html, nothing } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import { bridge } from '../../shared/bridge/bridge.js';
import type { LibraryEntry, LibraryState } from '../../shared/bridge/protocol.js';
import { go } from '../../shared/navigation/router.js';
import { icon } from '../../shared/icons.js';
import { strings, t } from '../../shared/strings/strings.js';
import { StoreController } from '../../shared/lit/store-controller.js';
import { usePublicMacro } from '../editor/index.js';
import { startLibraryImport, transfer } from '../transfer/index.js';

/**
 * The public macro library. Entries are callable macros with no trigger of their own; the page
 * manages them and hands one to the editor, where it becomes a sub-macro of the document in
 * front. It never turns an entry into a project macro: that is the editor's call step.
 */
@customElement('wg-library')
export class WgLibrary extends LitElement {
  @state()
  private entries: LibraryEntry[] = [];

  @state()
  private error = '';

  @state()
  private busy = '';

  protected readonly stringsController = new StoreController(this, strings);

  static styles = css`
    :host {
      display: block;
      padding: var(--spacingVerticalL) var(--spacingHorizontalXXL);
      max-width: 900px;
    }

    h1 {
      margin: 0 0 var(--spacingVerticalS);
      font-size: var(--fontSizeBase500);
      font-weight: var(--fontWeightSemibold);
    }

    .intro {
      color: var(--colorNeutralForeground2);
      margin: 0 0 var(--spacingVerticalL);
      line-height: 1.5;
    }

    .tools {
      display: flex;
      gap: var(--spacingHorizontalS);
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
      font-weight: var(--fontWeightSemibold);
    }

    .description,
    .version {
      color: var(--colorNeutralForeground2);
      font-size: var(--fontSizeBase200);
    }

    .entry-actions {
      display: flex;
      gap: var(--spacingHorizontalXS);
      align-items: center;
    }

    .empty,
    .error {
      color: var(--colorNeutralForeground2);
    }

    .error {
      color: var(--colorPaletteRedForeground1);
    }

    .empty {
      display: grid;
      justify-items: start;
      gap: var(--spacingVerticalS);
    }
  `;

  private unsubscribeLibrary: (() => void) | undefined;

  connectedCallback(): void {
    super.connectedCallback();
    this.unsubscribeLibrary = bridge.value.on('library.changed', (state) => this.apply(state));
    void this.load();
  }

  disconnectedCallback(): void {
    this.unsubscribeLibrary?.();
    this.unsubscribeLibrary = undefined;
    super.disconnectedCallback();
  }

  render() {
    return html`
      <h1>${t('Library.Heading')}</h1>
      <p class="intro">${t('Library.Intro')}</p>
      <div class="tools">
        <fluent-button appearance="accent" ?disabled=${this.busy.length > 0}
          @click=${() => void this.createEntry()}>${t('Library.New')}</fluent-button>
        <fluent-button appearance="subtle" ?disabled=${this.busy.length > 0}
          @click=${() => void this.importPackage()}>${t('Library.Import')}</fluent-button>
      </div>
      ${this.error ? html`<p class="error">${this.error}</p>` : nothing}
      ${this.entries.length === 0
        ? html`<div class="empty">
            <p>${t('Library.Empty')}</p>
            <fluent-button appearance="subtle" @click=${() => void go('macros')}>
              ${t('Library.OpenMacros')}
            </fluent-button>
          </div>`
        : html`<div class="cards">
            ${this.entries.map((entry) => html`
              <div class="card">
                <div>
                  <div class="name">${entry.name}</div>
                  <div class="description">${entry.description}</div>
                  <div class="version">v${entry.version}</div>
                </div>
                <div class="entry-actions">
                  <fluent-button appearance="accent"
                    ?disabled=${this.busy.length > 0}
                    @click=${() => void this.use(entry.id, entry.version)}>
                    ${t('Library.Use')}
                  </fluent-button>
                  <fluent-button appearance="subtle"
                    title=${t('Library.Delete')}
                    aria-label=${t('Library.Delete')}
                    ?disabled=${this.busy.length > 0}
                    @click=${() => void this.removeEntry(entry.id)}>
                    ${icon('delete')}
                  </fluent-button>
                </div>
              </div>
            `)}
          </div>`}
    `;
  }

  private async use(id: string, version: number): Promise<void> {
    this.busy = id;
    this.error = '';
    try {
      if (!(await usePublicMacro(id, version, 'local'))) {
        this.error = t('Library.UseFailed');
      }
    } catch (error) {
      this.error = String(error);
    } finally {
      this.busy = '';
    }
  }

  private async removeEntry(id: string): Promise<void> {
    this.busy = id;
    this.error = '';
    try {
      const state = await bridge.value.call<LibraryState>('library.delete', { id });
      this.apply(state);
    } catch (error) {
      this.error = String(error);
    } finally {
      this.busy = '';
    }
  }

  private async createEntry(): Promise<void> {
    this.busy = 'new';
    this.error = '';
    try {
      const state = await bridge.value.call<LibraryState>('library.create', {});
      this.apply(state);
    } catch (error) {
      this.error = String(error);
    } finally {
      this.busy = '';
    }
  }

  private async importPackage(): Promise<void> {
    this.busy = '';
    this.error = '';
    try {
      const session = await startLibraryImport(bridge.value);
      if (session !== null) {
        transfer.show(session);
      }
    } catch (error) {
      this.error = String(error);
    }
  }

  private async load(): Promise<void> {
    try {
      this.apply(await bridge.value.call<LibraryState>('library.state'));
    } catch (error) {
      this.error = String(error);
    }
  }

  private apply(state: LibraryState): void {
    this.entries = state.entries;
    this.error = state.error ?? '';
  }
}

declare global {
  interface HTMLElementTagNameMap {
    'wg-library': WgLibrary;
  }
}
