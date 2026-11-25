import { LitElement, css, html, nothing } from 'lit';
import { customElement } from 'lit/decorators.js';
import { t } from '../../shared/strings/strings.js';
import { StoreController } from '../../shared/lit/store-controller.js';
import { editor } from './editor-store.js';

/**
 * The one question about unsaved work. It lives in the shell rather than in the editor page so
 * it can stay up while the editor is being left, and so a dirty document's close, a page change,
 * the title bar's close and the host's own window close all show the same dialog.
 */
@customElement('wg-editor-unsaved')
export class WgEditorUnsaved extends LitElement {
  static styles = css`
    .overlay {
      position: fixed;
      inset: 0;
      z-index: 90;
      display: grid;
      place-items: center;
      background: var(--shell-scrim);
    }

    .dialog {
      width: min(460px, calc(100vw - 32px));
      padding: var(--spacingVerticalL) var(--spacingHorizontalXL);
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke1);
      border-radius: var(--borderRadiusLarge);
      background: var(--colorNeutralBackground1);
      box-shadow: var(--shadow16);
    }

    h2 {
      margin: 0 0 var(--spacingVerticalS);
      font-size: var(--fontSizeBase400);
      font-weight: var(--fontWeightSemibold);
    }

    p {
      margin: 0 0 var(--spacingVerticalL);
      color: var(--colorNeutralForeground1);
      white-space: pre-line;
    }

    .details {
      margin: 0 0 var(--spacingVerticalS);
      padding-inline-start: var(--spacingHorizontalM);
      color: var(--colorPaletteRedForeground1);
      font-size: var(--fontSizeBase200);
      line-height: 1.5;
      overflow-wrap: anywhere;
    }

    .error {
      margin: 0 0 var(--spacingVerticalS);
      color: var(--colorPaletteRedForeground1);
    }

    .actions {
      display: flex;
      justify-content: flex-end;
      gap: var(--spacingHorizontalS);
    }

    .danger {
      color: var(--colorPaletteRedForeground1);
    }
  `;

  protected readonly dialogController = new StoreController(this, editor.dialog);

  render() {
    const state = editor.dialog.get();
    if (!state.open) {
      return nothing;
    }

    const message = state.kind === 'document'
      ? t('Editor.UnsavedMessageNamed').replace('{0}', state.documents[0] ?? '')
      : t('Editor.UnsavedExitMessage').replace('{0}', state.documents.join('\n'));
    return html`
      <div class="overlay" @click=${() => editor.dialog.cancel()}>
        <div class="dialog" role="dialog" aria-modal="true" data-kind=${state.kind}
          @click=${(event: Event) => event.stopPropagation()}>
          <h2>${t('Editor.UnsavedTitle')}</h2>
          <p>${message}</p>
          ${state.details.length > 0
            ? html`<ul class="details">
                ${state.details.map((detail) => html`<li class="detail">${detail}</li>`)}
              </ul>`
            : nothing}
          ${state.error ? html`<p class="error">${state.error}</p>` : nothing}
          <div class="actions">
            <fluent-button appearance="subtle"
              ?disabled=${state.busy}
              @click=${() => editor.dialog.cancel()}>${t('Common.Cancel')}</fluent-button>
            <fluent-button appearance="subtle" class="danger"
              ?disabled=${state.busy}
              @click=${() => void editor.dialog.answer('discard')}>${t('Editor.Discard')}</fluent-button>
            <fluent-button appearance="accent"
              ?disabled=${state.busy}
              @click=${() => void editor.dialog.answer('save')}>${t('Editor.Save')}</fluent-button>
          </div>
        </div>
      </div>
    `;
  }
}

declare global {
  interface HTMLElementTagNameMap {
    'wg-editor-unsaved': WgEditorUnsaved;
  }
}