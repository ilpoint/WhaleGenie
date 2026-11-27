import { bridge } from '../../shared/bridge/bridge.js';
import { editor } from './editor-store.js';

/**
 * The macro list's two doors into the editor: open an existing macro, or start a blank document.
 * Both go through the session service so the snapshot and the tab strip land in the same state
 * box, then switch the route to show it.
 */
export async function openMacro(name: string): Promise<boolean> {
  await bridge.value.call('app.openEditor', { name });
  return true;
}

export async function newMacro(): Promise<boolean> {
  await bridge.value.call('app.newEditor');
  return true;
}

/**
 * The public library's one door into the editor: put a copy of the public macro into the document
 * the person is working on, creating a blank macro first when none is open. A public macro never
 * becomes a project macro by itself; it lives on as a sub-macro with a call step, which is what
 * "use it" means.
 */
export async function usePublicMacro(id: string, version = 0, source = 'local'): Promise<boolean> {
  await editor.session.ensureLoaded();
  if (editor.get().snapshot === null) {
    if (!(await editor.session.newDocument())) {
      return false;
    }
  }

  if (!(await editor.steps.addPublicMacro(id, version, source))) {
    return false;
  }

  const name = editor.get().snapshot?.name ?? '';
  await bridge.value.call('app.openEditor', { name });
  return true;
}
