import '@fluentui/web-components/button.js';
import '@fluentui/web-components/text.js';
import '@fluentui/web-components/label.js';
import '@fluentui/web-components/divider.js';
import '@fluentui/web-components/badge.js';
import '@fluentui/web-components/message-bar.js';
import '@fluentui/web-components/text-input.js';
import '@fluentui/web-components/switch.js';
import '@fluentui/web-components/checkbox.js';

import { BridgeClient } from './shared/bridge/client.js';
import { bridge } from './shared/bridge/bridge.js';
import { pickTransport } from './shared/bridge/pick.js';
import type {
  AppInfo,
  ProjectState,
  StringsBundle,
  SystemSwitchState,
  ThemeState,
} from './shared/bridge/protocol.js';
import { strings } from './shared/strings/strings.js';
import { theme } from './shared/theme/theme.js';
import { appInfo } from './shared/app/app.js';
import { project } from './shared/project/store.js';
import { windowMirror } from './shared/window/window.js';
import { currentRoute, go } from './shared/navigation/router.js';
import { systemSwitch } from './shared/system/system.js';
import './shared/icons/icon.js';
import './features/overlay/menu-overlay.js';
import './features/overlay/wheel.js';
import './features/overlay/panel.js';
import { installFocusReporting } from './shared/focus/focus.js';
import { editor } from './features/editor/editor-store.js';
import './app/shell.js';
import './app/workbench.js';
import { isWorkbench } from './shared/window/window-kind.js';

/**
 * 启动装配：选传输 → 建客户端 → 拉词表、主题、程序信息与项目 → 接事件 → 告诉宿主"收信口就绪"。
 * 同一份产物现在承载主窗口与独立工作台：?window=workbench 只画编辑器和运行调试，主窗口保留
 * 宏列表、公共宏库与设置。浮层仍由 ?overlay 决定，三者在第一个 await 之前分流。
 */
window.addEventListener('contextmenu', (event) => event.preventDefault());

const client = new BridgeClient(pickTransport());
bridge.connect(client);
installFocusReporting();

// 测试与调试的观察口：e2e 与控制台可以手动注册监听、手动调请求。
window.__bridge = client;

const query = new URLSearchParams(window.location.search);
const overlayRoute = query.get('overlay')
  ?? (window.location.hash.startsWith('#overlay/') ? window.location.hash.slice('#overlay/'.length) : null);
const workbench = overlayRoute === null && isWorkbench();

if (overlayRoute === null) {
  // The editor's leave question is answered by whichever shell carries the documents: the
  // workbench normally, the main shell in the rare fallback. Both use the same event names on
  // their own bridge surface.
  client.on('editor.leaveAsked', ({ requestId, documents }) => {
    editor.answerLeaveAsked(requestId, documents);
  });
  client.on('editor.leaveCancelled', ({ requestId }) => {
    editor.cancelLeaveAsked(requestId);
  });
  client.on('editor.changed', (snapshot) => editor.applyHostSnapshot(snapshot));
  client.on('editor.publicMacros', (macros) => editor.applyPublicMacros(macros));
}

const bundle = await client.call<StringsBundle>('strings.bundle');
strings.load(bundle);

theme.load(await client.call<ThemeState>('theme.get'));
client.on('strings.changed', (next) => strings.load(next));
client.on('theme.changed', (next) => theme.load(next));

if (overlayRoute) {
  // 浮层只画自己的圆/卡片，窗口四周要透出后面的游戏或桌面。
  document.documentElement.style.background = 'transparent';
  document.body.style.background = 'transparent';
  document.body.replaceChildren(document.createElement(
    overlayRoute.startsWith('wheel') ? 'wg-overlay-wheel'
      : overlayRoute.startsWith('panel') ? 'wg-overlay-panel' : 'wg-overlay-menu'));
} else {
  appInfo.load(await client.call<AppInfo>('app.info'));
  project.load(await client.call<ProjectState>('project.state'));
  if (!workbench) {
    systemSwitch.load(await client.call<SystemSwitchState>('system.state'));
  }

  client.on('window.changed', (next) => windowMirror.load(next));
  client.on('project.changed', (next) => project.load(next));
  client.on('settings.openRequested', () => {
    if (!workbench) {
      void go('settings');
    }
  });

  if (workbench) {
    client.on('run.openRequested', ({ name }) => {
      void client.call('run.load', { name }).then(() => go('run'));
    });
    client.on('editor.openRequested', ({ name }) => {
      const opened = name.length > 0
        ? editor.session.open(name)
        : editor.session.newDocument();
      void opened.then(() => void go('editor'));
    });
    if (currentRoute() !== 'editor' && currentRoute() !== 'run') {
      window.location.hash = 'editor';
    }
  } else {
    client.on('system.changed', (next) => systemSwitch.load(next));
    // Old #editor / #run links came from the one-window build. The workbench is now the door for
    // them; the main shell itself has no editor page.
    if (currentRoute() === 'editor' || currentRoute() === 'run') {
      window.location.hash = 'macros';
    }
  }

  document.body.replaceChildren(document.createElement(workbench ? 'wg-workbench' : 'wg-shell'));
}

await client.call('bridge.ready');
