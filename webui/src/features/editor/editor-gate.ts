import { LitElement, css, html, nothing, type TemplateResult } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import { bridge } from '../../shared/bridge/bridge.js';
import type {
  EditorAction,
  EditorParameter,
  EditorSnapshot,
  EditorStep,
  ProjectState,
} from '../../shared/bridge/protocol.js';
import { project } from '../../shared/project/store.js';
import { strings, t } from '../../shared/strings/strings.js';

/**
 * C0's editor gate: one real macro, read and changed through the C# session, then saved back
 * through the same rule the native editor uses. The full document/step UI belongs to C1; this page
 * exists to prove the snapshot-and-command path before the native editor is removed.
 */
@customElement('wg-editor-gate')
export class WgEditorGate extends LitElement {
  @state()
  private snapshot: EditorSnapshot | null = null;

  @state()
  private actions: EditorAction[] = [];

  @state()
  private actionKey = '';

  @state()
  private selectedId = '';

  @state()
  private error = '';

  private unsubscribeStrings?: () => void;
  private unsubscribeProject?: () => void;
  private opened = false;

  static styles = css`
    :host {
      display: block;
      max-width: 720px;
      padding: var(--spacingVerticalL) var(--spacingHorizontalXXL);
    }

    h1 {
      font-size: var(--font-size-base-500);
      font-weight: var(--font-weight-semibold);
      margin: 0 0 var(--spacingVerticalM);
    }

    .fields {
      display: grid;
      gap: var(--spacingVerticalS);
      margin-bottom: var(--spacingVerticalL);
    }

    input,
    textarea,
    .param select {
      font: inherit;
      padding: var(--spacingVerticalSN) var(--spacingHorizontalM);
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke1);
      border-radius: var(--borderRadiusMedium);
      background: var(--colorNeutralBackground1);
      color: var(--colorNeutralForeground1);
    }

    textarea {
      min-height: 80px;
      resize: vertical;
    }

    .triggers {
      list-style: none;
      margin: 0 0 var(--spacingVerticalM);
      padding: 0;
      display: grid;
      gap: var(--spacingVerticalS);
    }

    .triggers li {
      display: flex;
      align-items: center;
      gap: var(--spacingHorizontalS);
    }

    .trigger-kind {
      min-width: 96px;
      color: var(--colorNeutralForeground3);
    }

    .triggers button {
      border: none;
      background: transparent;
      color: var(--colorNeutralForeground2);
      font: inherit;
      cursor: pointer;
    }

    .toolbar {
      display: flex;
      align-items: center;
      gap: var(--spacingHorizontalS);
      margin-bottom: var(--spacingVerticalM);
    }

    .toolbar select {
      font: inherit;
      padding: var(--spacingVerticalSN) var(--spacingHorizontalM);
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke1);
      border-radius: var(--borderRadiusMedium);
      background: var(--colorNeutralBackground1);
      color: var(--colorNeutralForeground1);
      min-width: 240px;
    }

    .tree,
    .tree ol {
      list-style: none;
      margin: 0;
      padding: 0;
    }

    .tree ol {
      padding-left: var(--spacingHorizontalL);
    }

    .step {
      display: flex;
      align-items: center;
      gap: var(--spacingHorizontalS);
      padding: var(--spacingVerticalSN) var(--spacingHorizontalS);
      border-radius: var(--borderRadiusMedium);
      cursor: default;
    }

    .step:hover {
      background: var(--colorNeutralBackground1Hover);
    }

    .step.selected {
      background: var(--colorNeutralBackground1Selected);
    }

    .step-name {
      flex: 1;
      min-width: 0;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .step-actions {
      display: flex;
      gap: var(--spacingHorizontalXS);
    }

    .step-actions button {
      border: none;
      background: transparent;
      color: var(--colorNeutralForeground2);
      font: inherit;
      cursor: pointer;
      padding: 0 var(--spacingHorizontalXS);
    }

    .step-actions button:hover {
      color: var(--colorNeutralForeground1);
    }

    .inspector {
      display: grid;
      gap: var(--spacingVerticalS);
      margin: var(--spacingVerticalL) 0;
      padding: var(--spacingVerticalM);
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke1);
      border-radius: var(--borderRadiusMedium);
    }

    .param {
      display: grid;
      gap: var(--spacingVerticalXS);
    }

    .param label {
      color: var(--colorNeutralForeground3);
      font-size: var(--font-size-base-200);
    }

    .steps {
      margin: 0 0 var(--spacingVerticalL);
      color: var(--colorNeutralForeground2);
    }

    .actions {
      display: flex;
      gap: var(--spacingHorizontalS);
    }

    .error {
      color: var(--colorPaletteRedForeground1);
      margin-bottom: var(--spacingVerticalM);
    }
  `;

  connectedCallback(): void {
    super.connectedCallback();
    this.unsubscribeStrings = strings.subscribe(() => this.requestUpdate());
    this.unsubscribeProject = project.subscribe((state) => this.openFirst(state));
    void this.loadActions();
  }

  disconnectedCallback(): void {
    this.unsubscribeStrings?.();
    this.unsubscribeProject?.();
    super.disconnectedCallback();
  }

  render() {
    if (this.snapshot === null) {
      return html`<h1>${t('Shell.Editor')}</h1><p>${t('Editor.NoSteps')}</p>`;
    }

    return html`
      <h1>${t('Shell.Editor')}</h1>
      ${this.error ? html`<p class="error">${this.error}</p>` : nothing}
      <div class="fields">
        <input
          .value=${this.snapshot.name}
          placeholder=${t('Editor.NamePlaceholder')}
          aria-label=${t('Editor.NamePlaceholder')}
          @input=${(event: Event) => this.editName(event)}
        />
        <textarea
          .value=${this.snapshot.description}
          placeholder=${t('Editor.DescriptionPlaceholder')}
          aria-label=${t('Editor.DescriptionPlaceholder')}
          @input=${(event: Event) => this.editDescription(event)}
        ></textarea>
      </div>
      <h2>${t('Editor.TriggerEntries')}</h2>
      <ul class="triggers">
        ${this.snapshot.triggers.map((trigger) => html`
          <li>
            <span class="trigger-kind">${trigger.kindLabel}</span>
            ${trigger.kind === 'Key'
              ? html`<input
                  .value=${trigger.binding}
                  placeholder=${t('Editor.BindKey')}
                  @change=${(event: Event) => void this.setBinding(trigger.id, event)}
                />`
              : html`<span>${trigger.summary}</span>`}
            <button
              title=${t('Editor.RemoveTrigger')}
              aria-label=${t('Editor.RemoveTrigger')}
              @click=${() => void this.removeTrigger(trigger.id)}
            >×</button>
          </li>
        `)}
      </ul>
      <fluent-button appearance="subtle" @click=${this.addTrigger}>
        ${t('Editor.AddTrigger')}
      </fluent-button>
      <h2>${t('Run.Steps')}</h2>
      <div class="toolbar">
        <select .value=${this.actionKey} @change=${this.selectAction}>
          ${this.actions.filter((action) => !action.hidden).map((action) => html`
            <option value=${action.key}>${action.name}</option>
          `)}
        </select>
        <fluent-button appearance="subtle" @click=${this.addStep}>
          ${t('Editor.AddStep')}
        </fluent-button>
      </div>
      ${this.snapshot.steps.length === 0
        ? html`<p class="steps">${t('Editor.NoSteps')}</p>`
        : html`<ul class="tree">${this.renderSteps(this.snapshot.steps)}</ul>`}
      ${this.renderInspector()}
      <p class="steps">${t('Editor.GateActions').replace('{0}', String(this.actions.length))}</p>
      <div class="actions">
        <fluent-button appearance="accent" @click=${this.save}>${t('Common.Save')}</fluent-button>
        <fluent-button appearance="subtle" @click=${this.close}>${t('Common.Close')}</fluent-button>
      </div>
    `;
  }

  private async loadActions(): Promise<void> {
    try {
      const response = await bridge.value.call<{ count: number; actions: EditorAction[] }>(
        'editor.sessionActions');
      this.actions = response.actions;
      this.actionKey = response.actions.find((action) => !action.hidden)?.key ?? '';
    } catch {
      // The gate can still open a session even if the catalogue fails.
    }
  }

  private selectAction(event: Event): void {
    this.actionKey = (event.target as HTMLSelectElement).value;
  }

  private async addStep(): Promise<void> {
    if (this.snapshot === null || this.actionKey.length === 0) {
      return;
    }

    try {
      this.snapshot = await bridge.value.call<EditorSnapshot>('editor.sessionStep',
        { type: this.actionKey });
    } catch (error) {
      this.error = String(error);
    }
  }

  private async addTrigger(): Promise<void> {
    try {
      this.snapshot = await bridge.value.call<EditorSnapshot>('editor.sessionTriggerAdd');
    } catch (error) {
      this.error = String(error);
    }
  }

  private async removeTrigger(triggerId: string): Promise<void> {
    try {
      this.snapshot = await bridge.value.call<EditorSnapshot>('editor.sessionTriggerRemove',
        { triggerId });
    } catch (error) {
      this.error = String(error);
    }
  }

  private async setBinding(triggerId: string, event: Event): Promise<void> {
    try {
      this.snapshot = await bridge.value.call<EditorSnapshot>('editor.sessionTriggerBinding', {
        triggerId,
        binding: (event.target as HTMLInputElement).value,
      });
    } catch (error) {
      this.error = String(error);
    }
  }

  private async moveStep(stepId: string, delta: number): Promise<void> {
    try {
      this.snapshot = await bridge.value.call<EditorSnapshot>('editor.sessionMove',
        { stepId, delta });
    } catch (error) {
      this.error = String(error);
    }
  }

  private async removeStep(stepId: string): Promise<void> {
    try {
      this.snapshot = await bridge.value.call<EditorSnapshot>('editor.sessionRemove', { stepId });
    } catch (error) {
      this.error = String(error);
    }
  }

  private async patchParameter(stepId: string, name: string, event: Event): Promise<void> {
    const target = event.target as HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement;
    const value = target instanceof HTMLInputElement && target.type === 'checkbox'
      ? String(target.checked)
      : target.value;
    try {
      this.snapshot = await bridge.value.call<EditorSnapshot>('editor.sessionParameter', {
        stepId,
        name,
        value,
      });
    } catch (error) {
      this.error = String(error);
    }
  }

  private renderSteps(steps: EditorStep[]): TemplateResult {
    return html`
      ${steps.map((step) => html`
        <li>
          <div
            class="step ${this.selectedId === step.id ? 'selected' : ''}"
            @click=${() => { this.selectedId = step.id; }}
          >
            <span class="step-name">${step.name}</span>
            <span class="step-actions">
              <button
                title=${t('Editor.Context.MoveUp')}
                aria-label=${t('Editor.Context.MoveUp')}
                @click=${(event: Event) => { event.stopPropagation(); void this.moveStep(step.id, -1); }}
              >↑</button>
              <button
                title=${t('Editor.Context.MoveDown')}
                aria-label=${t('Editor.Context.MoveDown')}
                @click=${(event: Event) => { event.stopPropagation(); void this.moveStep(step.id, 1); }}
              >↓</button>
              <button
                title=${t('Editor.Delete')}
                aria-label=${t('Editor.Delete')}
                @click=${(event: Event) => { event.stopPropagation(); void this.removeStep(step.id); }}
              >×</button>
            </span>
          </div>
          ${step.steps.length > 0 ? html`<ol>${this.renderSteps(step.steps)}</ol>` : nothing}
        </li>
      `)}
    `;
  }

  private renderParameter(step: EditorStep, parameter: EditorParameter): TemplateResult {
    const change = (event: Event) => void this.patchParameter(step.id, parameter.name, event);
    if (parameter.kind === 'Bool') {
      return html`
        <div class="param">
          <label>${parameter.label}</label>
          <input
            type="checkbox"
            .checked=${parameter.value === 'true'}
            @change=${change}
          />
        </div>
      `;
    }

    if (parameter.kind === 'Choice' && parameter.options.length > 0) {
      return html`
        <div class="param">
          <label>${parameter.label}</label>
          <select .value=${parameter.value} @change=${change}>
            ${parameter.options.map((choice) => html`
              <option value=${choice.value}>${choice.label}</option>
            `)}
          </select>
        </div>
      `;
    }

    if (parameter.kind === 'MultilineText') {
      return html`
        <div class="param">
          <label>${parameter.label}</label>
          <textarea .value=${parameter.value} @change=${change}></textarea>
        </div>
      `;
    }

    return html`
      <div class="param">
        <label>${parameter.label}</label>
        <input .value=${parameter.value} @change=${change} />
      </div>
    `;
  }

  private selectedStep(): EditorStep | null {
    const find = (steps: EditorStep[]): EditorStep | null => {
      for (const step of steps) {
        if (step.id === this.selectedId) {
          return step;
        }
        const nested = find(step.steps);
        if (nested) {
          return nested;
        }
      }
      return null;
    };
    return this.snapshot ? find(this.snapshot.steps) : null;
  }

  private renderInspector(): TemplateResult | typeof nothing {
    const step = this.selectedStep();
    if (!step) {
      return nothing;
    }

    return html`
      <div class="inspector">
        <strong>${step.name}</strong>
        <span>${step.type}</span>
        ${step.parameters.map((parameter) => this.renderParameter(step, parameter))}
      </div>
    `;
  }

  private openFirst(state: ProjectState | null): void {
    if (this.opened || state === null) {
      return;
    }

    this.opened = true;
    const first = state.macros[0];
    void this.open(first?.name ?? null);
  }

  private async open(name: string | null): Promise<void> {
    try {
      const opened = await bridge.value.call<EditorSnapshot | null>('editor.sessionOpen',
        name ? { name } : {});
      this.snapshot = opened;
      this.selectedId = opened?.steps[0]?.id ?? '';
    } catch (error) {
      this.error = String(error);
    }
  }

  private editName(event: Event): void {
    this.snapshot = { ...this.snapshot!, name: (event.target as HTMLInputElement).value };
  }

  private editDescription(event: Event): void {
    this.snapshot = { ...this.snapshot!, description: (event.target as HTMLTextAreaElement).value };
  }

  private async save(): Promise<void> {
    if (this.snapshot === null) {
      return;
    }

    try {
      this.snapshot = await bridge.value.call<EditorSnapshot>('editor.sessionPatch', {
        name: this.snapshot.name,
        description: this.snapshot.description,
      });
      this.snapshot = await bridge.value.call<EditorSnapshot>('editor.sessionSave');
      this.error = '';
    } catch (error) {
      this.error = String(error);
    }
  }

  private async close(): Promise<void> {
    try {
      await bridge.value.call('editor.sessionClose');
    } finally {
      this.snapshot = null;
    }
  }
}

declare global {
  interface HTMLElementTagNameMap {
    'wg-editor-gate': WgEditorGate;
  }
}
