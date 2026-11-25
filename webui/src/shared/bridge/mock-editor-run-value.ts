import type { MockEditor } from './mock-editor.js';
import { openValue, valueCommand } from './mock-editor-value.js';

/** One editor command family, split out of MockEditor.run. */
export function handleValueRun(host: MockEditor, method: string, params: Record<string, unknown>): unknown {
  switch (method) {
      case 'editor.value': {
        switch (params['kind']) {
          case 'open':
            return openValue(host, params);
          case 'command':
            return valueCommand(host, params);
          case 'confirm': {
            if (!host.valueEditor) {
              throw new Error('没有打开的取值窗口。');
            }
            const outcome = host.valueEditor.confirm();
            if (outcome.closed) {
              host.valueEditor = null;
            }
            return { ...outcome, state: outcome.applied ? host.activeSnapshot() : null };
          }
          case 'cancel': {
            if (!host.valueEditor) {
              throw new Error('没有打开的取值窗口。');
            }
            const outcome = host.valueEditor.cancel();
            host.valueEditor = null;
            return { ...outcome, state: null };
          }
          default:
            throw new Error('不认识取值命令 ' + String(params['kind']) + '。');
        }
      }
    default:
      throw new Error('不认识编辑器命令 ' + method + '。');
  }
}
