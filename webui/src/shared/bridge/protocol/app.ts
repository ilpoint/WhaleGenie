/** app bridge shapes. The barrel protocol.ts re-exports this file. */

export interface AppInfo {
  name: string;
  version: string;
  language: string;
  debug: boolean;
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

export interface AboutState {
  projectUrl: string;
  notices: string;
}

export interface DialogRequest {
  requestId: number;
  title: string;
  message: string;
  confirmLabel: string;
  cancelLabel: string | null;
}
