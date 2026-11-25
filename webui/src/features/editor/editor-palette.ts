import { LitElement, html, nothing } from 'lit';
import { editorPaletteCss } from './editor-palette-css.js';
import { customElement, state } from 'lit/decorators.js';
import { icon, iconForCategory } from '../../shared/icons.js';
import { t } from '../../shared/strings/strings.js';
import { go } from '../../shared/navigation/router.js';
import { actionSections, paletteActions } from './editor-model.js';
import { editor } from './editor-store.js';
import { StoreController } from '../../shared/lit/store-controller.js';
import { editorBase } from './editor-css.js';
import './editor-submacro-dialogs.js';

/**
 * The action library in the shell's side bar: four docks' worth reduced to the three that have a
 * source in the editor today (basic actions, control flow, the document's own submacros). A click
 * selects a card, a double click or the toolbar's Add button places it; the category rail and the
 * search narrow the same catalogue the native palette read.
 */
@customElement('wg-editor-palette')
export class WgEditorPalette extends LitElement {
  static styles = [editorBase, editorPaletteCss];

  protected readonly editorController = new StoreController<void>(this, editor);

  @state()
  private copyOpen = false;

  @state()
  private deleteName = '';

  connectedCallback(): void {
    super.connectedCallback();
    editor.session.ensureLoaded();
  }

  disconnectedCallback(): void {
    super.disconnectedCallback();
  }

  render() {
    const state = editor.get();
    const actions = paletteActions(state.actions, state.paletteSource, state.category, state.search);
    const sections = actionSections(actions);
    const subMacros = state.snapshot?.subMacros ?? [];
    const needle = state.search.trim().toLocaleLowerCase();
    const shownSubMacros = subMacros.filter((name) =>
      needle.length === 0 || name.toLocaleLowerCase().includes(needle));

    return html`
      <div class="sources" role="tablist" aria-label=${t('Editor.Palette')}>
        ${this.sourceButton('basic', 'layout-grid', t('Editor.Dock.BasicActions'))}
        ${this.sourceButton('flow', 'workflow', t('Editor.Dock.Flow'))}
        ${this.sourceButton('sub', 'list-tree', t('Editor.Dock.SubMacros'))}
        ${this.sourceButton('public', 'folder', t('Editor.Dock.PublicMacros'))}
      </div>
      <div class="body">
        ${state.paletteSource === 'basic'
          ? html`
            <div class="rail" role="group" aria-label=${t('Editor.Palette')}>
              ${state.categories.map((category) => html`
                <button
                  type="button"
                  title=${category.label}
                  aria-pressed=${state.category === category.value ? 'true' : 'false'}
                  @click=${() => editor.ui.setPaletteCategory(category.value)}
                >
                  ${icon(category.value === null
                    ? 'layout-grid'
                    : iconForCategory(category.value))}
                  <span>${category.short}</span>
                </button>
              `)}
            </div>
          `
          : nothing}
        <div class="items">
          <input
            class="action-search search"
            type="search"
            .value=${state.search}
            placeholder=${t('Add.SearchAction')}
            aria-label=${t('Add.SearchAction')}
            @input=${(event: Event) =>
              editor.ui.setPaletteSearch((event.target as HTMLInputElement).value)}
          />
          <div class="scroll">
            ${state.paletteSource === 'sub'
              ? this.renderSubMacros(shownSubMacros)
              : state.paletteSource === 'public'
                ? this.renderPublicMacros()
                : sections.length === 0
                ? html`<p class="empty">${t('Add.NoActionFound')}</p>`
                : sections.map((section) => html`
                  <div class="section-title">${section.label}</div>
                  <div class="cards">
                    ${section.actions.map((action) => html`
                      <button
                        type="button"
                        class="chip card"
                        draggable="true"
                        title=${action.description || action.name}
                        aria-pressed=${state.selectedActionKey === action.key ? 'true' : 'false'}
                        @click=${() => editor.ui.selectAction(action.key)}
                        @dblclick=${() => void editor.action.openAction(action.key)}
                        @dragstart=${(event: DragEvent) => {
                          event.dataTransfer?.setData('application/x-whalegenie-action', action.key);
                          event.dataTransfer?.setData('text/plain', action.key);
                          if (event.dataTransfer) {
                            event.dataTransfer.effectAllowed = 'copy';
                          }
                        }}
                      >
                        ${icon(iconForCategory(action.category))}
                        <span>${action.name}</span>
                      </button>
                    `)}
                  </div>
                `)}
          </div>
        </div>
      </div>
      <div class="footer">
        <p class="hint">${t('Editor.DragActionHint')}</p>
        <button
          type="button"
          class="record"
          data-recording=${state.recording ? 'true' : 'false'}
          ?disabled=${state.snapshot === null}
          title=${state.recording ? t('Editor.StopRecording') : t('Editor.Record')}
          @click=${() => void editor.recording.toggleRecording()}
        >
          ${state.recording
            ? html`<span class="record-stop"></span>`
            : html`<span class="record-dot"></span>`}
          <span>${state.recording ? t('Editor.StopRecording') : t('Editor.Record')}</span>
          ${state.recording
            ? html`<span class="record-count">${state.captured}</span>`
            : nothing}
        </button>
      </div>
    `;
  }

  private sourceButton(source: 'basic' | 'flow' | 'sub' | 'public',
    iconName: 'layout-grid' | 'workflow' | 'list-tree' | 'folder', label: string) {
    const state = editor.get();
    return html`
      <button
        type="button"
        class="source"
        role="tab"
        aria-selected=${state.paletteSource === source ? 'true' : 'false'}
        title=${label}
        @click=${() => {
          this.copyOpen = false;
          this.deleteName = '';
          editor.ui.setPaletteSource(source);
          if (source === 'sub' && state.paletteSource === 'sub' && state.snapshot === null) {
            return;
          }
        }}
      >
        ${icon(iconName)}
        <span>${label}</span>
      </button>
    `;
  }

  private renderPublicMacros() {
    const state = editor.get();
    const needle = state.search.trim().toLocaleLowerCase();
    const macros = state.publicMacros.filter((macro) =>
      needle.length === 0
      || macro.name.toLocaleLowerCase().includes(needle)
      || macro.description.toLocaleLowerCase().includes(needle));
    return html`
      <p class="public-hint">${t('Editor.PublicMacroHint')}</p>
      ${macros.length === 0
        ? html`<p class="empty">${t('Palette.NoPublicMacros')}</p>`
        : macros.map((macro) => html`
          <button
            type="button"
            class="sub-card"
            title=${macro.description || macro.name}
            @click=${() => void editor.steps.addPublicMacro(macro.id)}
          >
            ${icon('folder')}
            <span>
              <span>${macro.name}</span>
              <span class="meta">${t('Palette.MacroVersion').replace('{0}', String(macro.version))}</span>
            </span>
          </button>
        `)}
      <button class="link-button public-manage" type="button"
        @click=${() => void go('library')}>${t('Editor.ManagePublicMacros')}</button>
    `;
  }

  /** No open document means no submacro list to add to; the two tools stay disabled. */
  private get noDocument(): boolean {
    return editor.get().snapshot === null;
  }

  private renderSubMacros(names: string[]) {
    return html`
      <div class="sub-tools">
        <button class="link-button sub-new" type="button" title=${t('Editor.NewSubMacro')}
          ?disabled=${this.noDocument}
          @click=${() => void editor.steps.createSubMacro()}>
          ${icon('plus')}${t('Editor.NewSubMacro')}
        </button>
        <button class="link-button sub-copy" type="button" title=${t('Editor.PickSubMacro')}
          ?disabled=${this.noDocument}
          @click=${() => { this.copyOpen = true; }}>
          ${icon('copy')}${t('Editor.Context.Copy')}
        </button>
      </div>
      ${names.length === 0
        ? html`<p class="empty">${t('Palette.NoSubMacros')}</p>`
        : names.map((name) => html`
          <div class="sub-card sub-row" data-submacro=${name}>
            <button type="button" class="sub-open" title=${t('Editor.OpenSubMacro')}
              @click=${() => void editor.session.openSubmacro(name)}>
              ${icon('workflow')}<span>${name}</span>
            </button>
            <button type="button" class="ghost sub-remove" title=${t('Editor.DeleteSubMacro')}
              aria-label=${t('Editor.DeleteSubMacro')}
              @click=${() => { this.deleteName = name; }}>
              ${icon('delete')}
            </button>
          </div>
        `)}
      <wg-submacro-copy-dialog .open=${this.copyOpen} .names=${names}
        @wg-close=${() => { this.copyOpen = false; }}></wg-submacro-copy-dialog>
      <wg-submacro-delete-dialog .open=${this.deleteName.length > 0} .name=${this.deleteName}
        @wg-close=${() => { this.deleteName = ''; }}></wg-submacro-delete-dialog>
    `;
  }
}

declare global {
  interface HTMLElementTagNameMap {
    'wg-editor-palette': WgEditorPalette;
  }
}
