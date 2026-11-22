/** editor bridge shapes. The generated module is the only place the snapshot records are written. */
import type {
  EditorChoiceSnapshot,
  EditorColumnSnapshot,
  EditorConditionCompareSnapshot,
  EditorConditionNodeSnapshot,
  EditorConditionSnapshot,
  EditorGlobalSnapshot,
  EditorHotstringEndKeySnapshot,
  EditorMacroFieldSnapshot,
  EditorMacroParameterSnapshot,
  EditorMacroResultSnapshot,
  EditorParameterSnapshot,
  EditorRecordingSnapshot,
  EditorRowSnapshot,
  EditorStepSectionSnapshot,
  EditorStepSnapshot,
  EditorTabSnapshot,
  EditorTriggerKindSnapshot,
  EditorTriggerSnapshot,
  EditorVariableSnapshot,
  EditorVariableTypeSnapshot,
} from '../generated/types.js';

export type {
  EditorActionBranchCommand,
  EditorActionCallArgumentCommand,
  EditorActionCommand,
  EditorActionFieldCommand,
  EditorActionGlobalCommand,
  EditorActionMetaCommand,
  EditorActionPageCommand,
  EditorActionPadKeyCommand,
  EditorActionReturnsCommand,
  EditorActionReturnsFixNameCommand,
  EditorActionRowAddCommand,
  EditorActionRowCellCommand,
  EditorActionRowMoveCommand,
  EditorActionRowRemoveCommand,
  EditorActionWindowCommand,
  EditorActionWindowRuleCommand,
  EditorConditionCommand,
  EditorDeclarationCommand,
  EditorTriggerAddCommand,
  EditorTriggerBindingCommand,
  EditorTriggerCommand,
  EditorTriggerEnabledCommand,
  EditorTriggerFieldCommand,
  EditorTriggerHotstringCommand,
  EditorTriggerHotstringEndCaptureCommand,
  EditorTriggerHotstringEndKeyCommand,
  EditorTriggerHotstringEndResetCommand,
  EditorTriggerIdleCommand,
  EditorTriggerInterfaceHotkeyClearCommand,
  EditorTriggerInterfaceHotkeyCommand,
  EditorTriggerLoopCommand,
  EditorTriggerOnceCommand,
  EditorTriggerRemoveCommand,
  EditorSessionActivateCommand,
  EditorSessionCloseCommand,
  EditorSessionCommand,
  EditorSessionExportCommand,
  EditorSessionMacroSettingCommand,
  EditorSessionNewCommand,
  EditorSessionOpenCommand,
  EditorSessionPatchCommand,
  EditorSessionPublicMacroAddCommand,
  EditorSessionRedoCommand,
  EditorSessionSaveCommand,
  EditorSessionSubOpenCommand,
  EditorRecordCommand,
  EditorSessionUndoCommand,
  EditorStepCommand,
  EditorStepsCommand,
  EditorVariableCommand,
  EditorActionCategorySnapshot,
  EditorActionPageSnapshot,
  EditorActionParameterSnapshot,
  EditorActionSnapshot,
  EditorChoiceSnapshot,
  EditorColumnSnapshot,
  EditorConditionCompareSnapshot,
  EditorConditionNodeSnapshot,
  EditorConditionSnapshot,
  EditorGlobalSnapshot,
  EditorHotstringEndKeySnapshot,
  EditorMacroFieldSnapshot,
  EditorMacroParameterSnapshot,
  EditorMacroResultSnapshot,
  EditorParameterSnapshot,
  EditorPublicMacroSnapshot,
  EditorRecordingSnapshot,
  EditorRowSnapshot,
  EditorSnapshot,
  EditorStepSectionSnapshot,
  EditorStepSnapshot,
  EditorTabSnapshot,
  EditorTriggerKindSnapshot,
  EditorTriggerSnapshot,
  EditorValueChoiceSnapshot,
  EditorValueFunctionSnapshot,
  EditorValuePartSnapshot,
  EditorVariableSnapshot,
  EditorVariableTypeSnapshot,
} from '../generated/types.js';

/** The page keeps its shorter names for the generated records. */
export type EditorChoice = EditorChoiceSnapshot;
export type EditorColumn = EditorColumnSnapshot;
export type EditorRow = EditorRowSnapshot;
export type EditorConditionNode = EditorConditionNodeSnapshot;
export type EditorCondition = EditorConditionSnapshot;
export type EditorConditionCompare = EditorConditionCompareSnapshot;
export type EditorParameter = EditorParameterSnapshot;
export type EditorStep = EditorStepSnapshot;
export type EditorStepSection = EditorStepSectionSnapshot;
export type EditorTab = EditorTabSnapshot;
export type EditorTrigger = EditorTriggerSnapshot;
export type EditorHotstringEndKey = EditorHotstringEndKeySnapshot;
export type EditorVariable = EditorVariableSnapshot;
export type EditorGlobal = EditorGlobalSnapshot;
export type EditorMacroField = EditorMacroFieldSnapshot;
export type EditorMacroParameter = EditorMacroParameterSnapshot;
export type EditorMacroResult = EditorMacroResultSnapshot;
export type EditorVariableType = EditorVariableTypeSnapshot;
export type EditorTriggerKind = EditorTriggerKindSnapshot;
export type EditorRecording = EditorRecordingSnapshot;
