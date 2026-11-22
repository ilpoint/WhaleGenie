import { LitElement, css, html, nothing } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import type { EditorKeyPad } from '../../shared/bridge/protocol.js';
import { t } from '../../shared/strings/strings.js';

/** The keyboard drawn on the page: a key field takes one press, a sequence takes them in order. */
@customElement('wg-key-pad')
export class WgKeyPad extends LitElement {
  @property({ attribute: false })
  public pad: EditorKeyPad | null = null;

  static styles = css`
    :host { display: contents; }
    .overlay { position: fixed; inset: 0; z-index: 80; display: grid; place-items: center; padding: 20px; background: var(--shell-scrim); }
    .card { width: min(760px, 100%); display: grid; gap: var(--spacingVerticalS); padding: var(--spacingVerticalL); background: var(--colorNeutralBackground1); border: var(--strokeWidthThin) solid var(--colorNeutralStroke1); border-radius: var(--borderRadiusLarge); box-shadow: var(--shadow64); }
    .head { display: flex; align-items: center; }
    .head h3 { margin: 0; flex: 1; font-size: var(--fontSizeBase400); }
    .row { display: flex; gap: 4px; justify-content: center; }
    .cap { min-width: 34px; height: 34px; padding: 0 6px; border: var(--strokeWidthThin) solid var(--colorNeutralStroke1); border-radius: var(--borderRadiusMedium); background: var(--colorNeutralBackground2); color: var(--colorNeutralForeground1); font: inherit; font-size: var(--fontSizeBase200); cursor: pointer; }
    .cap:hover { background: var(--colorNeutralBackground1Hover); }
    .cap:active { background: var(--colorNeutralBackground1Pressed); }
  `;

  render() {
    const pad = this.pad;
    if (!pad) {
      return nothing;
    }

    return html`
      <div class="overlay">
        <div class="card" role="dialog" aria-modal="true" aria-label=${t('Editor.KeyPad')}>
          <div class="head">
            <h3>${t('Editor.KeyPad')}</h3>
            <button class="cap" type="button" @click=${() => this.dispatchEvent(new CustomEvent('wg-pad-close'))}>
              ${t('Common.Close')}
            </button>
          </div>
          ${pad.rows.map((row) => html`
            <div class="row">
              ${row.map((cap) => html`
                <button class="cap" type="button" style=${'flex:' + String(cap.width) + ' 1 0'}
                  @click=${() => this.choose(cap.value)}>${cap.label}</button>
              `)}
            </div>
          `)}
        </div>
      </div>
    `;
  }

  private choose(value: string): void {
    this.dispatchEvent(new CustomEvent('wg-pad-key', { detail: { value } }));
  }
}

declare global {
  interface HTMLElementTagNameMap {
    'wg-key-pad': WgKeyPad;
  }
}
