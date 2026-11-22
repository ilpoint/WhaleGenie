import { LitElement, css, html, nothing } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import type { EditorActionRow } from '../../shared/bridge/protocol.js';
import { editor } from './editor-store.js';
import { editorBase } from './editor-css.js';
import './action-field.js';

/** One page of an action form: the rows the C# form laid out, with their block headings. */
@customElement('wg-action-page')
export class WgActionPage extends LitElement {
  @property({ attribute: false })
  public rows: EditorActionRow[] = [];

  static styles = [editorBase, css`
    :host { display: grid; gap: var(--spacingVerticalM); }
    .group { display: grid; gap: var(--spacingVerticalXS); padding-bottom: var(--spacingVerticalS); border-bottom: var(--strokeWidthThin) solid var(--colorNeutralStroke2); }
    .group-title { display: flex; align-items: baseline; gap: var(--spacingHorizontalS); font-weight: var(--fontWeightSemibold); color: var(--colorNeutralForeground1); }
    .group-title .index { color: var(--colorBrandForeground2); font-family: var(--fontFamilyMonospace); font-size: var(--fontSizeBase100); }
    .group-hint { color: var(--colorNeutralForeground3); font-size: var(--fontSizeBase100); }
    .row { display: grid; gap: var(--spacingHorizontalS); align-items: start; min-width: 0; }
    .row.one { grid-template-columns: minmax(0, 1fr); }
    .row.two { grid-template-columns: repeat(2, minmax(0, 1fr)); }
    .row.three { grid-template-columns: repeat(3, minmax(0, 1fr)); }
    .row.four { grid-template-columns: repeat(4, minmax(0, 1fr)); }
  `];

  render() {
    return this.rows.map((row) => html`
      ${row.isGroupStart
        ? html`<div class="group">
            <div class="group-title">
              <span class="index">${row.groupIndex}</span>
              <span>${row.groupTitle}</span>
            </div>
            ${row.groupHint ? html`<span class="group-hint">${row.groupHint}</span>` : nothing}
          </div>`
        : nothing}
      <div class=${'row ' + this.columns(row)}>
        ${row.cells.map((cell) => html`<wg-action-field .field=${cell}></wg-action-field>`)}
      </div>
      ${row.picker
        ? html`<button class="chip" type="button" title=${row.picker.hint}
            @click=${() => void this.pick(row)}>${row.picker.label}</button>`
        : nothing}
    `);
  }

  private columns(row: EditorActionRow): string {
    return row.cells.length >= 4 ? 'four' : row.cells.length === 3 ? 'three' : row.cells.length === 2 ? 'two' : 'one';
  }

  private pick(row: EditorActionRow): void {
    const name = row.cells[0]?.name;
    if (!name || !row.picker) {
      return;
    }

    void editor.pickAction(name, row.picker.mode);
  }
}

declare global {
  interface HTMLElementTagNameMap {
    'wg-action-page': WgActionPage;
  }
}
