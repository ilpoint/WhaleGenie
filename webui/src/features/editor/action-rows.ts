import { LitElement, css, html, nothing, type TemplateResult } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import type {
  EditorActionField,
  EditorColourRow,
  EditorImageRow,
  EditorKeyRow,
  EditorNarrowRow,
  EditorRegionRow,
  EditorKeyPad,
} from '../../shared/bridge/protocol.js';
import { keyboardChord } from '../../shared/input/capture.js';
import { t } from '../../shared/strings/strings.js';
import { editor } from './editor-store.js';
import { editorBase } from './editor-css.js';
import './key-pad.js';

/** The row families of an action field: regions, pictures, colours, colour points, keys, narrowing. */
@customElement('wg-action-rows')
export class WgActionRows extends LitElement {
  @property({ attribute: false })
  public field!: EditorActionField;

  @state()
  private pad: EditorKeyPad | null = null;

  @state()
  private padIndex = -1;

  private captureIndex = -1;

  private get stepId(): string {
    return editor.get().actionForm?.stepId ?? '';
  }

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
    .image-row { display: grid; grid-template-columns: 72px minmax(0, 1fr) auto; gap: var(--spacingHorizontalS); align-items: start; padding: var(--spacingVerticalXS) 0; border-top: var(--strokeWidthThin) solid var(--colorNeutralStroke2); }
    .image-row img { width: 72px; height: 52px; object-fit: contain; border: var(--strokeWidthThin) solid var(--colorNeutralStroke2); border-radius: var(--borderRadiusSmall); background: var(--colorNeutralBackground2); }
    .image-row .missing { display: grid; place-items: center; width: 72px; height: 52px; color: var(--colorNeutralForeground3); font-size: var(--fontSizeBase100); border: 1px dashed var(--colorNeutralStroke2); border-radius: var(--borderRadiusSmall); }
    .image-row .problem { color: var(--colorStatusDangerForeground1); font-size: var(--fontSizeBase100); }
    .image-row .file { color: var(--colorNeutralForeground3); font-size: var(--fontSizeBase100); overflow-wrap: anywhere; }
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
      <div class="row" style="grid-template-columns: repeat(4, minmax(0, 1fr)) 96px 74px 24px">
        <span class="head">${t('Add.RegionXShort')}</span>
        <span class="head">${t('Add.RegionYShort')}</span>
        <span class="head">${t('Add.RegionWidthShort')}</span>
        <span class="head">${t('Add.RegionHeightShort')}</span>
        <span class="head">${t('Add.RegionOrVariable')}</span>
        <span></span><span></span>
      </div>
      ${field.regions.length === 0
        ? html`<p class="hint">${t('Add.RegionEmpty')}</p>`
        : field.regions.map((row, index) => this.renderRegion(field, row, index))}
      <button class="chip add" type="button"
        @click=${() => void editor.action.pickAction(field.name, 'region')}>${t('Add.PickRegion')}</button>
    `;
  }

  private renderRegion(field: EditorActionField, row: EditorRegionRow, index: number): TemplateResult {
    return html`
      <div class="row" style="grid-template-columns: repeat(4, minmax(0, 1fr)) 96px 74px 24px">
        ${(['x', 'y', 'width', 'height'] as const).map((column) => html`
          <div class="cell">
            <input .value=${row[column].text} @change=${(event: Event) => void editor.action.patchActionRow(
              field.name, index, column, (event.target as HTMLInputElement).value)} />
            <button class="chip" type="button"
              @click=${() => void editor.value.openRowValue(this.stepId, field.name, index, column)}>ƒ</button>
          </div>
        `)}
        <div class="cell">
          <input .value=${row.variable.text} @change=${(event: Event) => void editor.action.patchActionRow(
            field.name, index, 'variable', (event.target as HTMLInputElement).value)} />
          <button class="chip" type="button"
            @click=${() => void editor.value.openRowValue(this.stepId, field.name, index, 'variable')}>ƒ</button>
        </div>
        <button class="chip" type="button" title=${t('Add.PickRegionHint')}
          @click=${() => void editor.action.pickAction(field.name, 'region', index)}>⌖</button>
        <button class="chip" type="button" title=${t('Editor.RemoveRow')}
          @click=${() => void editor.action.removeActionRow(field.name, index)}>×</button>
      </div>
    `;
  }

  private renderImages(field: EditorActionField): TemplateResult {
    return html`
      ${field.images.length === 0
        ? html`<p class="hint">${t('Add.ImageEmpty')}</p>`
        : field.images.map((row, index) => this.renderImage(field, row, index))}
      <button class="chip add" type="button"
        @click=${() => void editor.action.addActionRow(field.name)}>${t('Add.ImageAdd')}</button>
    `;
  }

  private renderImage(field: EditorActionField, row: EditorImageRow, index: number): TemplateResult {
    return html`
      <div class="image-row">
        ${row.preview?.dataUrl
          ? html`<img src=${row.preview.dataUrl} alt=${row.preview.fileName || row.text} />`
          : html`<span class="missing">${t('Add.ImageEmpty')}</span>`}
        <div class="row" style="gap: 3px">
          <input .value=${row.text} @change=${(event: Event) => void editor.action.patchActionRow(
            field.name, index, 'image', (event.target as HTMLInputElement).value)} />
          ${row.preview?.problem
            ? html`<span class="problem">${row.preview.problem}</span>`
            : html`<span class="file">${row.preview?.fileName ?? ''}</span>`}
        </div>
        <div class="row" style="grid-template-columns: repeat(6, auto); gap: 2px">
          <button class="chip" type="button"
            @click=${() => void editor.action.pickAction(field.name, 'path', index)}>${t('Editor.Browse')}</button>
          <button class="chip" type="button"
            @click=${() => void editor.action.pickAction(field.name, 'capture', index)}>${t('Add.CaptureImage')}</button>
          <button class="chip" type="button"
            @click=${() => void editor.action.patchActionRow(field.name, index, 'image', '')}>${t('Add.ClearImage')}</button>
          <button class="chip" type="button" ?disabled=${!row.canMoveUp}
            aria-label=${t('Add.ImageMoveUp')}
            @click=${() => void editor.action.moveActionRow(field.name, index, -1)}>↑</button>
          <button class="chip" type="button" ?disabled=${!row.canMoveDown}
            aria-label=${t('Add.ImageMoveDown')}
            @click=${() => void editor.action.moveActionRow(field.name, index, 1)}>↓</button>
          <button class="chip" type="button"
            aria-label=${t('Add.ImageRemove')}
            @click=${() => void editor.action.removeActionRow(field.name, index)}>×</button>
        </div>
      </div>
    `;
  }

  private renderColourPoints(field: EditorActionField): TemplateResult {
    return html`
      ${field.colourPoints.length === 0
        ? html`<p class="hint">${t('Add.ColourPointEmpty')}</p>`
        : nothing}
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
            @change=${(event: Event) => void editor.action.patchActionRow(field.name, index, 'mustNot',
              (event.target as HTMLInputElement).checked ? 'true' : 'false')} />
          ${this.colourPointCell(field, index, 'tolerance', row.tolerance)}
          <button class="chip" type="button"
            @click=${() => void editor.action.removeActionRow(field.name, index)}>×</button>
        </div>
      `)}
      <button class="chip add" type="button"
        @click=${() => void editor.action.addActionRow(field.name)}>${t('Add.ColourPointAdd')}</button>
      <button class="chip add" type="button"
        @click=${() => void editor.action.pickAction(field.name, 'lookColours')}>${t('Add.PickColours')}</button>
    `;
  }

  private colourPointCell(field: EditorActionField, index: number,
    column: string, value: string): TemplateResult {
    return html`
      <div class="cell">
        <input .value=${value} @change=${(event: Event) => void editor.action.patchActionRow(
          field.name, index, column, (event.target as HTMLInputElement).value)} />
        <button class="chip" type="button"
          @click=${() => void editor.value.openRowValue(this.stepId, field.name, index, column)}>ƒ</button>
      </div>
    `;
  }

  private renderColours(field: EditorActionField): TemplateResult {
    return html`
      ${field.colours.map((row, index) => this.renderColour(field, row, index))}
      <button class="chip add" type="button"
        @click=${() => void editor.action.addActionRow(field.name)}>${t('Editor.AddRow')}</button>
    `;
  }

  private renderColour(field: EditorActionField, row: EditorColourRow, index: number): TemplateResult {
    return html`
      <div class="row" style="grid-template-columns: 64px minmax(0, 1fr) 96px 24px">
        <input type="color" .value=${/^#[0-9a-f]{6}$/i.test(row.colour) ? row.colour : '#000000'}
          @input=${(event: Event) => void editor.action.patchActionRow(
            field.name, index, 'colour', (event.target as HTMLInputElement).value)} />
        <div class="cell">
          <input .value=${row.colour} @change=${(event: Event) => void editor.action.patchActionRow(
            field.name, index, 'colour', (event.target as HTMLInputElement).value)} />
          <button class="chip" type="button"
            @click=${() => void editor.value.openRowValue(this.stepId, field.name, index, 'colour')}>ƒ</button>
        </div>
        <div class="cell">
          <input type="number" min="0" .value=${row.tolerance}
            @change=${(event: Event) => void editor.action.patchActionRow(
              field.name, index, 'tolerance', (event.target as HTMLInputElement).value)} />
          <button class="chip" type="button"
            @click=${() => void editor.value.openRowValue(this.stepId, field.name, index, 'tolerance')}>ƒ</button>
        </div>
        <button class="chip" type="button"
          @click=${() => void editor.action.removeActionRow(field.name, index)}>×</button>
      </div>
    `;
  }

  private renderKeys(field: EditorActionField): TemplateResult {
    return html`
      ${field.keys.length === 0
        ? html`<p class="hint">${t('Add.KeyRunEmpty')}</p>`
        : nothing}
      ${field.keys.map((row, index) => this.renderKey(field, row, index))}
      <button class="chip add" type="button"
        @click=${() => void editor.action.addActionRow(field.name)}>${t('Add.KeyRunAdd')}</button>
      ${this.pad
        ? html`<wg-key-pad .pad=${this.pad} label=${t('Add.KeyRunPress').replace('{0}', String(this.padIndex + 1))}
            @wg-pad-key=${(event: CustomEvent<{ value: string }>) => void editor.action.padKey(
              field.name, event.detail.value, this.padIndex, 'keys')}
            @wg-pad-close=${() => { this.pad = null; }}></wg-key-pad>`
        : nothing}
    `;
  }

  private renderKey(field: EditorActionField, row: EditorKeyRow, index: number): TemplateResult {
    return html`
      <div class="row" style="grid-template-columns: minmax(0, 1fr) 88px 88px 24px">
        <div class="cell">
          <input .value=${row.keys} @change=${(event: Event) => void editor.action.patchActionRow(
            field.name, index, 'keys', (event.target as HTMLInputElement).value)} />
          <button class="chip" type="button"
            @click=${() => void editor.value.openRowValue(this.stepId, field.name, index, 'keys')}>ƒ</button>
          <button class="chip" type="button" title=${t('Add.OpenKeyPadHint')}
            @click=${() => void this.openPad(index)}>⌨</button>
          <button class="chip" type="button" title=${t('Add.CaptureKeyHint')}
            @click=${() => this.beginCapture(index)}>⌨+</button>
        </div>
        <div class="cell">
          <input type="number" min="0" .value=${row.hold}
            @change=${(event: Event) => void editor.action.patchActionRow(
              field.name, index, 'hold', (event.target as HTMLInputElement).value)} />
          <button class="chip" type="button"
            @click=${() => void editor.value.openRowValue(this.stepId, field.name, index, 'hold')}>ƒ</button>
        </div>
        <div class="cell">
          <input type="number" min="0" .value=${row.gap}
            @change=${(event: Event) => void editor.action.patchActionRow(
              field.name, index, 'gap', (event.target as HTMLInputElement).value)} />
          <button class="chip" type="button"
            @click=${() => void editor.value.openRowValue(this.stepId, field.name, index, 'gap')}>ƒ</button>
        </div>
        <button class="chip" type="button"
          @click=${() => void editor.action.removeActionRow(field.name, index)}>×</button>
      </div>
    `;
  }

  private async openPad(index: number): Promise<void> {
    const pad = await editor.action.keyPad('keys');
    if (pad) {
      this.padIndex = index;
      this.pad = pad;
    }
  }

  private beginCapture(index: number): void {
    this.captureIndex = index;
    window.addEventListener('keydown', this.onCaptureKey, true);
  }

  private onCaptureKey = (event: KeyboardEvent): void => {
    if (this.captureIndex < 0) {
      return;
    }

    const binding = keyboardChord(event);
    if (!binding) {
      return;
    }

    event.preventDefault();
    const index = this.captureIndex;
    this.captureIndex = -1;
    window.removeEventListener('keydown', this.onCaptureKey, true);
    void editor.action.padKey(this.field.name, binding, index, 'keys');
  };

  private renderNarrow(field: EditorActionField): TemplateResult {
    return html`
      ${field.narrowRows.map((row, index) => this.renderNarrowRow(field, row, index))}
      <button class="chip add" type="button"
        @click=${() => void editor.action.addActionRow(field.name)}>${t('Add.NarrowAdd')}</button>
    `;
  }

  private renderNarrowRow(field: EditorActionField, row: EditorNarrowRow, index: number): TemplateResult {
    return html`
      <div class="row" style="grid-template-columns: 140px minmax(0, 1fr) auto 24px">
        <select .value=${row.how} @change=${(event: Event) => void editor.action.patchActionRow(
          field.name, index, 'how', (event.target as HTMLSelectElement).value)}>
          ${row.ways.map((way) => html`<option value=${way.value}>${way.label}</option>`)}
        </select>
        <div class="cell">
          <input .value=${row.value} ?disabled=${!row.needsValue}
            @change=${(event: Event) => void editor.action.patchActionRow(
              field.name, index, 'value', (event.target as HTMLInputElement).value)} />
          <button class="chip" type="button" ?disabled=${!row.needsValue}
            @click=${() => void editor.value.openRowValue(this.stepId, field.name, index, 'value')}>ƒ</button>
        </div>
        <span class="count">${row.count}</span>
        <button class="chip" type="button" title=${t('Add.NarrowLookHint')}
          @click=${() => void editor.action.testNarrow(field.name, index)}>⌕</button>
        <button class="chip" type="button"
          @click=${() => void editor.action.pickAction(field.name, 'narrow', index)}>⌖</button>
        <button class="chip" type="button"
          @click=${() => void editor.action.removeActionRow(field.name, index)}>×</button>
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
                @change=${(event: Event) => void editor.action.patchActionRow(field.name, index,
                  column.name, (event.target as HTMLInputElement).value)} />
              <button class="chip" type="button"
                @click=${() => void editor.value.openRowValue(this.stepId, field.name, index, column.name)}>ƒ</button>
            </div>
          `)}
          <button class="chip" type="button"
            @click=${() => void editor.action.removeActionRow(field.name, index)}>×</button>
        </div>
      `)}
      <button class="chip add" type="button"
        @click=${() => void editor.action.addActionRow(field.name)}>${t('Editor.AddRow')}</button>
    `;
  }

}

declare global {
  interface HTMLElementTagNameMap {
    'wg-action-rows': WgActionRows;
  }
}