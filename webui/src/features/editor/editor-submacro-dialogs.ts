import { LitElement, css, html, nothing } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import { icon } from '../../shared/icons.js';
import { t } from '../../shared/strings/strings.js';
import { editor } from './editor-store.js';
import { editorBase } from './editor-css.js';

/**
 * The two lightweight submacro dialogs the palette opens: copy from a name, and confirm a delete.
 * They hold no document state; the command goes through the steps service, which keeps a rejected
 * delete's own words in the page notice after this dialog has closed.
 */
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
    gap: var(--spacingVerticalS);
    width: min(420px, 100%);
    max-height: min(560px, 100%);
    min-height: 0;
    padding: var(--spacingVerticalL);
    border: var(--strokeWidthThin) solid var(--colorNeutralStroke1);
    border-radius: var(--borderRadiusLarge);
    background: var(--colorNeutralBackground1);
    box-shadow: var(--shadow16);
  }

  .dialog h2 {
    margin: 0;
    font-size: var(--fontSizeBase400);
    font-weight: var(--fontWeightSemibold);
  }

  .dialog input {
    width: 100%;
    padding: var(--spacingVerticalXS) var(--spacingHorizontalS);
    border: var(--strokeWidthThin) solid var(--colorNeutralStroke1);
    border-bottom: var(--strokeWidthThicker) solid var(--colorNeutralStrokeAccessible);
    border-radius: var(--borderRadiusMedium);
    background: var(--colorNeutralBackground1);
    color: var(--colorNeutralForeground1);
    font: inherit;
    font-size: var(--fontSizeBase200);
  }

  .dialog input:focus-visible {
    outline: var(--strokeWidthThicker) solid var(--colorStrokeFocus2);
    outline-offset: -1px;
  }

  .candidates {
    display: grid;
    gap: 2px;
    min-height: 0;
    overflow: auto;
  }

  .candidates button {
    display: flex;
    align-items: center;
    gap: var(--spacingHorizontalS);
    width: 100%;
    min-height: 30px;
    padding: var(--spacingVerticalXS) var(--spacingHorizontalS);
    border: var(--strokeWidthThin) solid transparent;
    border-radius: var(--borderRadiusMedium);
    background: transparent;
    color: var(--colorNeutralForeground1);
    font-size: var(--fontSizeBase200);
    text-align: left;
    cursor: pointer;
  }

  .candidates button:hover {
    background: var(--colorSubtleBackgroundHover);
  }

  .candidates button[aria-selected='true'] {
    background: var(--colorBrandBackground2);
    border-color: var(--colorBrandStroke1);
    color: var(--colorBrandForeground2);
  }

  .candidates button svg {
    width: 13px;
    height: 13px;
    flex: none;
  }

  .empty,
  .message {
    margin: 0;
    color: var(--colorNeutralForeground2);
    font-size: var(--fontSizeBase200);
    line-height: 1.45;
  }

  .footer {
    display: flex;
    justify-content: flex-end;
    gap: var(--spacingHorizontalS);
  }
`;

@customElement('wg-submacro-copy-dialog')
export class WgSubMacroCopyDialog extends LitElement {
  @property({ type: Boolean, reflect: true })
  open = false;

  /** The submacros the open document already has; a project macro can still be typed by name. */
  @property({ attribute: false })
  names: readonly string[] = [];

  @state()
  private from = '';

  static styles = [editorBase, dialogStyles];

  render() {
    if (!this.open) {
      return nothing;
    }

    const needle = this.from.trim().toLocaleLowerCase();
    const candidates = needle.length === 0
      ? this.names
      : this.names.filter((name) => name.toLocaleLowerCase().includes(needle));
    return html`
      <div class="overlay" @click=${() => this.close()}>
        <div class="dialog" role="dialog" aria-modal="true"
          @click=${(event: Event) => event.stopPropagation()}>
          <h2>${t('Editor.PickSubMacro')}</h2>
          <input
            .value=${this.from}
            placeholder=${t('Editor.PickSubMacroSearch')}
            aria-label=${t('Editor.PickSubMacroSearch')}
            @input=${(event: Event) => { this.from = (event.target as HTMLInputElement).value; }}
            @keydown=${(event: KeyboardEvent) => {
              if (event.key === 'Enter') {
                event.preventDefault();
                this.confirm();
              }
            }}
          />
          <div class="candidates" role="listbox" aria-label=${t('Editor.PickSubMacro')}>
            ${this.names.length === 0
              ? html`<p class="empty">${t('Palette.NoSubMacros')}</p>`
              : candidates.map((name) => html`
                <button
                  type="button"
                  role="option"
                  aria-selected=${name === this.from ? 'true' : 'false'}
                  @click=${() => { this.from = name; }}
                >${icon('workflow')}<span>${name}</span></button>
              `)}
          </div>
          <div class="footer">
            <fluent-button appearance="subtle" @click=${() => this.close()}>
              ${t('Common.Cancel')}
            </fluent-button>
            <fluent-button appearance="accent" ?disabled=${this.from.trim().length === 0}
              @click=${() => this.confirm()}>
              ${t('Editor.Context.Copy')}
            </fluent-button>
          </div>
        </div>
      </div>
    `;
  }

  private confirm(): void {
    const from = this.from.trim();
    if (from.length === 0) {
      return;
    }

    void editor.steps.copySubMacro(from);
    this.close();
  }

  private close(): void {
    this.from = '';
    this.open = false;
    this.dispatchEvent(new CustomEvent('wg-close', { bubbles: true, composed: true }));
  }
}

@customElement('wg-submacro-delete-dialog')
export class WgSubMacroDeleteDialog extends LitElement {
  @property({ type: Boolean, reflect: true })
  open = false;

  @property({ attribute: false })
  name = '';

  static styles = [editorBase, dialogStyles];

  render() {
    if (!this.open || this.name.length === 0) {
      return nothing;
    }

    return html`
      <div class="overlay" @click=${() => this.close()}>
        <div class="dialog" role="dialog" aria-modal="true"
          @click=${(event: Event) => event.stopPropagation()}>
          <h2>${t('Editor.DeleteSubMacroTitle')}</h2>
          <p class="message">
            ${t('Editor.DeleteSubMacroMessage').replace('{0}', this.name)}
          </p>
          <div class="footer">
            <fluent-button appearance="subtle" @click=${() => this.close()}>
              ${t('Common.Cancel')}
            </fluent-button>
            <fluent-button appearance="accent" @click=${() => this.confirm()}>
              ${t('Editor.Delete')}
            </fluent-button>
          </div>
        </div>
      </div>
    `;
  }

  private confirm(): void {
    const name = this.name;
    this.close();
    void editor.steps.deleteSubMacro(name);
  }

  private close(): void {
    this.open = false;
    this.dispatchEvent(new CustomEvent('wg-close', { bubbles: true, composed: true }));
  }
}

declare global {
  interface HTMLElementTagNameMap {
    'wg-submacro-copy-dialog': WgSubMacroCopyDialog;
    'wg-submacro-delete-dialog': WgSubMacroDeleteDialog;
  }
}
