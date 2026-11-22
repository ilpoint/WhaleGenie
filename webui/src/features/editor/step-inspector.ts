import { LitElement, css, html, nothing, type TemplateResult } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import { icon } from '../../shared/icons.js';
import { t } from '../../shared/strings/strings.js';
import type { EditorConditionNode, EditorParameter, EditorStep } from '../../shared/bridge/protocol.js';
import { findStep } from './editor-model.js';
import { editor } from './editor-store.js';
import { editorBase } from './editor-css.js';
import './value-field.js';

/**
 * One step's parameters. The form is generated from the snapshot: a field is text, a number, a
 * condition, a key or a table, and every change is one command back to the C# session.
 */
@customElement('wg-step-inspector')
export class WgStepInspector extends LitElement {
  @state()
  private capturing = '';

  static styles = [editorBase, css`
    :host {
      display: block;
      padding: var(--spacingVerticalM) var(--spacingHorizontalL) var(--spacingVerticalXXL);
    }

    .empty {
      display: grid;
      gap: var(--spacingVerticalXS);
      padding: var(--spacingVerticalXXL) var(--spacingVerticalM);
      color: var(--colorNeutralForeground3);
      text-align: center;
      font-size: var(--fontSizeBase200);
    }

    .head {
      display: grid;
      gap: 2px;
      margin-bottom: var(--spacingVerticalM);
    }

    .head .name {
      font-size: var(--fontSizeBase400);
      font-weight: var(--fontWeightSemibold);
      overflow-wrap: anywhere;
    }

    .head .type {
      color: var(--colorNeutralForeground3);
      font-family: var(--fontFamilyMonospace);
      font-size: var(--fontSizeBase100);
    }

    .section {
      display: grid;
      gap: var(--spacingVerticalS);
      margin-bottom: var(--spacingVerticalL);
    }

    .section > h3 {
      margin: 0;
      color: var(--colorNeutralForeground2);
      font-size: var(--fontSizeBase200);
      font-weight: var(--fontWeightSemibold);
    }

    .step-meta {
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      gap: var(--spacingVerticalS) var(--spacingHorizontalS);
    }

    .step-meta .wide {
      grid-column: 1 / -1;
    }

    .param {
      display: grid;
      gap: var(--spacingVerticalXS);
      min-width: 0;
      padding: var(--spacingVerticalS) var(--spacingHorizontalS);
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
      border-radius: var(--borderRadiusMedium);
      background: var(--colorNeutralBackground2);
    }

    .param > label {
      color: var(--colorNeutralForeground2);
      font-size: var(--fontSizeBase200);
    }

    .param-row {
      display: flex;
      gap: var(--spacingHorizontalXS);
      min-width: 0;
      align-items: center;
    }

    .param-row > input,
    .param-row > textarea {
      flex: 1;
      min-width: 0;
    }

    .param textarea {
      min-height: 64px;
      resize: vertical;
    }

    .formula-mark {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      width: 14px;
      height: 14px;
      margin-right: 4px;
      border-radius: var(--borderRadiusSmall);
      background: var(--colorNeutralBackground3);
      color: var(--colorNeutralForeground3);
      font-family: var(--fontFamilyMonospace);
      font-size: 10px;
      line-height: 1;
    }

    .key-row {
      display: flex;
      gap: var(--spacingHorizontalXS);
    }

    .key-row input {
      flex: 1;
      min-width: 0;
    }

    .color-row {
      display: flex;
      gap: var(--spacingHorizontalXS);
    }

    .color-row input[type='color'] {
      width: 42px;
      padding: 2px;
      flex: none;
    }

    .color-row input:last-child {
      flex: 1;
      min-width: 0;
    }

    .check {
      display: flex;
      align-items: center;
      gap: var(--spacingHorizontalS);
      min-height: 28px;
    }

    .row-list {
      display: grid;
      gap: 3px;
      min-width: 0;
      overflow-x: auto;
    }

    .row-line {
      display: grid;
      gap: 3px;
      align-items: center;
      min-width: 0;
    }

    .value-cell {
      display: flex;
      align-items: center;
      gap: 2px;
      min-width: 0;
    }

    .value-cell input {
      flex: 1;
      min-width: 0;
    }

    .value-cell .chip {
      flex: none;
      width: 22px;
      min-width: 22px;
      height: 26px;
      padding: 0;
      display: grid;
      place-items: center;
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke1);
      border-radius: var(--borderRadiusSmall);
      background: var(--colorNeutralBackground1);
      color: var(--colorBrandForeground2);
      font-size: var(--fontSizeBase100);
      cursor: pointer;
    }

    .value-cell .chip:hover {
      background: var(--colorBrandBackground2);
    }

    .row-line input {
      min-width: 0;
    }

    .row-line .head {
      color: var(--colorNeutralForeground3);
      font-size: var(--fontSizeBase100);
    }

    .row-add {
      justify-self: start;
      margin-top: 2px;
    }

    .condition-group {
      display: grid;
      gap: var(--spacingVerticalXS);
      min-width: 0;
    }

    .condition-join {
      display: flex;
      align-items: center;
      gap: var(--spacingHorizontalS);
    }

    .condition-row {
      display: grid;
      grid-template-columns: minmax(0, 1fr) 96px minmax(0, 1fr) 24px;
      gap: 3px;
      align-items: center;
    }

    .condition-row input,
    .condition-row select {
      min-width: 0;
      width: 100%;
    }

    .condition-nested {
      display: grid;
      gap: 3px;
      padding: var(--spacingVerticalXS) var(--spacingHorizontalXS)
        var(--spacingVerticalXS) var(--spacingVerticalS);
      border-left: 2px solid var(--colorNeutralStroke2);
      border-radius: var(--borderRadiusSmall);
      background: var(--colorNeutralBackground1);
    }

    .condition-nested-head {
      display: flex;
      justify-content: flex-end;
    }

    .condition-add {
      display: flex;
      gap: var(--spacingHorizontalXS);
      flex-wrap: wrap;
    }

    .condition-add fluent-button,
    .row-add fluent-button {
      min-width: 0;
    }

    [hidden] {
      display: none !important;
    }

    .problem {
      white-space: pre-line;
    }
  `];

  private unsubscribe?: () => void;

  @property({ attribute: false })
  step: EditorStep | null = null;

  @property({ attribute: false })
  page = 'base';

  @property({ attribute: false })
  showHeader = true;

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
    const snapshot = state.snapshot;
    const step = this.step
      ?? (snapshot ? findStep(snapshot.steps, state.selectedStepId) : null);
    if (!step) {
      return html`
        <div class="empty">
          <span>${t('Editor.NoSelection')}</span>
          <span class="hint">${t('Editor.StepParameters')}</span>
        </div>
      `;
    }

    const problems = step.problem.length > 0
      ? html`<p class="problem">${step.problem}</p>`
      : nothing;

    return html`
      <div class="head" ?hidden=${!this.showHeader}>
        <span class="name">${step.name}</span>
        <span class="type">${step.type}</span>
        ${problems}
      </div>
      <section class="section" ?hidden=${this.page !== 'exec'}>
        <h3>${t('Editor.StepMeta')}</h3>
        <div class="step-meta">
          <label class="field wide">
            <span>${t('Add.StepComment')}</span>
            <input .value=${step.comment}
              @change=${(event: Event) => void editor.setStepMeta(step.id, 'comment',
                (event.target as HTMLInputElement).value)} />
          </label>
          <label class="field">
            <span>${t('Add.StepTimeout')}</span>
            <input type="number" min="0" .value=${String(step.timeoutMs)}
              @change=${(event: Event) => void editor.setStepMeta(step.id, 'timeoutMs',
                (event.target as HTMLInputElement).value)} />
          </label>
          <label class="field">
            <span>${t('Add.StepRetry')}</span>
            <input type="number" min="0" .value=${String(step.retryCount)}
              @change=${(event: Event) => void editor.setStepMeta(step.id, 'retryCount',
                (event.target as HTMLInputElement).value)} />
          </label>
          <label class="field">
            <span>${t('Add.StepRetryDelay')}</span>
            <input type="number" min="0" .value=${String(step.retryDelayMs)}
              @change=${(event: Event) => void editor.setStepMeta(step.id, 'retryDelayMs',
                (event.target as HTMLInputElement).value)} />
          </label>
          <label class="field">
            <span>${t('Add.StepOnError')}</span>
            <select .value=${step.onError}
              @change=${(event: Event) => void editor.setStepMeta(step.id, 'onError',
                (event.target as HTMLSelectElement).value)}>
              <option value="Stop">${t('Add.OnError.Stop')}</option>
              <option value="Continue">${t('Add.OnError.Continue')}</option>
              <option value="NextIteration">${t('Add.OnError.NextIteration')}</option>
              <option value="AskUser">${t('Add.OnError.AskUser')}</option>
            </select>
          </label>
        </div>
      </section>
      <section class="section" ?hidden=${this.page !== 'base' && this.page !== 'advanced'}>
        ${this.page === 'base'
          ? html`
            <label class="field wide">
              <span>${t('Add.StepComment')}</span>
              <input .value=${step.comment}
                @change=${(event: Event) => void editor.setStepMeta(step.id, 'comment',
                  (event.target as HTMLInputElement).value)} />
            </label>
          `
          : nothing}
        <h3>${t('Editor.ParameterValues')}</h3>
        ${step.parameters
          .filter((parameter) => this.page === 'advanced' ? parameter.advanced : !parameter.advanced)
          .map((parameter) => this.renderParameter(step, parameter))}
      </section>
    `;
  }

  private renderParameter(step: EditorStep, parameter: EditorParameter): TemplateResult {
    if (parameter.columns.length > 0) {
      return this.renderRows(step, parameter);
    }

    if (parameter.kind === 'Key') {
      return html`
        <div class="param" data-parameter=${parameter.name}>
          <label>${parameter.label}</label>
          <div class="key-row">
            <input
              data-capture=${step.id + ':' + parameter.name}
              .value=${parameter.value}
              placeholder=${this.capturing === step.id + ':' + parameter.name
                ? t('Add.PressKey') : ''}
              @keydown=${(event: KeyboardEvent) => this.captureKey(step, parameter, event)}
              @change=${(event: Event) => void editor.patchParameter(
                step.id, parameter.name, (event.target as HTMLInputElement).value)}
            />
            <fluent-button appearance="subtle"
              @click=${() => this.startCapture(step, parameter)}>
              ${t('Add.SetCombination')}
            </fluent-button>
          </div>
        </div>
      `;
    }

    if (parameter.kind === 'Image') {
      return html`
        <div class="param" data-parameter=${parameter.name}>
          <label>${parameter.label}</label>
          <div class="param-row">
            <input .value=${parameter.value}
              @change=${(event: Event) => void editor.patchParameter(
                step.id, parameter.name, (event.target as HTMLInputElement).value)} />
            <fluent-button appearance="subtle"
              @click=${() => void editor.pickImage(step.id, parameter.name)}>
              ${t('Editor.Browse')}
            </fluent-button>
          </div>
        </div>
      `;
    }

    if (this.isValueField(parameter)) {
      return html`
        <div class="param" data-parameter=${parameter.name}>
          <label>
            ${parameter.takesFormula
              ? html`<span class="formula-mark" title=${t('Add.FieldTakesFormula')}>ƒ</span>`
              : nothing}
            ${parameter.label}
          </label>
          <wg-value-field .stepId=${step.id} .parameter=${parameter}></wg-value-field>
        </div>
      `;
    }

    if (parameter.kind === 'Color') {
      const colour = /^#[0-9a-f]{6}$/i.test(parameter.value) ? parameter.value : '#000000';
      return html`
        <div class="param" data-parameter=${parameter.name}>
          <label>${parameter.label}</label>
          <div class="color-row">
            <input type="color" .value=${colour}
              @input=${(event: Event) => void editor.patchParameter(
                step.id, parameter.name, (event.target as HTMLInputElement).value)} />
            <input .value=${parameter.value}
              @change=${(event: Event) => void editor.patchParameter(
                step.id, parameter.name, (event.target as HTMLInputElement).value)} />
          </div>
        </div>
      `;
    }

    if (parameter.kind === 'Bool') {
      return html`
        <div class="param" data-parameter=${parameter.name}>
          <label class="check">
            <input type="checkbox" .checked=${parameter.value === 'true'}
              @change=${(event: Event) => void editor.patchParameter(
                step.id, parameter.name, (event.target as HTMLInputElement).checked ? 'true' : 'false')} />
            <span>${parameter.label}</span>
          </label>
        </div>
      `;
    }

    if (parameter.kind === 'Choice' && parameter.options.length > 0) {
      return html`
        <div class="param" data-parameter=${parameter.name}>
          <label>${parameter.label}</label>
          <select .value=${parameter.value}
            @change=${(event: Event) => void editor.patchParameter(
              step.id, parameter.name, (event.target as HTMLSelectElement).value)}>
            ${parameter.options.map((choice) => html`
              <option value=${choice.value}>${choice.label}</option>
            `)}
          </select>
        </div>
      `;
    }

    if (parameter.kind === 'Condition' && parameter.condition?.editable) {
      return html`
        <div class="param" data-parameter=${parameter.name}>
          <label>${parameter.label}</label>
          ${this.renderCondition(step, parameter, parameter.condition.join,
            parameter.condition.nodes, [])}
        </div>
      `;
    }

    if (parameter.kind === 'Condition') {
      return html`
        <div class="param" data-parameter=${parameter.name}>
          <label>${parameter.label}</label>
          <textarea .value=${parameter.value}
            @change=${(event: Event) => void editor.patchParameter(
              step.id, parameter.name, (event.target as HTMLTextAreaElement).value)}></textarea>
        </div>
      `;
    }

    if (parameter.kind === 'MultilineText') {
      return html`
        <div class="param" data-parameter=${parameter.name}>
          <label>${parameter.label}</label>
          <textarea .value=${parameter.value}
            @change=${(event: Event) => void editor.patchParameter(
              step.id, parameter.name, (event.target as HTMLTextAreaElement).value)}></textarea>
        </div>
      `;
    }

    return html`
      <div class="param" data-parameter=${parameter.name}>
        <label>${parameter.label}</label>
        <div class="param-row">
          <input .value=${parameter.value}
            @change=${(event: Event) => void editor.patchParameter(
              step.id, parameter.name, (event.target as HTMLInputElement).value)} />
        </div>
      </div>
    `;
  }

  private isValueField(parameter: EditorParameter): boolean {
    return parameter.offersValue
      && !parameter.isKey
      && !['Condition', 'Rows', 'Returns', 'Steps', 'Narrow', 'Images', 'Region',
        'ColourPoints', 'KeySequence', 'Macro', 'Step', 'Bool', 'Choice']
        .includes(parameter.kind);
  }

  private valueChip(label: string, run: () => void): TemplateResult {
    return html`
      <button type="button" class="chip" title=${t('Add.Value')}
        aria-label=${t('Add.Value') + ': ' + label}
        @click=${(event: Event) => {
          event.stopPropagation();
          run();
        }}>ƒ</button>
    `;
  }

  private renderRows(step: EditorStep, parameter: EditorParameter): TemplateResult {
    const columns = parameter.columns.map((column) => column.name);
    return html`
      <div class="param" data-parameter=${parameter.name}>
        <label>${parameter.label}</label>
        <div class="row-list">
          <div class="row-line" style=${'grid-template-columns: repeat(' + String(columns.length) + ', minmax(0, 1fr)) 24px'}>
            ${parameter.columns.map((column) => html`<span class="head">${column.name}</span>`)}
            <span></span>
          </div>
          ${parameter.rows.map((row, index) => html`
            <div class="row-line" style=${'grid-template-columns: repeat(' + String(columns.length) + ', minmax(0, 1fr)) 24px'}>
              ${parameter.columns.map((column, cell) => html`
                <div class="value-cell">
                  <input .value=${row.cells[cell] ?? ''} aria-label=${column.name}
                    @change=${(event: Event) => void editor.patchRowCell(
                      step.id, parameter.name, index, column.name,
                      (event.target as HTMLInputElement).value)} />
                  ${this.valueChip(column.name, () =>
                    void editor.openRowValue(step.id, parameter.name, index, column.name))}
                </div>
              `)}
              <button class="ghost" title=${t('Editor.RemoveRow')}
                aria-label=${t('Editor.RemoveRow')}
                @click=${() => void editor.removeParameterRow(step.id, parameter.name, index)}>
                ${icon('close')}
              </button>
            </div>
          `)}
        </div>
        <fluent-button appearance="subtle" class="row-add"
          @click=${() => void editor.addParameterRow(step.id, parameter.name)}>
          ${t('Editor.AddRow')}
        </fluent-button>
      </div>
    `;
  }

  private renderCondition(step: EditorStep, parameter: EditorParameter, join: string,
    nodes: EditorConditionNode[], path: number[]): TemplateResult {
    return html`
      <div class="condition-group">
        <div class="condition-join">
          <span class="caption">${t('Editor.ConditionJoin')}</span>
          <select aria-label=${t('Editor.ConditionJoin')} .value=${join}
            @change=${(event: Event) => void editor.setConditionJoin(
              step.id, parameter.name, path, (event.target as HTMLSelectElement).value)}>
            <option value="All">${t('Condition.All')}</option>
            <option value="Any">${t('Condition.Any')}</option>
            <option value="None">${t('Condition.None')}</option>
          </select>
        </div>
        ${nodes.map((node, index) => node.kind === 'row'
          ? this.renderConditionRow(step, parameter, node, [...path, index])
          : html`
            <div class="condition-nested">
              <div class="condition-nested-head">
                <button class="ghost" aria-label=${t('Editor.RemoveRow')}
                  title=${t('Editor.RemoveRow')}
                  @click=${() => void editor.removeConditionNode(
                    step.id, parameter.name, [...path, index])}>
                  ${icon('close')}
                </button>
              </div>
              ${this.renderCondition(step, parameter, node.join, node.children, [...path, index])}
            </div>
          `)}
        <div class="condition-add">
          <fluent-button appearance="subtle"
            @click=${() => void editor.addConditionRow(step.id, parameter.name, path)}>
            ${t('Editor.AddRow')}
          </fluent-button>
          <fluent-button appearance="subtle"
            @click=${() => void editor.addConditionGroup(step.id, parameter.name, path)}>
            ${t('Editor.AddGroup')}
          </fluent-button>
        </div>
      </div>
    `;
  }

  private renderConditionRow(step: EditorStep, parameter: EditorParameter,
    row: EditorConditionNode, path: number[]): TemplateResult {
    const compares = editor.get().snapshot?.conditionCompares ?? [];
    return html`
      <div class="condition-row">
        <div class="value-cell">
          <input .value=${row.left} aria-label=${t('Editor.ConditionLeft')}
            @change=${(event: Event) => void editor.setConditionRow(
              step.id, parameter.name, path, 'left', (event.target as HTMLInputElement).value)} />
          ${this.valueChip(t('Editor.ConditionLeft'), () =>
            void editor.openConditionValue(step.id, parameter.name, path, 'left'))}
        </div>
        <select .value=${row.compare} aria-label=${t('Editor.ConditionCompare')}
          @change=${(event: Event) => void editor.setConditionRow(
            step.id, parameter.name, path, 'compare', (event.target as HTMLSelectElement).value)}>
          ${compares.map((compare) => html`
            <option value=${compare.value}>${compare.label}</option>
          `)}
        </select>
        <div class="value-cell">
          <input .value=${row.right} aria-label=${t('Editor.ConditionRight')}
            @change=${(event: Event) => void editor.setConditionRow(
              step.id, parameter.name, path, 'right', (event.target as HTMLInputElement).value)} />
          ${this.valueChip(t('Editor.ConditionRight'), () =>
            void editor.openConditionValue(step.id, parameter.name, path, 'right'))}
        </div>
        <button class="ghost" title=${t('Editor.RemoveRow')} aria-label=${t('Editor.RemoveRow')}
          @click=${() => void editor.removeConditionNode(step.id, parameter.name, path)}>
          ${icon('close')}
        </button>
      </div>
    `;
  }

  private startCapture(step: EditorStep, parameter: EditorParameter): void {
    this.capturing = step.id + ':' + parameter.name;
    this.requestUpdate();
    void this.updateComplete.then(() => {
      const input = this.renderRoot.querySelector<HTMLInputElement>(
        `input[data-capture="${CSS.escape(this.capturing)}"]`);
      input?.focus();
      input?.select();
    });
  }

  private captureKey(step: EditorStep, parameter: EditorParameter, event: KeyboardEvent): void {
    const capture = step.id + ':' + parameter.name;
    if (this.capturing !== capture) {
      return;
    }

    if (event.key === 'Control' || event.key === 'Shift' || event.key === 'Alt' || event.key === 'Meta') {
      return;
    }

    const key = this.keyName(event);
    if (!key) {
      return;
    }

    event.preventDefault();
    this.capturing = '';
    this.requestUpdate();
    void editor.patchParameter(step.id, parameter.name, key);
  }

  private keyName(event: KeyboardEvent): string {
    const parts: string[] = [];
    if (event.ctrlKey) {
      parts.push('Ctrl');
    }

    if (event.altKey) {
      parts.push('Alt');
    }

    if (event.shiftKey) {
      parts.push('Shift');
    }

    if (event.metaKey) {
      parts.push('Win');
    }

    let key = event.key;
    if (key === ' ') {
      key = 'Space';
    } else if (key.length === 1) {
      key = key.toUpperCase();
    }

    parts.push(key);
    return parts.join('+');
  }
}

declare global {
  interface HTMLElementTagNameMap {
    'wg-step-inspector': WgStepInspector;
  }
}
