import { LitElement, css, html, nothing } from 'lit';
import { customElement } from 'lit/decorators.js';
import { icon, iconForCategory } from '../../shared/icons.js';
import { t } from '../../shared/strings/strings.js';
import { actionSections, paletteActions } from './editor-model.js';
import { editor } from './editor-store.js';
import { editorBase } from './editor-css.js';

/**
 * The action library in the shell's side bar: four docks' worth reduced to the three that have a
 * source in the editor today (basic actions, control flow, the document's own submacros). A click
 * selects a card, a double click or the toolbar's Add button places it; the category rail and the
 * search narrow the same catalogue the native palette read.
 */
@customElement('wg-editor-palette')
export class WgEditorPalette extends LitElement {
  static styles = [editorBase, css`
    :host {
      display: flex;
      flex-direction: column;
      height: 100%;
      min-height: 0;
      background: var(--colorNeutralBackground3);
    }

    .sources {
      display: grid;
      grid-template-columns: repeat(4, minmax(0, 1fr));
      gap: 2px;
      padding: var(--spacingVerticalS) var(--spacingHorizontalS) var(--spacingVerticalXS);
      border-bottom: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
    }

    .source {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      gap: 2px;
      min-width: 0;
      min-height: 44px;
      padding: var(--spacingVerticalXS) 2px;
      border: var(--strokeWidthThin) solid transparent;
      border-radius: var(--borderRadiusMedium);
      background: transparent;
      color: var(--colorNeutralForeground2);
      font-size: var(--fontSizeBase200);
      line-height: 1.2;
      text-align: center;
      cursor: pointer;
    }

    .source:hover {
      background: var(--colorSubtleBackgroundHover);
      color: var(--colorNeutralForeground1);
    }

    .source[aria-selected='true'] {
      background: var(--colorBrandBackground2);
      border-color: var(--colorBrandStroke1);
      color: var(--colorBrandForeground2);
    }

    .source:focus-visible {
      outline: var(--strokeWidthThicker) solid var(--colorStrokeFocus2);
      outline-offset: -1px;
    }

    .source svg {
      width: 14px;
      height: 14px;
    }

    .body {
      display: flex;
      flex: 1;
      min-height: 0;
    }

    .rail {
      width: 64px;
      flex: none;
      overflow-y: auto;
      border-right: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
      padding: var(--spacingVerticalXS) 3px;
    }

    .rail button {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      gap: 2px;
      width: 100%;
      min-height: 46px;
      padding: 2px;
      border: var(--strokeWidthThin) solid transparent;
      border-radius: var(--borderRadiusMedium);
      background: transparent;
      color: var(--colorNeutralForeground3);
      font-size: var(--fontSizeBase100);
      line-height: 1.2;
      cursor: pointer;
    }

    .rail button:hover {
      background: var(--colorSubtleBackgroundHover);
      color: var(--colorNeutralForeground1);
    }

    .rail button[aria-pressed='true'] {
      background: var(--colorBrandBackground2);
      border-color: var(--colorBrandStroke1);
      color: var(--colorBrandForeground2);
    }

    .rail button:focus-visible {
      outline: var(--strokeWidthThicker) solid var(--colorStrokeFocus2);
      outline-offset: -1px;
    }

    .rail svg {
      width: 16px;
      height: 16px;
    }

    .items {
      display: flex;
      flex: 1;
      flex-direction: column;
      min-width: 0;
      min-height: 0;
    }

    .search {
      flex: none;
      margin: var(--spacingVerticalXS) var(--spacingHorizontalS) 0;
      padding: var(--spacingVerticalXS) var(--spacingHorizontalS);
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke1);
      border-bottom: var(--strokeWidthThicker) solid var(--colorNeutralStrokeAccessible);
      border-radius: var(--borderRadiusMedium);
      background: var(--colorNeutralBackground1);
      color: var(--colorNeutralForeground1);
      font: inherit;
      font-size: var(--fontSizeBase200);
    }

    .search:focus-visible {
      outline: var(--strokeWidthThicker) solid var(--colorStrokeFocus2);
      outline-offset: -1px;
    }

    .search::placeholder {
      color: var(--colorNeutralForeground4);
    }

    .scroll {
      flex: 1;
      min-height: 0;
      overflow: auto;
      padding: var(--spacingVerticalXS) var(--spacingHorizontalS) var(--spacingVerticalM);
    }

    .section-title {
      margin: var(--spacingVerticalS) 0 var(--spacingVerticalXS);
      color: var(--colorNeutralForeground3);
      font-size: var(--fontSizeBase100);
      font-weight: var(--fontWeightSemibold);
      letter-spacing: .02em;
    }

    .section-title:first-child {
      margin-top: 2px;
    }

    .cards {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(92px, 1fr));
      gap: 6px;
    }

    .card {
      align-items: center;
      justify-content: center;
      min-height: 30px;
      padding: 4px 6px;
      font-size: var(--fontSizeBase200);
      line-height: 1.3;
    }

    .card svg {
      width: 13px;
      height: 13px;
      flex: none;
      opacity: .85;
    }

    .sub-card {
      display: flex;
      align-items: center;
      gap: var(--spacingHorizontalXS);
      width: 100%;
      min-height: 34px;
      margin-bottom: var(--spacingVerticalXS);
      padding: var(--spacingVerticalXS) var(--spacingHorizontalS);
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke1);
      border-radius: var(--borderRadiusMedium);
      background: var(--colorNeutralBackground1);
      color: var(--colorNeutralForeground1);
      font-size: var(--fontSizeBase200);
      text-align: left;
      cursor: pointer;
    }

    .sub-card:hover {
      background: var(--colorNeutralBackground1Hover);
    }

    .sub-card .meta {
      display: block;
      color: var(--colorNeutralForeground3);
      font-size: var(--fontSizeBase100);
    }

    .sub-card:focus-visible {
      outline: var(--strokeWidthThicker) solid var(--colorStrokeFocus2);
      outline-offset: -1px;
    }

    .empty {
      padding: var(--spacingVerticalL) var(--spacingHorizontalS);
      color: var(--colorNeutralForeground3);
      font-size: var(--fontSizeBase200);
      line-height: 1.45;
      text-align: center;
    }

    .footer {
      flex: none;
      display: grid;
      gap: var(--spacingVerticalXS);
      padding: var(--spacingVerticalS) var(--spacingHorizontalS);
      border-top: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
    }

    .footer .hint {
      font-size: var(--fontSizeBase100);
      line-height: 1.35;
    }

    .record {
      display: flex;
      align-items: center;
      justify-content: center;
      gap: var(--spacingHorizontalS);
      min-height: 30px;
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke1);
      border-radius: var(--borderRadiusMedium);
      background: var(--colorNeutralBackground1);
      color: var(--colorNeutralForeground1);
      font-size: var(--fontSizeBase200);
      cursor: pointer;
    }

    .record:hover {
      background: var(--colorNeutralBackground1Hover);
    }

    .record:focus-visible {
      outline: var(--strokeWidthThicker) solid var(--colorStrokeFocus2);
      outline-offset: -1px;
    }

    .record[disabled] {
      color: var(--colorNeutralForegroundDisabled);
      cursor: default;
    }

    .record-dot,
    .record-stop {
      width: 10px;
      height: 10px;
      flex: none;
    }

    .record-dot {
      border-radius: 50%;
      background: var(--colorPaletteRedBackground3);
    }

    .record-stop {
      background: var(--colorNeutralForegroundOnBrand);
    }

    .record[data-recording='true'] {
      border-color: var(--colorPaletteRedBorder2);
      background: var(--colorPaletteRedBackground3);
      color: var(--colorNeutralForegroundOnBrand);
    }

    .record[data-recording='true'] .record-stop {
      animation: record-pulse 1.2s ease-in-out infinite alternate;
    }

    .record-count {
      font-size: var(--fontSizeBase100);
      opacity: .85;
    }

    @keyframes record-pulse {
      from { opacity: 1; }
      to { opacity: .35; }
    }

    @media (prefers-reduced-motion: reduce) {
      .record[data-recording='true'] .record-stop {
        animation: none;
      }
    }
  `];

  private unsubscribe?: () => void;

  connectedCallback(): void {
    super.connectedCallback();
    editor.ensureLoaded();
    this.unsubscribe = editor.subscribe(() => this.requestUpdate());
  }

  disconnectedCallback(): void {
    this.unsubscribe?.();
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
                  @click=${() => editor.setPaletteCategory(category.value)}
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
              editor.setPaletteSearch((event.target as HTMLInputElement).value)}
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
                        @click=${() => editor.selectAction(action.key)}
                        @dblclick=${() => void editor.openAction(action.key)}
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
          @click=${() => void editor.toggleRecording()}
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
          editor.setPaletteSource(source);
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
    if (macros.length === 0) {
      return html`<p class="empty">${t('Palette.NoPublicMacros')}</p>`;
    }

    return html`
      ${macros.map((macro) => html`
        <button
          type="button"
          class="sub-card"
          title=${macro.description || macro.name}
          @click=${() => void editor.addPublicMacro(macro.id)}
        >
          ${icon('folder')}
          <span>
            <span>${macro.name}</span>
            <span class="meta">${t('Palette.MacroVersion').replace('{0}', String(macro.version))}</span>
          </span>
        </button>
      `)}
    `;
  }

  private renderSubMacros(names: string[]) {
    if (names.length === 0) {
      return html`<p class="empty">${t('Palette.NoSubMacros')}</p>`;
    }

    return html`
      ${names.map((name) => html`
        <button
          type="button"
          class="sub-card"
          title=${t('Editor.OpenSubMacro')}
          @click=${() => void editor.openSubmacro(name)}
        >
          ${icon('workflow')}
          <span>${name}</span>
        </button>
      `)}
    `;
  }
}

declare global {
  interface HTMLElementTagNameMap {
    'wg-editor-palette': WgEditorPalette;
  }
}
