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

// ---- 事件词表 --------------------------------------------------------------

export interface BridgeEvents {
  'strings.changed': StringsBundle;
  'theme.changed': ThemeState;
  'window.changed': WindowState;
}

export type BridgeEventName = keyof BridgeEvents;
