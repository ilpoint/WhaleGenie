import { html, nothing, type TemplateResult } from 'lit';
import type {
  EditorActionField,
  EditorConditionNode,
  EditorWindowPreview,
} from '../../../shared/bridge/protocol.js';
import type { WindowRuleState } from '../../../shared/bridge/protocol.js';
import { t } from '../../../shared/strings/strings.js';
import { editor } from '../editor-store.js';

export interface ActionFieldHost {
  field: EditorActionField;
  stepId: string;
  livePreview: EditorWindowPreview | null;
  capturing: boolean;
  beginCapture(): void;
  captureKey(event: KeyboardEvent): void;
  write(field: string, value: string): void;
  pick(name: string, mode: string): void;
  openWindowRule(field: EditorActionField): void;
  windowPick(name: string): void;
  openPad(name: string, index: number, mode: string): Promise<void>;
  scheduleWindowPreview(event: CustomEvent<WindowRuleState>): void;
  submitWindowRule(event: CustomEvent<WindowRuleState>): Promise<void>;
}

export function renderEditor(host: ActionFieldHost, field: EditorActionField): TemplateResult | typeof nothing {
    if (field.isRegionList || field.isColourPoints || field.isKeySequence || field.isImageList
      || field.isColourRows || field.isNarrow) {
      return html`<wg-action-rows .field=${field}></wg-action-rows>`;
    }

    if (field.condition?.editable) {
      return renderCondition(host, field);
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
              @click=${() => void host.openWindowRule(field)}>${t('WindowRule.Choose')}</button>
            <button class="chip" type="button"
              @click=${() => void host.windowPick(field.name)}>${t('WindowRule.Menu.Capture')}</button>
            <button class="chip" type="button"
              @click=${() => void editor.action.applyActionWindow(field.name, 'follow')}>${t('WindowRule.Following')}</button>
            <button class="chip" type="button"
              @click=${() => void editor.action.applyActionWindow(field.name, 'foreground')}>${t('WindowRule.Menu.Foreground')}</button>
          </div>
          ${renderWindowPreview(host, field)}
        </div>
      `;
    }

    if (field.isMultiline || field.isCode) {
      return html`
        <textarea .value=${field.value} placeholder=${field.placeholder}
          @change=${(event: Event) => host.write('text', (event.target as HTMLTextAreaElement).value)}></textarea>
      `;
    }

    if (field.isWindow) {
      return html`
        <div class="inline">
          <input .value=${field.value}
            @change=${(event: Event) => host.write('text', (event.target as HTMLInputElement).value)} />
          <button class="chip" type="button"
            @click=${() => void host.pick(field.name, 'windowTitle')}>${t('Add.PickWindow')}</button>
        </div>
      `;
    }

    if (field.isKey || field.isKeys) {
      return html`
        <div class="inline">
          <input .value=${field.value}
            placeholder=${host.capturing ? t('Add.PressKey') : field.placeholder}
            @keydown=${(event: KeyboardEvent) => host.captureKey(event)}
            @change=${(event: Event) => host.write('key', (event.target as HTMLInputElement).value)} />
          <button class="chip" type="button"
            @click=${() => host.beginCapture()}>
            ${t('Add.SetCombination')}
          </button>
          <button class="chip" type="button" title=${t('Editor.KeyPad')}
            @click=${() => void host.openPad(field.name, -1, 'key')}>⌨</button>
        </div>
        ${field.offersValue
          ? html`<button class="chip" type="button"
              @click=${() => void editor.value.openValue(host.stepId, field.name)}>${t('Add.Value')}</button>`
          : nothing}
      `;
    }

    if (field.isNumber && !field.isNumberFormula) {
      return html`<wg-value-field .stepId=${host.stepId} .parameter=${field}></wg-value-field>`;
    }

    if (field.kind === 'Bool') {
      return html`
        <label class="inline">
          <input type="checkbox" .checked=${field.value === 'true'}
            @change=${(event: Event) => host.write('bool',
              (event.target as HTMLInputElement).checked ? 'true' : 'false')} />
          <span class="hint">${field.label}</span>
        </label>
      `;
    }

    if (field.isMacro) {
      return html`
        <div class="inline">
          <select .value=${field.value}
            @change=${(event: Event) => host.write('macro', (event.target as HTMLSelectElement).value)}>
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
          @change=${(event: Event) => host.write('choice', (event.target as HTMLSelectElement).value)}>
          <option value="">${t('Add.SelectHint')}</option>
          ${field.steps.map((choice) => html`<option value=${choice.value}>${choice.label}</option>`)}
        </select>
      `;
    }

    if (field.kind === 'Choice' && field.options.length > 0) {
      return html`
        <select .value=${field.value}
          @change=${(event: Event) => host.write('choice', (event.target as HTMLSelectElement).value)}>
          ${field.options.map((choice) => html`<option value=${choice.value}>${choice.label}</option>`)}
        </select>
      `;
    }

    if (field.isImage) {
      return html`
        <div class="inline">
          <input .value=${field.value}
            @change=${(event: Event) => host.write('text', (event.target as HTMLInputElement).value)} />
          <button class="chip" type="button"
            @click=${() => void editor.steps.pickImage(host.stepId, field.name)}>${t('Editor.Browse')}</button>
          <button class="chip" type="button"
            @click=${() => void host.pick(field.name, 'capture')}>${t('Add.CaptureImage')}</button>
          <button class="chip" type="button"
            @click=${() => host.write('text', '')}>${t('Add.ClearImage')}</button>
        </div>
        ${renderImagePreview(host, field.imagePreview)}
      `;
    }

    if (field.isPath) {
      return html`
        <div class="inline">
          <input .value=${field.value}
            @change=${(event: Event) => host.write('text', (event.target as HTMLInputElement).value)} />
          <button class="chip" type="button"
            @click=${() => void host.pick(field.name, 'path')}>${t('Editor.Browse')}</button>
        </div>
        ${renderImagePreview(host, field.imagePreview)}
      `;
    }

    if (field.isColor) {
      return html`
        <div class="inline">
          <wg-value-field .stepId=${host.stepId} .parameter=${field}></wg-value-field>
          <button class="chip" type="button"
            @click=${() => void host.pick(field.name, 'color')}>${t('Editor.PickColor')}</button>
        </div>
      `;
    }

    if (field.isSelector || field.isBrowserTarget) {
      return html`
        <div class="inline">
          <input .value=${field.value}
            @change=${(event: Event) => host.write('text', (event.target as HTMLInputElement).value)} />
          <button class="chip" type="button"
            @click=${() => void host.pick(field.name,
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
                @click=${() => host.write('text', candidate.selector)}>${candidate.selector}</button>`)}</div>`
          : nothing}
      `;
    }

    return html`<wg-value-field .stepId=${host.stepId} .parameter=${field}></wg-value-field>`;
  }

export function renderImagePreview(host: ActionFieldHost, preview: EditorActionField['imagePreview']): TemplateResult | typeof nothing {
    if (!preview) {
      return nothing;
    }

    return html`
      <div class="preview">
        ${preview.dataUrl
          ? html`<img src=${preview.dataUrl} alt=${preview.fileName || host.field.label} />`
          : nothing}
        <span class="file">${preview.fileName}</span>
        ${preview.problem ? html`<span class="problem">${preview.problem}</span>` : nothing}
      </div>
    `;
  }

export function renderCondition(host: ActionFieldHost, field: EditorActionField): TemplateResult {
    return html`
      <div class="condition">
        <div class="inline">
          <span class="hint">${t('Editor.ConditionJoin')}</span>
          <select .value=${field.condition?.join ?? 'All'}
            @change=${(event: Event) => void editor.steps.setConditionJoin(
              host.stepId, field.name, [], (event.target as HTMLSelectElement).value)}>
            <option value="All">${t('Condition.All')}</option>
            <option value="Any">${t('Condition.Any')}</option>
            <option value="None">${t('Condition.None')}</option>
          </select>
        </div>
        ${(field.condition?.nodes ?? []).map((node, index) =>
          renderConditionNode(host, field, node, [index]))}
        <div class="condition-add">
          <button class="chip" type="button"
            @click=${() => void editor.steps.addConditionRow(host.stepId, field.name, [])}>
            ${t('Editor.AddRow')}
          </button>
          <button class="chip" type="button"
            @click=${() => void editor.steps.addConditionGroup(host.stepId, field.name, [])}>
            ${t('Editor.AddGroup')}
          </button>
        </div>
      </div>
    `;
  }

export function renderConditionNode(host: ActionFieldHost, field: EditorActionField, node: EditorConditionNode,
  path: number[]): TemplateResult | typeof nothing {
    if (node.kind === 'group') {
      return html`
        <div class="condition-nested">
          <div class="inline">
            <select .value=${node.join}
              @change=${(event: Event) => void editor.steps.setConditionJoin(
                host.stepId, field.name, path, (event.target as HTMLSelectElement).value)}>
              <option value="All">${t('Condition.All')}</option>
              <option value="Any">${t('Condition.Any')}</option>
              <option value="None">${t('Condition.None')}</option>
            </select>
            <button class="chip" type="button"
              @click=${() => void editor.steps.removeConditionNode(host.stepId, field.name, path)}>
              ${t('Editor.RemoveRow')}
            </button>
          </div>
          ${node.children.map((child, index) => renderConditionNode(host, field, child, [...path, index]))}
          <div class="condition-add">
            <button class="chip" type="button"
              @click=${() => void editor.steps.addConditionRow(host.stepId, field.name, path)}>
              ${t('Editor.AddRow')}
            </button>
            <button class="chip" type="button"
              @click=${() => void editor.steps.addConditionGroup(host.stepId, field.name, path)}>
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
            host.stepId, field.name, path, 'left', (event.target as HTMLInputElement).value)} />
          <button class="chip" type="button"
            @click=${() => void editor.value.openConditionValue(host.stepId, field.name, path, 'left')}>ƒ</button>
        </div>
        <select .value=${node.compare}
          @change=${(event: Event) => void editor.steps.setConditionRow(
            host.stepId, field.name, path, 'compare', (event.target as HTMLSelectElement).value)}>
          ${(editor.get().snapshot?.conditionCompares ?? []).map((compare) => html`
            <option value=${compare.value}>${compare.label}</option>`)}
        </select>
        <div class="inline">
          <input .value=${node.right} @change=${(event: Event) => void editor.steps.setConditionRow(
            host.stepId, field.name, path, 'right', (event.target as HTMLInputElement).value)} />
          <button class="chip" type="button"
            @click=${() => void editor.value.openConditionValue(host.stepId, field.name, path, 'right')}>ƒ</button>
        </div>
        <button class="chip" type="button"
          @click=${() => void editor.steps.removeConditionNode(host.stepId, field.name, path)}>×</button>
      </div>
    `;
  }

export function renderWindowPreview(host: ActionFieldHost, field: EditorActionField): TemplateResult | typeof nothing {
    const preview = host.livePreview ?? field.windowPreview;
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
