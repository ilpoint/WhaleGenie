import { LitElement, css, html, nothing, type TemplateResult } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import type {
  EditorActionField,
  EditorConditionNode,
  EditorKeyPad,
  EditorWindowPreview,
} from '../../shared/bridge/protocol.js';
import { emptyRule } from '../../shared/window-rule/window-rule-dialog.js';
import type { WindowRuleState } from '../../shared/bridge/protocol.js';
import { t } from '../../shared/strings/strings.js';
import { editor } from './editor-store.js';
import { editorBase } from './editor-css.js';
import './value-field.js';
import './action-rows.js';
import './key-pad.js';
import '../../shared/window-rule/window-rule-dialog.js';

/**
 * One field of an action draft. The field's kind and the buttons beside it come from the snapshot;
 * the page never decides that a name means a window or a number means a duration.
 */
@customElement('wg-action-field')
export class WgActionField extends LitElement {
  @property({ attribute: false })
  public field!: EditorActionField;

  @state()
  private capturing = false;

  @state()
  private pad: EditorKeyPad | null = null;

  @state()
  private windowRuleField: EditorActionField | null = null;

  @state()
  private windowRule: WindowRuleState = emptyRule();

  @state()
  private livePreview: EditorWindowPreview | null = null;

  private previewTimer = 0;

  private padTarget = { name: '', index: -1, mode: 'key' };

  static styles = [editorBase, css`
    :host { display: grid; gap: var(--spacingVerticalXS); min-width: 0; }
    .label { display: flex; align-items: center; gap: var(--spacingHorizontalXS); color: var(--colorNeutralForeground2); font-size: var(--fontSizeBase200); }
    .label .required { color: var(--colorStatusDangerForeground1); }
    .label .formula { display: inline-grid; place-items: center; width: 14px; height: 14px; border-radius: var(--borderRadiusSmall); background: var(--colorNeutralBackground3); color: var(--colorNeutralForeground3); font-family: var(--fontFamilyMonospace); font-size: 10px; }
    .hint { color: var(--colorNeutralForeground3); font-size: var(--fontSizeBase100); white-space: pre-line; }
    .missing { color: var(--colorStatusDangerForeground1); }
    .inline { display: flex; gap: var(--spacingHorizontalXS); align-items: center; min-width: 0; }
    .inline > input, .inline > select { flex: 1; min-width: 0; }
    input, select, textarea { min-width: 0; }
    textarea { min-height: 68px; resize: vertical; }
    .condition { display: grid; gap: var(--spacingVerticalXS); }
    .condition-row { display: grid; grid-template-columns: minmax(0, 1fr) 96px minmax(0, 1fr) 24px; gap: 3px; align-items: center; }
    .condition-nested { display: grid; gap: 3px; padding: var(--spacingVerticalXS); border-left: 2px solid var(--colorNeutralStroke2); background: var(--colorNeutralBackground1); }
    .condition-add { display: flex; gap: var(--spacingHorizontalXS); flex-wrap: wrap; }
    .chips { display: flex; gap: 3px; flex-wrap: wrap; }
    .chip { border: var(--strokeWidthThin) solid var(--colorNeutralStroke1); border-radius: var(--borderRadiusSmall); background: var(--colorNeutralBackground1); color: var(--colorBrandForeground2); font-size: var(--fontSizeBase100); padding: 1px 5px; cursor: pointer; }
    .window { display: grid; gap: 3px; padding: var(--spacingVerticalS) var(--spacingHorizontalS); border: var(--strokeWidthThin) solid var(--colorNeutralStroke2); border-radius: var(--borderRadiusMedium); background: var(--colorNeutralBackground2); }
    .preview { display: grid; gap: 3px; justify-items: start; }
    .preview img { display: block; max-width: 240px; max-height: 110px; object-fit: contain; border: var(--strokeWidthThin) solid var(--colorNeutralStroke2); border-radius: var(--borderRadiusMedium); background: var(--colorNeutralBackground2); }
    .preview .file { color: var(--colorNeutralForeground3); font-size: var(--fontSizeBase100); overflow-wrap: anywhere; }
    .preview .problem { color: var(--colorStatusDangerForeground1); font-size: var(--fontSizeBase100); }
    .window .summary { color: var(--colorNeutralForeground1); }
    .window .note { color: var(--colorNeutralForeground3); font-size: var(--fontSizeBase100); }
    .preview-note { display: grid; gap: 2px; padding-top: var(--spacingVerticalXS); border-top: var(--strokeWidthThin) solid var(--colorNeutralStroke2); color: var(--colorNeutralForeground2); font-size: var(--fontSizeBase100); }
  `];

  render() {
    const field = this.field;
    if (!field || !field.isApplicable) {
      return nothing;
    }

    return html`
      <div class="param" data-parameter=${field.name}>
      <span class="label">
        ${field.takesFormula ? html`<span class="formula" title=${t('Add.FieldTakesFormula')}>ƒ</span>` : nothing}
        <span>${field.label}</span>
        ${field.required ? html`<span class="required">*</span>` : nothing}
      </span>
      ${this.renderEditor(field)}
      ${field.isMissing ? html`<span class="hint missing">${t('Add.StillNeeded')}</span>` : nothing}
      ${field.hint ? html`<span class="hint">${field.hint}</span>` : nothing}
      ${field.jitterRange ? html`<span class="hint">${field.jitterRange}</span>` : nothing}
      ${field.channelNote ? html`<span class="hint">${field.channelNote}</span>` : nothing}
      ${this.pad
        ? html`<wg-key-pad .pad=${this.pad}
            @wg-pad-key=${(event: CustomEvent<{ value: string }>) => void this.onPadKey(event)}
            @wg-pad-close=${() => { this.pad = null; }}></wg-key-pad>`
        : nothing}
      ${this.windowRuleField
        ? html`<wg-window-rule .rule=${this.windowRule} .title=${t('WindowRule.Choose')}
            .preview=${this.livePreview}
            @wg-rule-change=${(event: CustomEvent<WindowRuleState>) => this.scheduleWindowPreview(event)}
            @wg-rule-submit=${(event: CustomEvent<WindowRuleState>) => void this.submitWindowRule(event)}
            @wg-rule-close=${() => { this.windowRuleField = null; }}></wg-window-rule>`
        : nothing}
      </div>
    `;
  }

  private renderEditor(field: EditorActionField): TemplateResult | typeof nothing {
    if (field.isRegionList || field.isColourPoints || field.isKeySequence || field.isImageList
      || field.isColourRows || field.isNarrow) {
      return html`<wg-action-rows .field=${field}></wg-action-rows>`;
    }

    if (field.condition?.editable) {
      return this.renderCondition(field);
    }

    if (field.isWindowRule) {
      return html`
        <div class="window">
          <span class="summary">${field.windowRuleSummary}</span>
          ${field.isFollowingWindow
            ? html`<span class="note">${t('WindowRule.Following')}</span>`
            : nothing}
          <div class="chips">
            <button class="chip" type="button"
              @click=${() => void this.openWindowRule(field)}>${t('WindowRule.Choose')}</button>
            <button class="chip" type="button"
              @click=${() => void this.windowPick(field.name)}>${t('WindowRule.Menu.Capture')}</button>
            <button class="chip" type="button"
              @click=${() => void editor.action.applyActionWindow(field.name, 'follow')}>${t('WindowRule.Following')}</button>
            <button class="chip" type="button"
              @click=${() => void editor.action.applyActionWindow(field.name, 'foreground')}>${t('WindowRule.Menu.Foreground')}</button>
          </div>
          ${this.renderWindowPreview(field)}
        </div>
      `;
    }

    if (field.isMultiline || field.isCode) {
      return html`
        <textarea .value=${field.value} placeholder=${field.placeholder}
          @change=${(event: Event) => this.write('text', (event.target as HTMLTextAreaElement).value)}></textarea>
      `;
    }

    if (field.isWindow) {
      return html`
        <div class="inline">
          <input .value=${field.value}
            @change=${(event: Event) => this.write('text', (event.target as HTMLInputElement).value)} />
          <button class="chip" type="button"
            @click=${() => void this.pick(field.name, 'windowTitle')}>${t('Add.PickWindow')}</button>
        </div>
      `;
    }

    if (field.isKey || field.isKeys) {
      return html`
        <div class="inline">
          <input .value=${field.value}
            placeholder=${this.capturing ? t('Add.PressKey') : field.placeholder}
            @keydown=${(event: KeyboardEvent) => this.captureKey(event)}
            @change=${(event: Event) => this.write('key', (event.target as HTMLInputElement).value)} />
          <button class="chip" type="button"
            @click=${() => this.beginCapture()}>
            ${t('Add.SetCombination')}
          </button>
          <button class="chip" type="button" title=${t('Editor.KeyPad')}
            @click=${() => void this.openPad(field.name, -1, 'key')}>⌨</button>
        </div>
        ${field.offersValue
          ? html`<button class="chip" type="button"
              @click=${() => void editor.value.openValue(this.stepId, field.name)}>${t('Add.Value')}</button>`
          : nothing}
      `;
    }

    if (field.isNumber && !field.isNumberFormula) {
      return html`<wg-value-field .stepId=${this.stepId} .parameter=${field}></wg-value-field>`;
    }

    if (field.kind === 'Bool') {
      return html`
        <label class="inline">
          <input type="checkbox" .checked=${field.value === 'true'}
            @change=${(event: Event) => this.write('bool',
              (event.target as HTMLInputElement).checked ? 'true' : 'false')} />
          <span class="hint">${field.label}</span>
        </label>
      `;
    }

    if (field.isMacro) {
      return html`
        <div class="inline">
          <select .value=${field.value}
            @change=${(event: Event) => this.write('macro', (event.target as HTMLSelectElement).value)}>
            <option value="">${t('Add.SelectHint')}</option>
            ${field.macros.map((choice) => html`<option value=${choice.value}>${choice.label}</option>`)}
            ${field.macros.some((choice) => choice.value === field.value)
              ? nothing
              : html`<option value=${field.value}>${field.value}</option>`}
          </select>
        </div>
      `;
    }

    if (field.isStepChoice) {
      return html`
        <select .value=${field.value}
          @change=${(event: Event) => this.write('choice', (event.target as HTMLSelectElement).value)}>
          <option value="">${t('Add.SelectHint')}</option>
          ${field.steps.map((choice) => html`<option value=${choice.value}>${choice.label}</option>`)}
        </select>
      `;
    }

    if (field.kind === 'Choice' && field.options.length > 0) {
      return html`
        <select .value=${field.value}
          @change=${(event: Event) => this.write('choice', (event.target as HTMLSelectElement).value)}>
          ${field.options.map((choice) => html`<option value=${choice.value}>${choice.label}</option>`)}
        </select>
      `;
    }

    if (field.isImage) {
      return html`
        <div class="inline">
          <input .value=${field.value}
            @change=${(event: Event) => this.write('text', (event.target as HTMLInputElement).value)} />
          <button class="chip" type="button"
            @click=${() => void editor.steps.pickImage(this.stepId, field.name)}>${t('Editor.Browse')}</button>
          <button class="chip" type="button"
            @click=${() => void this.pick(field.name, 'capture')}>${t('Add.CaptureImage')}</button>
          <button class="chip" type="button"
            @click=${() => this.write('text', '')}>${t('Add.ClearImage')}</button>
        </div>
        ${this.renderImagePreview(field.imagePreview)}
      `;
    }

    if (field.isPath) {
      return html`
        <div class="inline">
          <input .value=${field.value}
            @change=${(event: Event) => this.write('text', (event.target as HTMLInputElement).value)} />
          <button class="chip" type="button"
            @click=${() => void this.pick(field.name, 'path')}>${t('Editor.Browse')}</button>
        </div>
        ${this.renderImagePreview(field.imagePreview)}
      `;
    }

    if (field.isColor) {
      return html`
        <div class="inline">
          <wg-value-field .stepId=${this.stepId} .parameter=${field}></wg-value-field>
          <button class="chip" type="button"
            @click=${() => void this.pick(field.name, 'color')}>${t('Editor.PickColor')}</button>
        </div>
      `;
    }

    if (field.isSelector || field.isBrowserTarget) {
      return html`
        <div class="inline">
          <input .value=${field.value}
            @change=${(event: Event) => this.write('text', (event.target as HTMLInputElement).value)} />
          <button class="chip" type="button"
            @click=${() => void this.pick(field.name,
              field.isSelector ? 'element' : 'browser')}>${field.isSelector
                ? t('Add.PickElement') : t('Add.PickBrowserElement')}</button>
          <button class="chip" type="button"
            @click=${() => void (field.isSelector
              ? editor.action.testActionElement(field.name) : editor.action.testActionBrowser(field.name))}>
            ${t('Add.TestElement')}</button>
        </div>
        ${field.candidates.length > 0
          ? html`<div class="chips">${field.candidates.map((candidate) => html`
              <button class="chip" type="button" title=${candidate.text}
                @click=${() => this.write('text', candidate.selector)}>${candidate.selector}</button>`)}</div>`
          : nothing}
      `;
    }

    return html`<wg-value-field .stepId=${this.stepId} .parameter=${field}></wg-value-field>`;
  }

  private renderImagePreview(preview: EditorActionField['imagePreview']): TemplateResult | typeof nothing {
    if (!preview) {
      return nothing;
    }

    return html`
      <div class="preview">
        ${preview.dataUrl
          ? html`<img src=${preview.dataUrl} alt=${preview.fileName || this.field.label} />`
          : nothing}
        <span class="file">${preview.fileName}</span>
        ${preview.problem ? html`<span class="problem">${preview.problem}</span>` : nothing}
      </div>
    `;
  }

  private renderCondition(field: EditorActionField): TemplateResult {
    return html`
      <div class="condition">
        <div class="inline">
          <span class="hint">${t('Editor.ConditionJoin')}</span>
          <select .value=${field.condition?.join ?? 'All'}
            @change=${(event: Event) => void editor.steps.setConditionJoin(
              this.stepId, field.name, [], (event.target as HTMLSelectElement).value)}>
            <option value="All">${t('Condition.All')}</option>
            <option value="Any">${t('Condition.Any')}</option>
            <option value="None">${t('Condition.None')}</option>
          </select>
        </div>
        ${(field.condition?.nodes ?? []).map((node, index) =>
          this.renderConditionNode(field, node, [index]))}
        <div class="condition-add">
          <button class="chip" type="button"
            @click=${() => void editor.steps.addConditionRow(this.stepId, field.name, [])}>
            ${t('Editor.AddRow')}
          </button>
          <button class="chip" type="button"
            @click=${() => void editor.steps.addConditionGroup(this.stepId, field.name, [])}>
            ${t('Editor.AddGroup')}
          </button>
        </div>
      </div>
    `;
  }

  private renderConditionNode(field: EditorActionField, node: EditorConditionNode,
    path: number[]): TemplateResult | typeof nothing {
    if (node.kind === 'group') {
      return html`
        <div class="condition-nested">
          <div class="inline">
            <select .value=${node.join}
              @change=${(event: Event) => void editor.steps.setConditionJoin(
                this.stepId, field.name, path, (event.target as HTMLSelectElement).value)}>
              <option value="All">${t('Condition.All')}</option>
              <option value="Any">${t('Condition.Any')}</option>
              <option value="None">${t('Condition.None')}</option>
            </select>
            <button class="chip" type="button"
              @click=${() => void editor.steps.removeConditionNode(this.stepId, field.name, path)}>
              ${t('Editor.RemoveRow')}
            </button>
          </div>
          ${node.children.map((child, index) => this.renderConditionNode(field, child, [...path, index]))}
          <div class="condition-add">
            <button class="chip" type="button"
              @click=${() => void editor.steps.addConditionRow(this.stepId, field.name, path)}>
              ${t('Editor.AddRow')}
            </button>
            <button class="chip" type="button"
              @click=${() => void editor.steps.addConditionGroup(this.stepId, field.name, path)}>
              ${t('Editor.AddGroup')}
            </button>
          </div>
        </div>
      `;
    }

    return html`
      <div class="condition-row">
        <div class="inline">
          <input .value=${node.left} @change=${(event: Event) => void editor.steps.setConditionRow(
            this.stepId, field.name, path, 'left', (event.target as HTMLInputElement).value)} />
          <button class="chip" type="button"
            @click=${() => void editor.value.openConditionValue(this.stepId, field.name, path, 'left')}>ƒ</button>
        </div>
        <select .value=${node.compare}
          @change=${(event: Event) => void editor.steps.setConditionRow(
            this.stepId, field.name, path, 'compare', (event.target as HTMLSelectElement).value)}>
          ${(editor.get().snapshot?.conditionCompares ?? []).map((compare) => html`
            <option value=${compare.value}>${compare.label}</option>`)}
        </select>
        <div class="inline">
          <input .value=${node.right} @change=${(event: Event) => void editor.steps.setConditionRow(
            this.stepId, field.name, path, 'right', (event.target as HTMLInputElement).value)} />
          <button class="chip" type="button"
            @click=${() => void editor.value.openConditionValue(this.stepId, field.name, path, 'right')}>ƒ</button>
        </div>
        <button class="chip" type="button"
          @click=${() => void editor.steps.removeConditionNode(this.stepId, field.name, path)}>×</button>
      </div>
    `;
  }

  private get stepId(): string {
    return editor.get().actionForm?.stepId ?? '';
  }

  private write(field: string, value: string): void {
    void editor.action.patchActionField(this.field.name, field, value);
  }

  private pick(name: string, mode: string): void {
    void editor.action.pickAction(name, mode);
  }

  private openWindowRule(field: EditorActionField): void {
    let seen: Record<string, unknown> = {};
    try {
      const parsed = JSON.parse(field.value || '{}') as unknown;
      if (parsed && typeof parsed === 'object') {
        seen = parsed as Record<string, unknown>;
      }
    } catch {
      seen = {};
    }

    this.windowRule = {
      title: String(seen['title'] ?? ''),
      process: String(seen['process'] ?? ''),
      className: String(seen['class'] ?? seen['className'] ?? ''),
      handle: Number(seen['handle'] ?? 0) || 0,
      how: String(seen['how'] ?? 'titleContains'),
      caseSensitive: seen['caseSensitive'] === true,
      trim: seen['trim'] !== false,
      multiline: seen['multiline'] === true,
    };
    this.livePreview = field.windowPreview;
    this.windowRuleField = field;
  }

  private scheduleWindowPreview(event: CustomEvent<WindowRuleState>): void {
    if (!this.windowRuleField) {
      return;
    }

    window.clearTimeout(this.previewTimer);
    const rule = event.detail;
    this.previewTimer = window.setTimeout(() => {
      if (!this.windowRuleField) {
        return;
      }

      const json = JSON.stringify({
        title: rule.title, process: rule.process, class: rule.className, handle: rule.handle,
        how: rule.how, caseSensitive: rule.caseSensitive, trim: rule.trim, multiline: rule.multiline,
      });
      void editor.action.previewActionWindow(this.windowRuleField.name, json).then((preview) => {
        this.livePreview = preview;
      });
    }, 160);
  }

  private async submitWindowRule(event: CustomEvent<WindowRuleState>): Promise<void> {
    const field = this.windowRuleField;
    if (!field) {
      return;
    }

    const rule = event.detail;
    const json = JSON.stringify({
      title: rule.title, process: rule.process, class: rule.className, handle: rule.handle,
      how: rule.how, caseSensitive: rule.caseSensitive, trim: rule.trim, multiline: rule.multiline,
    });
    this.windowRuleField = null;
    this.livePreview = null;
    await editor.action.applyActionWindowRule(field.name, json);
  }

  private renderWindowPreview(field: EditorActionField): TemplateResult | typeof nothing {
    const preview = this.livePreview ?? field.windowPreview;
    if (!preview) {
      return nothing;
    }

    return html`
      <div class="preview-note">
        <span>${preview.summary}</span>
        ${preview.rows.map((row) => html`<span class="note">${row.title}${row.part ? ' · ' + row.part : ''}</span>`)}
        ${preview.more ? html`<span class="note">${preview.more}</span>` : nothing}
        ${preview.note ? html`<span class="note">${preview.note}</span>` : nothing}
      </div>
    `;
  }

  private async openPad(name: string, index: number, mode: string): Promise<void> {
    const pad = await editor.action.keyPad(mode);
    if (pad) {
      this.padTarget = { name, index, mode };
      this.pad = pad;
    }
  }

  private async onPadKey(event: CustomEvent<{ value: string }>): Promise<void> {
    await editor.action.padKey(this.padTarget.name, event.detail.value,
      this.padTarget.index, this.padTarget.mode);
    if (this.padTarget.index < 0) {
      this.pad = null;
    }
  }

  private async windowPick(name: string): Promise<void> {
    const picked = await editor.action.pickWindowForAction();
    if (picked?.chosen && picked.rule.length > 0) {
      await editor.action.applyActionWindowRule(name, picked.rule);
    }
  }

  private beginCapture(): void {
    this.capturing = true;
    window.addEventListener('keydown', this.onWindowKey, true);
    this.requestUpdate();
  }

  private onWindowKey = (event: KeyboardEvent): void => {
    this.captureKey(event);
  };

  private captureKey(event: KeyboardEvent): void {
    if (!this.capturing || ['Control', 'Shift', 'Alt', 'Meta'].includes(event.key)) {
      return;
    }

    event.preventDefault();
    const parts: string[] = [];
    if (event.ctrlKey) parts.push('Ctrl');
    if (event.altKey) parts.push('Alt');
    if (event.shiftKey) parts.push('Shift');
    if (event.metaKey) parts.push('Win');
    let key = event.key;
    if (key === ' ') key = 'Space';
    else if (key.length === 1) key = key.toUpperCase();
    parts.push(key);
    this.capturing = false;
    window.removeEventListener('keydown', this.onWindowKey, true);
    this.write('key', parts.join('+'));
  }
}

declare global {
  interface HTMLElementTagNameMap {
    'wg-action-field': WgActionField;
  }
}
