import { LitElement, css, html, nothing, type TemplateResult } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import { icon } from '../../shared/icons.js';
import { t } from '../../shared/strings/strings.js';
import { editor } from './editor-store.js';
import { editorBase } from './editor-css.js';

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
  .row-line select {
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

  private unsubscribe?: () => void;

  connectedCallback(): void {
    super.connectedCallback();
    this.unsubscribe = editor.subscribe(() => this.requestUpdate());
  }

  disconnectedCallback(): void {
    this.unsubscribe?.();
    super.disconnectedCallback();
  }

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
                  ${(snapshot.variableTypes).map((type) => html`
                    <option value=${type.value}>${type.label}</option>
                  `)}
                </select>
                ${variable.isDeclared
                  ? html`<input .value=${variable.initial}
                      placeholder=${t('LocalVariables.InitialPlaceholder')}
                      @change=${(event: Event) => void editor.variables.patchVariable(variable.name,
                        'initial', (event.target as HTMLInputElement).value)} />`
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

  private unsubscribe?: () => void;

  connectedCallback(): void {
    super.connectedCallback();
    this.unsubscribe = editor.subscribe(() => this.requestUpdate());
  }

  disconnectedCallback(): void {
    this.unsubscribe?.();
    super.disconnectedCallback();
  }

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
                  ${snapshot.variableTypes.map((type) => html`
                    <option value=${type.value}>${type.label}</option>
                  `)}
                </select>
                <input .value=${global.default}
                  placeholder=${t('GlobalVariables.DefaultPlaceholder')}
                  @change=${(event: Event) => void editor.variables.patchGlobal(global.name, 'default',
                    (event.target as HTMLInputElement).value)} />
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

declare global {
  interface HTMLElementTagNameMap {
    'wg-local-variables': WgLocalVariables;
    'wg-global-variables': WgGlobalVariables;
  }
}
