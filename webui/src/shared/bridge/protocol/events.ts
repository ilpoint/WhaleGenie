/** events bridge shapes. The barrel protocol.ts re-exports this file. */
import type { DialogRequest, StringsBundle, ThemeState, WindowState } from './app.js';
import type { ProjectState } from './project.js';
import type { McpState } from './mcp.js';
import type { RunLogBatch, RunLook, RunState, RunStep } from './run.js';
import type { SurfacePanelSnapshot } from './panel.js';
import type { SurfaceWheelSnapshot } from './wheel.js';

export interface BridgeEvents {
  'strings.changed': StringsBundle;
  'theme.changed': ThemeState;
  'window.changed': WindowState;
  'project.changed': ProjectState;
  'dialog.request': DialogRequest;
  'mcp.changed': McpState;
  'editor.openRequested': { name: string };
  'run.changed': RunState;
  'run.steps': RunStep[];
  'run.log': RunLogBatch;
  'run.look': RunLook | null;
  'run.openRequested': { name: string };
  'settings.openRequested': Record<string, never>;
  'surface.wheel.changed': SurfaceWheelSnapshot;
  'surface.panel.changed': SurfacePanelSnapshot;
}

export type BridgeEventName = keyof BridgeEvents;
