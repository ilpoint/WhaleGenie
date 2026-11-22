/** events bridge shapes. The barrel protocol.ts re-exports this file. */
import type { DialogRequest, StringsBundle, ThemeState, WindowState } from './app.js';
import type { ProjectState } from './project.js';
import type { McpState } from './mcp.js';
import type { RunState } from './run.js';

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
