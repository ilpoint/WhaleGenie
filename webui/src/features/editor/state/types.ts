import type {
  EditorAction,
  EditorActionCategory,
  EditorActionForm,
  EditorPublicMacro,
  EditorSnapshot,
  EditorTab,
  EditorTrialResult,
  EditorValueSnapshot,
} from '../../../shared/bridge/protocol.js';
import type { PaletteSource } from '../editor-model.js';

export type InspectorTab = 'step' | 'variables' | 'macro';

/** The editor's page-side state. C# owns the macro; this mirrors snapshots and visual choices. */
export interface EditorUiState {
  snapshot: EditorSnapshot | null;
  tabs: EditorTab[];
  activeKey: string;
  actions: EditorAction[];
  categories: EditorActionCategory[];
  publicMacros: EditorPublicMacro[];
  selectedStepId: string;
  selectedStepIds: string[];
  selectedActionKey: string;
  collapsedSteps: Set<string>;
  paletteSource: PaletteSource;
  category: string | null;
  search: string;
  inspectorTab: InspectorTab;
  inspectorVisible: boolean;
  inspectorWidth: number;
  recording: boolean;
  captured: number;
  error: string;
  notice: string;
  saved: boolean;
  closeRequest: EditorTab | null;
  loading: boolean;
  draftName: string;
  draftDescription: string;
  draftGroup: string;
  valueEditor: EditorValueSnapshot | null;
  valueError: string;
  stepEditorOpen: boolean;
  actionForm: EditorActionForm | null;
  actionPage: string;
  actionError: string;
  trial: EditorTrialResult | null;
}
