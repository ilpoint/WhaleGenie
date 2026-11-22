/** editor bridge shapes. The barrel protocol.ts re-exports this file. */
import type {
  EditorValueChoice,
  EditorValueFunctionSnapshot,
  EditorValuePart,
} from './editor-value.js';

/** The editor session's snapshot: the macro fields and the step tree. */
export interface EditorChoice {
  value: string;
  label: string;
}

export interface EditorColumn {
  name: string;
  required: boolean;
}

export interface EditorRow {
  cells: string[];
}

export interface EditorConditionNode {
  kind: 'group' | 'row';
  join: string;
  left: string;
  compare: string;
  right: string;
  children: EditorConditionNode[];
}

export interface EditorCondition {
  join: string;
  nodes: EditorConditionNode[];
  editable: boolean;
}

export interface EditorConditionCompare {
  value: string;
  label: string;
}

export interface EditorParameter {
  name: string;
  label: string;
  kind: string;
  value: string;
  options: EditorChoice[];
  columns: EditorColumn[];
  rows: EditorRow[];
  condition: EditorCondition | null;
  display: string;
  valueForm: string;
  offersValue: boolean;
  takesFormula: boolean;
  showsNameMark: boolean;
  isNumber: boolean;
  isNumberFormula: boolean;
  isMultiline: boolean;
  isColor: boolean;
  isPath: boolean;
  isKey: boolean;
  placeholder: string;
  required: boolean;
  parts: EditorValuePart[];
  advanced: boolean;
  group: string;
  groupLabel: string;
  isReturns: boolean;
}

export interface EditorStep {
  id: string;
  type: string;
  name: string;
  enabled: boolean;
  comment: string;
  timeoutMs: number;
  retryCount: number;
  retryDelayMs: number;
  onError: string;
  acceptsSteps: boolean;
  detail: string;
  problem: string;
  inputMark: string;
  metaSummary: string;
  parameters: EditorParameter[];
  sections: EditorStepSection[];
}

/** One child list of a block: the "then" list of an if, the "catch" list of a try. */
export interface EditorStepSection {
  name: string;
  label: string;
  steps: EditorStep[];
}

export interface EditorTab {
  key: string;
  name: string;
  dirty: boolean;
  problem: boolean;
  isSubMacro: boolean;
}

export interface EditorTrigger {
  id: string;
  kind: string;
  kindLabel: string;
  summary: string;
  enabled: boolean;
  binding: string;
  loop: string;
  once: boolean;
  timerMode: string;
  time: string;
  interval: string;
  unit: string;
  filePath: string;
  fileChange: string;
  fileFilter: string;
  fileSubfolders: boolean;
  processName: string;
  processChange: string;
  hotstringText: string;
  colorMatch: string;
  colorHex: string;
  colorTolerance: string;
  colorX: string;
  colorY: string;
  followCursor: boolean;
  idleSeconds: string;
  windowTitle: string;
  windowProcess: string;
  windowClass: string;
  windowHow: string;
  windowChange: string;
  interfaceTitle: string;
  interfaceProcess: string;
  interfaceHow: string;
  windowCaseSensitive: boolean;
  windowTrim: boolean;
  windowMultiline: boolean;
  hotstringEndChars: string;
  hotstringCaseSensitive: boolean;
  hotstringInsideWord: boolean;
  hotstringRequireEnd: boolean;
  hotstringEraseTyped: boolean;
  hotstringKeepEnd: boolean;
  hotstringMaximumLength: number;
  hotstringEndKeys: EditorHotstringEndKey[];
  hotstringEndCaptureProblem: string;
  interfaceHotkey: string;
  interfaceHotkeyProblem: string;
  interfaceGroupMembers: string[];
}

export interface EditorHotstringEndKey {
  value: string;
  label: string;
  checked: boolean;
  isCustom: boolean;
}

export interface EditorVariable {
  name: string;
  type: string;
  initial: string;
  description: string;
  source: string;
  isDeclared: boolean;
}

export interface EditorGlobal {
  name: string;
  type: string;
  default: string;
  description: string;
  persisted: boolean;
}

export interface EditorMacroField {
  name: string;
  type: string;
}

export interface EditorMacroParameter {
  name: string;
  type: string;
  required: boolean;
  default: string;
  description: string;
  fields: EditorMacroField[];
}

export interface EditorMacroResult {
  name: string;
  type: string;
  description: string;
  fields: EditorMacroField[];
}

export interface EditorVariableType {
  value: string;
  label: string;
}

export interface EditorTriggerKind {
  value: string;
  label: string;
}

export interface EditorSnapshot {
  name: string;
  description: string;
  group: string;
  subMacros: string[];
  callableParameters: EditorMacroParameter[];
  callableResults: EditorMacroResult[];
  followInputLimit: boolean;
  inputLimit: number;
  toggleKey: string;
  dirty: boolean;
  stepCount: number;
  steps: EditorStep[];
  triggers: EditorTrigger[];
  variables: EditorVariable[];
  globals: EditorGlobal[];
  valueChoices: EditorValueChoice[];
  valueFunctions: EditorValueFunctionSnapshot[];
  conditionCompares: EditorConditionCompare[];
  variableTypes: EditorVariableType[];
  triggerKinds: EditorTriggerKind[];
  canUndo: boolean;
  canRedo: boolean;
  canSave: boolean;
  nameProblem: string | null;
  problemSummary: string;
  declarationProblemSummary: string;
  callImpactSummary: string;
  notice: string;
  triggerProblemSummary: string;
  toggleKeyProblem: string;
  recording: EditorRecording;
  isSubMacro: boolean;
}

export interface EditorRecording {
  positionIndex: number;
  positionOptions: string[];
  mouseMovement: boolean;
  mouseButtons: boolean;
  keyboardKeys: boolean;
  gamepadButtons: boolean;
  delays: boolean;
  ignoreDelaysBelowMs: number;
  ignoreInjected: boolean;
  delayScale: number;
  delayScaleDisplay: string;
  hasDelayScale: boolean;
}
