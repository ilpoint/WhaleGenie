import type { BridgeApi } from '../../../shared/bridge/client.js';
import type {
  BridgeMethod,
  EditorActionCommand,
  EditorActionForm,
  EditorSnapshot,
  EditorUnsavedDocumentsSnapshot,
} from '../../../shared/bridge/protocol.js';
import { describeError } from '../../../shared/errors.js';
import { applySnapshotToState, createEditorState } from '../state/editor-state.js';
import type { EditorUiState } from '../state/types.js';

/**
 * The one mutable state box plus the bridge calls every command family shares. Services own the
 * commands; this class owns the snapshot, listeners, error text, and the generic request helpers.
 *
 * Every request runs through one serial queue. A command's answer and the snapshot it carries
 * then arrive in the order the person made the edits, so a slow earlier answer cannot overwrite
 * a later tree; a failed command settles and lets the ones behind it run.
 */
export class EditorCore {
  readonly state: EditorUiState;

  private readonly listeners = new Set<() => void>();

  private noticeTimer = 0;

  private tail: Promise<void> = Promise.resolve();

  constructor(readonly client: BridgeApi, state: EditorUiState = createEditorState()) {
    this.state = state;
  }

  get(): EditorUiState {
    return this.state;
  }

  subscribe(listener: () => void): () => void {
    this.listeners.add(listener);
    listener();
    return () => this.listeners.delete(listener);
  }

  notify(): void {
    for (const listener of this.listeners) {
      listener();
    }
  }

  clearError(): void {
    if (this.state.error.length > 0) {
      this.state.error = '';
      this.notify();
    }
  }

  setError(message: string): void {
    this.state.error = message;
    this.notify();
  }

  fail(error: unknown): void {
    this.state.error = describeError(error);
    this.notify();
  }

  /** A success message belongs to the action that made it; it goes away on its own. */
  showNotice(text: string): void {
    this.clearNoticeTimer();
    this.state.notice = text;
    const shown = text;
    this.noticeTimer = window.setTimeout(() => {
      if (this.state.notice === shown) {
        this.state.notice = '';
        this.notify();
      }
    }, 2500);
    this.notify();
  }

  clearNotice(): void {
    this.clearNoticeTimer();
    if (this.state.notice.length > 0) {
      this.state.notice = '';
      this.notify();
    }
  }

  /** Drops the feedback of the document the page is leaving; the dialog owns its own state. */
  clearDocumentTransient(): void {
    this.clearNoticeTimer();
    this.state.notice = '';
    this.state.error = '';
    this.state.saved = false;
    this.notify();
  }

  private clearNoticeTimer(): void {
    if (this.noticeTimer !== 0) {
      window.clearTimeout(this.noticeTimer);
      this.noticeTimer = 0;
    }
  }

  /** Appends one bridge request to the queue and returns its answer. */
  serial<T>(work: () => Promise<T>): Promise<T> {
    const result = this.tail.then(work, work);
    this.tail = result.then(() => undefined, () => undefined);
    return result;
  }

  /** A bridge call that belongs in the same order as the state-changing commands. */
  request<T>(method: BridgeMethod, params?: unknown): Promise<T> {
    return this.serial(() => this.client.call<T>(method, params));
  }

  async mutate<T>(method: BridgeMethod, params: unknown, apply: (value: T) => void): Promise<boolean> {
    return this.serial(async () => {
      try {
        const value = await this.client.call<T>(method, params);
        apply(value);
        this.state.error = '';
        this.notify();
        return true;
      } catch (error) {
        this.fail(error);
        return false;
      }
    });
  }

  async snapshotCommand(method: BridgeMethod, params?: unknown): Promise<boolean> {
    return this.mutate<EditorSnapshot | null>(method, params,
      (value) => this.applySnapshot(value));
  }

  async actionCommand(method: BridgeMethod, params: Record<string, unknown>): Promise<boolean> {
    return this.formCall(method, params);
  }

  /** One draft edit through the action command union, answered with the whole form. */
  async actionEdit(command: EditorActionCommand): Promise<boolean> {
    return this.formCall('editor.action', command);
  }

  private async formCall(method: BridgeMethod, params: unknown): Promise<boolean> {
    return this.serial(async () => {
      try {
        const form = await this.client.call<EditorActionForm>(method, params);
        this.state.actionForm = form.open ? form : null;
        this.state.actionPage = form.page;
        this.state.actionError = '';
        this.notify();
        return true;
      } catch (error) {
        this.state.actionError = describeError(error);
        this.notify();
        return false;
      }
    });
  }

  applySnapshot(snapshot: EditorSnapshot | null): void {
    if (snapshot && this.state.snapshot
        && snapshot.revision <= this.state.snapshot.revision) {
      return;
    }

    applySnapshotToState(this.state, snapshot);
  }

  async reload(): Promise<void> {
    try {
      const snapshot = await this.request<EditorSnapshot | null>('editor.sessionState');
      this.applySnapshot(snapshot);
    } catch (error) {
      this.state.error = describeError(error);
    }

    this.notify();
  }

  /** The host's list of documents with unsaved changes; read-only and ordered with commands. */
  async unsavedDocuments(): Promise<string[]> {
    const answer = await this.request<EditorUnsavedDocumentsSnapshot>('editor.unsavedDocuments');
    return answer.names;
  }
}