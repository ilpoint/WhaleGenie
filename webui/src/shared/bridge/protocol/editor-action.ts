/** editor-action bridge shapes. The generated module is the only place the snapshot records are written. */
import type { EditorParameter } from './editor.js';
import type {
  EditorActionCategorySnapshot,
  EditorActionFieldSnapshot,
  EditorActionFormSnapshot,
  EditorActionPageSnapshot,
  EditorActionParameterSnapshot,
  EditorActionRowSnapshot,
  EditorActionSnapshot,
  EditorBrowserCandidateSnapshot,
  EditorCallArgumentSnapshot,
  EditorCharacterCapSnapshot,
  EditorCharacterPadSnapshot,
  EditorClipboardPreviewSnapshot,
  EditorColourPointRowSnapshot,
  EditorColourRowSnapshot,
  EditorDurationSnapshot,
  EditorGenericRowSnapshot,
  EditorGlobalDraftSnapshot,
  EditorImagePreviewSnapshot,
  EditorImageRowSnapshot,
  EditorKeyCapSnapshot,
  EditorKeyPadSnapshot,
  EditorKeyRowSnapshot,
  EditorNarrowRowSnapshot,
  EditorPickerSnapshot,
  EditorPublicMacroSnapshot,
  EditorRegionRowSnapshot,
  EditorReturnsRowSnapshot,
  EditorSteppingMetaSnapshot,
  EditorWindowPreviewRowSnapshot,
  EditorWindowPreviewSnapshot,
  EditorWindowRuleChoiceSnapshot,
} from '../generated/types.js';

export type {
  EditorCatalogue,
  EditorTrialBox,
  EditorTrialReply,
  EditorTrialResult,
  EditorActionCategorySnapshot,
  EditorActionFieldSnapshot,
  EditorActionFormSnapshot,
  EditorActionPageSnapshot,
  EditorActionParameterSnapshot,
  EditorActionRowSnapshot,
  EditorActionSnapshot,
  EditorBrowserCandidateSnapshot,
  EditorCallArgumentSnapshot,
  EditorCharacterCapSnapshot,
  EditorCharacterPadSnapshot,
  EditorClipboardPreviewSnapshot,
  EditorColourPointRowSnapshot,
  EditorColourRowSnapshot,
  EditorDurationSnapshot,
  EditorGenericRowSnapshot,
  EditorGlobalDraftSnapshot,
  EditorImagePreviewSnapshot,
  EditorImageRowSnapshot,
  EditorKeyCapSnapshot,
  EditorKeyPadSnapshot,
  EditorKeyRowSnapshot,
  EditorNarrowRowSnapshot,
  EditorPickerSnapshot,
  EditorPublicMacroSnapshot,
  EditorRegionRowSnapshot,
  EditorReturnsRowSnapshot,
  EditorSteppingMetaSnapshot,
  EditorWindowPreviewRowSnapshot,
  EditorWindowPreviewSnapshot,
  EditorWindowRuleChoiceSnapshot,
} from '../generated/types.js';

/** The page keeps its shorter names for the generated records. */
export type EditorActionParameter = EditorActionParameterSnapshot;
export type EditorAction = EditorActionSnapshot;
export type EditorActionCategory = EditorActionCategorySnapshot;
export type EditorPublicMacro = EditorPublicMacroSnapshot;
export type EditorActionPage = EditorActionPageSnapshot;
export type EditorActionForm = EditorActionFormSnapshot;
export type EditorActionRow = EditorActionRowSnapshot;
export type EditorPicker = EditorPickerSnapshot;
export type EditorActionField = EditorActionFieldSnapshot;
export type EditorRegionRow = EditorRegionRowSnapshot;
export type EditorImagePreview = EditorImagePreviewSnapshot;
export type EditorImageRow = EditorImageRowSnapshot;
export type EditorColourRow = EditorColourRowSnapshot;
export type EditorColourPointRow = EditorColourPointRowSnapshot;
export type EditorKeyRow = EditorKeyRowSnapshot;
export type EditorNarrowRow = EditorNarrowRowSnapshot;
export type EditorGenericRow = EditorGenericRowSnapshot;
export type EditorReturnsRow = EditorReturnsRowSnapshot;
export type EditorCallArgument = EditorCallArgumentSnapshot;
export type EditorSteppingMeta = EditorSteppingMetaSnapshot;
export type EditorDuration = EditorDurationSnapshot;
export type EditorGlobalDraft = EditorGlobalDraftSnapshot;
export type EditorWindowRuleChoice = EditorWindowRuleChoiceSnapshot;
export type EditorWindowPreview = EditorWindowPreviewSnapshot;
export type EditorWindowPreviewRow = EditorWindowPreviewRowSnapshot;
export type EditorBrowserCandidate = EditorBrowserCandidateSnapshot;
export type EditorClipboardPreview = EditorClipboardPreviewSnapshot;
export type EditorKeyCap = EditorKeyCapSnapshot;
export type EditorKeyPad = EditorKeyPadSnapshot;
export type EditorCharacterCap = EditorCharacterCapSnapshot;
export type EditorCharacterPad = EditorCharacterPadSnapshot;

export type EditorValueField = EditorParameter | EditorActionField;
