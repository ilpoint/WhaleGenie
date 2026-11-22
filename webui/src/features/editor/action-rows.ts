import { LitElement, css, html, nothing, type TemplateResult } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import type {
  EditorActionField,
  EditorColourRow,
  EditorImageRow,
  EditorKeyRow,
  EditorNarrowRow,
  EditorRegionRow,
} from '../../shared/bridge/protocol.js';
import { t } from '../../shared/strings/strings.js';
import { editor } from './editor-store.js';
import { editorBase } from './editor-css.js';

/** The row families of an action field: regions, pictures, colours, colour points, keys, narrowing. */
@customElement('wg-action-rows')
export class WgActionRows extends LitElement {
  @property({ attribute: false })
  public field!: EditorActionField;

  static styles = [editorBase, css`
    :host { display: grid; gap: 3px; min-width: 0; }
    .row { display: grid; gap: 3px; align-items: center; min-width: 0; }
    .row input, .row select { min-width: 0; width: 100%; }
    .head { color: var(--colorNeutralForeground3); font-size: var(--fontSizeBase100); }
    .add { justify-self: start; }
    .cell { display: flex; gap: 2px; min-width: 0; }
    .cell input { flex: 1; }
    .chip { flex: none; border: var(--strokeWidthThin) solid var(--colorNeutralStroke1); border-radius: var(--borderRadiusSmall); background: var(--colorNeutralBackground1); color: var(--colorBrandForeground2); font-size: var(--fontSizeBase100); cursor: pointer; }
    .wide { grid-column: 1 / -1; }
    .count { color: var(--colorNeutralForeground3); font-size: var(--fontSizeBase100); }
  `];

  render() {
    const field = this.field;
    if (field.isRegionList) {
      return this.renderRegions(field);
    }

    if (field.isImageList) {
      return this.renderImages(field);
    }

    if (field.isColourPoints) {
      return this.renderColourPoints(field);
    }

    if (field.isColourRows) {
      return this.renderColours(field);
    }

    if (field.isKeySequence) {
      return this.renderKeys(field);
    }

    if (field.isNarrow) {
      return this.renderNarrow(field);
    }

    return this.renderGeneric(field);
  }

  private renderRegions(field: EditorActionField): TemplateResult {
    return html`
      <div class="row" style="grid-template-columns: repeat(4, minmax(0, 1fr)) 96px 24px">
        <span class="head">${t('Add.RegionXShort')}</span>
        <span class="head">${t('Add.RegionYShort')}</span>
        <span class="head">${t('Add.RegionWidthShort')}</span>
        <span class="head">${t('Add.RegionHeightShort')}</span>
        <span class="head">${t('Add.RegionOrVariable')}</span>
        <span></span>
      </div>
      ${field.regions.map((row, index) => this.renderRegion(field, row, index))}
      <button class="chip add" type="button"
        @click=${() => void editor.addActionRow(field.name)}>${t('Add.RegionAdd')}</button>
    `;
  }

  private renderRegion(field: EditorActionField, row: EditorRegionRow, index: number): TemplateResult {
    return html`
      <div class="row" style="grid-template-columns: repeat(4, minmax(0, 1fr)) 96px 24px">
        ${(['x', 'y', 'width', 'height'] as const).map((column) => html`
          <div class="cell">
            <input .value=${row[column]} @change=${(event: Event) => void editor.patchActionRow(
              field.name, index, column, (event.target as HTMLInputElement).value)} />
            <button class="chip" type="button"
              @click=${() => void editor.openRowValue(this.stepId, field.name, index, column)}>ƒ</button>
          </div>
        `)}
        <input .value=${row.variable} @change=${(event: Event) => void editor.patchActionRow(
          field.name, index, 'variable', (event.target as HTMLInputElement).value)} />
        <button class="chip" type="button" title=${t('Editor.RemoveRow')}
          @click=${() => void editor.removeActionRow(field.name, index)}>×</button>
      </div>
    `;
  }

  private renderImages(field: EditorActionField): TemplateResult {
    return html`
      ${field.images.map((row, index) => this.renderImage(field, row, index))}
      <button class="chip add" type="button"
        @click=${() => void editor.addActionRow(field.name)}>${t('Add.ImageAdd')}</button>
    `;
  }

  private renderImage(field: EditorActionField, row: EditorImageRow, index: number): TemplateResult {
    return html`
      <div class="row" style="grid-template-columns: minmax(0, 1fr) auto auto auto auto auto">
        <input .value=${row.text} @change=${(event: Event) => void editor.patchActionRow(
          field.name, index, 'image', (event.target as HTMLInputElement).value)} />
        <button class="chip" type="button"
          @click=${() => void editor.pickImage(this.stepId, field.name)}>${t('Editor.Browse')}</button>
        <button class="chip" type="button"
          @click=${() => void editor.pickAction(field.name, 'capture', index)}>${t('Add.CaptureImage')}</button>
        <button class="chip" type="button" ?disabled=${!row.canMoveUp}
          @click=${() => void editor.moveActionRow(field.name, index, -1)}>↑</button>
        <button class="chip" type="button" ?disabled=${!row.canMoveDown}
          @click=${() => void editor.moveActionRow(field.name, index, 1)}>↓</button>
        <button class="chip" type="button"
          @click=${() => void editor.removeActionRow(field.name, index)}>×</button>
      </div>
    `;
  }

  private renderColourPoints(field: EditorActionField): TemplateResult {
    return html`
      <div class="row" style="grid-template-columns: repeat(5, minmax(0, 1fr)) 24px">
        <span class="head">dx</span><span class="head">dy</span><span class="head">${t('Add.ColourPointColour')}</span>
        <span class="head">${t('Add.ColourPointMustNot')}</span><span class="head">${t('Editor.ColorTolerance')}</span><span></span>
      </div>
      ${field.colourPoints.map((row, index) => html`
        <div class="row" style="grid-template-columns: repeat(5, minmax(0, 1fr)) 24px">
          ${this.colourPointCell(field, index, 'dx', row.dx)}
          ${this.colourPointCell(field, index, 'dy', row.dy)}
          ${this.colourPointCell(field, index, 'colour', row.colour)}
          <input type="checkbox" .checked=${row.mustNot}
            @change=${(event: Event) => void editor.patchActionRow(field.name, index, 'mustNot',
              (event.target as HTMLInputElement).checked ? 'true' : 'false')} />
          ${this.colourPointCell(field, index, 'tolerance', row.tolerance)}
          <button class="chip" type="button"
            @click=${() => void editor.removeActionRow(field.name, index)}>×</button>
        </div>
      `)}
      <button class="chip add" type="button"
        @click=${() => void editor.addActionRow(field.name)}>${t('Add.ColourPointAdd')}</button>
    `;
  }

  private colourPointCell(field: EditorActionField, index: number,
    column: string, value: string): TemplateResult {
    return html`
      <div class="cell">
        <input .value=${value} @change=${(event: Event) => void editor.patchActionRow(
          field.name, index, column, (event.target as HTMLInputElement).value)} />
        <button class="chip" type="button"
          @click=${() => void editor.openRowValue(this.stepId, field.name, index, column)}>ƒ</button>
      </div>
    `;
  }

  private renderColours(field: EditorActionField): TemplateResult {
    return html`
      ${field.colours.map((row, index) => this.renderColour(field, row, index))}
      <button class="chip add" type="button"
        @click=${() => void editor.addActionRow(field.name)}>${t('Editor.AddRow')}</button>
    `;
  }

  private renderColour(field: EditorActionField, row: EditorColourRow, index: number): TemplateResult {
    return html`
      <div class="row" style="grid-template-columns: 64px minmax(0, 1fr) 96px 24px">
        <input type="color" .value=${/^#[0-9a-f]{6}$/i.test(row.colour) ? row.colour : '#000000'}
          @input=${(event: Event) => void editor.patchActionRow(
            field.name, index, 'colour', (event.target as HTMLInputElement).value)} />
        <input .value=${row.colour} @change=${(event: Event) => void editor.patchActionRow(
          field.name, index, 'colour', (event.target as HTMLInputElement).value)} />
        <input type="number" min="0" .value=${row.tolerance}
          @change=${(event: Event) => void editor.patchActionRow(
            field.name, index, 'tolerance', (event.target as HTMLInputElement).value)} />
        <button class="chip" type="button"
          @click=${() => void editor.removeActionRow(field.name, index)}>×</button>
      </div>
    `;
  }

  private renderKeys(field: EditorActionField): TemplateResult {
    return html`
      ${field.keys.map((row, index) => this.renderKey(field, row, index))}
      <button class="chip add" type="button"
        @click=${() => void editor.addActionRow(field.name)}>${t('Add.KeyRunAdd')}</button>
    `;
  }

  private renderKey(field: EditorActionField, row: EditorKeyRow, index: number): TemplateResult {
    return html`
      <div class="row" style="grid-template-columns: minmax(0, 1fr) 88px 88px 24px">
        <div class="cell">
          <input .value=${row.keys} @change=${(event: Event) => void editor.patchActionRow(
            field.name, index, 'keys', (event.target as HTMLInputElement).value)} />
          <button class="chip" type="button" title=${t('Add.SetCombination')}>⌨</button>
        </div>
        <input type="number" min="0" .value=${row.hold}
          @change=${(event: Event) => void editor.patchActionRow(
            field.name, index, 'hold', (event.target as HTMLInputElement).value)} />
        <input type="number" min="0" .value=${row.gap}
          @change=${(event: Event) => void editor.patchActionRow(
            field.name, index, 'gap', (event.target as HTMLInputElement).value)} />
        <button class="chip" type="button"
          @click=${() => void editor.removeActionRow(field.name, index)}>×</button>
      </div>
    `;
  }

  private renderNarrow(field: EditorActionField): TemplateResult {
    return html`
      ${field.narrowRows.map((row, index) => this.renderNarrowRow(field, row, index))}
      <button class="chip add" type="button"
        @click=${() => void editor.addActionRow(field.name)}>${t('Add.NarrowAdd')}</button>
    `;
  }

  private renderNarrowRow(field: EditorActionField, row: EditorNarrowRow, index: number): TemplateResult {
    return html`
      <div class="row" style="grid-template-columns: 140px minmax(0, 1fr) auto 24px">
        <select .value=${row.how} @change=${(event: Event) => void editor.patchActionRow(
          field.name, index, 'how', (event.target as HTMLSelectElement).value)}>
          ${row.ways.map((way) => html`<option value=${way.value}>${way.label}</option>`)}
        </select>
        <input .value=${row.value} ?disabled=${!row.needsValue}
          @change=${(event: Event) => void editor.patchActionRow(
            field.name, index, 'value', (event.target as HTMLInputElement).value)} />
        <span class="count">${row.count}</span>
        <button class="chip" type="button"
          @click=${() => void editor.pickAction(field.name, 'narrow', index)}>⌖</button>
        <button class="chip" type="button"
          @click=${() => void editor.removeActionRow(field.name, index)}>×</button>
      </div>
    `;
  }

  private renderGeneric(field: EditorActionField): TemplateResult | typeof nothing {
    if (field.columns.length === 0) {
      return nothing;
    }

    return html`
      <div class="row" style=${'grid-template-columns: repeat(' + String(field.columns.length) + ', minmax(0, 1fr)) 24px'}>
        ${field.columns.map((column) => html`<span class="head">${column.name}</span>`)}
        <span></span>
      </div>
      ${field.rows.map((row, index) => html`
        <div class="row" style=${'grid-template-columns: repeat(' + String(field.columns.length) + ', minmax(0, 1fr)) 24px'}>
          ${field.columns.map((column, cell) => html`
            <div class="cell">
              <input .value=${row.cells[cell] ?? ''} aria-label=${column.name}
                @change=${(event: Event) => void editor.patchActionRow(field.name, index,
                  column.name, (event.target as HTMLInputElement).value)} />
              <button class="chip" type="button"
                @click=${() => void editor.openRowValue(this.stepId, field.name, index, column.name)}>ƒ</button>
            </div>
          `)}
          <button class="chip" type="button"
            @click=${() => void editor.removeActionRow(field.name, index)}>×</button>
        </div>
      `)}
      <button class="chip add" type="button"
        @click=${() => void editor.addActionRow(field.name)}>${t('Editor.AddRow')}</button>
    `;
  }

  private get stepId(): string {
    return editor.get().actionForm?.stepId ?? '';
  }
}

declare global {
  interface HTMLElementTagNameMap {
    'wg-action-rows': WgActionRows;
  }
}
