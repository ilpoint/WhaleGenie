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

theme.load(await client.call<ThemeState>('theme.get'));
appInfo.load(await client.call<AppInfo>('app.info'));
project.load(await client.call<ProjectState>('project.state'));

client.on('strings.changed', (next) => strings.load(next));
client.on('theme.changed', (next) => theme.load(next));
client.on('window.changed', (next) => windowMirror.load(next));
client.on('project.changed', (next) => project.load(next));
client.on('settings.openRequested', () => go('settings'));
client.on('run.openRequested', ({ name }) => {
  void client.call('run.load', { name }).then(() => go('run'));
});
client.on('editor.openRequested', ({ name }) => {
  void editor.session.open(name).then(() => go('editor'));
});

await client.call('bridge.ready');
