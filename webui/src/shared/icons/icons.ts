import { bridgeApi } from '../bridge/bridge.js';

/**
 * Lucide paths by the name a project stores. The program owns the icon table; this module only
 * asks once per name and remembers the answer, so twenty menu cells with one icon cost one call.
 */
const cache = new Map<string, Promise<string | null>>();

/** Lucide.Avalonia 的几何串每段以 "F1 "（非零填充规则）开头；SVG 只允许规则在整条路径上， */
/** 因此把各段拼成一条多子路径，描边不受影响。 */
export function svgPath(path: string): string {
  return path.replaceAll('F1 ', '').replaceAll('F0 ', '').trim();
}

export function iconPath(name: string): Promise<string | null> {
  const known = cache.get(name);
  if (known) {
    return known;
  }

  const asked = bridgeApi.call<{ path: string } | null>('icon.path', { name })
    .then((answer) => answer?.path ?? null)
    .catch(() => null);
  cache.set(name, asked);
  return asked;
}
