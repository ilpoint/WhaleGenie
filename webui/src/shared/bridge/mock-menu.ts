import type { MockShellContext } from './mock-context.js';

/** The menu overlay's commands: the demo project's pages, and the edits the page can make. */
export function handleSurfaceMenu(
  host: MockShellContext,
  method: string,
  params: Record<string, unknown>,
): unknown {
  switch (method) {
    case 'surface.menu.state':
      return host.surfaceMenu;

    case 'surface.menu.activate':
      return { started: true };

    case 'surface.menu.close':
      return undefined;

    case 'surface.menu.pin':
      return { pinned: params['pinned'] === true };

    case 'surface.menu.focus':
      return undefined;

    case 'surface.menu.edit':
    case 'surface.menu.debug':
      return { opened: true };

    case 'surface.menu.icon':
    case 'surface.menu.replace':
      return host.surfaceMenu;

    case 'surface.menu.page.add':
      return addPage(host, params);

    case 'surface.menu.page.remove':
      return removePage(host, params);

    case 'surface.menu.move':
      return moveCell(host, params);

    case 'surface.menu.rule.add':
    case 'surface.menu.block':
      return host.surfaceMenu;

    default:
      throw new Error('unknown surface method: ' + method);
  }
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