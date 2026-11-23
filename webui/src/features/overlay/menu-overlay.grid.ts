import { html, type TemplateResult } from 'lit';
import type { SurfaceMenuCell, SurfaceMenuPage, SurfaceMenuSnapshot } from '../../shared/bridge/protocol.js';
import { t } from '../../shared/strings/strings.js';
import { fileUrl } from '../../shared/files/file-url.js';
import '../../shared/icons/icon.js';

export interface GridActions {
  matches: (cell: SurfaceMenuCell) => boolean;
  filtering: boolean;
  drop: (event: DragEvent, page: SurfaceMenuPage, row: number, column: number) => void;
  add: (page: SurfaceMenuPage, row: number, column: number) => void;
  emptyMenu: (event: MouseEvent, page: SurfaceMenuPage, row: number, column: number) => void;
  dragStart: (event: DragEvent, cell: SurfaceMenuCell) => void;
  activate: (cell: SurfaceMenuCell) => void;
  cellMenu: (event: MouseEvent, cell: SurfaceMenuCell,
    page: SurfaceMenuPage, row: number, column: number) => void;
}

export function findCell(snapshot: SurfaceMenuSnapshot | null,
  entryId: string | null): SurfaceMenuCell | undefined {
  return snapshot
    ? snapshot.common.concat(snapshot.context)
      .flatMap((page) => page.cells)
      .find((cell) => cell.entryId === entryId)
    : undefined;
}

export function matchesCell(cell: SurfaceMenuCell, query: string): boolean {
  const wanted = query.trim().toLowerCase();
  return wanted.length === 0
    || cell.macroName.toLowerCase().includes(wanted)
    || cell.description.toLowerCase().includes(wanted);
}

/** 一个半区的 4×3 格子；空格的点击、右键与整区的滚轮翻页都从页面交进来。 */
export function renderGrid(page: SurfaceMenuPage,
  wheel: (event: WheelEvent) => void, actions: GridActions): TemplateResult {
  const slots: TemplateResult[] = [];
  for (let row = 0; row < 3; row += 1) {
    for (let column = 0; column < 4; column += 1) {
      slots.push(renderCell(page, row, column, actions));
    }
  }

  return html`<div class="grid" @wheel=${wheel}>${slots}</div>`;
}

function renderCell(page: SurfaceMenuPage, row: number, column: number,
  actions: GridActions): TemplateResult {
  const cell = page.cells.find((candidate) => candidate.row === row && candidate.column === column);
  if (!cell) {
    return html`<button class="cell empty" title=${t('Menu.AddCell')}
      @dragover=${(event: DragEvent) => event.preventDefault()}
      @drop=${(event: DragEvent) => actions.drop(event, page, row, column)}
      @click=${() => actions.add(page, row, column)}
      @contextmenu=${(event: MouseEvent) => actions.emptyMenu(event, page, row, column)}>
      <wg-icon name="SquarePlus" .size=${24} .strokeWidth=${1.4}></wg-icon>
    </button>`;
  }

  const dimmed = actions.filtering && !actions.matches(cell);
  return html`
    <button class="cell ${dimmed ? 'dimmed' : ''}" title=${cell.description.length > 0
        ? cell.macroName + '\n' + cell.description : cell.macroName}
      draggable="true"
      @dragstart=${(event: DragEvent) => actions.dragStart(event, cell)}
      @dragover=${(event: DragEvent) => event.preventDefault()}
      @drop=${(event: DragEvent) => actions.drop(event, page, row, column)}
      @click=${() => actions.activate(cell)}
      @contextmenu=${(event: MouseEvent) => actions.cellMenu(event, cell, page, row, column)}>
      ${cell.iconKind === 'image' && cell.iconValue
        ? html`<img src=${fileUrl(cell.iconValue)} alt="" />`
        : cell.iconKind === 'builtin' && cell.iconValue
          ? html`<wg-icon name=${cell.iconValue} .size=${44} .strokeWidth=${1.5}></wg-icon>`
          : html`<span class="name">${cell.macroName}</span>`}
    </button>
  `;
}