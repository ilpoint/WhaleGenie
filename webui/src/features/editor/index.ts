import { go } from '../../shared/navigation/router.js';
import { editor } from './editor-store.js';

/**
 * The macro list's two doors into the editor: open an existing macro, or start a blank document.
 * Both go through the session service so the snapshot and the tab strip land in the same state
 * box, then switch the route to show it.
 */
export async function openMacro(name: string): Promise<boolean> {
  const ok = await editor.session.open(name);
  if (ok) {
    await go('editor');
  }

  return ok;
}

export async function newMacro(): Promise<boolean> {
  const ok = await editor.session.newDocument();
  if (ok) {
    await go('editor');
  }

  return ok;
}

/**
 * The public library's one door into the editor: put a copy of the public macro into the document
 * the person is working on, creating a blank macro first when none is open. A public macro never
 * becomes a project macro by itself; it lives on as a sub-macro with a call step, which is what
 * "use it" means.
 */
export async function usePublicMacro(id: string): Promise<boolean> {
  await editor.session.ensureLoaded();
  if (editor.get().snapshot === null) {
    if (!(await editor.session.newDocument())) {
      return false;
    }
  }

  if (!(await editor.steps.addPublicMacro(id))) {
    return false;
  }

  await go('editor');
  return true;
}
