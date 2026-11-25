import type { EditorCore } from './editor-core.js';
import type {
  EditorDocumentSaveResult,
  EditorSaveResponse,
  EditorSnapshot,
} from '../../../shared/bridge/protocol.js';
import { bridgeErrorCode, describeError } from '../../../shared/errors.js';
import { t } from '../../../shared/strings/strings.js';
import type { EditorCatalogueService } from './catalogue-service.js';
import type { EditorValueService } from './value-service.js';

/** What one save command came to, and the per-document reasons the person still has to fix. */
export interface SaveOutcome {
  ok: boolean;
  error: string;
  details: string[];
}

/** What closing one document came to; a refused close is the same question, not an error banner. */
export interface CloseOutcome {
  ok: boolean;
  unsaved: boolean;
  error: string;
}

/** Where the failure should be read: the page's own error bar, or the dialog the save came from. */
export type SaveSurface = 'page' | 'dialog';

const SAVE_SUCCEEDED = new Set(['saved', 'unchanged']);

/** The document lifecycle. C# owns the documents; every command addresses one by stable id. */
export class EditorSessionService {
  private started = false;

  private startedLoaded = false;

  private ready: Promise<void> | null = null;

  private savedTimer = 0;

  constructor(
    private readonly core: EditorCore,
    private readonly strings: { subscribe(listener: () => void): () => void },
    private readonly value: EditorValueService,
    private readonly catalogue: EditorCatalogueService,
  ) {}

  /**
   * Loads the shared catalogue and the open document once. The editor page can ignore the
   * promise; the library's "insert in the editor" path awaits it, because it has to know whether
   * any document is open before it creates one.
   */
  ensureLoaded(): Promise<void> {
    if (!this.started) {
      this.started = true;
      this.strings.subscribe(() => {
        if (this.startedLoaded) {
          void this.core.reload();
        }
      });
      this.ready = this.start();
    }

    return this.ready ?? Promise.resolve();
  }

  refresh(): void {
    if (!this.startedLoaded) {
      return;
    }

    void this.core.reload();
  }

  private async start(): Promise<void> {
    await this.catalogue.ensureLoaded();
    await this.core.reload();
    this.startedLoaded = true;
    this.core.state.loading = false;
    this.core.notify();
  }

  /** Re-reads the catalogue after a publish added a public macro. */
  async refreshCatalogue(): Promise<void> {
    await this.catalogue.refresh();
  }

  async open(name: string): Promise<boolean> {
    await this.ensureLoaded();
    this.prepareDocumentChange();
    const ok = await this.core.snapshotCommand('editor.session', { kind: 'open', name });
    if (ok) {
      this.resetSelection('variables');
    }

    return ok;
  }

  async newDocument(): Promise<boolean> {
    await this.ensureLoaded();
    this.prepareDocumentChange();
    const ok = await this.core.snapshotCommand('editor.session', { kind: 'new' });
    if (ok) {
      this.resetSelection('macro');
    }

    return ok;
  }

  async openSubmacro(name: string, ownerDocumentId?: string): Promise<boolean> {
    await this.ensureLoaded();
    this.prepareDocumentChange();
    const command = ownerDocumentId === undefined
      ? { kind: 'subOpen', name }
      : { kind: 'subOpen', name, ownerDocumentId };
    const ok = await this.core.snapshotCommand('editor.session', command);
    if (ok) {
      this.resetSelection();
    }

    return ok;
  }

  async activateDocument(documentId: string): Promise<boolean> {
    await this.ensureLoaded();
    if (documentId === this.core.state.activeDocumentId) {
      return true;
    }

    this.prepareDocumentChange();
    const ok = await this.core.snapshotCommand('editor.session', {
      kind: 'activate', documentId,
    });
    if (ok) {
      this.resetSelection();
    }

    return ok;
  }

  /** Drops the drafts the page had open and stops a recording; the documents stay open. */
  async deactivate(): Promise<void> {
    if (!this.startedLoaded) {
      return;
    }

    try {
      const snapshot = await this.core.request<EditorSnapshot | null>('editor.session', {
        kind: 'deactivate',
      });
      this.core.applySnapshot(snapshot);
    } catch (error) {
      this.core.fail(error);
      return;
    }

    this.value.closeValue();
    this.core.notify();
  }

  /** The host's list of unsaved documents; the page's tab copy is only the fallback. */
  async unsavedNames(): Promise<string[]> {
    try {
      const names = await this.core.unsavedDocuments();
      return names.filter((name) => name.trim().length > 0);
    } catch {
      return this.dirtyNames();
    }
  }

  hasDirtyDocuments(): boolean {
    return this.core.state.tabs.some((tab) => tab.dirty);
  }

  dirtyNames(): string[] {
    return this.core.state.tabs.filter((tab) => tab.dirty).map((tab) => tab.name);
  }

  /** Saves the current document through the same command the shortcuts use. */
  async save(): Promise<boolean> {
    const outcome = await this.saveDocument(undefined, 'page');
    return outcome.ok;
  }

  /** Saves one document by its stable id, or the active one when no id is given. */
  async saveDocument(documentId?: string, surface: SaveSurface = 'page'): Promise<SaveOutcome> {
    const hasDocument = this.core.state.snapshot !== null;
    if (documentId === undefined && !hasDocument) {
      return { ok: false, error: t('Editor.NoDocument'), details: [] };
    }

    const activeId = this.core.state.activeDocumentId;
    if (hasDocument && (documentId === undefined || documentId === activeId)) {
      const committed = await this.commitDrafts();
      if (!committed) {
        return this.pageFailure(surface, this.core.state.error, []);
      }
    }

    try {
      const response = await this.core.request<EditorSaveResponse>('editor.save',
        documentId === undefined ? {} : { documentId });
      return this.applySaveResponse(response, surface);
    } catch (error) {
      return this.pageFailure(surface, describeError(error), []);
    }
  }

  /** Saves every dirty document once; children before their owners, as C# does. */
  async saveAll(surface: SaveSurface = 'page'): Promise<SaveOutcome> {
    if (this.core.state.snapshot !== null && !(await this.commitDrafts())) {
      return this.pageFailure(surface, this.core.state.error, []);
    }

    try {
      const response = await this.core.request<EditorSaveResponse>('editor.saveAll', {});
      return this.applySaveResponse(response, surface);
    } catch (error) {
      return this.pageFailure(surface, describeError(error), []);
    }
  }

  /** Closes the dirty documents without writing; the project keeps the version it has. */
  async discardAll(): Promise<{ ok: boolean; error: string }> {
    try {
      const snapshot = await this.core.request<EditorSnapshot | null>('editor.discardAll', {});
      this.core.applySnapshot(snapshot);
      this.core.notify();
      return { ok: true, error: '' };
    } catch (error) {
      this.core.fail(error);
      return { ok: false, error: describeError(error) };
    }
  }

  /** Closes one document; an unsaved one answers with the question rather than a banner. */
  async closeDocument(documentId: string, discard = false): Promise<CloseOutcome> {
    const command = discard
      ? { kind: 'close', documentId, discard: true }
      : { kind: 'close', documentId };
    try {
      const snapshot = await this.core.request<EditorSnapshot | null>('editor.session', command);
      this.core.applySnapshot(snapshot);
      this.core.notify();
      return { ok: true, unsaved: false, error: '' };
    } catch (error) {
      return {
        ok: false,
        unsaved: !discard && bridgeErrorCode(error) === 'Editor.Unsaved',
        error: describeError(error),
      };
    }
  }

  setDraft(field: 'name' | 'description' | 'group', value: string): void {
    if (field === 'name') {
      this.core.state.draftName = value;
    } else if (field === 'description') {
      this.core.state.draftDescription = value;
    } else {
      this.core.state.draftGroup = value;
    }

    this.core.notify();
  }

  async commitDrafts(): Promise<boolean> {
    const snapshot = this.core.state.snapshot;
    if (!snapshot) {
      return false;
    }

    if (this.core.state.draftName === snapshot.name
        && this.core.state.draftDescription === snapshot.description
        && this.core.state.draftGroup === snapshot.group) {
      return true;
    }

    return this.core.snapshotCommand('editor.session', {
      kind: 'patch',
      name: this.core.state.draftName,
      description: this.core.state.draftDescription,
      group: this.core.state.draftGroup,
    });
  }

  /** Copies this macro into the public library in the callable shape; C# owns the versioning. */
  async publishToLibrary(): Promise<boolean> {
    const snapshot = this.core.state.snapshot;
    if (!snapshot || snapshot.isSubMacro) {
      return false;
    }

    if (!snapshot.canSave) {
      this.core.setError(snapshot.nameProblem
        ?? (snapshot.declarationProblemSummary || t('Editor.Problem.CannotSave')));
      return false;
    }

    // A macro has to be in the project before the library can take a copy of it: a new document
    // that has never been saved has no project entry to publish. Saving here is the honest
    // meaning of "publish this macro", not a second, hidden save the person did not ask for.
    if (!(await this.save())) {
      return false;
    }

    const name = this.core.state.snapshot?.name;
    if (!name) {
      return false;
    }

    try {
      await this.core.request<unknown>('library.publish', { name });
      this.core.state.error = '';
      this.core.showNotice(t('Editor.Published').replace('{0}', name));
      await this.refreshCatalogue();
      this.core.notify();
      return true;
    } catch (error) {
      this.core.fail(error);
      return false;
    }
  }

  async run(): Promise<void> {
    const snapshot = this.core.state.snapshot;
    if (!snapshot || snapshot.isSubMacro || snapshot.steps.length === 0) {
      return;
    }

    if (!(await this.save())) {
      return;
    }

    try {
      await this.core.request('app.openRun', { name: snapshot.name });
    } catch (error) {
      this.core.fail(error);
    }
  }

  async setRecordOption(field: string, value: string): Promise<boolean> {
    return this.core.snapshotCommand('editor.record', { kind: 'option', field, value });
  }

  async setDelayScale(factor: number): Promise<boolean> {
    return this.core.snapshotCommand('editor.record', { kind: 'delayScale', factor });
  }

  async setMacroSetting(field: string, value: string): Promise<boolean> {
    return this.core.snapshotCommand('editor.session', { kind: 'macroSetting', field, value });
  }

  private prepareDocumentChange(): void {
    this.core.state.actionForm = null;
    this.core.state.actionError = '';
    this.value.closeValue();
  }

  private resetSelection(inspector?: 'variables' | 'macro'): void {
    this.core.state.selectedStepId = '';
    this.core.state.selectedStepIds = [];
    if (inspector) {
      this.core.state.inspectorTab = inspector;
    }

    this.core.notify();
  }

  private applySaveResponse(response: EditorSaveResponse, surface: SaveSurface): SaveOutcome {
    this.core.applySnapshot(response.snapshot);
    const failures = response.results.filter((result) => !SAVE_SUCCEEDED.has(result.outcome));
    const details = failures.map((result) => this.saveDetail(result));
    const ok = failures.length === 0;
    const error = ok ? '' : t('Editor.UnsavedBlockedTitle');

    if (surface === 'page') {
      if (ok) {
        this.core.state.error = '';
        this.markSaved();
      } else {
        this.core.state.error = [error, ...details].filter((line) => line.length > 0).join(' · ');
      }
    } else if (ok) {
      this.core.state.error = '';
    }

    this.core.notify();
    return { ok, error, details };
  }

  private saveDetail(result: EditorDocumentSaveResult): string {
    const problem = result.problem?.trim() ?? '';
    return problem.length > 0 ? result.name + ' · ' + problem : result.name;
  }

  private pageFailure(surface: SaveSurface,
    message: string, details: string[]): SaveOutcome {
    const error = message.trim().length > 0 ? message : t('Editor.Problem.CannotSave');
    if (surface === 'page') {
      this.core.state.error = [error, ...details].filter((line) => line.length > 0).join(' · ');
      this.core.notify();
    }

    return { ok: false, error, details };
  }

  /** A successful save is a short, self-clearing mark; the page's notice is left alone. */
  private markSaved(): void {
    this.core.state.saved = true;
    window.clearTimeout(this.savedTimer);
    this.savedTimer = window.setTimeout(() => {
      this.core.state.saved = false;
      this.core.notify();
    }, 2500);
  }
}