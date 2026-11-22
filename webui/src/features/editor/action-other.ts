import { LitElement, css, html } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import type { EditorActionForm } from '../../shared/bridge/protocol.js';
import { t } from '../../shared/strings/strings.js';
import { editor } from './editor-store.js';
import { editorBase } from './editor-css.js';

/** The step itself: its name, whether it runs, and the JSON node it will add. */
@customElement('wg-action-other')
export class WgActionOther extends LitElement {
  @property({ attribute: false })
  public form!: EditorActionForm;

  static styles = [editorBase, css`
    :host { display: grid; gap: var(--spacingVerticalM); }
    .id { display: flex; gap: var(--spacingHorizontalXS); align-items: center; }
    .id code { flex: 1; padding: 4px 6px; border: var(--strokeWidthThin) solid var(--colorNeutralStroke2); border-radius: var(--borderRadiusSmall); background: var(--colorNeutralBackground2); font-family: var(--fontFamilyMonospace); font-size: var(--fontSizeBase100); overflow-wrap: anywhere; }
    .json { margin: 0; padding: var(--spacingVerticalS); max-height: 40vh; overflow: auto; border: var(--strokeWidthThin) solid var(--colorNeutralStroke2); border-radius: var(--borderRadiusMedium); background: var(--colorNeutralBackground2); font-family: var(--fontFamilyMonospace); font-size: var(--fontSizeBase100); white-space: pre-wrap; }
    .hint { color: var(--colorNeutralForeground3); font-size: var(--fontSizeBase100); }
  `];

  render() {
    return html`
      <div>
        <div class="id">
          <code>${this.form.stepId}</code>
          <button class="chip" type="button"
            @click=${() => void this.copy()}>\u2398 ${t('Add.StepId')}</button>
        </div>
        <p class="hint">${t('Add.StepIdHint')}</p>
      </div>
      <label class="inline">
        <input type="checkbox" .checked=${this.form.meta.enabled}
          @change=${(event: Event) => void editor.action.patchActionMeta('enabled',
            (event.target as HTMLInputElement).checked ? 'true' : 'false')} />
        <span>${t('Add.StepEnabled')}</span>
      </label>
      <div>
        <h3>${t('Add.JsonNode')}</h3>
        <pre class="json">${this.form.json}</pre>
      </div>
    `;
  }

  private async copy(): Promise<void> {
    try {
      await navigator.clipboard.writeText(this.form.stepId);
    } catch {
      // A page that may not write the clipboard still leaves the id visible above.
    }
  }
}

declare global {
  interface HTMLElementTagNameMap {
    'wg-action-other': WgActionOther;
  }
}
