/** editor-value bridge shapes. The barrel protocol.ts re-exports this file. */

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

export interface EditorValueFunction {
  name: string;
  signature: string;
  description: string;
  group: string;
}

export interface EditorValuePart {
  kind: string;
  text: string;
  name: string;
}

export interface EditorValueNode {
  kind: string;
  path: number[] | null;
  start: number;
  length: number;
  text: string;
  operator: string;
  tone: string;
  name: string;
  before: string;
  isName: boolean;
  isGhost: boolean;
  isRequired: boolean;
  isEmpty: boolean;
  isSelected: boolean;
  hasProblem: boolean;
  problemHint: string;
  hoverHint: string | null;
  canRemove: boolean;
  hasNoArguments: boolean;
  hasNoItems: boolean;
  stacked: boolean;
  compact: boolean;
  argumentLabel: string;
  argumentFlag: string;
  argumentIndex: number;
  argumentOptional: boolean;
  argumentRequired: boolean;
  children: EditorValueNode[];
  ghost: EditorValueNode | null;
}

export interface EditorValueOperator {
  token: string;
  insert: string;
  hint: string;
  tone: string;
  enabled: boolean;
}

export interface EditorValueToken {
  text: string;
  insert: string;
}

export interface EditorValueFunctionGroup {
  group: string;
  label: string;
  selected: boolean;
}

export interface EditorValuePickerFunctionArgument {
  label: string;
  hint: string;
}

export interface EditorValuePickerFunction {
  name: string;
  tag: string;
  heading: string;
  writtenAs: string;
  arityNote: string;
  hint: string;
  group: string;
  insert: string;
  arguments: EditorValuePickerFunctionArgument[];
}

export interface EditorValueVariableFilter {
  group: string;
  label: string;
  hint: string;
  selected: boolean;
}

export interface EditorValueVariable {
  name: string;
  display: string;
  type: string | null;
  group: string;
  origin: string;
  source: string;
  description: string;
  persistent: boolean;
  tone: string;
  details: string;
}

export interface EditorValueWall {
  filters: EditorValueVariableFilter[];
  selectedFilter: string;
  search: string;
  looking: string;
  details: string;
  isEmpty: boolean;
  shown: EditorValueVariable[];
}

export interface EditorValueSnapshot {
  mode: string;
  title: string;
  hint: string;
  target: string;
  targetLine: string;
  showsBand: boolean;
  showsFormulaMaterials: boolean;
  panelSpan: number;
  isTextMode: boolean;
  isRunMode: boolean;
  isValueMode: boolean;
  isNameMode: boolean;
  recipe: EditorValueNode | null;
  caret: number;
  spanEnd: number;
  caretPath: number[];
  caretAfter: boolean;
  selectedPath: number[] | null;
  isValueEmpty: boolean;
  emptyBandHint: string;
  structureHint: string;
  text: string;
  note: string;
  noteIsProblem: boolean;
  saying: string;
  reads: boolean;
  hasSaying: boolean;
  canConfirm: boolean;
  canUndo: boolean;
  canRedo: boolean;
  canClear: boolean;
  textBody: string;
  isTypingNumber: boolean;
  typingText: string;
  typingPlaceholder: string;
  canInsertTyped: boolean;
  typingNote: string;
  hasTypingNote: boolean;
  functionQuery: string;
  selectedFunctionGroup: string | null;
  isAllFunctions: boolean;
  hasChosenFunction: boolean;
  hasChosenVariable: boolean;
  allowNewName: boolean;
  newName: string;
  canCreateName: boolean;
  newNameLine: string;
  operators: EditorValueOperator[];
  values: EditorValueToken[];
  functionGroups: EditorValueFunctionGroup[];
  functions: EditorValuePickerFunction[];
  wall: EditorValueWall;
}

export interface EditorValueOutcome {
  closed: boolean;
  applied: boolean;
  snapshot: EditorValueSnapshot;
}
