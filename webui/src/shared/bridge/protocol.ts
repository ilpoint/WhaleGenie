/**
 * 桥协议的形状。宿主只认三类信封：请求 `{id, method, params}`、应答
 * `{id, ok, result|error}`、事件 `{event, payload}`；这里用类型把每一面的词写死，前端调
 * 什么方法、听什么事件，编译器都能查。
 */

export interface BridgeRequest {
  id: number;
  method: string;
  params?: unknown;
}

export interface BridgeFailurePayload {
  code: string;
  message: string;
}

export interface BridgeSuccess<T = unknown> {
  id: number;
  ok: true;
  result?: T;
}

export interface BridgeError {
  id: number;
  ok: false;
  error: BridgeFailurePayload;
}

export type BridgeResponse<T = unknown> = BridgeSuccess<T> | BridgeError;

export interface BridgeEventEnvelope {
  event: string;
  payload?: unknown;
}

export type BridgeMessage<T = unknown> = BridgeResponse<T> | BridgeEventEnvelope;

// ---- 各方法的结果形状（阶段 A 覆盖的域） -----------------------------------

export interface ProjectInfo {
  path: string | null;
  name: string | null;
  untitled: boolean;
  dirty: boolean;
  revision: number;
  macros: number;
}

export interface AppInfo {
  name: string;
  version: string;
  language: string;
  debug: boolean;
  project: ProjectInfo;
}

export interface StringsBundle {
  language: string;
  entries: Record<string, string>;
}

export type ThemeMode = 'dark' | 'light' | 'custom';

export interface ThemeState {
  mode: ThemeMode;
  baseLight: boolean;
  accentHex: string | null;
  /** False when a custom accent cannot be made readable and the built-in one is in use. */
  accentUsable?: boolean;
  fill?: string;
  surface?: string;
  text?: string;
  defaultAccent?: string;
}

export type WindowPlacement = 'normal' | 'maximized' | 'minimized';

export interface WindowState {
  state: WindowPlacement;
  topmost: boolean;
}

// ---- 项目文档（阶段 B：宏列表） ---------------------------------------------

export interface MacroEntry {
  name: string;
  group: string;
  description: string;
  enabled: boolean;
  running: boolean;
  triggerSummary: string;
  triggerActions: string;
  triggerPreview: string;
  /** 这个宏属于哪些触发视图（"All"、"Keys"…），由程序按触发入口判定。 */
  views: string[];
}

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

export interface RunState {
  loaded: string;
  status: string;
  statusKind: string;
  busy: boolean;
  paused: boolean;
  canRun: boolean;
  canStep: boolean;
  canStop: boolean;
  question: string;
  wantsTypedAnswer: boolean;
  answerText: string;
  isAskingUser: boolean;
  isAsking: boolean;
  arguments: RunArgument[];
  look: RunLook | null;
  globals: RunGlobal[];
  locals: RunLocal[];
  steps: RunStep[];
  log: RunLine[];
}

export interface RunLook {
  stepId: string;
  type: string;
  looking: string;
  image: string;
}

export interface RunArgument {
  name: string;
  label: string;
  hint: string;
  required: boolean;
  value: string;
  given: boolean;
  missing: boolean;
}

export interface RunGlobal {
  name: string;
  type: string;
  value: string;
  defaultValue: string;
}

export interface RunLocal {
  name: string;
  macro: string;
  type: string;
}

export interface RunStep {
  id: string;
  label: string;
  detail: string;
  depth: number;
  shown: boolean;
  container: boolean;
  current: boolean;
  breakpoint: boolean;
  canBreak: boolean;
  time: string;
  failure: string;
  condition: string;
  hitTimes: number;
  expression: string;
}

export interface RunLine {
  time: string;
  level: string;
  text: string;
}

export interface SettingsState {
  loadLastProjectAtStart: boolean;
  systemAtStart: boolean;
  enabledFirst: boolean;
  ignoreOwnInput: boolean;
  soundSystem: boolean;
  soundMacro: boolean;
  preloadOcr: boolean;
  inputRate: number;
  frameRateFollow: boolean;
  frameRate: number;
  frameRateMax: number;
  frameCache: boolean;
  frameCacheAge: number;
  logCount: number;
  language: string;
  systemHotkey: string;
  recordStopKey: string;
  systemHotkeyProblem: string;
  recordStopProblem: string;
  storage: SettingsStorage[];
  driverStatus: string;
  driverReady: boolean;
  attachDriverAtStart: boolean;
  runAsAdmin: boolean;
  adminStatus: string;
  windowWay: string;
  screenWay: string;
  windowWays: SettingsChoice[];
  screenWays: SettingsChoice[];
  menu: SettingsMenuState;
  wheel: SettingsWheelState;
}

export interface WindowRuleState {
  title: string;
  process: string;
  className: string;
  handle: number;
  how: string;
  caseSensitive: boolean;
  trim: boolean;
  multiline: boolean;
}

export interface SettingsMenuRule {
  key: string;
  summary: string;
  pageCountLabel: string;
  isDefault: boolean;
  isBlocked: boolean;
  canAddPage: boolean;
  rule: WindowRuleState;
}

export interface SettingsMenuState {
  launcher: string;
  display: string;
  status: string;
  isMiddle: boolean;
  isCustom: boolean;
  problem: string;
  rules: SettingsMenuRule[];
}

export interface SettingsWheelChoice {
  value: string | number;
  label: string;
}

export interface SettingsWheelScene {
  key: string;
  summary: string;
  isGlobal: boolean;
  rule: WindowRuleState;
}

export interface SettingsWheelIcon {
  kind: string;
  value: string;
}

export interface SettingsWheelSlot {
  ring: string;
  index: number;
  x: number;
  y: number;
  position: string;
  macro: string;
  note: string;
  label: string;
  icon: SettingsWheelIcon | null;
  inherited: boolean;
  overridden: boolean;
  empty: boolean;
  tone: string;
}

export interface SettingsWheelGeometry {
  diameter: number;
  center: number;
  inner: number;
  outer: number;
  third: number;
  edge: number;
  outerCount: number;
  thirdCount: number;
  hasOuter: boolean;
  hasThird: boolean;
}

export interface SettingsMacroChoice {
  name: string;
  enabled: boolean;
}

export interface SettingsWheelState {
  enabled: boolean;
  launcher: string;
  launcherStatus: string;
  launcherMessage: string;
  onlyInPrograms: boolean;
  diameter: number;
  fontSize: number;
  thirdCapacity: number;
  timeoutMs: number;
  repeatKey: string;
  repeatDisplay: string;
  wideOuter: boolean;
  hideEmptyOuter: boolean;
  hideLabelWithIcon: boolean;
  centerName: boolean;
  clampToScreen: boolean;
  movePointerToCenter: boolean;
  showGuide: boolean;
  extendedHint: boolean;
  followTheme: boolean;
  gloss: boolean;
  hoverBorder: boolean;
  popAnimation: boolean;
  isSystemOff: boolean;
  problem: string;
  launcherChoices: SettingsWheelChoice[];
  thirdChoices: SettingsWheelChoice[];
  scenes: SettingsWheelScene[];
  selectedScene: string;
  geometry: SettingsWheelGeometry;
  slots: SettingsWheelSlot[];
  preview: SettingsWheelSlot[];
  macros: SettingsMacroChoice[];
}

export interface WindowEntryState {
  handle: number;
  title: string;
  process: string;
  className: string;
  detail: string;
  summary: string;
}

export interface AboutState {
  projectUrl: string;
  notices: string;
}

export interface WindowRulePickerState {
  windows: WindowEntryState[];
  hows: SettingsChoice[];
}

export interface SettingsChoice {
  value: string;
  label: string;
}

export interface SettingsStorage {
  name: string;
  what: string;
  where: string;
  folder: string;
}

export interface LibraryEntry {
  id: string;
  name: string;
  version: number;
  description: string;
}

export interface LibraryState {
  entries: LibraryEntry[];
  error: string | null;
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
  valueFunctions: EditorValueFunction[];
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
  isSubMacro: boolean;
}

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

export interface EditorActionForm {
  open: boolean;
  isEditing: boolean;
  canConfirm: boolean;
  canRun: boolean;
  validationMessage: string;
  actionKey: string;
  name: string;
  description: string;
  step: EditorStep | null;
  pages: EditorActionPage[];
  json: string;
  stepId: string;
  commitLabel: string;
  runNote: string;
}

export interface EditorCatalogue {
  count: number;
  actions: EditorAction[];
  categories: EditorActionCategory[];
  publicMacros: EditorPublicMacro[];
}

export interface ProjectState {
  path: string | null;
  name: string | null;
  untitled: boolean;
  dirty: boolean;
  revision: number;
  systemEnabled: boolean;
  views: string[];
  groups: string[];
  macros: MacroEntry[];
}

// ---- 程序向页面提问（确认框）与 MCP 浮层 ------------------------------------

export interface DialogRequest {
  requestId: number;
  title: string;
  message: string;
  confirmLabel: string;
  cancelLabel: string | null;
}

export interface McpState {
  allowControl: boolean;
  allowRun: boolean;
  config: string;
  auditFile: string;
}

// ---- 事件词表 --------------------------------------------------------------

export interface BridgeEvents {
  'strings.changed': StringsBundle;
  'theme.changed': ThemeState;
  'window.changed': WindowState;
  'project.changed': ProjectState;
  'dialog.request': DialogRequest;
  'mcp.changed': McpState;
  'editor.openRequested': { name: string };
  'run.changed': RunState;
  'run.openRequested': { name: string };
  'settings.openRequested': Record<string, never>;
}

export type BridgeEventName = keyof BridgeEvents;