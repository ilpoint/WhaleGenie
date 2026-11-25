import { LitElement, css, html, nothing } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import { bridge } from '../../shared/bridge/bridge.js';
import type { ProjectImportPlan } from '../../shared/bridge/protocol.js';
import { project } from '../../shared/project/store.js';
import { describeError } from '../../shared/errors.js';
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

  .sources {
    display: flex;
    gap: var(--spacingHorizontalXS);
  }

  .sources button {
    flex: 1;
    min-height: 30px;
    padding: var(--spacingVerticalXS) var(--spacingHorizontalS);
    border: var(--strokeWidthThin) solid var(--colorNeutralStroke1);
    border-radius: var(--borderRadiusMedium);
    background: var(--colorNeutralBackground1);
    color: var(--colorNeutralForeground1);
    font: inherit;
    font-size: var(--fontSizeBase200);
    cursor: pointer;
  }

  .sources button[aria-selected='true'] {
    border-color: var(--colorBrandStroke1);
    background: var(--colorBrandBackground2);
    color: var(--colorBrandForeground2);
  }

  .file-pick {
    justify-content: center;
    border-style: dashed !important;
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

  @state()
  private source: 'public' | 'project' | 'file' = 'public';

  @state()
  private query = '';

  @state()
  private path = '';

  @state()
  private fileMacros: string[] = [];

  @state()
  private busy = false;

  @state()
  private error = '';

  static styles = [editorBase, dialogStyles];

  willUpdate(changed: Map<string, unknown>): void {
    if (this.open && changed.has('open')) {
      this.query = '';
      this.path = '';
      this.fileMacros = [];
      this.error = '';
      this.busy = false;
    }
  }

  render() {
    if (!this.open) {
      return nothing;
    }

    return html`
      <div class="overlay" @click=${() => this.close()}>
        <div class="dialog" role="dialog" aria-modal="true"
          @click=${(event: Event) => event.stopPropagation()}>
          <h2>${t('Editor.PickSubMacro')}</h2>
          <div class="sources" role="tablist" aria-label=${t('Editor.ImportSubMacro')}>
            ${this.sourceButton('public', t('Editor.ImportSource.Public'))}
            ${this.sourceButton('project', t('Editor.ImportSource.Project'))}
            ${this.sourceButton('file', t('Editor.ImportSource.File'))}
          </div>
          <input
            .value=${this.query}
            placeholder=${t('Editor.PickSubMacroSearch')}
            aria-label=${t('Editor.PickSubMacroSearch')}
            @input=${(event: Event) => { this.query = (event.target as HTMLInputElement).value; }}
          />
          ${this.error.length > 0 ? html`<p class="error">${this.error}</p>` : nothing}
          <div class="candidates" role="listbox" aria-label=${t('Editor.ImportSubMacro')}>
            ${this.source === 'public' ? this.renderPublic(this.needle()) : nothing}
            ${this.source === 'project' ? this.renderProject(this.needle()) : nothing}
            ${this.source === 'file' ? this.renderFile(this.needle()) : nothing}
          </div>
          <div class="footer">
            <fluent-button appearance="subtle" @click=${() => this.close()}>
              ${t('Common.Cancel')}
            </fluent-button>
          </div>
        </div>
      </div>
    `;
  }

  private sourceButton(source: 'public' | 'project' | 'file', label: string) {
    return html`
      <button type="button" role="tab" aria-selected=${this.source === source ? 'true' : 'false'}
        @click=${() => { this.source = source; }}>
        ${label}
      </button>
    `;
  }

  private needle(): string {
    return this.query.trim().toLocaleLowerCase();
  }

  private renderPublic(needle: string) {
    const entries = editor.get().publicMacros.filter((entry) =>
      needle.length === 0 || entry.name.toLocaleLowerCase().includes(needle));
    return entries.length === 0
      ? html`<p class="empty">${t('Palette.NoPublicMacros')}</p>`
      : entries.map((entry) => html`
        <button type="button" role="option"
          @click=${() => this.take('public', entry.id)}>
          ${icon('folder')}<span>${entry.name}</span>
        </button>
      `);
  }

  private renderProject(needle: string) {
    const macros = (project.current()?.macros ?? []).filter((macro) =>
      needle.length === 0 || macro.name.toLocaleLowerCase().includes(needle));
    return macros.length === 0
      ? html`<p class="empty">${t('Editor.NoProjectsMacros')}</p>`
      : macros.map((macro) => html`
        <button type="button" role="option"
          @click=${() => this.take('project', macro.name)}>
          ${icon('workflow')}<span>${macro.name}</span>
        </button>
      `);
  }

  private renderFile(needle: string) {
    if (this.fileMacros.length === 0) {
      return html`
        <button type="button" class="file-pick" ?disabled=${this.busy}
          @click=${() => void this.pickFile()}>
          ${icon('folder')}<span>${t('Editor.ChoosePackage')}</span>
        </button>
      `;
    }

    const macros = this.fileMacros.filter((name) =>
      needle.length === 0 || name.toLocaleLowerCase().includes(needle));
    return html`
      <p class="message">${this.path}</p>
      ${macros.length === 0
        ? html`<p class="empty">${t('Import.NoMacros')}</p>`
        : macros.map((name) => html`
          <button type="button" role="option" @click=${() => this.take('file', name, this.path)}>
            ${icon('workflow')}<span>${name}</span>
          </button>
        `)}
    `;
  }

  private async pickFile(): Promise<void> {
    this.error = '';
    this.busy = true;
    try {
      const path = await bridge.value.call<string | null>('dialog.pickOpenPackage');
      if (!path) {
        return;
      }

      const plan = await bridge.value.call<ProjectImportPlan>('project.importPlan', { path });
      this.path = path;
      this.fileMacros = plan.macros.map((macro) => macro.name);
      if (this.fileMacros.length === 0) {
        this.error = t('Import.NoMacros');
      }
    } catch (error) {
      this.error = describeError(error);
    } finally {
      this.busy = false;
    }
  }

  private take(source: 'public' | 'project' | 'file', reference: string, path?: string): void {
    this.close();
    void editor.steps.importSubMacro(source, reference, path);
  }

  private close(): void {
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
