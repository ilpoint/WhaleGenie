import { LitElement, css, html } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import type { EditorActionField, EditorGlobalDraft } from '../../shared/bridge/protocol.js';
import { t } from '../../shared/strings/strings.js';
import { editor } from './editor-store.js';
import { editorBase } from './editor-css.js';
import './value-field.js';

/** The global declaration a "set variable" step carries when it writes a shared value. */
@customElement('wg-action-global')
export class WgActionGlobal extends LitElement {
  @property({ attribute: false })
  public draft!: EditorGlobalDraft;

  static styles = [editorBase, css`
    :host { display: grid; gap: var(--spacingVerticalS); padding: var(--spacingVerticalS); border: var(--strokeWidthThin) solid var(--colorNeutralStroke2); border-radius: var(--borderRadiusMedium); background: var(--colorNeutralBackground2); }
    .grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: var(--spacingVerticalS); }
    .field { display: grid; gap: 3px; }
    .field > span { color: var(--colorNeutralForeground2); font-size: var(--fontSizeBase200); }
    .problem { color: var(--colorStatusDangerForeground1); font-size: var(--fontSizeBase100); }
    .preview { color: var(--colorNeutralForeground3); font-size: var(--fontSizeBase100); }
  `];

  render() {
    const draft = this.draft;
    return html`
      <strong>${t('Add.GlobalDeclaration')}</strong>
      <div class="grid">
        <label class="field">
          <span>${t('GlobalVariables.Name')}</span>
          <input .value=${draft.name}
            @change=${(event: Event) => this.patch('name', (event.target as HTMLInputElement).value)} />
        </label>
        <label class="field">
          <span>${t('GlobalVariables.Type')}</span>
          <select .value=${draft.type}
            @change=${(event: Event) => this.patch('type', (event.target as HTMLSelectElement).value)}>
            ${draft.types.map((type) => html`<option value=${type.value}>${type.label}</option>`)}
          </select>
        </label>
        <label class="field">
          <span>${t('GlobalVariables.Default')}</span>
          <wg-value-field .stepId=${''} .target=${'global'}
            .parameter=${globalDefaultField(draft)}></wg-value-field>
        </label>
        <label class="field">
          <span>${t('GlobalVariables.Description')}</span>
          <input .value=${draft.description}
            @change=${(event: Event) => this.patch('description', (event.target as HTMLInputElement).value)} />
        </label>
      </div>
      <label class="inline">
        <input type="checkbox" .checked=${draft.persistValue}
          @change=${(event: Event) => this.patch('persist',
            (event.target as HTMLInputElement).checked ? 'true' : 'false')} />
        <span>${t('GlobalVariables.Persist')}</span>
      </label>
      ${draft.problem ? html`<span class="problem">${draft.problem}</span>` : ''}
    `;
  }

  private patch(field: string, value: string): void {
    void editor.action.patchActionGlobal(field, value);
  }
}

function globalDefaultField(draft: EditorGlobalDraft): EditorActionField {
  return {
    name: 'default', label: t('GlobalVariables.Default'), hint: '', kind: 'Text',
    valueForm: 'Value', value: draft.default, display: draft.default, parts: draft.defaultParts,
    options: [], columns: [], condition: null, isApplicable: true, isDefault: false, isMissing: false,
    required: false, advanced: false, group: '', groupLabel: '', offersValue: true,
    takesFormula: true, showsNameMark: false, allowsNewName: false, isNumber: false,
    isNumberFormula: false, isDuration: false, unit: '', unitLabel: '', durationUnits: [],
    isPercent: false, percentValue: 0, channelNote: '', jitterRange: '', isMultiline: false,
    isCode: false, isColor: false, colour: '', isPath: false, pathIntent: '', pathFilter: '',
    isKey: false, isKeys: false, isSelector: false, isBrowserTarget: false, isImage: false,
    isImageList: false, isRegion: false, isRegionList: false, isColourPoints: false,
    isColourRows: false, isKeySequence: false, isNarrow: false, isWindow: false,
    isWindowRule: false, isMacro: false, isStepChoice: false, isRecord: false, isPoint: false,
    isRect: false, placeholder: '', hasToBeAnswered: false, isFollowingWindow: false,
    windowRuleSummary: '', regions: [], images: [], colours: [], colourPoints: [], keys: [],
    narrowRows: [], rows: [], macros: [], steps: [], candidates: [], windowPreview: null,
    imagePreview: null, minimum: null, maximum: null, increment: 1, jitterPercent: null,
    emptyMeans: 'Zero',
  } as unknown as EditorActionField;
}

declare global {
  interface HTMLElementTagNameMap {
    'wg-action-global': WgActionGlobal;
  }
}
