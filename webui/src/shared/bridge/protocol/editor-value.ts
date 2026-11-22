/** editor-value bridge shapes. The generated module is the only place these are written. */
import type {
  EditorValueChoiceSnapshot,
  EditorValueFunction,
  EditorValueFunctionArgument,
  EditorValuePartSnapshot,
  EditorValueSnapshot,
} from '../generated/types.js';

export type {
  EditorValueChoiceSnapshot,
  EditorValueFunction,
  EditorValueFunctionSnapshot,
  EditorValuePartSnapshot,
  EditorValueFunctionArgument,
  EditorValueFunctionGroup,
  EditorValueNode,
  EditorValueOperator,
  EditorValueSnapshot,
  EditorValueToken,
  EditorValueVariable,
  EditorValueVariableFilter,
  EditorValueWall,
} from '../generated/types.js';

/** The session's names for the generated records the page still uses. */
export type EditorValueChoice = EditorValueChoiceSnapshot;
export type EditorValuePart = EditorValuePartSnapshot;

/** What confirming or cancelling the value editor came to. */
export interface EditorValueOutcome {
  closed: boolean;
  applied: boolean;
  snapshot: EditorValueSnapshot;
}

/** The picker's function list keeps its older name in the page. */
export type EditorValuePickerFunction = EditorValueFunction;
export type EditorValuePickerFunctionArgument = EditorValueFunctionArgument;
