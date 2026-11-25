/**
 * 极简路由：路由就是地址栏的 hash。宏列表是默认页，旧的 #home 只作为别名解析到它；
 * 离开当前页之前可以先问一个守卫——编辑器用它把没保存的宏问清楚。
 */

export type Route = 'macros' | 'editor' | 'run' | 'library' | 'settings' | 'about';

export const routes: readonly Route[] = ['macros', 'editor', 'run', 'library', 'settings', 'about'];

export type NavigationGuard = (to: Route) => boolean | Promise<boolean>;

let guard: NavigationGuard | null = null;

/** 装一个离开守卫；只有最后装上的那个生效，返回卸载句柄。 */
export function setNavigationGuard(next: NavigationGuard): () => void {
  guard = next;
  return () => {
    if (guard === next) {
      guard = null;
    }
  };
}

export function currentRoute(): Route {
  const name = window.location.hash.replace(/^#/, '');
  if (name === 'home') {
    return 'macros';
  }

  return (routes as readonly string[]).includes(name) ? (name as Route) : 'macros';
}

/**
 * 切换页面。守卫可以说不：编辑器里的宏没保存完时，用户选了「取消」，地址就不动，
 * 页面还停在原处。所有调用点都把它当异步的，不假设 hash 已经变了。
 */
export async function go(route: Route): Promise<void> {
  if (guard && route !== currentRoute() && !(await guard(route))) {
    return;
  }

  window.location.hash = route;
}
