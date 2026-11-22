/** editor-value bridge shapes. The generated module is the only place these are written. */
import type {
  EditorValueChoiceSnapshot,
  EditorValueFunction,
  EditorValueFunctionArgument,
  EditorValuePartSnapshot,
} from '../generated/types.js';

export type {
  EditorValueCancelCommand,
  EditorValueChoiceSnapshot,
  EditorValueCommand,
  EditorValueConfirmCommand,
  EditorValueFunction,
  EditorValueKeyCommand,
  EditorValueOpenCommand,
  EditorValueFunctionSnapshot,
  EditorValuePartSnapshot,
  EditorValueFunctionArgument,
  EditorValueFunctionGroup,
  EditorValueNode,
  EditorValueOperator,
  EditorValueOutcome,
  EditorValueSnapshot,
  EditorValueToken,
  EditorValueVariable,
  EditorValueVariableFilter,
  EditorValueWall,
} from '../generated/types.js';

/** The session's names for the generated records the page still uses. */
export type EditorValueChoice = EditorValueChoiceSnapshot;
export type EditorValuePart = EditorValuePartSnapshot;

/** The picker's function list keeps its older name in the page. */
export type EditorValuePickerFunction = EditorValueFunction;
export type EditorValuePickerFunctionArgument = EditorValueFunctionArgument;
