import { LitElement, css, html, nothing, type TemplateResult } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import { icon } from '../../shared/icons.js';
import { t } from '../../shared/strings/strings.js';
import { editor } from './editor-store.js';
import { StoreController } from '../../shared/lit/store-controller.js';
import { WindowEventController } from '../../shared/lit/window-event-controller.js';
import { editorBase } from './editor-css.js';
import './action-page.js';
import './action-exec.js';
import './action-other.js';
import './action-returns.js';
import './action-global.js';

/** The action form: a draft step, edited page by page, committed only on confirm. */
@customElement('wg-action-editor')
export class WgActionEditor extends LitElement {
  static styles = [editorBase, css`
    :host { position: fixed; inset: 0; z-index: 50; display: grid; place-items: center; padding: 16px; background: var(--shell-scrim, rgba(0, 0, 0, 0.35)); box-sizing: border-box; }
    .dialog { display: flex; flex-direction: column; width: min(1060px, 100%); height: min(820px, 100%); min-height: 0; overflow: hidden; background: var(--colorNeutralBackground1); border: var(--strokeWidthThin) solid var(--colorNeutralStroke1); border-radius: var(--borderRadiusLarge); box-shadow: var(--shadow64); }
    header { display: flex; align-items: center; gap: var(--spacingHorizontalS); flex: none; padding: var(--spacingVerticalS) var(--spacingHorizontalL); border-bottom: var(--strokeWidthThin) solid var(--colorNeutralStroke2); background: var(--colorNeutralBackground2); }
    header h2 { margin: 0; font-size: var(--fontSizeBase300); font-weight: var(--fontWeightSemibold); }
    header .type { color: var(--colorNeutralForeground3); font-size: var(--fontSizeBase100); }
    header .description { color: var(--colorNeutralForeground3); font-size: var(--fontSizeBase100); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; max-width: 38%; }
    header .close { margin-left: auto; }
    .tabs { display: flex; flex: none; gap: var(--spacingHorizontalXS); padding: 0 var(--spacingHorizontalL); border-bottom: var(--strokeWidthThin) solid var(--colorNeutralStroke2); background: var(--colorNeutralBackground2); }
    .tabs button { min-width: 0; height: 34px; padding: 0 var(--spacingHorizontalS); border: none; border-bottom: 2px solid transparent; background: transparent; color: var(--colorNeutralForeground2); font: inherit; font-size: var(--fontSizeBase200); cursor: pointer; }
    .tabs button[aria-selected='true'] { color: var(--colorNeutralForeground1); border-bottom-color: var(--colorBrandBackground); }
    .tabs button:focus-visible { outline: var(--strokeWidthThicker) solid var(--colorStrokeFocus2); outline-offset: -2px; }
    .body { flex: 1; min-height: 0; overflow: auto; padding: var(--spacingVerticalM) var(--spacingHorizontalL) var(--spacingVerticalL); display: grid; gap: var(--spacingVerticalL); align-content: start; }
    .sections { display: grid; gap: var(--spacingVerticalS); }
    .banner { margin: 0 0 var(--spacingVerticalS); padding: var(--spacingVerticalS) var(--spacingHorizontalM); border-radius: var(--borderRadiusMedium); font-size: var(--fontSizeBase200); white-space: pre-wrap; }
    .banner.problem { color: var(--colorStatusDangerForeground1); background: var(--colorStatusDangerBackground1); border: var(--strokeWidthThin) solid var(--colorStatusDangerBorder1); }
    .block { display: grid; gap: 3px; }
    .block > span { color: var(--colorNeutralForeground2); font-size: var(--fontSizeBase200); }
    .note { margin: 0; color: var(--colorNeutralForeground3); font-size: var(--fontSizeBase100); }
    .summary { padding: var(--spacingVerticalS) var(--spacingHorizontalS); border: var(--strokeWidthThin) solid var(--colorNeutralStroke2); border-radius: var(--borderRadiusMedium); background: var(--colorNeutralBackground2); color: var(--colorNeutralForeground2); font-size: var(--fontSizeBase200); }
    .call { display: grid; gap: 3px; }
    .call-line { display: grid; grid-template-columns: minmax(0, 1fr) minmax(0, 1.2fr) 80px; gap: var(--spacingHorizontalS); align-items: center; }
    .call-line .name { color: var(--colorNeutralForeground2); font-size: var(--fontSizeBase200); }
    .call-line .hint { grid-column: 1 / -1; color: var(--colorNeutralForeground3); font-size: var(--fontSizeBase100); }
    .call-line input[type='text'] { min-width: 0; }
    h3 { margin: 0 0 var(--spacingVerticalXS); color: var(--colorNeutralForeground2); font-size: var(--fontSizeBase200); font-weight: var(--fontWeightSemibold); }
    footer { flex: none; display: flex; align-items: center; gap: var(--spacingHorizontalXS); padding: var(--spacingVerticalS) var(--spacingHorizontalL); border-top: var(--strokeWidthThin) solid var(--colorNeutralStroke2); background: var(--colorNeutralBackground2); }
    footer .note { flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    footer .problem { color: var(--colorStatusDangerForeground1); }
    footer .notice { color: var(--colorStatusSuccessForeground1); }
    footer fluent-button { min-width: 0; }
    .overlay { position: fixed; inset: 0; z-index: 70; display: grid; place-items: center; padding: 20px; background: var(--shell-scrim); }
    .card { width: min(880px, 100%); max-height: 88vh; display: flex; flex-direction: column; gap: var(--spacingVerticalS); padding: var(--spacingVerticalL); background: var(--colorNeutralBackground1); border: var(--strokeWidthThin) solid var(--colorNeutralStroke1); border-radius: var(--borderRadiusLarge); box-shadow: var(--shadow64); }
    .card h3 { margin: 0; font-size: var(--fontSizeBase400); }
    .card p { margin: 0; color: var(--colorNeutralForeground2); white-space: pre-line; }
    .picture { position: relative; overflow: auto; border: var(--strokeWidthThin) solid var(--colorNeutralStroke2); border-radius: var(--borderRadiusMedium); background: var(--colorNeutralBackground2); }
    .picture img { display: block; max-width: 100%; height: auto; }
    .mark { position: absolute; border: 2px solid var(--colorBrandStroke1); box-shadow: 0 0 0 1px var(--colorNeutralBackground1); pointer-events: none; }
    .mark[data-chosen='true'] { border-color: var(--colorStatusSuccessBorder1); }
    .actions { display: flex; justify-content: flex-end; gap: var(--spacingHorizontalS); }
  `];

  @state()
  private trialAsk: { title: string; question: string } | null = null;

  protected readonly editorController = new StoreController<void>(this, editor);

  protected readonly keydownController = new WindowEventController(this, 'keydown', (event) => {
    this.onKeydown(event);
  });

  private async run(): Promise<void> {
    const reply = await editor.action.runAction();
    if (reply?.needsConfirm && reply.question) {
      this.trialAsk = { title: reply.title ?? '', question: reply.question };
    }
  }

  private async confirmTrial(): Promise<void> {
    this.trialAsk = null;
    await editor.action.runAction(true);
  }

  private renderTrialAsk(): TemplateResult {
    return html`
      <div class="overlay">
        <div class="card" role="alertdialog" aria-modal="true">
          <h3>${this.trialAsk?.title}</h3>
          <p>${this.trialAsk?.question}</p>
          <div class="actions">
            <fluent-button appearance="subtle"
              @click=${() => { this.trialAsk = null; }}>${t('Common.Cancel')}</fluent-button>
            <fluent-button appearance="accent"
              @click=${() => void this.confirmTrial()}>${t('Common.Ok')}</fluent-button>
          </div>
        </div>
      </div>
    `;
  }

  private renderTrialResult(): TemplateResult | typeof nothing {
    const trial = editor.get().trial;
    if (!trial) {
      return nothing;
    }

    return html`
      <div class="overlay">
        <div class="card" role="dialog" aria-modal="true">
          <h3>${trial.success ? t('Add.RanStep').replace('{0}', editor.get().actionForm?.name ?? '')
            : t('Run.Failed')}</h3>
          ${trial.image
            ? html`
              <div class="picture">
                <img src=${trial.image} alt=${trial.looking} />
                ${trial.boxes.map((box, index) => html`
                  <span class="mark"
                    data-chosen=${index + 1 === trial.chosenIndex ? 'true' : 'false'}
                    style=${this.markStyle(trial, box)}></span>`)}
              </div>`
            : nothing}
          <p>${trial.detail}</p>
          <div class="actions">
            <fluent-button appearance="accent"
              @click=${() => editor.action.closeTrial()}>${t('Common.Close')}</fluent-button>
          </div>
        </div>
      </div>
    `;
  }

  private markStyle(trial: NonNullable<ReturnType<typeof editor.get>['trial']>,
    box: { x: number; y: number; width: number; height: number }): string {
    if (trial.frameWidth <= 0 || trial.frameHeight <= 0) {
      return 'display:none';
    }

    const left = ((box.x - trial.originX) / trial.frameWidth) * 100;
    const top = ((box.y - trial.originY) / trial.frameHeight) * 100;
    const width = (box.width / trial.frameWidth) * 100;
    const height = (box.height / trial.frameHeight) * 100;
    return 'left:' + left + '%;top:' + top + '%;width:' + width + '%;height:' + height + '%';
  }

  private onKeydown = (event: KeyboardEvent): void => {
    if (event.key === 'Escape' && editor.get().actionForm) {
      event.preventDefault();
      void editor.action.cancelAction();
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
      : form.page;

    return html`
      <div class="dialog" role="dialog" aria-modal="true" aria-label=${form.name}>
        <header>
          <h2>${form.isEditing ? t('Add.EditTitle') : t('Add.Title')}</h2>
          <span class="type">${form.name}</span>
          ${form.description ? html`<span class="description" title=${form.description}>${form.description}</span>` : nothing}
          <button class="ghost close" type="button" title=${t('Common.Close')}
            @click=${() => void editor.action.cancelAction()}>${icon('close')}</button>
        </header>
        <div class="tabs" role="tablist">
          ${pages.map((candidate) => html`
            <button type="button" role="tab"
              aria-selected=${candidate.key === page ? 'true' : 'false'}
              title=${candidate.label}
              @click=${() => void editor.action.setActionPage(candidate.key)}>
              ${candidate.label}${candidate.hasDot ? ' ·' : ''}
            </button>
          `)}
        </div>
        <div class="body">
          ${state.actionError
            ? html`<p class="banner problem" role="alert">${state.actionError}</p>`
            : nothing}
          ${this.renderPage(form, page)}
        </div>
        <footer>
          <span class=${form.validationMessage
            ? 'note problem'
            : form.notice ? (form.noticeIsWarning ? 'note problem' : 'note notice') : 'note'}
            title=${form.validationMessage || form.notice}>
            ${form.validationMessage || form.notice || ''}
          </span>
          ${form.canRun
            ? html`<fluent-button appearance="subtle"
                @click=${() => void this.run()}>${form.runLabel || t('Add.RunStep')}</fluent-button>`
            : nothing}
          <fluent-button appearance="subtle"
            @click=${() => void editor.action.cancelAction()}>${t('Add.Cancel')}</fluent-button>
          <fluent-button appearance="accent" ?disabled=${!form.canConfirm}
            @click=${() => void editor.action.confirmAction()}>${form.commitLabel}</fluent-button>
        </footer>
      </div>
      ${this.trialAsk ? this.renderTrialAsk() : nothing}
      ${this.renderTrialResult()}
    `;
  }

  private renderPage(form: ReturnType<typeof editor.get>['actionForm'], page: string): TemplateResult | typeof nothing {
    if (!form) {
      return nothing;
    }

    if (page === 'exec') {
      return html`<wg-action-exec .form=${form}></wg-action-exec>`;
    }

    if (page === 'other') {
      return html`<wg-action-other .form=${form}></wg-action-other>`;
    }

    const rows = page === 'advanced' ? form.advancedRows : form.rows;
    return html`
      <wg-action-page .rows=${rows}></wg-action-page>
      ${page === 'base' ? this.renderBase(form) : nothing}
    `;
  }

  private renderBase(form: NonNullable<ReturnType<typeof editor.get>['actionForm']>): TemplateResult {
    return html`
      <label class="block">
        <span>${t('Add.StepComment')}</span>
        <input .value=${form.meta.comment} placeholder=${t('Add.StepCommentPlaceholder')}
          @change=${(event: Event) => void editor.action.patchActionMeta('comment',
            (event.target as HTMLInputElement).value)} />
      </label>
      ${form.showsAttempt && form.attemptSummary
        ? html`<div class="summary">${form.attemptSummary}</div>`
        : nothing}
      ${form.showsBranches
        ? html`
          <div class="summary">
            ${form.branchSummary}
            <button class="chip" type="button"
              @click=${() => void editor.action.addActionBranch()}>${t('Add.AddBranch')}</button>
          </div>
        `
        : nothing}
      ${form.callArguments.length > 0
        ? html`
          <section class="sections">
            <h3>${t('Add.CallArguments')}</h3>
            <div class="call">
              ${form.callArguments.map((argument) => html`
                <div class="call-line">
                  <span class="name">${argument.name}${argument.required ? ' *' : ''}</span>
                  <input type="text" .value=${argument.value}
                    ?disabled=${argument.given}
                    @change=${(event: Event) => void editor.action.patchActionCallArgument(argument.name, {
                      value: (event.target as HTMLInputElement).value,
                    })} />
                  <label class="inline">
                    <input type="checkbox" .checked=${argument.given}
                      @change=${(event: Event) => void editor.action.patchActionCallArgument(argument.name, {
                        given: (event.target as HTMLInputElement).checked,
                      })} />
                    <span class="note">${t('Add.CallArgumentGiven')}</span>
                  </label>
                  ${argument.hint ? html`<span class="hint">${argument.hint}</span>` : nothing}
                </div>
              `)}
            </div>
          </section>
        `
        : nothing}
      ${form.globalDraft
        ? html`<wg-action-global .draft=${form.globalDraft}></wg-action-global>`
        : nothing}
      <section class="sections">
        <h3>${t('Add.ReturnPickHint')}</h3>
        <wg-action-returns .rows=${form.returns}></wg-action-returns>
      </section>
      ${form.clipboardPreview.shown
        ? html`<p class="note">${form.clipboardPreview.note}</p>`
        : nothing}
    `;
  }
}

declare global {
  interface HTMLElementTagNameMap {
    'wg-action-editor': WgActionEditor;
  }
}
