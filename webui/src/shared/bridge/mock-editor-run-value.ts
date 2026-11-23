import type { MockEditor } from './mock-editor.js';

/** One editor command family, split out of MockEditor.run. */
export function handleValueRun(host: MockEditor, method: string, params: Record<string, unknown>): unknown {
  switch (method) {
      case 'editor.value': {
        switch (params['kind']) {
          case 'open':
            return host.openValue(params);
          case 'command':
            return host.valueCommand(params);
          case 'confirm': {
            if (!host.valueEditor) {
              throw new Error('没有打开的取值窗口。');
            }
            const outcome = host.valueEditor.confirm();
            if (outcome.closed) {
              host.valueEditor = null;
            }
            return outcome;
          }
          case 'cancel': {
            if (!host.valueEditor) {
              throw new Error('没有打开的取值窗口。');
            }
            const outcome = host.valueEditor.cancel();
            host.valueEditor = null;
            return outcome;
          }
          default:
            throw new Error('不认识取值命令 ' + String(params['kind']) + '。');
        }
      }
    default:
      throw new Error('不认识编辑器命令 ' + method + '。');
  }
}
