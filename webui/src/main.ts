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
import type { AppInfo, ProjectState, StringsBundle, ThemeState } from './shared/bridge/protocol.js';
import { strings } from './shared/strings/strings.js';
import { theme } from './shared/theme/theme.js';
import { appInfo } from './shared/app/app.js';
import { project } from './shared/project/store.js';
import { windowMirror } from './shared/window/window.js';
import { go } from './shared/navigation/router.js';
import './features/overlay/menu-overlay.js';
import { installFocusReporting } from './shared/focus/focus.js';
import { editor } from './features/editor/editor-store.js';
import './app/shell.js';

/**
 * 启动装配：选传输 → 建客户端 → 拉词表、主题、程序信息与项目 → 接事件 → 告诉宿主"收信口就绪"。
 * 顺序不能乱：收信口在 HostTransport 构造时注册，`bridge.ready` 一定在拉完第一轮数据之后
 * 才发——宿主缓存的推送（窗口状态、切语言、项目变化）等到页面能接住时才到。
 */
const client = new BridgeClient(pickTransport());
bridge.connect(client);
installFocusReporting();

// 测试与调试的观察口：e2e 与控制台可以手动注册监听、手动调请求。
window.__bridge = client;

const bundle = await client.call<StringsBundle>('strings.bundle');
strings.load(bundle);

// 同一份产物也承载浮层：#overlay/menu 只画菜单，不画壳。浮层与壳是两个 WebView，各自附着在
// 自己的桥面上（Main / Menu），所以这里必须先决定画哪一张，再去拉那一张要的数据。
// The native host asks for an overlay twice over — a query the file URL always keeps and the hash
// the router reads — because a file navigation may drop the fragment on some WebView builds.
const overlay = new URLSearchParams(window.location.search).has('overlay')
  || window.location.hash.startsWith('#overlay/');

theme.load(await client.call<ThemeState>('theme.get'));
client.on('strings.changed', (next) => strings.load(next));
client.on('theme.changed', (next) => theme.load(next));

if (overlay) {
  document.body.replaceChildren(document.createElement('wg-overlay-menu'));
} else {
  appInfo.load(await client.call<AppInfo>('app.info'));
  project.load(await client.call<ProjectState>('project.state'));

  client.on('window.changed', (next) => windowMirror.load(next));
  client.on('project.changed', (next) => project.load(next));
  client.on('settings.openRequested', () => go('settings'));
  client.on('run.openRequested', ({ name }) => {
    void client.call('run.load', { name }).then(() => go('run'));
  });
  client.on('editor.openRequested', ({ name }) => {
    void editor.session.open(name).then(() => go('editor'));
  });
}

await client.call('bridge.ready');
