/** events bridge shapes. The barrel protocol.ts re-exports this file. */
import type { DialogRequest, StringsBundle, ThemeState, WindowState } from './app.js';
import type { EditorPublicMacroSnapshot, EditorSnapshot } from './editor.js';
import type { ProjectState } from './project.js';
import type { SystemSwitchState } from './system.js';
import type { McpState } from './mcp.js';
import type { LibraryState } from './library.js';
import type { RunLogBatch, RunLook, RunState, RunStep } from './run.js';
import type { SurfacePanelSnapshot } from './panel.js';
import type { SurfaceWheelSnapshot } from './wheel.js';

export interface BridgeEvents {
  'strings.changed': StringsBundle;
  'theme.changed': ThemeState;
  'window.changed': WindowState;
  'project.changed': ProjectState;
  'system.changed': SystemSwitchState;
  'dialog.request': DialogRequest;
  'mcp.changed': McpState;
  'library.changed': LibraryState;
  'editor.openRequested': { name: string };
  'editor.changed': EditorSnapshot | null;
  'editor.publicMacros': EditorPublicMacroSnapshot[];
  'editor.leaveAsked': { requestId: number; documents: string[] };
  'editor.leaveCancelled': { requestId: number };
  'run.changed': RunState;
  'run.steps': RunStep[];
  'run.log': RunLogBatch;
  'run.look': RunLook | null;
  'run.openRequested': { name: string };
  'settings.openRequested': Record<string, never>;
  'surface.wheel.changed': SurfaceWheelSnapshot;
  'surface.panel.changed': SurfacePanelSnapshot;
  'surface.menu.closed': null;
  'surface.menu.pinned': { pinned: boolean };
  'surface.menu.refreshed': null;
  'surface.wheel.opened': { pop: boolean; ms: number };
  'surface.wheel.aim': { x: number; y: number; ring: string | null; index: number; hasAim: boolean };
  'surface.wheel.closed': null;
}

export type BridgeEventName = keyof BridgeEvents;
