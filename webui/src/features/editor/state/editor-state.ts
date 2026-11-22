import type { EditorSnapshot } from '../../../shared/bridge/protocol.js';
import { normalizeSelection } from '../editor-model.js';
import type { EditorUiState } from './types.js';

export function createEditorState(): EditorUiState {
  return {
    snapshot: null,
    tabs: [],
    activeKey: '',
    actions: [],
    categories: [],
    publicMacros: [],
    selectedStepId: '',
    selectedStepIds: [],
    selectedActionKey: '',
    collapsedSteps: new Set<string>(),
    paletteSource: 'basic',
    category: null,
    search: '',
    inspectorTab: 'variables',
    // The shell opens 1280 wide; the column is a nuisance below 1000 and the toolbar can
    // bring it back. The choice is kept for the rest of the session once the person toggles it.
    inspectorVisible: typeof window === 'undefined' ? true : window.innerWidth >= 1000,
    inspectorWidth: 360,
    recording: false,
    captured: 0,
    error: '',
    notice: '',
    saved: false,
    closeRequest: null,
    loading: true,
    draftName: '',
    draftDescription: '',
    draftGroup: '',
    valueEditor: null,
    valueError: '',
    stepEditorOpen: false,
    actionForm: null,
    actionPage: 'base',
    actionError: '',
    trial: null,
  };
}

/** Applies a session snapshot, keeping the selection valid against the new tree. */
export function applySnapshotToState(state: EditorUiState, snapshot: EditorSnapshot | null): void {
  state.snapshot = snapshot;
  if (snapshot === null) {
    state.selectedStepId = '';
    state.selectedStepIds = [];
    state.draftName = '';
    state.draftDescription = '';
    state.draftGroup = '';
    return;
  }

  state.draftName = snapshot.name;
  state.draftDescription = snapshot.description;
  state.draftGroup = snapshot.group;
  const selection = normalizeSelection(snapshot, state.selectedStepId, state.selectedStepIds);
  state.selectedStepId = selection.selectedStepId;
  state.selectedStepIds = selection.selectedStepIds;
}
