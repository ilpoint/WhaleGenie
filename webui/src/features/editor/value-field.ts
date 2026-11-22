import { LitElement, css, html, nothing } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import type { EditorParameter } from '../../shared/bridge/protocol.js';
import { t } from '../../shared/strings/strings.js';
import { editor } from './editor-store.js';
import { editorBase } from './editor-css.js';

/**
 * One value field in its native shape: the display lives inside a bordered box and the picker is
 * a chip on that box's right edge, never a second row underneath it. Plain text, names, colours
 * and multi-line values are read-only here because their shape is built in the token window; a
 * number can still be typed and carries the same chip at its edge.
 */
@customElement('wg-value-field')
export class WgValueField extends LitElement {
  @property({ attribute: false })
  stepId = '';

  @property({ attribute: false })
  parameter!: EditorParameter;

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
    void editor.openValue(this.stepId, this.parameter.name);
  }

  private patchNumber(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    void editor.patchParameter(this.stepId, this.parameter.name, value);
  }

  render() {
    const parameter = this.parameter;
    const chip = parameter.offersValue
      ? html`
          <button class="chip" type="button" title=${t('Add.ValueHint')}
            @click=${() => this.openPicker()}>
            <span class="glyph">ƒ</span>${t('Add.Value')}
          </button>
        `
      : nothing;

    if (parameter.isNumber && !parameter.isNumberFormula) {
      return html`
        <div class="shell">
          <input class="number" type="number" .value=${parameter.display || parameter.value}
            aria-label=${parameter.label}
            @change=${(event: Event) => this.patchNumber(event)} />
          ${chip}
        </div>
      `;
    }

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
}

declare global {
  interface HTMLElementTagNameMap {
    'wg-value-field': WgValueField;
  }
}
