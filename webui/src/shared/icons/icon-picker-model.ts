/** The icon picker's list maths, kept pure so the search and paging edges are tested directly. */
export const IconsPerPage = 42;

export function filterIcons(names: readonly string[], query: string): string[] {
  const wanted = query.trim().toLocaleLowerCase();
  return wanted.length === 0
    ? [...names]
    : names.filter((name) => name.toLocaleLowerCase().includes(wanted));
}

export function pageCount(matchCount: number): number {
  return Math.max(1, Math.ceil(matchCount / IconsPerPage));
}

export function clampPage(page: number, count: number): number {
  return Math.max(0, Math.min(Math.max(0, count - 1), page));
}

export function pageIcons(names: readonly string[], page: number): string[] {
  return names.slice(page * IconsPerPage, (page + 1) * IconsPerPage);
}
