import { LitElement, css, html, nothing } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import type {
  EditorActionField,
  EditorKeyPad,
  EditorWindowPreview,
} from '../../shared/bridge/protocol.js';
import { emptyRule } from '../../shared/window-rule/window-rule-dialog.js';
import type { WindowRuleState } from '../../shared/bridge/protocol.js';
import { t } from '../../shared/strings/strings.js';
import { editor } from './editor-store.js';
import { renderEditor } from './action-fields/render.js';
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
  capturing = false;

  @state()
  private pad: EditorKeyPad | null = null;

  @state()
  private windowRuleField: EditorActionField | null = null;

  @state()
  private windowRule: WindowRuleState = emptyRule();

  @state()
  livePreview: EditorWindowPreview | null = null;

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
      ${renderEditor(this, field)}
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

  get stepId(): string {
    return editor.get().actionForm?.stepId ?? '';
  }

  write(field: string, value: string): void {
    void editor.action.patchActionField(this.field.name, field, value);
  }

  pick(name: string, mode: string): void {
    void editor.action.pickAction(name, mode);
  }

  openWindowRule(field: EditorActionField): void {
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

  scheduleWindowPreview(event: CustomEvent<WindowRuleState>): void {
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

  async submitWindowRule(event: CustomEvent<WindowRuleState>): Promise<void> {
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

  async openPad(name: string, index: number, mode: string): Promise<void> {
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

  async windowPick(name: string): Promise<void> {
    const picked = await editor.action.pickWindowForAction();
    if (picked?.chosen && picked.rule.length > 0) {
      await editor.action.applyActionWindowRule(name, picked.rule);
    }
  }

  beginCapture(): void {
    this.capturing = true;
    window.addEventListener('keydown', this.onWindowKey, true);
    this.requestUpdate();
  }

  private onWindowKey = (event: KeyboardEvent): void => {
    this.captureKey(event);
  };

  captureKey(event: KeyboardEvent): void {
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
