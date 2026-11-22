import { LitElement, css, html, nothing } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import type { EditorCharacterPad } from '../../shared/bridge/protocol.js';
import { t } from '../../shared/strings/strings.js';

/** The literal keyboard for a hotstring abbreviation: one cap writes one character. */
@customElement('wg-character-pad')
export class WgCharacterPad extends LitElement {
  @property({ attribute: false })
  public pad: EditorCharacterPad | null = null;

  @property()
  public text = '';

  @state()
  private shift = false;

  @state()
  private value = '';

  static styles = css`
    :host { display: contents; }
    .overlay { position: fixed; inset: 0; z-index: 80; display: grid; place-items: center; padding: 20px; background: var(--shell-scrim); }
    .card { width: min(880px, 100%); max-height: 90vh; overflow: auto; display: grid; gap: var(--spacingVerticalS); padding: var(--spacingVerticalL); background: var(--colorNeutralBackground1); border: var(--strokeWidthThin) solid var(--colorNeutralStroke1); border-radius: var(--borderRadiusLarge); box-shadow: var(--shadow64); }
    .head { display: flex; align-items: center; gap: var(--spacingHorizontalS); }
    .head h3 { margin: 0; flex: 1; font-size: var(--fontSizeBase400); }
    .preview { min-height: 34px; padding: var(--spacingVerticalS); border: var(--strokeWidthThin) solid var(--colorNeutralStroke2); border-radius: var(--borderRadiusMedium); background: var(--colorNeutralBackground2); font-family: var(--fontFamilyMonospace); font-size: var(--fontSizeBase400); white-space: pre-wrap; overflow-wrap: anywhere; }
    .count { color: var(--colorNeutralForeground3); font-size: var(--fontSizeBase100); }
    .rows { display: grid; gap: 5px; }
    .row { display: flex; gap: 5px; justify-content: center; }
    .cap { flex: 1 1 0; min-width: 34px; height: 36px; padding: 0 6px; border: var(--strokeWidthThin) solid var(--colorNeutralStroke1); border-radius: var(--borderRadiusMedium); background: var(--colorNeutralBackground2); color: var(--colorNeutralForeground1); font: inherit; cursor: pointer; }
    .cap:hover { background: var(--colorNeutralBackground2Hover); }
    .cap.on { border-color: var(--colorBrandStroke1); background: var(--colorBrandBackground2); color: var(--colorBrandForeground2); }
    .actions { display: flex; justify-content: flex-end; gap: var(--spacingHorizontalS); }
    .error { color: var(--colorStatusDangerForeground1); font-size: var(--fontSizeBase100); }
  `;

  connectedCallback(): void {
    super.connectedCallback();
    this.value = this.text.slice(0, this.pad?.maximumLength ?? 40);
    window.addEventListener('keydown', this.onKeydown);
  }

  disconnectedCallback(): void {
    window.removeEventListener('keydown', this.onKeydown);
    super.disconnectedCallback();
  }

  render() {
    const pad = this.pad;
    if (!pad) {
      return nothing;
    }

    return html`
      <div class="overlay">
        <div class="card" role="dialog" aria-modal="true" aria-label=${t('HotstringKeyboard.Title')}>
          <div class="head">
            <h3>${t('HotstringKeyboard.Title')}</h3>
            <button class="cap" type="button" @click=${() => this.close()}>${t('Common.Close')}</button>
          </div>
          <div class="preview">${this.value || '—'}</div>
          <span class="count">${this.value.length}/${pad.maximumLength}</span>
          <p class="count">${t('HotstringKeyboard.Hint')}</p>
          <div class="rows">
            ${pad.rows.map((row) => html`
              <div class="row">
                ${row.map((cap) => this.renderCap(cap))}
              </div>
            `)}
          </div>
          <div class="actions">
            <button class="cap" type="button" @click=${() => { this.value = ''; }}>${t('HotstringKeyboard.Clear')}</button>
            <button class="cap" type="button" @click=${() => this.backspace()}>${t('HotstringKeyboard.Backspace')}</button>
            <button class="cap on" type="button" @click=${() => this.submit()}>${t('Common.Ok')}</button>
          </div>
        </div>
      </div>
    `;
  }

  private renderCap(cap: EditorCharacterPad['rows'][number][number]) {
    return html`
      <button class="cap ${cap.kind === 'shift' && this.shift ? 'on' : ''}" type="button"
        style=${'flex:' + String(cap.width) + ' 1 0'}
        @click=${() => this.press(cap)}>
        ${cap.kind === 'shift' ? t('HotstringKeyboard.Shift')
          : cap.kind === 'backspace' ? t('HotstringKeyboard.Backspace')
            : cap.kind === 'tab' ? t('HotstringKeyboard.Tab')
              : cap.kind === 'space' ? t('HotstringKeyboard.Space')
                : cap.kind === 'enter' ? t('HotstringKeyboard.Enter')
                  : cap.kind === 'clear' ? t('HotstringKeyboard.Clear')
                    : this.shift ? cap.shifted : cap.normal}
      </button>
    `;
  }

  private press(cap: EditorCharacterPad['rows'][number][number]): void {
    if (cap.kind === 'shift') {
      this.shift = !this.shift;
      return;
    }

    if (cap.kind === 'backspace') {
      this.backspace();
      return;
    }

    if (cap.kind === 'clear') {
      this.value = '';
      return;
    }

    const maximum = this.pad?.maximumLength ?? 40;
    const text = cap.kind === 'tab' ? '\t' : cap.kind === 'space' ? ' ' : cap.kind === 'enter' ? '\n'
      : this.shift ? cap.shifted : cap.normal;
    if (this.value.length + text.length <= maximum) {
      this.value += text;
    }
  }

  private backspace(): void {
    this.value = [...this.value].slice(0, -1).join('');
  }

  private submit(): void {
    this.dispatchEvent(new CustomEvent('wg-character-submit', {
      detail: { text: this.value },
    }));
  }

  private close(): void {
    this.dispatchEvent(new CustomEvent('wg-character-close'));
  }

  private onKeydown = (event: KeyboardEvent): void => {
    if (event.key === 'Escape') {
      event.preventDefault();
      this.close();
    }
  };
}

declare global {
  interface HTMLElementTagNameMap {
    'wg-character-pad': WgCharacterPad;
  }
}
