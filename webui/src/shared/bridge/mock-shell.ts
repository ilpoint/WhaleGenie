import type { ThemeState } from './protocol.js';
import type { MockShellContext } from './mock-context.js';

/** handleShell-domain answers for the mock host. The transport routes by the method prefix. */
export function handleShell(host: MockShellContext, method: string, params: Record<string, unknown>): unknown {
  let result: unknown;
  switch (method) {
        case 'app.info':
          result = host.info;
          break;
        case 'strings.bundle':
          result = host.bundle();
          break;
        case 'theme.get':
          result = host.theme;
          break;
        case 'theme.set': {
          const mode = String(params['mode'] ?? 'dark') as ThemeState['mode'];
          const accentHex = (params['accentHex'] as string | null) ?? null;
          host.theme = {
            mode,
            baseLight: params['baseLight'] === true,
            accentHex,
            accentUsable: true,
            fill: accentHex ?? '#c8401c',
            surface: accentHex ?? '#f5e6e2',
            text: accentHex ?? '#c8401c',
            defaultAccent: '#c8401c',
          };
          host.push('theme.changed', host.theme);
          result = host.theme;
          break;
        }
        case 'surface.menu.state':
          result = host.surfaceMenu;
          break;
        case 'surface.menu.activate':
          result = { started: true };
          break;
        case 'window.minimize':
          host.windowState = { ...host.windowState, state: 'minimized' };
          host.push('window.changed', host.windowState);
          break;
        case 'window.maximize':
          host.windowState = {
            ...host.windowState,
            state: host.windowState.state === 'maximized' ? 'normal' : 'maximized',
          };
          host.push('window.changed', host.windowState);
          break;
        case 'window.close':
          break;
        case 'window.pin':
          host.windowState = { ...host.windowState, topmost: !host.windowState.topmost };
          host.push('window.changed', host.windowState);
          break;
        case 'window.drag':
        case 'window.focus':
        case 'app.quit':
        case 'app.openExternal':
          break;
        case 'app.openRun':
          host.push('run.openRequested', { name: String(params['name'] ?? '') });
          break;
        case 'app.about':
          result = {
            projectUrl: 'https://github.com/ilpoint/WhaleGenie',
            notices: '第三方组件\n\n- Avalonia — MIT\n- Lit — BSD-3-Clause',
          };
          break;
        case 'app.openManual':
  }

  return result;
}
