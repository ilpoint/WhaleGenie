import { LitElement, css, html, nothing, type TemplateResult } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import type { EditorActionField, EditorGlobal, EditorVariable } from '../../shared/bridge/protocol.js';
import { icon } from '../../shared/icons.js';
import { t } from '../../shared/strings/strings.js';
import { editor } from './editor-store.js';
import { StoreController } from '../../shared/lit/store-controller.js';
import { editorBase } from './editor-css.js';
import './value-field.js';

const dialogStyles = css`
  :host {
    display: contents;
  }

  .overlay {
    position: fixed;
    inset: 0;
    z-index: 85;
    display: grid;
    place-items: center;
    padding: var(--spacingVerticalXL);
    background: var(--shell-scrim);
  }

  .dialog {
    display: flex;
    flex-direction: column;
    width: min(760px, 100%);
    max-height: min(640px, 100%);
    min-height: 0;
    padding: var(--spacingVerticalL);
    border: var(--strokeWidthThin) solid var(--colorNeutralStroke1);
    border-radius: var(--borderRadiusLarge);
    background: var(--colorNeutralBackground1);
    box-shadow: var(--shadow16);
  }

  .dialog-head {
    display: flex;
    align-items: baseline;
    gap: var(--spacingHorizontalS);
    flex: none;
    margin-bottom: var(--spacingVerticalS);
  }

  .dialog-head h2 {
    margin: 0 auto 0 0;
    font-size: var(--fontSizeBase400);
    font-weight: var(--fontWeightSemibold);
  }

  .rows {
    flex: 1;
    min-height: 0;
    overflow: auto;
    display: grid;
    gap: var(--spacingVerticalXS);
  }

  .row {
    display: grid;
    gap: 3px;
    padding: var(--spacingVerticalXS) var(--spacingHorizontalS);
    border: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
    border-radius: var(--borderRadiusMedium);
    background: var(--colorNeutralBackground2);
  }

  .row-line {
    display: grid;
    grid-template-columns: minmax(0, 1fr) 96px minmax(0, 1fr) minmax(0, 1.2fr) 24px;
    gap: 3px;
    align-items: center;
  }

  .row-line input,
  .row-line select,
  .row-line wg-value-field {
    min-width: 0;
    width: 100%;
  }

  .row-line.full {
    grid-template-columns: minmax(0, 1fr) 96px minmax(0, 1fr) minmax(0, 1.2fr) 24px 90px;
  }

  .footer {
    flex: none;
    display: flex;
    justify-content: flex-end;
    gap: var(--spacingHorizontalS);
    margin-top: var(--spacingVerticalM);
  }

  .empty {
    padding: var(--spacingVerticalXL);
    color: var(--colorNeutralForeground3);
    font-size: var(--fontSizeBase200);
    text-align: center;
  }

  .hint {
    font-size: var(--fontSizeBase100);
  }

  .source {
    color: var(--colorNeutralForeground3);
    font-size: var(--fontSizeBase100);
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
`;

@customElement('wg-local-variables')
export class WgLocalVariables extends LitElement {
  @property({ type: Boolean, reflect: true })
  open = false;

  static styles = [editorBase, dialogStyles];

  protected readonly editorController = new StoreController<void>(this, editor);

  render() {
    if (!this.open) {
      return nothing;
    }

    const snapshot = editor.get().snapshot;
    return html`
      <div class="overlay" @click=${() => this.close()}>
        <div class="dialog" role="dialog" aria-modal="true" @click=${(event: Event) => event.stopPropagation()}>
          ${this.renderBody(snapshot)}
        </div>
      </div>
    `;
  }

  private renderBody(snapshot: ReturnType<typeof editor.get>['snapshot']): TemplateResult {
    if (!snapshot) {
      return html`<p class="empty">${t('Editor.NoDocument')}</p>`;
    }

    return html`
      <div class="dialog-head">
        <h2>${t('LocalVariables.Title')}</h2>
        <span class="hint">${t('LocalVariables.Hint')}</span>
      </div>
      <div class="rows">
        ${snapshot.variables.length === 0
          ? html`<p class="empty">${t('LocalVariables.Empty')}</p>`
          : snapshot.variables.map((variable) => html`
            <div class="row">
              <div class="row-line">
                <input .value=${variable.name} ?readonly=${!variable.isDeclared}
                  placeholder=${t('LocalVariables.New')}
                  @change=${(event: Event) => void editor.variables.patchVariable(variable.name, 'name',
                    (event.target as HTMLInputElement).value)} />
                <select .value=${variable.type} ?disabled=${!variable.isDeclared}
                  @change=${(event: Event) => void editor.variables.patchVariable(variable.name, 'type',
                    (event.target as HTMLSelectElement).value)}>
                  ${(editor.get().catalogue?.variableTypes ?? []).map((type) => html`
                    <option value=${type.value}>${type.label}</option>
                  `)}
                </select>
                ${variable.isDeclared
                  ? html`<wg-value-field .stepId=${''} .target=${'variable'}
                      .variableFamily=${'local'} .parameter=${variableInitialField(variable)}></wg-value-field>`
                  : html`<span class="source">${variable.source}</span>`}
                <input .value=${variable.description}
                  @change=${(event: Event) => void editor.variables.patchVariable(variable.name, 'description',
                    (event.target as HTMLInputElement).value)} />
                ${variable.isDeclared
                  ? html`<button class="ghost" title=${t('LocalVariables.Delete')}
                      aria-label=${t('LocalVariables.Delete')}
                      @click=${() => void editor.variables.removeVariable(variable.name)}>
                      ${icon('close')}
                    </button>`
                  : html`<span></span>`}
              </div>
              ${variable.isDeclared
                ? nothing
                : html`<span class="source">${t('LocalVariables.StepHint')}</span>`}
            </div>
          `)}
      </div>
      <div class="footer">
        <button class="link-button" type="button" @click=${() => void editor.variables.addVariable()}>
          ${t('LocalVariables.New')}
        </button>
        <fluent-button appearance="subtle" @click=${() => this.close()}>
          ${t('Common.Close')}
        </fluent-button>
      </div>
    `;
  }

  private close(): void {
    this.open = false;
    this.dispatchEvent(new CustomEvent('wg-close', { bubbles: true, composed: true }));
  }
}

@customElement('wg-global-variables')
export class WgGlobalVariables extends LitElement {
  @property({ type: Boolean, reflect: true })
  open = false;

  static styles = [editorBase, dialogStyles];

  protected readonly editorController = new StoreController<void>(this, editor);

  render() {
    if (!this.open) {
      return nothing;
    }

    const snapshot = editor.get().snapshot;
    return html`
      <div class="overlay" @click=${() => this.close()}>
        <div class="dialog" role="dialog" aria-modal="true" @click=${(event: Event) => event.stopPropagation()}>
          ${this.renderBody(snapshot)}
        </div>
      </div>
    `;
  }

  private renderBody(snapshot: ReturnType<typeof editor.get>['snapshot']): TemplateResult {
    if (!snapshot) {
      return html`<p class="empty">${t('Editor.NoDocument')}</p>`;
    }

    return html`
      <div class="dialog-head">
        <h2>${t('GlobalVariables.Title')}</h2>
        <span class="hint">${t('GlobalVariables.Help')}</span>
      </div>
      <div class="rows">
        ${snapshot.globals.length === 0
          ? html`<p class="empty">${t('GlobalVariables.NoVariables')}</p>`
          : snapshot.globals.map((global) => html`
            <div class="row">
              <div class="row-line full">
                <input .value=${global.name}
                  @change=${(event: Event) => void editor.variables.patchGlobal(global.name, 'name',
                    (event.target as HTMLInputElement).value)} />
                <select .value=${global.type}
                  @change=${(event: Event) => void editor.variables.patchGlobal(global.name, 'type',
                    (event.target as HTMLSelectElement).value)}>
                  ${(editor.get().catalogue?.variableTypes ?? []).map((type) => html`
                    <option value=${type.value}>${type.label}</option>
                  `)}
                </select>
                <wg-value-field .stepId=${''} .target=${'variable'}
                  .variableFamily=${'global'} .parameter=${globalDefaultField(global)}></wg-value-field>
                <input .value=${global.description}
                  @change=${(event: Event) => void editor.variables.patchGlobal(global.name, 'description',
                    (event.target as HTMLInputElement).value)} />
                <label class="inline" title=${t('Variable.Persist')}>
                  <input type="checkbox" .checked=${global.persisted}
                    @change=${(event: Event) => void editor.variables.patchGlobal(global.name, 'persist',
                      String((event.target as HTMLInputElement).checked))} />
                </label>
                <button class="ghost" title=${t('GlobalVariables.Remove')}
                  aria-label=${t('GlobalVariables.Remove')}
                  @click=${() => void editor.variables.removeGlobal(global.name)}>${icon('close')}</button>
              </div>
              <span class="hint">${t('Variable.Persist')}</span>
            </div>
          `)}
      </div>
      <div class="footer">
        <button class="link-button" type="button" @click=${() => void editor.variables.addGlobal()}>
          ${t('GlobalVariables.New')}
        </button>
        <fluent-button appearance="subtle" @click=${() => this.close()}>
          ${t('Common.Close')}
        </fluent-button>
      </div>
    `;
  }

  private close(): void {
    this.open = false;
    this.dispatchEvent(new CustomEvent('wg-close', { bubbles: true, composed: true }));
  }
}

function variableValueField(name: string, label: string, value: string,
  parts: EditorActionField['parts']): EditorActionField {
  return {
    name, label, hint: '', kind: 'Text',
    valueForm: 'Value', value, display: value, parts, options: [], columns: [], condition: null,
    isApplicable: true, isDefault: false, isMissing: false, required: false, advanced: false,
    group: '', groupLabel: '', offersValue: true, takesFormula: true, showsNameMark: false,
    allowsNewName: false, isNumber: false, isNumberFormula: false, isDuration: false,
    unit: '', unitLabel: '', durationUnits: [], isPercent: false, percentValue: 0,
    channelNote: '', jitterRange: '', isMultiline: false, isCode: false, isColor: false,
    colour: '', isPath: false, pathIntent: '', pathFilter: '', isKey: false, isKeys: false,
    isSelector: false, isBrowserTarget: false, isImage: false, isImageList: false,
    isRegion: false, isRegionList: false, isColourPoints: false, isColourRows: false,
    isKeySequence: false, isNarrow: false, isWindow: false, isWindowRule: false,
    isMacro: false, isStepChoice: false, isRecord: false, isPoint: false, isRect: false,
    placeholder: t('LocalVariables.InitialPlaceholder'), hasToBeAnswered: false,
    isFollowingWindow: false,
    windowRuleSummary: '', regions: [], images: [], colours: [], colourPoints: [], keys: [],
    narrowRows: [], rows: [], macros: [], steps: [], candidates: [], windowPreview: null,
    imagePreview: null, minimum: null, maximum: null, increment: 1, jitterPercent: null,
    emptyMeans: 'Zero',
  } as unknown as EditorActionField;
}

function variableInitialField(variable: EditorVariable): EditorActionField {
  return variableValueField(variable.name, t('LocalVariables.Initial'), variable.initial,
    variable.initialParts);
}

function globalDefaultField(global: EditorGlobal): EditorActionField {
  return variableValueField(global.name, t('GlobalVariables.Default'), global.default,
    global.defaultParts);
}

declare global {
  interface HTMLElementTagNameMap {
    'wg-local-variables': WgLocalVariables;
    'wg-global-variables': WgGlobalVariables;
  }
}
