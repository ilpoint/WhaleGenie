import { LitElement, css, html, nothing } from 'lit';
import { customElement } from 'lit/decorators.js';
import { icon } from '../../shared/icons.js';
import { t } from '../../shared/strings/strings.js';
import { editor } from './editor-store.js';
import { editorBase } from './editor-css.js';

/**
 * The document strip: one tab per macro or submacro the C# workspace has open. The C# session is
 * the truth — closing a clean tab asks the bridge to close, and a dirty tab opens the save-and-close
 * question first.
 */
@customElement('wg-editor-tabs')
export class WgEditorTabs extends LitElement {
  static styles = [editorBase, css`
    :host {
      display: block;
      border-bottom: var(--strokeWidthThin) solid var(--colorNeutralStroke1);
      background: var(--colorNeutralBackground2);
      min-width: 0;
    }

    .strip {
      display: flex;
      align-items: stretch;
      min-width: 0;
      overflow-x: auto;
      scrollbar-width: thin;
    }

    .tab-wrap {
      display: flex;
      align-items: center;
      flex: 0 0 auto;
      max-width: 240px;
      border-right: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
      border-bottom: 2px solid transparent;
      background: transparent;
      color: var(--colorNeutralForeground2);
    }

    .tab-wrap:hover {
      background: var(--colorNeutralBackground2Hover);
      color: var(--colorNeutralForeground1);
    }

    .tab-wrap[data-active='true'] {
      background: var(--colorNeutralBackground1);
      border-bottom-color: var(--colorBrandBackground);
      color: var(--colorNeutralForeground1);
    }

    .tab {
      display: flex;
      align-items: center;
      gap: var(--spacingHorizontalXS);
      flex: 1 1 auto;
      min-width: 0;
      min-height: 34px;
      padding: 0 var(--spacingHorizontalXS) 0 var(--spacingHorizontalM);
      border: none;
      background: transparent;
      color: inherit;
      cursor: default;
      text-align: left;
    }

    .tab:focus-visible {
      outline: var(--strokeWidthThicker) solid var(--colorStrokeFocus2);
      outline-offset: -2px;
    }

    .tab .name {
      min-width: 0;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .tab svg {
      width: 13px;
      height: 13px;
      flex: none;
      opacity: .8;
    }

    .dirty {
      width: 6px;
      height: 6px;
      border-radius: 50%;
      background: var(--colorNeutralForeground2);
      flex: none;
    }

    .problem {
      display: grid;
      place-items: center;
      color: var(--colorStatusWarningForeground1);
      flex: none;
    }

    .problem svg {
      width: 13px;
      height: 13px;
      opacity: 1;
    }

    .close {
      width: 20px;
      height: 20px;
      margin-inline-end: var(--spacingHorizontalXS);
    }

    .new {
      align-self: center;
      margin: 0 var(--spacingHorizontalXS);
    }

    .export {
      align-self: center;
      margin-left: auto;
    }

    .confirm-overlay {
      position: fixed;
      inset: 0;
      z-index: 80;
      display: grid;
      place-items: center;
      background: var(--shell-scrim);
    }

    .confirm {
      width: min(420px, calc(100vw - 32px));
      padding: var(--spacingVerticalL) var(--spacingHorizontalXL);
    }

    .confirm h2 {
      margin: 0 0 var(--spacingVerticalS);
      font-size: var(--fontSizeBase400);
      font-weight: var(--fontWeightSemibold);
    }

    .confirm p {
      margin: 0 0 var(--spacingVerticalL);
      color: var(--colorNeutralForeground1);
    }

    .confirm-actions {
      display: flex;
      justify-content: flex-end;
      gap: var(--spacingHorizontalS);
    }
  `];

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
    const state = editor.get();
    return html`
      <div class="strip" role="tablist" aria-label=${t('Editor.DocumentTabs')} @keydown=${this.onKeydown}>
        ${state.tabs.map((tab) => html`
          <div class="tab-wrap" data-active=${tab.key === state.activeKey ? 'true' : 'false'}>
            <button
              class="tab"
              role="tab"
              data-key=${tab.key}
              aria-selected=${tab.key === state.activeKey ? 'true' : 'false'}
              tabindex=${tab.key === state.activeKey ? '0' : '-1'}
              title=${tab.name}
              @click=${() => void editor.activateTab(tab.key)}
              @auxclick=${(event: MouseEvent) => {
                if (event.button === 1) {
                  event.preventDefault();
                  editor.requestCloseTab(tab.key);
                }
              }}
            >
              ${icon(tab.isSubMacro ? 'workflow' : 'file-text')}
              <span class="name">${tab.name}</span>
              ${tab.dirty ? html`<span class="dirty" title=${t('Editor.Unsaved')}></span>` : nothing}
              ${tab.problem
                ? html`<span class="problem" aria-hidden="true">${icon('triangle-alert')}</span>`
                : nothing}
            </button>
            <button
              class="ghost close"
              title=${t('Common.Close')}
              aria-label=${t('Common.Close')}
              @click=${(event: Event) => {
                event.stopPropagation();
                editor.requestCloseTab(tab.key);
              }}
            >${icon('close')}</button>
          </div>
        `)}
        <span style="flex:1"></span>
        <button
          class="ghost export"
          title=${t('Editor.ExportDocument')}
          aria-label=${t('Editor.ExportDocument')}
          @click=${() => void editor.exportDocument()}
        >${icon('export')}</button>
        <button
          class="ghost new"
          title=${t('Main.AddMacro')}
          aria-label=${t('Main.AddMacro')}
          @click=${() => void editor.newDocument()}
        >${icon('plus')}</button>
      </div>
      ${state.closeRequest
        ? html`
          <div class="confirm-overlay" @click=${() => editor.cancelClose()}>
            <div class="confirm panel" role="dialog" aria-modal="true"
              @click=${(event: Event) => event.stopPropagation()}>
              <h2>${t('Editor.UnsavedTitle')}</h2>
              <p>${t('Editor.UnsavedMessage')}</p>
              <div class="confirm-actions">
                <fluent-button appearance="subtle" @click=${() => editor.cancelClose()}>
                  ${t('Common.Cancel')}
                </fluent-button>
                <fluent-button appearance="accent" @click=${() => void editor.confirmClose()}>
                  ${t('Editor.Save')}
                </fluent-button>
              </div>
            </div>
          </div>
        `
        : nothing}
    `;
  }

  private onKeydown = (event: KeyboardEvent): void => {
    const state = editor.get();
    const index = state.tabs.findIndex((tab) => tab.key === state.activeKey);
    if (index < 0) {
      return;
    }

    let next = index;
    if (event.key === 'ArrowLeft') {
      next = Math.max(0, index - 1);
    } else if (event.key === 'ArrowRight') {
      next = Math.min(state.tabs.length - 1, index + 1);
    } else if (event.key === 'Home') {
      next = 0;
    } else if (event.key === 'End') {
      next = state.tabs.length - 1;
    } else {
      return;
    }

    event.preventDefault();
    const tab = state.tabs[next];
    if (!tab) {
      return;
    }

    void editor.activateTab(tab.key).then(() => {
      const button = this.renderRoot.querySelector<HTMLButtonElement>(
        `.tab[data-key="${CSS.escape(tab.key)}"]`);
      button?.focus();
    });
  };
}

declare global {
  interface HTMLElementTagNameMap {
    'wg-editor-tabs': WgEditorTabs;
  }
}
