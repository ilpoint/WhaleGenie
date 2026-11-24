import { demoWindows } from './mock-fixtures.js';

/** Window-domain answers for the mock host: the list a rule or a title field reads, and one pick. */
export function handleWindow(method: string): unknown {
  switch (method) {
    case 'window.list':
      return {
        windows: demoWindows,
        hows: [
          { value: 'titleContains', label: '标题包含' },
          { value: 'titleEquals', label: '标题等于' },
          { value: 'process', label: '进程名包含' },
          { value: 'class', label: '窗口类包含' },
          { value: 'handle', label: '窗口句柄（当前实例）' },
        ],
      };
    case 'window.pick':
      return demoWindows[0];
    default:
      return undefined;
  }
}
