import { LitElement, css, html, nothing } from 'lit';
import { customElement } from 'lit/decorators.js';
import { icon } from '../../shared/icons.js';
import { t } from '../../shared/strings/strings.js';
import { editor } from './editor-store.js';
import { editorBase } from './editor-css.js';
import './step-inspector.js';

/** The action form: a draft step, edited page by page, committed only on confirm. */
@customElement('wg-action-editor')
export class WgActionEditor extends LitElement {
  static styles = [editorBase, css`
    :host { position: fixed; inset: 0; z-index: 50; display: grid; place-items: center; padding: 16px; background: var(--shell-scrim, rgba(0, 0, 0, 0.35)); box-sizing: border-box; }
    .dialog { display: flex; flex-direction: column; width: min(960px, 100%); height: min(760px, 100%); min-height: 0; overflow: hidden; background: var(--colorNeutralBackground1); border: var(--strokeWidthThin) solid var(--colorNeutralStroke1); border-radius: var(--borderRadiusLarge); box-shadow: var(--shadow64); }
    header { display: flex; align-items: center; gap: var(--spacingHorizontalS); flex: none; padding: var(--spacingVerticalS) var(--spacingHorizontalL); border-bottom: var(--strokeWidthThin) solid var(--colorNeutralStroke2); background: var(--colorNeutralBackground2); }
    header h2 { margin: 0; font-size: var(--fontSizeBase300); font-weight: var(--fontWeightSemibold); }
    header .type { color: var(--colorNeutralForeground3); font-size: var(--fontSizeBase100); }
    header .close { margin-left: auto; }
    .tabs { display: flex; flex: none; gap: var(--spacingHorizontalXS); padding: 0 var(--spacingHorizontalL); border-bottom: var(--strokeWidthThin) solid var(--colorNeutralStroke2); background: var(--colorNeutralBackground2); }
    .tabs button { min-width: 0; height: 34px; padding: 0 var(--spacingHorizontalS); border: none; border-bottom: 2px solid transparent; background: transparent; color: var(--colorNeutralForeground2); font: inherit; font-size: var(--fontSizeBase200); cursor: pointer; }
    .tabs button[aria-selected='true'] { color: var(--colorNeutralForeground1); border-bottom-color: var(--colorBrandBackground); }
    .tabs button:focus-visible { outline: var(--strokeWidthThicker) solid var(--colorStrokeFocus2); outline-offset: -2px; }
    .body { flex: 1; min-height: 0; overflow: auto; padding: 0 var(--spacingHorizontalL); }
    .other { padding: var(--spacingVerticalM) 0; display: grid; grid-template-columns: max-content minmax(0, 1fr); gap: 6px var(--spacingHorizontalS); color: var(--colorNeutralForeground2); font-size: var(--fontSizeBase200); }
    .other dt { color: var(--colorNeutralForeground3); }
    .other dd { margin: 0; font-family: var(--fontFamilyMonospace); overflow-wrap: anywhere; }
    .json { margin: var(--spacingVerticalM) 0; padding: 8px; max-height: 52vh; overflow: auto; background: var(--colorNeutralBackground2); border: var(--strokeWidthThin) solid var(--colorNeutralStroke2); border-radius: var(--borderRadiusMedium); font-family: var(--fontFamilyMonospace); font-size: var(--fontSizeBase100); white-space: pre-wrap; }
    footer { flex: none; display: flex; align-items: center; gap: var(--spacingHorizontalXS); padding: var(--spacingVerticalS) var(--spacingHorizontalL); border-top: var(--strokeWidthThin) solid var(--colorNeutralStroke2); background: var(--colorNeutralBackground2); }
    footer .note { flex: 1; min-width: 0; color: var(--colorNeutralForeground3); font-size: var(--fontSizeBase100); overflow: hidden; text-overflow: ellipsis; }
    footer .note.problem { color: var(--colorStatusDangerForeground1); }
    footer fluent-button { min-width: 0; }
  `];

  private unsubscribe?: () => void;

  connectedCallback(): void {
    super.connectedCallback();
    this.unsubscribe = editor.subscribe(() => this.requestUpdate());
    window.addEventListener('keydown', this.onKeydown);
  }

  disconnectedCallback(): void {
    this.unsubscribe?.();
    window.removeEventListener('keydown', this.onKeydown);
    super.disconnectedCallback();
  }

  private onKeydown = (event: KeyboardEvent): void => {
    if (event.key === 'Escape' && editor.get().actionForm) {
      event.preventDefault();
      void editor.cancelAction();
    }
  };

  render() {
    const state = editor.get();
    const form = state.actionForm;
    this.style.display = form ? 'grid' : 'none';
    if (!form) {
      return nothing;
    }

    const pages = form.pages.filter((page) => page.present);
    const page = pages.some((candidate) => candidate.key === state.actionPage)
      ? state.actionPage
      : pages[0]?.key ?? 'base';
    const name = form.isEditing ? t('Add.EditTitle') : t('Add.Title');

    return html`
      <div class="dialog" role="dialog" aria-modal="true" aria-label=${name}>
        <header>
          <h2>${name}</h2>
          <span class="type">${form.name}</span>
          <button class="ghost close" type="button" title=${t('Common.Close')}
            @click=${() => void editor.cancelAction()}>${icon('close')}</button>
        </header>
        <div class="tabs" role="tablist">
          ${pages.map((candidate) => html`
            <button type="button" role="tab"
              aria-selected=${candidate.key === page ? 'true' : 'false'}
              title=${candidate.label}
              @click=${() => editor.setActionPage(candidate.key)}>
              ${candidate.label}${candidate.hasDot ? ' ·' : ''}
            </button>
          `)}
        </div>
        <div class="body">
          ${page === 'other'
            ? html`
              <dl class="other">
                <dt>${t('Add.StepId')}</dt><dd>${form.stepId}</dd>
                <dt>${t('Editor.StepParameters')}</dt><dd>${form.actionKey}</dd>
              </dl>
              <pre class="json">${form.json}</pre>
            `
            : html`<wg-step-inspector .step=${form.step} .page=${page}
                .showHeader=${false}></wg-step-inspector>`}
        </div>
        <footer>
          <span class=${form.validationMessage ? 'note problem' : 'note'}
            title=${form.validationMessage || form.runNote}>
            ${form.validationMessage || form.runNote}
          </span>
          <fluent-button appearance="subtle" ?disabled=${!form.canRun}
            title=${form.runNote}>${t('Add.RunStep')}</fluent-button>
          <fluent-button appearance="subtle"
            @click=${() => void editor.cancelAction()}>${t('Add.Cancel')}</fluent-button>
          <fluent-button appearance="accent" ?disabled=${!form.canConfirm}
            @click=${() => void editor.confirmAction()}>${form.commitLabel}</fluent-button>
        </footer>
      </div>
    `;
  }
}

declare global {
  interface HTMLElementTagNameMap {
    'wg-action-editor': WgActionEditor;
  }
}
