import { Store } from '../../../shared/store.js';
import { t } from '../../../shared/strings/strings.js';
import type { EditorCore } from './editor-core.js';
import type { EditorSessionService, SaveOutcome } from './session-service.js';

export type EditorDialogKind = 'document' | 'leave' | 'host';

export type EditorDialogChoice = 'save' | 'discard' | 'cancel';

/** The one question the editor can ask, whatever the reason it was opened. */
export interface EditorDialogState {
  open: boolean;
  kind: EditorDialogKind;
  documents: string[];
  details: string[];
  busy: boolean;
  error: string;
}

const CLOSED: EditorDialogState = {
  open: false,
  kind: 'leave',
  documents: [],
  details: [],
  busy: false,
  error: '',
};

/**
 * The only question about unsaved work. A dirty document's close, a page change and the host's
 * window close all open this dialog, and an answer already in progress is never replaced by a
 * second one: two callers wait for the same answer and the host gets one reply.
 */
export class EditorDialogService {
  private readonly store = new Store<EditorDialogState>({ ...CLOSED });

  private resolveAnswer: ((leave: boolean) => void) | null = null;

  private readonly closedWaiters = new Set<() => void>();

  private documentId: string | null = null;

  private leaveRun: Promise<boolean> | null = null;

  constructor(
    private readonly session: EditorSessionService,
    private readonly core: EditorCore,
  ) {}

  subscribe(listener: (state: EditorDialogState) => void): () => void {
    return this.store.subscribe(listener);
  }

  get(): EditorDialogState {
    return this.store.get();
  }

  /** The tab strip's close button: a clean document closes, a dirty one asks first. */
  closeDocument(documentId: string): Promise<void> {
    return this.requestClose(documentId);
  }

  /**
   * True when the page may change now; otherwise shows the question and waits for the answer.
   * Host documents come from the program when the host asks, because the page's tab strip may not
   * have caught up; they open the same dialog and the save/discard answers act on every document.
   */
  confirmLeave(hostDocuments?: readonly string[]): Promise<boolean> {
    if (this.leaveRun !== null) {
      return this.leaveRun;
    }

    this.leaveRun = this.runLeave(hostDocuments).finally(() => {
      this.leaveRun = null;
    });
    return this.leaveRun;
  }

  /** The dialog's three answers. A save a document refuses keeps the dialog up with its reason. */
  async answer(choice: EditorDialogChoice): Promise<void> {
    const state = this.get();
    if (!state.open || state.busy) {
      return;
    }

    if (choice === 'cancel') {
      this.finish(false);
      return;
    }

    this.patch({ busy: true, details: [], error: '' });
    if (state.kind === 'document') {
      await this.answerDocument(choice);
      return;
    }

    await this.answerLeave(choice);
  }

  cancel(): void {
    void this.answer('cancel');
  }

  private async requestClose(documentId: string): Promise<void> {
    if (this.get().open) {
      return;
    }

    const tab = this.core.state.tabs.find((candidate) => candidate.documentId === documentId);
    const snapshot = this.core.state.snapshot;
    const name = tab?.name
      ?? (snapshot !== null && snapshot.documentId === documentId ? snapshot.name : '');
    if (name.length === 0) {
      return;
    }

    if (tab?.dirty === true) {
      this.openDocument(documentId, name);
      return;
    }

    // The tab may claim to be clean while C# has moved on. The close command answers
    // Editor.Unsaved in that case, and the same question opens instead of an error banner.
    const outcome = await this.session.closeDocument(documentId);
    if (outcome.ok) {
      return;
    }

    if (outcome.unsaved) {
      this.openDocument(documentId, name);
      return;
    }

    this.core.setError(outcome.error);
  }

  private async runLeave(hostDocuments?: readonly string[]): Promise<boolean> {
    // One question at a time: a document close already on screen is answered first.
    while (this.get().open) {
      await this.whenClosed();
    }

    const given = (hostDocuments ?? []).filter((name) => name.trim().length > 0);
    const documents = given.length > 0 ? given : await this.session.unsavedNames();
    if (documents.length === 0) {
      return true;
    }

    const answer = this.show(given.length > 0 ? 'host' : 'leave', documents, null);
    return await answer;
  }

  private openDocument(documentId: string, name: string): void {
    void this.show('document', [name], documentId);
  }

  private show(kind: EditorDialogKind, documents: readonly string[],
    documentId: string | null): Promise<boolean> {
    const answer = new Promise<boolean>((resolve) => {
      this.resolveAnswer = resolve;
    });
    this.documentId = documentId;
    this.store.set({
      open: true,
      kind,
      documents: [...documents],
      details: [],
      busy: false,
      error: '',
    });
    return answer;
  }

  private async answerDocument(choice: EditorDialogChoice): Promise<void> {
    const documentId = this.documentId;
    if (documentId === null) {
      this.patch({ busy: false });
      return;
    }

    if (choice === 'save') {
      const saved = await this.session.saveDocument(documentId, 'dialog');
      if (!saved.ok) {
        this.showSaveFailure(saved);
        return;
      }

      const closed = await this.session.closeDocument(documentId);
      if (!closed.ok) {
        this.patch({
          busy: false,
          error: closed.unsaved
            ? t('Editor.UnsavedMessageNamed').replace('{0}', this.get().documents[0] ?? '')
            : closed.error,
        });
        return;
      }

      this.finish(true);
      return;
    }

    const closed = await this.session.closeDocument(documentId, true);
    if (!closed.ok) {
      this.patch({ busy: false, error: closed.error });
      return;
    }

    this.finish(true);
  }

  private async answerLeave(choice: EditorDialogChoice): Promise<void> {
    if (choice === 'save') {
      const saved = await this.session.saveAll('dialog');
      if (!saved.ok) {
        this.showSaveFailure(saved);
        return;
      }
    } else {
      const discarded = await this.session.discardAll();
      if (!discarded.ok) {
        this.patch({ busy: false, error: discarded.error });
        return;
      }
    }

    this.finish(true);
  }

  private showSaveFailure(outcome: SaveOutcome): void {
    this.patch({
      busy: false,
      details: [...outcome.details],
      error: outcome.error,
    });
  }

  private patch(change: Partial<EditorDialogState>): void {
    this.store.set({ ...this.get(), ...change });
  }

  private finish(leave: boolean): void {
    const resolve = this.resolveAnswer;
    this.resolveAnswer = null;
    this.documentId = null;
    this.store.set({ ...CLOSED });
    resolve?.(leave);
    for (const waiter of this.closedWaiters) {
      waiter();
    }

    this.closedWaiters.clear();
  }

  private whenClosed(): Promise<void> {
    if (!this.get().open) {
      return Promise.resolve();
    }

    return new Promise<void>((resolve) => {
      this.closedWaiters.add(resolve);
    });
  }
}