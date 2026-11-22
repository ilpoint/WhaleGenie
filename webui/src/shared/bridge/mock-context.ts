import type {
  AppInfo,
  ProjectState,
  RunState,
  SettingsState,
  StringsBundle,
  ThemeState,
  WindowState,
} from './protocol.js';

/** The mutable state a mock handler may read or write. */
export interface MockContext {
  info: AppInfo;
  settingsState: SettingsState;
  push(event: string, payload: unknown): void;
  bundle(): StringsBundle;
}

/** Run state the run handler reads and replaces. */
export interface MockRunContext {
  runState: RunState;
}

/** Shell state the app/strings/theme/window handler reads. */
export interface MockShellContext {
  info: AppInfo;
  theme: ThemeState;
  windowState: WindowState;
  projectState: ProjectState;
  push(event: string, payload: unknown): void;
  bundle(): StringsBundle;
}
