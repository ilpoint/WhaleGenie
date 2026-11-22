import type { MockEditor } from './mock-editor.js';

/** One editor command family, split out of MockEditor.run. */
export function handleValueRun(host: MockEditor, method: string, params: Record<string, unknown>): unknown {
  switch (method) {
      case 'editor.valueOpen':
        return host.openValue(params);
      case 'editor.valueState':
        return host.valueEditor?.snapshot() ?? (() => { throw new Error('没有打开的取值窗口。'); })();
      case 'editor.valueCommand':
        return host.valueCommand(params);
      case 'editor.valueConfirm': {
        if (!host.valueEditor) {
          throw new Error('没有打开的取值窗口。');
        }
        const outcome = host.valueEditor.confirm();
        if (outcome.closed) {
          host.valueEditor = null;
        }
        return outcome;
      }
      case 'editor.valueCancel': {
        if (!host.valueEditor) {
          throw new Error('没有打开的取值窗口。');
        }
        const outcome = host.valueEditor.cancel();
        host.valueEditor = null;
        return outcome;
      }
    default:
      throw new Error('不认识编辑器命令 ' + method + '。');
  }
}
