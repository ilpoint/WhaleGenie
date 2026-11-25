import type { EditorCore } from './editor-core.js';
import type { EditorCatalogue } from '../../../shared/bridge/protocol.js';
import { describeError } from '../../../shared/errors.js';

export interface CatalogueStrings {
  subscribe(listener: () => void): () => void;
}

/**
 * The tables every editor document shares: the action library and the value, variable, condition
 * and trigger vocabularies. C# builds them from the same descriptions the native windows use, so
 * the page loads them once, again when the language changes, and after a publish adds a macro.
 */
export class EditorCatalogueService {
  private ready: Promise<void> | null = null;

  private loaded = false;

  private lastError = '';

  constructor(
    private readonly core: EditorCore,
    private readonly strings: CatalogueStrings,
  ) {}

  /** Loads once for this page; later calls reuse the same first answer. */
  ensureLoaded(): Promise<void> {
    if (this.ready === null) {
      this.ready = this.load();
      this.strings.subscribe(() => {
        if (this.loaded) {
          void this.reload();
        }
      });
    }

    return this.ready;
  }

  /** Re-reads the catalogue after a publish or a language change. */
  async refresh(): Promise<void> {
    if (this.ready === null) {
      await this.ensureLoaded();
      return;
    }

    await this.reload();
  }

  private async reload(): Promise<void> {
    await this.ready;
    await this.load();
  }

  private async load(): Promise<void> {
    try {
      const catalogue = await this.core.request<EditorCatalogue>('editor.sessionActions');
      this.core.state.catalogue = catalogue;
      this.core.state.actions = catalogue.actions;
      this.core.state.categories = catalogue.categories;
      this.core.state.publicMacros = catalogue.publicMacros;
      if (this.core.state.selectedActionKey.length > 0
          && !catalogue.actions.some((action) => action.key === this.core.state.selectedActionKey)) {
        this.core.state.selectedActionKey = '';
      }

      if (this.lastError.length > 0 && this.core.state.error === this.lastError) {
        this.core.state.error = '';
      }

      this.lastError = '';
      this.loaded = true;
    } catch (error) {
      this.lastError = describeError(error);
      this.core.state.error = this.lastError;
    }

    this.core.notify();
  }
}