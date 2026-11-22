import type { BridgeApi } from '../../shared/bridge/client.js';
import { bridgeApi } from '../../shared/bridge/bridge.js';
import type { ProjectState } from '../../shared/bridge/protocol.js';
import { project } from '../../shared/project/store.js';
import { strings } from '../../shared/strings/strings.js';
import { EditorCore } from './services/editor-core.js';
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
}

/**
 * The editor's composition root. Every command family lives behind a namespace; this class only
 * wires the shared state box and the lifecycle calls the shell needs.
 */
export class EditorStore {
  private readonly core: EditorCore;

  readonly session: EditorSessionService;

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
    this.session = new EditorSessionService(
      this.core,
      deps.strings ?? strings,
      deps.project ?? project,
      this.value,
    );
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

  ensureLoaded(): void {
    this.session.ensureLoaded();
  }

  refresh(): void {
    this.session.refresh();
  }

  /** Leaving the route must not keep a draft, a value window, or a recorder alive. */
  leaveEditor(): void {
    this.core.state.actionForm = null;
    this.core.state.actionError = '';
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
