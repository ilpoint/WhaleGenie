import { html, nothing, type TemplateResult } from 'lit';
import type { SurfaceMenuMacro } from '../../shared/bridge/protocol.js';
import { t as translate } from '../../shared/strings/strings.js';
import '../../shared/icons/icon.js';

/** The second-level chooser opened from a cell's context menu. */
export interface MacroMenuState {
  pageId: string;
  row: number;
  column: number;
  x: number;
  y: number;
  withIcon: boolean;
}

export interface MacroMenuActions {
  query: (value: string) => void;
  choose: (macro: SurfaceMenuMacro) => void;
  cancel: () => void;
}

export function renderMacroMenu(state: MacroMenuState | null, macros: SurfaceMenuMacro[],
  query: string, loading: boolean, actions: MacroMenuActions): TemplateResult | typeof nothing {
  if (!state) {
    return nothing;
  }

  const wanted = query.trim().toLocaleLowerCase();
  const shown = wanted.length === 0
    ? macros
    : macros.filter((macro) => macro.name.toLocaleLowerCase().includes(wanted)
      || macro.preview.toLocaleLowerCase().includes(wanted));
  return html`
    <div class="context macro-menu" style=${'left:' + state.x + 'px;top:' + state.y + 'px'}>
      <div class="macro-head">
        <strong>${translate('Menu.PickMacro')}</strong>
        <button class="plain" @click=${actions.cancel} aria-label=${translate('Common.Cancel')}>
          <wg-icon name="X" .size=${13}></wg-icon></button>
      </div>
      <input class="macro-search" type="search" placeholder=${translate('Menu.PickMacroSearch')}
        .value=${query}
        @input=${(event: Event) => actions.query((event.target as HTMLInputElement).value)} />
      <div class="macro-list">
        ${loading ? html`<span class="note">…</span>` : nothing}
        ${!loading && shown.length === 0
          ? html`<span class="note">${translate('Menu.MacroNoResults')}</span>` : nothing}
        ${shown.map((macro) => html`
          <button class="macro-row" @click=${() => actions.choose(macro)}>
            <strong>${macro.name}</strong>
            <span>${macro.preview}</span>
          </button>`)}
      </div>
    </div>
  `;
}
