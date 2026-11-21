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

export interface EditorParameter {
  name: string;
  label: string;
  kind: string;
  value: string;
  options: EditorChoice[];
}

export interface EditorStep {
  id: string;
  type: string;
  name: string;
  enabled: boolean;
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
}

export interface EditorVariable {
  name: string;
  type: string;
  initial: string;
  description: string;
}

export interface EditorVariableType {
  value: string;
  label: string;
}

export interface EditorSnapshot {
  name: string;
  description: string;
  dirty: boolean;
  stepCount: number;
  steps: EditorStep[];
  triggers: EditorTrigger[];
  variables: EditorVariable[];
  variableTypes: EditorVariableType[];
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
}

export type BridgeEventName = keyof BridgeEvents;
