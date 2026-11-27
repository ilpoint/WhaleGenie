import type { MacroEntry } from '../../shared/bridge/protocol.js';

/**
 * The macro list's filter. C# says which views a macro belongs to and whether "enabled first" is
 * on; the page owns the search, the group chip and the order the cards are drawn in.
 */
export interface MacroFilter {
  view: string;
  /** '' is every group, null is the ungrouped macros, anything else is a group name. */
  group: string | null;
  search: string;
  enabledFirst: boolean;
}

/** The names of every macro after moving one card beside another, for the project's order. */
export function moveMacroNames(
  macros: readonly MacroEntry[],
  source: string,
  target: string,
  after: boolean,
): string[] {
  const names = macros.map((entry) => entry.name);
  const from = names.indexOf(source);
  if (from < 0 || source === target) {
    return names;
  }

  names.splice(from, 1);
  const targetIndex = names.indexOf(target);
  if (targetIndex < 0) {
    return macros.map((entry) => entry.name);
  }

  names.splice(after ? targetIndex + 1 : targetIndex, 0, source);
  return names;
}

/** The cards the list draws, in the order it draws them. */
export function filterMacros(macros: MacroEntry[], filter: MacroFilter): MacroEntry[] {
  const wanted = filter.search.trim().toLowerCase();
  const group = filter.group;
  const shown = macros
    .filter((entry) => entry.views.includes(filter.view))
    .filter((entry) => group === ''
      || (group === null
        ? entry.group.trim().length === 0
        : entry.group.trim().toLowerCase() === group.toLowerCase()))
    .filter((entry) => wanted.length === 0 || entry.name.toLowerCase().includes(wanted));

  // A stable sort keeps the project's own order inside each of the two groups.
  return filter.enabledFirst
    ? [...shown].sort((left, right) => Number(right.enabled) - Number(left.enabled))
    : shown;
}
