import { LitElement, css, html } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import type { EditorActionForm, EditorDuration } from '../../shared/bridge/protocol.js';
import { t } from '../../shared/strings/strings.js';
import { editor } from './editor-store.js';
import { editorBase } from './editor-css.js';

/** The settings every step has: pauses, a budget, retries and the rule a failure follows. */
@customElement('wg-action-exec')
export class WgActionExec extends LitElement {
  @property({ attribute: false })
  public form!: EditorActionForm;

  connectedCallback(): void {
    super.connectedCallback();
    this.classList.add('step-meta');
  }

  static styles = [editorBase, css`
    :host { display: grid; gap: var(--spacingVerticalL); }
    h3 { margin: 0; color: var(--colorNeutralForeground2); font-size: var(--fontSizeBase200); font-weight: var(--fontWeightSemibold); }
    .grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: var(--spacingVerticalS) var(--spacingHorizontalS); }
    .field { display: grid; gap: 3px; min-width: 0; }
    .field > span { color: var(--colorNeutralForeground2); font-size: var(--fontSizeBase200); }
    .inline { display: flex; gap: var(--spacingHorizontalXS); align-items: center; }
    .inline input { flex: 1; min-width: 0; }
    .plan { color: var(--colorNeutralForeground3); font-size: var(--fontSizeBase100); }
    .warn { color: var(--colorStatusWarningForeground1); font-size: var(--fontSizeBase100); }
    .rule { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 3px; }
    .rule label { display: flex; gap: var(--spacingHorizontalXS); align-items: center; padding: var(--spacingVerticalS); border: var(--strokeWidthThin) solid var(--colorNeutralStroke2); border-radius: var(--borderRadiusMedium); background: var(--colorNeutralBackground2); font-size: var(--fontSizeBase200); }
  `];

  render() {
    const meta = this.form.meta;
    return html`
      <section>
        <h3>${t('Add.Page.Exec')}</h3>
        <div class="grid">
          ${this.duration('delayBeforeMs', t('Add.StepDelayBefore'), meta.delayBefore)}
          ${this.duration('delayAfterMs', t('Add.StepDelayAfter'), meta.delayAfter)}
          ${this.duration('timeoutMs', t('Add.StepTimeout'), meta.timeout)}
          <label class="field">
            <span>${t('Add.StepRetry')}</span>
            <input type="number" min="0" max="20" .value=${meta.retryCount === null ? '' : String(meta.retryCount)}
              @change=${(event: Event) => this.meta('retryCount', (event.target as HTMLInputElement).value)} />
          </label>
          ${this.duration('retryDelayMs', t('Add.StepRetryDelay'), meta.retryDelay)}
          <label class="field">
            <span>${t('Add.StepBackoff')}</span>
            <select .value=${meta.retryBackoff}
              @change=${(event: Event) => this.meta('retryBackoff', (event.target as HTMLSelectElement).value)}>
              <option value="fixed">${t('Add.Backoff.Fixed')}</option>
              <option value="doubling">${t('Add.Backoff.Doubling')}</option>
              <option value="jitter">${t('Add.Backoff.Jitter')}</option>
            </select>
          </label>
        </div>
        ${meta.retryPlan ? html`<p class="plan">${meta.retryPlan}</p>` : ''}
      </section>
      <section>
        <h3>${t('Add.StepOnError')}</h3>
        <div class="rule">
          ${this.rule('stop', t('Add.OnError.Stop'), meta.onError)}
          ${this.rule('continue', t('Add.OnError.Continue'), meta.onError)}
          ${this.rule('nextIteration', t('Add.OnError.NextIteration'), meta.onError)}
          ${this.rule('ask', t('Add.OnError.AskUser'), meta.onError)}
        </div>
        ${meta.onError === 'ask'
          ? html`<p class="warn">${t('Add.Exec.AskWarn')}</p>`
          : ''}
      </section>
    `;
  }

  private duration(field: string, label: string, duration: EditorDuration) {
    return html`
      <label class="field">
        <span>${label}</span>
        <div class="inline">
          <input type="number" min="0" .value=${String(duration.milliseconds)}
            @change=${(event: Event) => this.meta(field, (event.target as HTMLInputElement).value)} />
          <span class="plan">${t('Editor.Milliseconds')}</span>
        </div>
      </label>
    `;
  }

  private rule(value: string, label: string, chosen: string) {
    return html`
      <label>
        <input type="radio" name="wg-on-error" value=${value} .checked=${chosen === value}
          @change=${() => this.meta('onError', value)} />
        <span>${label}</span>
      </label>
    `;
  }

  private meta(field: string, value: string): void {
    void editor.action.patchActionMeta(field, value);
  }
}

declare global {
  interface HTMLElementTagNameMap {
    'wg-action-exec': WgActionExec;
  }
}
