import type { BridgeApi } from '../../shared/bridge/client.js';
import { bridgeApi } from '../../shared/bridge/bridge.js';
import type { EditorSnapshot, ProjectState } from '../../shared/bridge/protocol.js';
import { strings } from '../../shared/strings/strings.js';
import { EditorCatalogueService } from './services/catalogue-service.js';
import { EditorCore } from './services/editor-core.js';
import { EditorDialogService } from './services/dialog-service.js';
import { EditorSessionService } from './services/session-service.js';
import { EditorActionService } from './services/action-service.js';
import { EditorStepService } from './services/step-service.js';
import { EditorValueService } from './services/value-service.js';
import { EditorTriggersService } from './services/triggers-service.js';
import { EditorDeclarationsService } from './services/declarations-service.js';
import { EditorVariablesService } from './services/variables-service.js';
import { EditorRecordingService } from './services/recording-service.js';
import { EditorUiService } from './services/ui-service.js';
import type { EditorUiState } from './state/types.js';

export interface EditorStrings {
  subscribe(listener: () => void): () => void;
}

export interface EditorProject {
  subscribe(listener: (state: ProjectState | null) => void): () => void;
}

export interface EditorDeps {
  client?: BridgeApi;
  strings?: EditorStrings;
  project?: EditorProject;
  /** Answers the host's leave question; the shell wires it to editor.leaveAnswered. */
  answerHost?: (requestId: number, leave: boolean) => void;
}

/**
 * The editor's composition root. Every command family lives behind a namespace; this class only
 * wires the shared state box and the lifecycle calls the shell needs.
 */
export class EditorStore {
  private readonly core: EditorCore;

  private readonly answerHost: (requestId: number, leave: boolean) => void;

  private hostRequestId: number | null = null;

  readonly catalogue: EditorCatalogueService;

  readonly session: EditorSessionService;

  readonly dialog: EditorDialogService;

  readonly action: EditorActionService;

  readonly steps: EditorStepService;

  readonly value: EditorValueService;

  readonly triggers: EditorTriggersService;

  readonly declarations: EditorDeclarationsService;

  readonly variables: EditorVariablesService;

  readonly recording: EditorRecordingService;

  readonly ui: EditorUiService;

  constructor(deps: EditorDeps = {}) {
    this.core = new EditorCore(deps.client ?? bridgeApi);
    this.value = new EditorValueService(this.core);
    this.catalogue = new EditorCatalogueService(this.core, deps.strings ?? strings);
    this.session = new EditorSessionService(
      this.core,
      deps.strings ?? strings,
      this.value,
      this.catalogue,
    );
    this.dialog = new EditorDialogService(this.session, this.core);
    this.answerHost = deps.answerHost ?? ((requestId, leave) => {
      void bridgeApi.call('editor.leaveAnswered', { requestId, leave });
    });
    this.action = new EditorActionService(this.core);
    this.steps = new EditorStepService(this.core);
    this.triggers = new EditorTriggersService(this.core);
    this.declarations = new EditorDeclarationsService(this.core);
    this.variables = new EditorVariablesService(this.core);
    this.recording = new EditorRecordingService(this.core);
    this.ui = new EditorUiService(this.core);
  }

  get(): EditorUiState {
    return this.core.get();
  }

  subscribe(listener: () => void): () => void {
    return this.core.subscribe(listener);
  }

  ensureLoaded(): Promise<void> {
    return this.session.ensureLoaded();
  }

  refresh(): void {
    this.session.refresh();
  }

  /**
   * The host asked whether its window may close with these documents unsaved. The same dialog is
   * used, and the answer goes back once for the one request.
   */
  answerLeaveAsked(requestId: number, documents: readonly string[]): void {
    this.hostRequestId = requestId;
    void this.dialog.confirmLeave(documents).then((leave) => {
      this.answerHost(requestId, leave);
      if (this.hostRequestId === requestId) {
        this.hostRequestId = null;
      }
    });
  }

  /** The host stopped waiting for one question; close it if that is the one on screen. */
  cancelLeaveAsked(requestId: number): void {
    if (this.hostRequestId !== requestId) {
      return;
    }

    this.hostRequestId = null;
    this.dialog.cancel();
  }

  /** A snapshot the host pushed because a project replacement closed documents, not a command. */
  applyHostSnapshot(snapshot: EditorSnapshot | null): void {
    this.core.applySnapshot(snapshot);
    this.core.notify();
  }

  /** Leaving the route must not keep a draft, a value window, a recorder or a question alive. */
  leaveEditor(): void {
    this.dialog.cancel();
    this.core.state.actionForm = null;
    this.core.state.actionError = '';
    this.core.clearDocumentTransient();
    this.value.closeValue();
    if (this.core.state.recording) {
      void this.recording.stopRecording();
    }

    this.recording.clearTimer();
  }

  clearError(): void {
    this.core.clearError();
  }

  setError(message: string): void {
    this.core.setError(message);
  }
}

export const editor = new EditorStore();

export type { EditorUiState, InspectorTab } from './state/types.js';