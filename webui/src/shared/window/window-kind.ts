import type { BridgeMethod } from '../bridge/generated/methods.js';

/** Which shell this page is: the main list/settings shell or the separate macro workbench. */
export type WindowKind = 'main' | 'workbench';

export function windowKind(): WindowKind {
  return new URLSearchParams(window.location.search).get('window') === 'workbench'
    ? 'workbench'
    : 'main';
}

export function isWorkbench(): boolean {
  return windowKind() === 'workbench';
}

export type WindowCommand = 'minimize' | 'maximize' | 'close' | 'pin' | 'drag' | 'focus';

// The method names are kept as dotted literals: the bridge inventory reads the page's claims from
// them, and the workbench's copies are what make a click in that window move that window only.
const mainCommands = {
  minimize: 'window.minimize',
  maximize: 'window.maximize',
  close: 'window.close',
  pin: 'window.pin',
  drag: 'window.drag',
  focus: 'window.focus',
} as const satisfies Record<WindowCommand, BridgeMethod>;

const workbenchCommands = {
  minimize: 'workbench.window.minimize',
  maximize: 'workbench.window.maximize',
  close: 'workbench.window.close',
  pin: 'workbench.window.pin',
  drag: 'workbench.window.drag',
  focus: 'workbench.window.focus',
} as const satisfies Record<WindowCommand, BridgeMethod>;

export function windowCommand(command: WindowCommand): BridgeMethod {
  return (isWorkbench() ? workbenchCommands : mainCommands)[command];
}
