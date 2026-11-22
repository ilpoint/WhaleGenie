/** editor-action bridge shapes. The barrel protocol.ts re-exports this file. */
import type { EditorChoice, EditorColumn, EditorCondition, EditorParameter } from './editor.js';
import type { EditorValuePart } from './editor-value.js';

export interface EditorActionParameter {
  name: string;
  label: string;
  kind: string;
  required: boolean;
  placeholder: string;
  options: EditorChoice[];
}

export interface EditorAction {
  key: string;
  name: string;
  category: string;
  categoryLabel: string;
  categoryShort: string;
  group: string;
  groupLabel: string;
  groupRank: number;
  description: string;
  parameters: EditorActionParameter[];
}

/** The rail's order and labels; "All" is the null-valued first entry. */
export interface EditorActionCategory {
  value: string | null;
  label: string;
  short: string;
}

export interface EditorPublicMacro {
  id: string;
  version: number;
  name: string;
  description: string;
}

export interface EditorActionPage {
  key: string;
  label: string;
  present: boolean;
  hasDot: boolean;
}

export interface EditorPicker {
  kind: string;
  label: string;
  hint: string;
  mode: string;
}

export interface EditorRegionRow {
  x: string;
  y: string;
  width: string;
  height: string;
  variable: string;
}

export interface EditorImagePreview {
  dataUrl: string;
  fileName: string;
  problem: string;
}

export interface EditorImageRow {
  text: string;
  canMoveUp: boolean;
  canMoveDown: boolean;
  preview: EditorImagePreview | null;
}

export interface EditorColourRow {
  colour: string;
  tolerance: string;
}

export interface EditorColourPointRow {
  dx: string;
  dy: string;
  colour: string;
  mustNot: boolean;
  tolerance: string;
}

export interface EditorKeyRow {
  number: number;
  keys: string;
  hold: string;
  gap: string;
}

export interface EditorNarrowRow {
  how: string;
  value: string;
  count: string;
  needsValue: boolean;
  wantsWords: boolean;
  onPage: boolean;
  ways: EditorChoice[];
}

export interface EditorGenericRow {
  cells: string[];
}

export interface EditorBrowserCandidate {
  selector: string;
  text: string;
}

export interface EditorWindowPreviewRow {
  title: string;
  partLabel: string;
  part: string;
}

export interface EditorWindowPreview {
  summary: string;
  note: string;
  more: string;
  rows: EditorWindowPreviewRow[];
}

export interface EditorActionField {
  name: string;
  label: string;
  hint: string;
  kind: string;
  valueForm: string;
  value: string;
  display: string;
  parts: EditorValuePart[];
  options: EditorChoice[];
  columns: EditorColumn[];
  condition: EditorCondition | null;
  isApplicable: boolean;
  isDefault: boolean;
  isMissing: boolean;
  required: boolean;
  advanced: boolean;
  group: string;
  groupLabel: string;
  offersValue: boolean;
  takesFormula: boolean;
  showsNameMark: boolean;
  allowsNewName: boolean;
  isNumber: boolean;
  isNumberFormula: boolean;
  isDuration: boolean;
  unit: string;
  unitLabel: string;
  durationUnits: EditorChoice[];
  isPercent: boolean;
  percentValue: number;
  channelNote: string;
  jitterRange: string;
  isMultiline: boolean;
  isCode: boolean;
  isColor: boolean;
  colour: string;
  isPath: boolean;
  pathIntent: string;
  pathFilter: string;
  isKey: boolean;
  isKeys: boolean;
  isSelector: boolean;
  isBrowserTarget: boolean;
  isImage: boolean;
  isImageList: boolean;
  isRegion: boolean;
  isRegionList: boolean;
  isColourPoints: boolean;
  isColourRows: boolean;
  isKeySequence: boolean;
  isNarrow: boolean;
  isWindow: boolean;
  isWindowRule: boolean;
  isMacro: boolean;
  isStepChoice: boolean;
  isRecord: boolean;
  isPoint: boolean;
  isRect: boolean;
  placeholder: string;
  hasToBeAnswered: boolean;
  isFollowingWindow: boolean;
  windowRuleSummary: string;
  regions: EditorRegionRow[];
  images: EditorImageRow[];
  colours: EditorColourRow[];
  colourPoints: EditorColourPointRow[];
  keys: EditorKeyRow[];
  narrowRows: EditorNarrowRow[];
  rows: EditorGenericRow[];
  macros: EditorChoice[];
  steps: EditorChoice[];
  candidates: EditorBrowserCandidate[];
  windowPreview: EditorWindowPreview | null;
  imagePreview: EditorImagePreview | null;
}

export interface EditorActionRow {
  groupKey: string;
  groupTitle: string;
  groupHint: string;
  groupIndex: number;
  isGroupStart: boolean;
  isQuad: boolean;
  cells: EditorActionField[];
  picker: EditorPicker | null;
}

export interface EditorReturnsRow {
  key: string;
  label: string;
  hint: string;
  structure: string;
  structureTip: string;
  ticked: boolean;
  name: string;
  isNameTaken: boolean;
  nameWarning: string;
  isPrimary: boolean;
}

export interface EditorCallArgument {
  name: string;
  hint: string;
  required: boolean;
  declared: boolean;
  given: boolean;
  value: string;
}

export interface EditorDuration {
  milliseconds: number;
  written: string;
  unit: string;
  units: EditorChoice[];
}

export interface EditorSteppingMeta {
  enabled: boolean;
  comment: string;
  timeout: EditorDuration;
  retryCount: number | null;
  retryDelay: EditorDuration;
  delayBefore: EditorDuration;
  delayAfter: EditorDuration;
  retryBackoff: string;
  onError: string;
  retryPlan: string;
  tuned: boolean;
}

export interface EditorGlobalDraft {
  name: string;
  type: string;
  default: string;
  defaultPreview: string;
  description: string;
  persistValue: boolean;
  problem: string;
  hasError: boolean;
  types: EditorChoice[];
}

export interface EditorWindowRuleChoice {
  summary: string;
  rule: string;
  count: number;
}

export interface EditorClipboardPreview {
  shown: boolean;
  note: string;
}

export interface EditorKeyCap {
  label: string;
  value: string;
  width: number;
}

export interface EditorKeyPad {
  mode: string;
  maxLength: number;
  rows: EditorKeyCap[][];
}

export interface EditorCharacterCap {
  normal: string;
  shifted: string;
  width: number;
  kind: string;
}

export interface EditorCharacterPad {
  maximumLength: number;
  rows: EditorCharacterCap[][];
}

export interface EditorTrialBox {
  x: number;
  y: number;
  width: number;
  height: number;
  score: number;
  role: string;
  label: string | null;
}

export interface EditorTrialResult {
  success: boolean;
  status: string;
  key: string;
  detail: string;
  image: string | null;
  boxes: EditorTrialBox[];
  chosenIndex: number;
  looking: string;
  originX: number;
  originY: number;
  frameWidth: number;
  frameHeight: number;
}

export interface EditorTrialReply {
  needsConfirm: boolean;
  title?: string;
  question?: string;
  refused?: string;
  result?: EditorTrialResult;
}

export type EditorValueField = EditorParameter | EditorActionField;

export interface EditorActionForm {
  open: boolean;
  isEditing: boolean;
  canConfirm: boolean;
  canRun: boolean;
  running: boolean;
  runLabel: string;
  validationMessage: string;
  validationIsProblem: boolean;
  actionKey: string;
  name: string;
  description: string;
  stepId: string;
  commitLabel: string;
  page: string;
  pages: EditorActionPage[];
  rows: EditorActionRow[];
  advancedRows: EditorActionRow[];
  returns: EditorReturnsRow[];
  callArguments: EditorCallArgument[];
  meta: EditorSteppingMeta;
  globalDraft: EditorGlobalDraft | null;
  windowRules: EditorWindowRuleChoice[];
  windowAbove: string;
  branchSummary: string;
  attemptSummary: string;
  isBlock: boolean;
  showsBranches: boolean;
  showsAttempt: boolean;
  json: string;
  notice: string;
  noticeIsWarning: boolean;
  clipboardPreview: EditorClipboardPreview;
}

export interface EditorCatalogue {
  count: number;
  actions: EditorAction[];
  categories: EditorActionCategory[];
  publicMacros: EditorPublicMacro[];
}
