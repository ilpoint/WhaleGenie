import type { MockShellContext } from './mock-context.js';

/** The menu overlay's commands: the demo project's pages, and the edits the page can make. */
export function handleSurfaceMenu(
  host: MockShellContext,
  method: string,
  params: Record<string, unknown>,
): unknown {
  const simple: Record<string, () => unknown> = {
    'surface.menu.state': () => host.surfaceMenu,
    'surface.menu.activate': () => ({ started: true }),
    'surface.menu.close': () => undefined,
    'surface.menu.pin': () => ({ pinned: params['pinned'] === true }),
    'surface.menu.focus': () => undefined,
    'surface.menu.edit': () => ({ opened: true }),
    'surface.menu.debug': () => ({ opened: true }),
    'surface.menu.macros': () => [
      { name: 'demo', preview: 'Ctrl+F1' },
      { name: 'second', preview: 'F2' },
    ],
    'surface.menu.cell.assign': () => assignCell(host, params),
    'surface.menu.cell.icon': () => setCellIcon(host, params),
    'surface.menu.image': () => ({ path: 'C:\\demo\\icon.png' }),
    'surface.menu.rule.add': () => host.surfaceMenu,
    'surface.menu.block': () => host.surfaceMenu,
  };
  const known = simple[method];
  if (known) {
    return known();
  }

  switch (method) {
    case 'surface.menu.page.add':
      return addPage(host, params);
    case 'surface.menu.page.remove':
      return removePage(host, params);
    case 'surface.menu.move':
      return moveCell(host, params);
    default:
      throw new Error('unknown surface method: ' + method);
  }
}

function assignCell(host: MockShellContext, params: Record<string, unknown>): unknown {
  const pageId = String(params['pageId'] ?? '');
  const row = Number(params['row'] ?? 0);
  const column = Number(params['column'] ?? 0);
  const name = String(params['name'] ?? '');
  const pages = [...host.surfaceMenu.common, ...host.surfaceMenu.context];
  const page = pages.find((candidate) => candidate.pageId === pageId);
  if (!page) {
    return host.surfaceMenu;
  }

  const cells = page.cells.filter((cell) => cell.row !== row || cell.column !== column);
  cells.push({
    row, column, macroName: name, description: '',
    entryId: 'mock-' + pageId + '-' + row + '-' + column,
    iconKind: null, iconValue: null,
  });
  const update = (list: typeof host.surfaceMenu.common) => list.map((candidate) =>
    candidate.pageId === pageId ? { ...candidate, cells } : candidate);
  host.surfaceMenu = {
    ...host.surfaceMenu,
    common: update(host.surfaceMenu.common),
    context: update(host.surfaceMenu.context),
  };
  return host.surfaceMenu;
}

function setCellIcon(host: MockShellContext, params: Record<string, unknown>): unknown {
  const entryId = String(params['entryId'] ?? '');
  const kind = String(params['kind'] ?? '');
  const value = String(params['value'] ?? '');
  const update = (list: typeof host.surfaceMenu.common) => list.map((page) => ({
    ...page,
    cells: page.cells.map((cell) => cell.entryId === entryId
      ? { ...cell, iconKind: kind, iconValue: value }
      : cell),
  }));
  host.surfaceMenu = {
    ...host.surfaceMenu,
    common: update(host.surfaceMenu.common),
    context: update(host.surfaceMenu.context),
  };
  return host.surfaceMenu;
}

function addPage(host: MockShellContext, params: Record<string, unknown>): unknown {
  const pageId = String(params['pageId'] ?? '');
  const next = {
    pageId: 'mock-' + (host.surfaceMenu.common.length + host.surfaceMenu.context.length + 1),
    isDefault: false,
    ruleSummary: '',
    cells: [],
  };
  host.surfaceMenu = pageId.length === 0
    ? { ...host.surfaceMenu, common: [...host.surfaceMenu.common, next] }
    : { ...host.surfaceMenu, context: [...host.surfaceMenu.context, next] };
  return host.surfaceMenu;
}

function removePage(host: MockShellContext, params: Record<string, unknown>): unknown {
  const pageId = String(params['pageId'] ?? '');
  host.surfaceMenu = {
    ...host.surfaceMenu,
    common: host.surfaceMenu.common.filter((page) => page.pageId !== pageId),
    context: host.surfaceMenu.context.filter((page) => page.pageId !== pageId),
  };
  return host.surfaceMenu;
}

function moveCell(host: MockShellContext, params: Record<string, unknown>): unknown {
  const entryId = String(params['entryId'] ?? '');
  const pageId = String(params['pageId'] ?? '');
  const row = Number(params['row'] ?? 0);
  const column = Number(params['column'] ?? 0);
  host.surfaceMenu = {
    ...host.surfaceMenu,
    common: host.surfaceMenu.common.map((page) => ({
      ...page,
      cells: page.cells
        .filter((cell) => cell.entryId !== entryId)
        .concat(page.pageId === pageId && page.cells.some((cell) => cell.entryId === entryId)
          ? [{ ...page.cells.find((cell) => cell.entryId === entryId)!, row, column }]
          : []),
    })),
    context: host.surfaceMenu.context,
  };
  return host.surfaceMenu;
}