import type { MockEditor } from './mock-editor.js';
import { handleActionCommand } from './mock-editor-action-commands.js';
import { actionConfirm, actionOpen } from './mock-editor-action-form.js';

/** One editor command family, split out of MockEditor.run. */
export function handleActionRun(host: MockEditor, method: string, params: Record<string, unknown>): unknown {
  switch (method) {
    case 'editor.actionOpen':
      return actionOpen(host, params);
    case 'editor.actionConfirm':
      return actionConfirm(host);
    case 'editor.actionCancel':
      host.actionForm = null;
      host.actionStep = null;
      return host.activeSnapshot();
    case 'editor.action':
      return handleActionCommand(host, params);
    case 'editor.actionPick':
      return host.actionForm;
    case 'editor.actionLook':
      return host.actionForm;
    case 'editor.actionWindowPreview':
      return null;
    case 'editor.actionTestBrowser':
    case 'editor.actionTestElement':
    case 'editor.actionTestNarrow':
      return host.actionForm;
    case 'editor.actionKeyPad':
      return {
        mode: 'key', maxLength: 0,
        rows: [
          [{ label: 'Esc', value: 'Esc', width: 1 }, { label: 'F1', value: 'F1', width: 1 },
            { label: 'F13', value: 'F13', width: 1 }],
          [{ label: 'Num 7', value: 'NumPad7', width: 1 },
            { label: 'MediaPlay', value: 'MediaPlay', width: 1 },
            { label: 'BrowserHome', value: 'BrowserHome', width: 1 }],
        ],
      };
    case 'editor.actionWindowPick':
      return { chosen: false, rule: '', title: '' };
    case 'editor.actionRun':
      return params['confirmed'] === true
        ? {
          needsConfirm: false,
          result: {
            success: true, status: 'Completed', key: '', detail: '', image: null,
            boxes: [], chosenIndex: 0, looking: '', originX: 0, originY: 0,
            frameWidth: 0, frameHeight: 0,
          },
        }
        : { needsConfirm: false, refused: '' };
    default:
      throw new Error('不认识编辑器命令 ' + method + '。');
  }
}
