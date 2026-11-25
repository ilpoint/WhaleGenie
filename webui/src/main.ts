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
import { currentRoute, go, setNavigationGuard } from './shared/navigation/router.js';
import { systemSwitch } from './shared/system/system.js';
import './shared/icons/icon.js';
import './features/overlay/menu-overlay.js';
import './features/overlay/wheel.js';
import './features/overlay/panel.js';
import { installFocusReporting } from './shared/focus/focus.js';
import { editor } from './features/editor/editor-store.js';
import './app/shell.js';

/**
 * 启动装配：选传输 → 建客户端 → 拉词表、主题、程序信息与项目 → 接事件 → 告诉宿主"收信口就绪"。
 * 顺序不能乱：收信口在 HostTransport 构造时注册，`bridge.ready` 一定在拉完第一轮数据之后
 * 才发——宿主缓存的推送（窗口状态、切语言、项目变化）等到页面能接住时才到。
 */
// WebView2 自带浏览器右键菜单；桌面程序里没有“返回/刷新/打印/检查”的位置，页面自己的菜单
// 仍然在各自元素上处理，并且先于这里阻止默认。
window.addEventListener('contextmenu', (event) => event.preventDefault());

const client = new BridgeClient(pickTransport());
bridge.connect(client);
installFocusReporting();

// 测试与调试的观察口：e2e 与控制台可以手动注册监听、手动调请求。
window.__bridge = client;

// 同一份产物也承载浮层：#overlay/menu 只画菜单，不画壳。浮层与壳是两个 WebView，各自附着在
// 自己的桥面上（Main / Menu），所以这里必须先决定画哪一张，再去拉那一张要的数据。
// The native host asks for an overlay twice over — a query the file URL always keeps and the hash
// the router reads — because a file navigation may drop the fragment on some WebView builds.
// 这个判断在第一个 await 之前完成，下面的离开守卫才能在壳第一次可点击时就已经装好。
const query = new URLSearchParams(window.location.search);
const overlayRoute = query.get('overlay')
  ?? (window.location.hash.startsWith('#overlay/') ? window.location.hash.slice('#overlay/'.length) : null);

if (!overlayRoute) {
  // 离开编辑器先问未保存的宏。守卫在数据加载前就装好，第一次点左侧也不会漏。
  setNavigationGuard(async (to) => {
    if (currentRoute() !== 'editor' || to === 'editor') {
      return true;
    }

    if (!(await editor.dialog.confirmLeave())) {
      return false;
    }

    // 页面离开时先把取值/动作草稿与录制交给宿主收尾，再让页面清掉自己的副本。
    await editor.session.deactivate();
    return true;
  });

  // 原生窗口关闭/退出也会问同一个问题；网页画出来，回答再让宿主继续。没有页面时宿主
  // 的 AskAsync 返回 null，仍走它的原生兜底。
  client.on('editor.leaveAsked', ({ requestId, documents }) => {
    editor.answerLeaveAsked(requestId, documents);
  });
  client.on('editor.leaveCancelled', ({ requestId }) => {
    editor.cancelLeaveAsked(requestId);
  });

  // A project replacement closed documents in C#; no command the page sent carries that change.
  client.on('editor.changed', (snapshot) => editor.applyHostSnapshot(snapshot));
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
  systemSwitch.load(await client.call<SystemSwitchState>('system.state'));

  client.on('window.changed', (next) => windowMirror.load(next));
  client.on('project.changed', (next) => project.load(next));
  client.on('system.changed', (next) => systemSwitch.load(next));
  client.on('settings.openRequested', () => void go('settings'));
  client.on('run.openRequested', ({ name }) => {
    void client.call('run.load', { name }).then(() => go('run'));
  });
  client.on('editor.openRequested', ({ name }) => {
    void editor.session.open(name).then(() => void go('editor'));
  });
}

await client.call('bridge.ready');
