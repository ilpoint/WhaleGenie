/**
 * 极简路由：路由就是地址栏的 hash。阶段 A 只有壳自己的页面；等编辑器进来时这里换成
 * 文档页签，hash 的形状不变。
 */

export type Route = 'home' | 'macros' | 'editor' | 'run' | 'library' | 'settings' | 'about';

export const routes: readonly Route[] = ['home', 'macros', 'editor', 'run', 'library', 'settings', 'about'];

export function currentRoute(): Route {
  const name = window.location.hash.replace(/^#/, '');
  return (routes as readonly string[]).includes(name) ? (name as Route) : 'home';
}

export function go(route: Route): void {
  window.location.hash = route;
}