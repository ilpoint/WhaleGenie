import type { MockEditor } from './mock-editor.js';
import {
  activate,
  close,
  macroSetting,
  newDocument,
  open,
  patch,
  subOpen,
} from './mock-editor-documents.js';
import { addStep } from './mock-editor-step-commands.js';

/**
 * The document-lifecycle half of the editor's session command: open, create, switch, close,
 * rename and deactivate. It sits in its own file so the command dispatcher stays a dispatcher
 * rather than growing one branch per kind.
 */
export function handleDocumentCommand(host: MockEditor, kind: string,
    params: Record<string, unknown>): unknown {
  switch (kind) {
    case 'open':
      return open(host, host.text(params, 'name'));
    case 'new':
      return newDocument(host);
    case 'subOpen':
      return subOpen(host, host.text(params, 'name'), host.optionalText(params, 'ownerDocumentId'));
    case 'activate':
      return activate(host, host.text(params, 'documentId'));
    case 'deactivate':
      host.valueEditor = null;
      host.actionForm = null;
      host.actionStep = null;
      host.recording = false;
      return host.activeSnapshot();
    case 'close':
      return close(host, host.optionalText(params, 'documentId'), params['discard'] === true);
    case 'patch':
      return patch(host, params);
    case 'macroSetting':
      return macroSetting(host, params);
    case 'publicMacroAdd':
      return addStep(host, { type: 'control.runMacro' });
    default:
      throw new Error('不认识会话命令 ' + kind + '。');
  }
}