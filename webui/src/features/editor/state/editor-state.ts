import type { EditorSnapshot } from '../../../shared/bridge/protocol.js';
import { normalizeSelection } from '../editor-model.js';
import type { EditorUiState } from './types.js';

export function createEditorState(): EditorUiState {
  return {
    snapshot: null,
    tabs: [],
    activeDocumentId: '',
    actions: [],
    categories: [],
    publicMacros: [],
    catalogue: null,
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

/**
 * Applies a session snapshot, keeping the selection valid against the new tree. A document-level
 * message belongs to the document that produced it: an empty editor or another tab must not keep
 * showing the last one's notice, error or saved mark. The close question lives in the dialog
 * service, so switching documents does not touch it.
 */
export function applySnapshotToState(state: EditorUiState, snapshot: EditorSnapshot | null): void {
  const previousDocumentId = state.activeDocumentId;
  state.snapshot = snapshot;
  if (snapshot === null) {
    state.selectedStepId = '';
    state.selectedStepIds = [];
    state.draftName = '';
    state.draftDescription = '';
    state.draftGroup = '';
    state.tabs = [];
    state.activeDocumentId = '';
    state.actionForm = null;
    state.actionError = '';
    state.notice = '';
    state.error = '';
    state.saved = false;
    state.valueEditor = null;
    state.valueError = '';
    state.stepEditorOpen = false;
    state.recording = false;
    state.captured = 0;
    state.trial = null;
    return;
  }

  state.tabs = snapshot.tabs;
  state.activeDocumentId = snapshot.activeDocumentId ?? snapshot.documentId;
  if (state.activeDocumentId !== previousDocumentId) {
    // The previous document's success or failure is not this document's state.
    state.notice = '';
    state.error = '';
    state.saved = false;
    state.valueEditor = null;
    state.valueError = '';
    state.stepEditorOpen = false;
  }

  state.actionForm = snapshot.actionForm?.open ? snapshot.actionForm : null;
  if (state.actionForm) {
    state.actionPage = state.actionForm.page;
  }

  state.draftName = snapshot.name;
  state.draftDescription = snapshot.description;
  state.draftGroup = snapshot.group;
  const selection = normalizeSelection(snapshot, state.selectedStepId, state.selectedStepIds);
  state.selectedStepId = selection.selectedStepId;
  state.selectedStepIds = selection.selectedStepIds;
}