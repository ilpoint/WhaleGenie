import { LitElement, css, html } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import type { EditorReturnsRow } from '../../shared/bridge/protocol.js';
import { t } from '../../shared/strings/strings.js';
import { editor } from './editor-store.js';
import { editorBase } from './editor-css.js';

/** The result table: what this step hands over, under which name, and what shape it has. */
@customElement('wg-action-returns')
export class WgActionReturns extends LitElement {
  @property({ attribute: false })
  public rows: EditorReturnsRow[] = [];

  static styles = [editorBase, css`
    :host { display: grid; gap: 3px; }
    .head, .line { display: grid; grid-template-columns: 44px minmax(0, 1fr) minmax(0, 0.9fr) minmax(0, 0.9fr); gap: var(--spacingHorizontalS); align-items: center; }
    .head { color: var(--colorNeutralForeground3); font-size: var(--fontSizeBase100); }
    .line { padding: var(--spacingVerticalXS) 0; border-top: var(--strokeWidthThin) solid var(--colorNeutralStroke2); }
    .line .label { overflow-wrap: anywhere; }
    .line .structure { color: var(--colorNeutralForeground3); font-family: var(--fontFamilyMonospace); font-size: var(--fontSizeBase100); overflow-wrap: anywhere; }
    .line input[type='text'] { width: 100%; min-width: 0; }
    .warning { grid-column: 2 / -1; color: var(--colorStatusDangerForeground1); font-size: var(--fontSizeBase100); }
    .fix { justify-self: start; border: 0; background: transparent; color: var(--colorBrandForeground2); cursor: pointer; padding: 0; font: inherit; }
    .empty { color: var(--colorNeutralForeground3); font-size: var(--fontSizeBase100); }
  `];

  render() {
    return html`
      <div class="head">
        <span>${t('Add.ReturnKeep')}</span>
        <span>${t('Add.ReturnResult')}</span>
        <span>${t('Add.ReturnShape')}</span>
        <span>${t('Add.ReturnName')}</span>
      </div>
      ${this.rows.length === 0
        ? html`<span class="empty">${t('Add.ReturnPickHint')}</span>`
        : this.rows.map((row) => html`
          <div class="line">
            <input type="checkbox" .checked=${row.ticked} aria-label=${row.label}
              @change=${(event: Event) => void editor.patchActionReturn(row.key, {
                ticked: (event.target as HTMLInputElement).checked,
              })} />
            <span class="label" title=${row.hint}>${row.label}</span>
            <span class="structure" title=${row.structureTip}>${row.structure}</span>
            <input type="text" .value=${row.name} ?disabled=${!row.ticked}
              aria-label=${t('Add.ReturnName')}
              @change=${(event: Event) => void editor.patchActionReturn(row.key, {
                name: (event.target as HTMLInputElement).value,
              })} />
            ${row.isNameTaken
              ? html`<span class="warning">${row.nameWarning}
                  <button class="fix" type="button"
                    @click=${() => void editor.fixActionReturnName(row.key)}>${t('Add.NameMakeUnique')}</button>
                </span>`
              : ''}
          </div>
        `)}
    `;
  }
}

declare global {
  interface HTMLElementTagNameMap {
    'wg-action-returns': WgActionReturns;
  }
}
