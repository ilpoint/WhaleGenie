import { LitElement, css, html, nothing } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import type { EditorActionField } from '../../shared/bridge/protocol.js';
import { t } from '../../shared/strings/strings.js';
import { editor } from './editor-store.js';
import { editorBase } from './editor-css.js';

/**
 * One value field in its native shape: the display lives inside a bordered box and the picker is
 * a chip on that box's right edge, never a second row underneath it. Every value form — text,
 * run, whole value and bare name — is read-only here because its shape is built in the token
 * window; a number can still be typed and carries the same chip at its edge.
 */
@customElement('wg-value-field')
export class WgValueField extends LitElement {
  @property({ attribute: false })
  stepId = '';

  @property({ attribute: false })
  parameter!: EditorActionField;

  /** What the chip opens: a field of the action form or one argument of a macro call. */
  @property({ attribute: false })
  target: 'field' | 'argument' | 'declaration' | 'global' | 'variable' = 'field';

  @property({ attribute: false })
  declarationIndex = -1;

  @property({ attribute: false })
  variableFamily: 'local' | 'global' = 'local';

  static styles = [editorBase, css`
    :host {
      display: block;
      min-width: 0;
    }

    .shell {
      position: relative;
      display: flex;
      align-items: center;
      width: 100%;
      min-height: 32px;
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke1);
      border-bottom-color: var(--colorNeutralStrokeAccessible);
      border-radius: var(--borderRadiusMedium);
      background: var(--colorNeutralBackground1);
      color: var(--colorNeutralForeground1);
      font-size: var(--fontSizeBase200);
    }

    .shell.readonly {
      padding: 5px 58px 5px var(--spacingHorizontalS);
      align-items: flex-start;
    }

    .shell.multiline {
      min-height: 58px;
    }

    .shell:focus-within {
      border-bottom-color: var(--colorBrandBackground);
    }

    .read {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 3px;
      min-width: 0;
      max-height: 180px;
      overflow: auto;
      padding: 2px 0;
      line-height: 1.5;
      white-space: pre-wrap;
      overflow-wrap: anywhere;
    }

    .multiline .read {
      max-height: 220px;
    }

    .placeholder {
      color: var(--colorNeutralForeground4);
    }

    .name-part {
      display: inline-flex;
      align-items: center;
      max-width: 100%;
      padding: 0 4px;
      border: var(--strokeWidthThin) solid var(--colorBrandStroke2);
      border-radius: var(--borderRadiusSmall);
      background: var(--colorBrandBackground2);
      color: var(--colorBrandForeground2);
      font-family: var(--fontFamilyMonospace);
      font-size: var(--fontSizeBase100);
      line-height: 17px;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    .swatch {
      flex: none;
      width: 18px;
      height: 18px;
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke1);
      border-radius: var(--borderRadiusSmall);
      margin-right: var(--spacingHorizontalXS);
    }

    .number-wrap {
      display: flex;
      align-items: center;
      gap: var(--spacingHorizontalXS);
      min-width: 0;
    }

    .number-wrap .shell {
      flex: 1;
      min-width: 0;
    }

    input.number {
      width: 100%;
      min-width: 0;
      height: 30px;
      padding: 0 58px 0 var(--spacingHorizontalS);
      border: 0;
      background: transparent;
      color: inherit;
      font: inherit;
      outline: none;
      appearance: textfield;
      -moz-appearance: textfield;
    }

    input.number::-webkit-outer-spin-button,
    input.number::-webkit-inner-spin-button {
      margin: 0;
      -webkit-appearance: none;
    }

    select.unit {
      flex: none;
      width: 94px;
      min-width: 0;
    }

    input.slider {
      width: 100%;
      margin-top: 4px;
    }

    .jitter {
      display: flex;
      align-items: center;
      gap: 4px;
      margin-top: 3px;
      color: var(--colorNeutralForeground3);
      font-size: var(--fontSizeBase100);
    }

    .jitter input {
      width: 72px;
    }

    .chip {
      position: absolute;
      top: 4px;
      right: 4px;
      display: inline-flex;
      align-items: center;
      justify-content: center;
      height: 24px;
      padding: 0 8px;
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke1);
      border-radius: var(--borderRadiusMedium);
      background: var(--colorNeutralBackground2);
      color: var(--colorNeutralForeground2);
      font: inherit;
      font-size: var(--fontSizeBase100);
      cursor: pointer;
    }

    .chip:hover {
      background: var(--colorNeutralBackground2Hover);
      color: var(--colorNeutralForeground1);
    }

    .chip:focus-visible {
      outline: var(--strokeWidthThicker) solid var(--colorStrokeFocus2);
      outline-offset: -1px;
    }

    .chip .glyph {
      margin-right: 3px;
      font-family: var(--fontFamilyMonospace);
      font-size: var(--fontSizeBase100);
      color: var(--colorBrandForeground2);
    }
  `];

  private openPicker(): void {
    if (this.target === 'argument') {
      void editor.value.openCallArgument(this.parameter.name);
      return;
    }

    if (this.target === 'declaration') {
      void editor.value.openDeclaration(this.declarationIndex);
      return;
    }

    if (this.target === 'global') {
      void editor.value.openGlobal();
      return;
    }

    if (this.target === 'variable') {
      void editor.value.openVariable(this.variableFamily, this.parameter.name);
      return;
    }

    void editor.value.openValue(this.stepId, this.parameter.name);
  }

  private patchNumber(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    void editor.action.patchActionField(this.parameter.name, 'number', value);
  }

  private patchUnit(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    void editor.action.patchActionField(this.parameter.name, 'unit', value);
  }

  private patchJitter(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    void editor.action.patchActionField(this.parameter.name, 'jitter', value);
  }

  private patchPercent(value: string): void {
    void editor.action.patchActionField(this.parameter.name, 'number', value);
  }

  private chip() {
    return this.parameter.offersValue
      ? html`
          <button class="chip" type="button" title=${t('Add.ValueHint')}
            @click=${() => this.openPicker()}>
            <span class="glyph">ƒ</span>${t('Add.Value')}
          </button>
        `
      : nothing;
  }

  private renderNumber(parameter: EditorActionField, chip: ReturnType<WgValueField['chip']>) {
    return html`
      <div class="number-wrap">
        <div class="shell">
          <input class="number" type="number" .value=${parameter.display || parameter.value}
            min=${parameter.minimum === null ? nothing : String(parameter.minimum)}
            max=${parameter.maximum === null ? nothing : String(parameter.maximum)}
            step=${String(parameter.increment)}
            aria-label=${parameter.label}
            @change=${(event: Event) => this.patchNumber(event)} />
          ${chip}
        </div>
        ${parameter.isDuration && parameter.durationUnits.length > 0
          ? html`<select class="unit" aria-label=${parameter.unitLabel}
              .value=${parameter.unit}
              @change=${(event: Event) => this.patchUnit(event)}>
              ${parameter.durationUnits.map((unit) => html`
                <option value=${unit.value}>${unit.label}</option>`)}
            </select>`
          : nothing}
      </div>
      ${parameter.isPercent
        ? html`<input class="slider" type="range" min="0" max="100" step="0.1"
            .value=${String(parameter.percentValue)}
            aria-label=${parameter.label}
            @input=${(event: Event) => this.patchPercent((event.target as HTMLInputElement).value)} />`
        : nothing}
      ${parameter.isDuration
        ? html`<label class="jitter">
            <span>±</span>
            <input type="number" min="0" max="100" step="1"
              .value=${parameter.jitterPercent === null ? '' : String(parameter.jitterPercent)}
              @change=${(event: Event) => this.patchJitter(event)} />
            <span>%</span>
          </label>`
        : nothing}
    `;
  }

  private renderDisplay(parameter: EditorActionField, chip: ReturnType<WgValueField['chip']>) {
    const parts = parameter.parts ?? [];
    return html`
      <div class="shell readonly ${parameter.isMultiline ? 'multiline' : ''}">
        <div class="read">
          ${parameter.isColor
            ? html`<span class="swatch" style=${'background:' + (/^#[0-9a-f]{6}$/i.test(parameter.display)
                ? parameter.display : '#000000')}></span>`
            : nothing}
          ${parts.length > 0
            ? parts.map((part) => part.kind === 'name'
              ? html`<span class="name-part" title=${part.name}>${part.name}</span>`
              : html`<span>${part.text}</span>`)
            : parameter.display || parameter.value
              ? html`<span>${parameter.display || parameter.value}</span>`
              : html`<span class="placeholder">${parameter.placeholder || '—'}</span>`}
        </div>
        ${chip}
      </div>
    `;
  }

  render() {
    const parameter = this.parameter;
    const chip = this.chip();
    return parameter.isNumber && !parameter.isNumberFormula
      ? this.renderNumber(parameter, chip)
      : this.renderDisplay(parameter, chip);
  }
}

declare global {
  interface HTMLElementTagNameMap {
    'wg-value-field': WgValueField;
  }
}
