import { html, nothing, type TemplateResult } from 'lit';
import type { SurfaceMenuCell } from '../../shared/bridge/protocol.js';
import { t } from '../../shared/strings/strings.js';
import '../../shared/icons/icon.js';

/** 菜单格子的右键菜单状态与动作；页面只持有位置，画与命令留在这一份。 */
export interface CellMenuState {
  cell: SurfaceMenuCell;
  pageId: string;
  row: number;
  column: number;
  x: number;
  y: number;
}

export interface PageMenuState {
  pageId: string;
  x: number;
  y: number;
}

export interface EmptyCellMenuState {
  pageId: string;
  row: number;
  column: number;
  x: number;
  y: number;
}

export interface CellMenuActions {
  edit: (cell: SurfaceMenuCell) => void;
  debug: (cell: SurfaceMenuCell) => void;
  chooseIcon: (cell: SurfaceMenuCell) => void;
  replace: (cell: SurfaceMenuCell) => void;
  addRule: () => void;
  block: () => void;
}

export function renderCellMenu(state: CellMenuState | null,
  actions: CellMenuActions): TemplateResult | typeof nothing {
  if (!state) {
    return nothing;
  }

  const { cell, x, y } = state;
  return html`
    <div class="context" style=${'left:' + x + 'px;top:' + y + 'px'}>
      <button @click=${() => actions.edit(cell)}>
        <wg-icon name="Pencil" .size=${14}></wg-icon>${t('Menu.EditMacro')}</button>
      <button @click=${() => actions.debug(cell)}>
        <wg-icon name="BugPlay" .size=${14}></wg-icon>${t('Menu.Debug')}</button>
      <button @click=${() => actions.chooseIcon(cell)}>
        <wg-icon name="Image" .size=${14}></wg-icon>${t('Menu.ChooseIcon')}</button>
      <button @click=${() => actions.replace(cell)}>
        <wg-icon name="RefreshCw" .size=${14}></wg-icon>${t('Menu.Replace')}</button>
      <button @click=${() => actions.addRule()}>
        <wg-icon name="SquarePlus" .size=${14}></wg-icon>${t('Menu.AddRule')}</button>
      <button @click=${() => actions.block()}>
        <wg-icon name="Ban" .size=${14}></wg-icon>${t('Menu.Block')}</button>
    </div>
  `;
}

export function renderEmptyCellMenu(state: EmptyCellMenuState | null,
  add: (withIcon: boolean) => void): TemplateResult | typeof nothing {
  if (!state) {
    return nothing;
  }

  return html`
    <div class="context" style=${'left:' + state.x + 'px;top:' + state.y + 'px'}>
      <button @click=${() => add(false)}>
        <wg-icon name="SquarePlus" .size=${14}></wg-icon>${t('Menu.AddCell')}</button>
      <button @click=${() => add(true)}>
        <wg-icon name="ImagePlus" .size=${14}></wg-icon>${t('Menu.AddCellIcon')}</button>
    </div>
  `;
}

export function renderPageMenu(state: PageMenuState | null,
  remove: (pageId: string) => void): TemplateResult | typeof nothing {
  if (!state) {
    return nothing;
  }

  return html`
    <div class="context" style=${'left:' + state.x + 'px;top:' + state.y + 'px'}>
      <button @click=${() => remove(state.pageId)}>
        <wg-icon name="X" .size=${14}></wg-icon>${t('Menu.RemovePage')}</button>
    </div>
  `;
}
