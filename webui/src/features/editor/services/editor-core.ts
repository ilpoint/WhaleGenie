import type { BridgeApi } from '../../../shared/bridge/client.js';
import type {
  BridgeMethod,
  EditorActionCommand,
  EditorActionForm,
  EditorSnapshot,
  EditorTab,
} from '../../../shared/bridge/protocol.js';
import { describeError } from '../../../shared/errors.js';
import { applySnapshotToState, createEditorState } from '../state/editor-state.js';
import type { EditorUiState } from '../state/types.js';

/**
 * The one mutable state box plus the bridge calls every command family shares. Services own the
 * commands; this class owns the snapshot, listeners, error text, and the generic request helpers.
 */
export class EditorCore {
  readonly state: EditorUiState;

  private readonly listeners = new Set<() => void>();

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

  async mutate<T>(method: BridgeMethod, params: unknown, apply: (value: T) => void): Promise<boolean> {
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
      const snapshot = await this.client.call<EditorSnapshot | null>('editor.sessionState');
      if (snapshot) {
        this.applySnapshot(snapshot);
      } else {
        const tabs = await this.client.call<{ activeKey: string | null; tabs: EditorTab[] }>(
          'editor.sessionTabs');
        if (tabs.activeKey) {
          const activated = await this.client.call<EditorSnapshot>('editor.session', {
            kind: 'activate', key: tabs.activeKey,
          });
          this.applySnapshot(activated);
        } else {
          this.applySnapshot(null);
        }
      }
    } catch (error) {
      this.state.error = describeError(error);
    }

    this.notify();
  }
}
