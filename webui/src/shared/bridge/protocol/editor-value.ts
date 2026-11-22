/** editor-value bridge shapes. The generated module is the only place these are written. */
import type {
  EditorValueFunction,
  EditorValueFunctionArgument,
  EditorValueSnapshot,
} from '../generated/types.js';

export type {
  EditorValueFunction,
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

/** One value the session's variable wall offers; part of the session snapshot, not the picker. */
export interface EditorValueChoice {
  name: string;
  type: string;
  origin: string;
  source: string;
  description: string;
  group: string;
  display: string;
  persistent: boolean;
}

/** One row of the session's value-function table. */
export interface EditorValueFunctionSnapshot {
  name: string;
  signature: string;
  description: string;
  group: string;
}

/** One piece of a value field: literal text or a variable name. */
export interface EditorValuePart {
  kind: string;
  text: string;
  name: string;
}

/** What confirming or cancelling the value editor came to. */
export interface EditorValueOutcome {
  closed: boolean;
  applied: boolean;
  snapshot: EditorValueSnapshot;
}

/** The picker's function list keeps its older name in the page. */
export type EditorValuePickerFunction = EditorValueFunction;
export type EditorValuePickerFunctionArgument = EditorValueFunctionArgument;
