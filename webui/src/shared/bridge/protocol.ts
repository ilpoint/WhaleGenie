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
  parameters: EditorParameter[];
  steps: EditorStep[];
}

export interface EditorTab {
  key: string;
  name: string;
  dirty: boolean;
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
  globals: RunGlobal[];
  locals: RunLocal[];
  steps: RunStep[];
  log: RunLine[];
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
  theme: string;
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
  description: string;
  hidden: boolean;
  parameters: EditorActionParameter[];
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
}

export type BridgeEventName = keyof BridgeEvents;
