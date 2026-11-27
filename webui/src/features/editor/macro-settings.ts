import { LitElement, html, nothing, type TemplateResult } from 'lit';
import { macroSettingsCss } from './macro-settings-css.js';
import { customElement, state } from 'lit/decorators.js';
import { icon } from '../../shared/icons.js';
import { t } from '../../shared/strings/strings.js';
import type {
  EditorCharacterPad,
  EditorTrigger,
  ProjectState,
  WindowRuleState,
} from '../../shared/bridge/protocol.js';
import { keyboardChord } from '../../shared/input/capture.js';
import { project } from '../../shared/project/store.js';
import { editor } from './editor-store.js';
import { renderTriggerFields, ruleHow } from './triggers/trigger-fields.js';
import { renderSpecific } from './specific-render.js';
import { StoreController } from '../../shared/lit/store-controller.js';
import { editorBase } from './editor-css.js';
import './character-pad.js';
import '../../shared/window-rule/window-rule-dialog.js';

/**
 * Macro settings: the general half (name, description, group and the trigger entries) and the
 * specific half (input limit, arm key, callable interface). A submacro has no entries and no arm
 * key of its own, so those sections are hidden rather than shown empty.
 */
@customElement('wg-macro-settings')
export class WgMacroSettings extends LitElement {
  @state()
  private page: 'general' | 'specific' = 'general';

  @state()
  private selectedTrigger = '';

  @state()
  capturingTrigger = '';

  @state()
  private newTriggerKind = 'Key';

  @state()
  private characterPad: EditorCharacterPad | null = null;

  /** The entry whose rule the shared window-rule form is open for. */
  @state()
  private ruleTrigger = '';

  @state()
  private projectState: ProjectState | null = null;

  private characterTrigger = '';
  private endCaptureTrigger = '';
  private interfaceCaptureTrigger = '';

  static styles = [editorBase, macroSettingsCss];

  protected readonly editorController = new StoreController<void>(this, editor);

  protected readonly projectController = new StoreController(this, project, (value) => {
    this.projectState = value;
  });

  disconnectedCallback(): void {
    window.removeEventListener('keydown', this.onEndCapture, true);
    window.removeEventListener('keydown', this.onInterfaceCapture, true);
    super.disconnectedCallback();
  }

  render() {
    const state = editor.get();
    const snapshot = state.snapshot;
    if (!snapshot) {
      return html`<p class="empty">${t('Editor.NoDocument')}</p>`;
    }

    return html`
      <div class="title">
        ${icon('settings')}
        <h3>${t('Editor.MacroSettings')}</h3>
      </div>
      <div class="tabs" role="tablist">
        <button type="button" role="tab" aria-selected=${this.page === 'general' ? 'true' : 'false'}
          @click=${() => { this.page = 'general'; }}>
          ${t('Editor.MacroSettings.General')}
        </button>
        <button type="button" role="tab" aria-selected=${this.page === 'specific' ? 'true' : 'false'}
          @click=${() => { this.page = 'specific'; }}>
          ${t('Editor.MacroSettings.Specific')}
        </button>
      </div>
      <div class="body">
        ${state.saved ? html`<p class="saved">${t('Editor.SavedNotice')}</p>` : nothing}
        ${this.page === 'general' ? this.renderGeneral() : renderSpecific()}
      </div>
      <div class="footer">
        ${state.error
          ? html`<span class="error" title=${state.error}>${state.error}</span>`
          : html`<span style="flex:1"></span>`}
        <fluent-button appearance="accent"
          ?disabled=${!snapshot.dirty || !snapshot.canSave}
          @click=${() => void editor.session.save()}>${t('Editor.Save')}</fluent-button>
        <fluent-button
          ?disabled=${snapshot.isSubMacro || snapshot.steps.length === 0 || !snapshot.canSave}
          @click=${() => void editor.session.run()}>${t('Editor.RunDebug')}</fluent-button>
      </div>
      ${this.renderProjectStatus()}
      ${this.characterPad
        ? html`<wg-character-pad .pad=${this.characterPad} .text=${this.hotstringText()}
            @wg-character-submit=${(event: CustomEvent<{ text: string }>) => void this.submitCharacter(event)}
            @wg-character-close=${() => { this.characterPad = null; }}></wg-character-pad>`
        : nothing}
    `;
  }

  private renderProjectStatus(): TemplateResult | typeof nothing {
    const state = this.projectState;
    if (!state) {
      return nothing;
    }

    if (state.saveError) {
      return html`
        <div class="project-status" data-project-status="error" title=${state.saveError}>
          ${t('Editor.Project.WriteFailed')} · ${state.saveError}
        </div>
      `;
    }

    if (state.saving) {
      return html`
        <div class="project-status" data-project-status="saving">${t('Editor.Project.Writing')}</div>
      `;
    }

    if (!state.autoSave && state.dirty) {
      return html`
        <div class="project-status" data-project-status="dirty">${t('Editor.Project.NotWritten')}</div>
      `;
    }

    return nothing;
  }

  private renderGeneral(): TemplateResult {
    const snapshot = editor.get().snapshot;
    if (!snapshot) {
      return html``;
    }

    const triggers = snapshot.triggers;
    const selected = triggers.find((trigger) => trigger.id === this.selectedTrigger)
      ?? triggers[0] ?? null;
    return html`
      <div class="page">
        <div class="group">
          <h3>${t('Editor.MacroSettings.General')}</h3>
          <label class="field">
            <span>${t('Editor.MacroName')}</span>
            <input .value=${snapshot.name} data-field="name"
              placeholder=${t('Editor.NamePlaceholder')}
              @input=${(event: Event) => editor.session.setDraft('name',
                (event.target as HTMLInputElement).value)}
              @change=${() => void editor.session.commitDrafts()} />
          </label>
          <label class="field">
            <span>${t('Editor.MacroDescription')}</span>
            <textarea .value=${snapshot.description} data-field="description"
              placeholder=${t('Editor.DescriptionPlaceholder')}
              @input=${(event: Event) => editor.session.setDraft('description',
                (event.target as HTMLTextAreaElement).value)}
              @change=${() => void editor.session.commitDrafts()}></textarea>
          </label>
          <label class="field">
            <span>${t('Editor.MacroGroup')}</span>
            <input .value=${snapshot.group} data-field="group"
              placeholder=${t('Editor.GroupPlaceholder')}
              @input=${(event: Event) => editor.session.setDraft('group',
                (event.target as HTMLInputElement).value)}
              @change=${() => void editor.session.commitDrafts()} />
          </label>
          ${snapshot.nameProblem
            ? html`<p class="problem">${snapshot.nameProblem}</p>`
            : nothing}
          ${snapshot.isSubMacro
            ? html`<p class="hint">${t('Editor.Dock.SubMacros')}</p>`
            : nothing}
        </div>
        ${snapshot.isSubMacro ? nothing : this.renderTriggers(triggers, selected)}
      </div>
    `;
  }

  private renderTriggers(triggers: EditorTrigger[], selected: EditorTrigger | null): TemplateResult {
    return html`
      <div class="group">
        <h3>${t('Editor.TriggerEntries')}</h3>
        <div class="triggers">
          ${triggers.map((trigger) => html`
            <div class="trigger" role="button" tabindex="0"
              data-selected=${trigger.id === selected?.id ? 'true' : 'false'}
              @click=${() => { this.selectedTrigger = trigger.id; }}
              @keydown=${(event: KeyboardEvent) => {
                if (event.key === 'Enter' || event.key === ' ') {
                  event.preventDefault();
                  this.selectedTrigger = trigger.id;
                }
              }}>
              <input type="checkbox" .checked=${trigger.enabled}
                title=${t('Editor.TriggerEnabled')} aria-label=${t('Editor.TriggerEnabled')}
                @click=${(event: Event) => event.stopPropagation()}
                @change=${(event: Event) => void editor.triggers.setTriggerEnabled(
                  trigger.id, (event.target as HTMLInputElement).checked)} />
              <span class="body">
                <span class="kind">${trigger.kindLabel}</span>
                <span class="summary">${trigger.summary}</span>
              </span>
              <button class="ghost" title=${t('Editor.RemoveTrigger')}
                aria-label=${t('Editor.RemoveTrigger')}
                @click=${(event: Event) => {
                  event.stopPropagation();
                  void editor.triggers.removeTrigger(trigger.id);
                }}>${icon('close')}</button>
            </div>
          `)}
          ${triggers.length === 0
            ? html`<p class="empty">${t('Editor.NoTriggerSelected')}</p>`
            : nothing}
        </div>
        ${selected ? renderTriggerFields(this, selected) : nothing}
        ${this.renderTriggerRule()}
        ${editor.get().snapshot?.triggerProblemSummary
          ? html`<p class="problem">${editor.get().snapshot?.triggerProblemSummary}</p>`
          : nothing}
        <div class="inline">
          <select aria-label=${t('Editor.AddTrigger')} .value=${this.newTriggerKind}
            @change=${(event: Event) => {
              this.newTriggerKind = (event.target as HTMLSelectElement).value;
            }}>
            ${(editor.get().catalogue?.triggerKinds ?? []).map((kind) => html`
              <option value=${kind.value}>${kind.label}</option>
            `)}
          </select>
          <button class="link-button" type="button" @click=${() => void this.addTrigger()}>
            ${t('Editor.AddTrigger')}
          </button>
        </div>
      </div>
    `;
  }

  private hotstringText(): string {
    const trigger = editor.get().snapshot?.triggers.find((entry) =>
      entry.id === this.characterTrigger);
    return trigger?.hotstringText ?? '';
  }

  openWindowRule(trigger: EditorTrigger): void {
    this.ruleTrigger = trigger.id;
  }

  /** The window-rule form an entry's rule is set with, once an entry asked for it. */
  private renderTriggerRule(): TemplateResult | typeof nothing {
    const trigger = editor.get().snapshot?.triggers.find((entry) =>
      entry.id === this.ruleTrigger);
    if (!trigger) {
      return nothing;
    }

    return html`<wg-window-rule
      .rule=${this.triggerRule(trigger)}
      .title=${t('WindowRule.Choose')}
      .allowHandle=${trigger.kind === 'Window'}
      @wg-rule-submit=${(event: CustomEvent<WindowRuleState>) => void this.submitTriggerRule(event)}
      @wg-rule-close=${() => { this.ruleTrigger = ''; }}></wg-window-rule>`;
  }

  /** The entry's rule as the shared form reads it. */
  private triggerRule(trigger: EditorTrigger): WindowRuleState {
    const isWindow = trigger.kind === 'Window';
    return {
      title: isWindow ? trigger.windowTitle : trigger.interfaceTitle,
      process: isWindow ? trigger.windowProcess : trigger.interfaceProcess,
      className: trigger.windowClass,
      handle: 0,
      how: ruleHow(isWindow ? trigger.windowHow : trigger.interfaceHow) || 'titleContains',
      caseSensitive: trigger.windowCaseSensitive,
      trim: trigger.windowTrim,
      multiline: trigger.windowMultiline,
    };
  }

  /**
   * Writes the rule the form handed over into the entry. Every part goes in — the text, the part of
   * the window it is held up against, and the three switches — so what the form shows is what the
   * group will be recognised by. An interface entry keeps the ten comparisons that name a window by
   * something that outlives it, so a handle rule is not offered for one.
   */
  private async submitTriggerRule(event: CustomEvent<WindowRuleState>): Promise<void> {
    const triggerId = this.ruleTrigger;
    this.ruleTrigger = '';
    const trigger = editor.get().snapshot?.triggers.find((entry) => entry.id === triggerId);
    if (!trigger) {
      return;
    }

    const rule = event.detail;
    const isWindow = trigger.kind === 'Window';
    const write = (field: string, value: string): Promise<boolean> => (isWindow
      ? editor.triggers.setTriggerWindow(triggerId, field, value)
      : editor.triggers.setTriggerInterface(triggerId, field, value));

    await write('how', isWindow ? rule.how : rule.how === 'handle' ? 'titleContains' : rule.how);
    await write('title', rule.title);
    await write('process', rule.process);
    await write('class', rule.className);
    await write('caseSensitive', String(rule.caseSensitive));
    await write('trim', String(rule.trim));
    await write('multiline', String(rule.multiline));
    if (isWindow) {
      await write('handle', String(rule.handle));
    }
  }

  async openCharacterPad(trigger: EditorTrigger): Promise<void> {
    const pad = await editor.triggers.hotstringKeyPad();
    if (pad) {
      this.characterTrigger = trigger.id;
      this.characterPad = pad;
    }
  }

  private async submitCharacter(event: CustomEvent<{ text: string }>): Promise<void> {
    await editor.triggers.setTriggerHotstring(this.characterTrigger, event.detail.text);
    this.characterPad = null;
  }

  beginEndCapture(trigger: EditorTrigger): void {
    this.endCaptureTrigger = trigger.id;
    window.addEventListener('keydown', this.onEndCapture, true);
  }

  private onEndCapture = (event: KeyboardEvent): void => {
    if (this.endCaptureTrigger.length === 0) {
      return;
    }

    event.preventDefault();
    const trigger = this.endCaptureTrigger;
    this.endCaptureTrigger = '';
    window.removeEventListener('keydown', this.onEndCapture, true);
    if (event.key === 'Escape') {
      return;
    }

    const character = event.key === ' ' ? ' ' : event.key === 'Tab' ? '\t'
      : event.key === 'Enter' ? '\n' : event.key;
    if ([...character].length === 1) {
      void editor.triggers.captureTriggerHotstringEnd(trigger, character);
    }
  };

  beginInterfaceCapture(trigger: EditorTrigger): void {
    this.interfaceCaptureTrigger = trigger.id;
    window.addEventListener('keydown', this.onInterfaceCapture, true);
  }

  private onInterfaceCapture = (event: KeyboardEvent): void => {
    if (this.interfaceCaptureTrigger.length === 0) {
      return;
    }

    event.preventDefault();
    const trigger = this.interfaceCaptureTrigger;
    this.interfaceCaptureTrigger = '';
    window.removeEventListener('keydown', this.onInterfaceCapture, true);
    if (event.key === 'Escape') {
      return;
    }

    const binding = keyboardChord(event);
    if (binding) {
      void editor.triggers.setTriggerInterfaceHotkey(trigger, binding);
    }
  };

  private async addTrigger(): Promise<void> {
    const before = new Set((editor.get().snapshot?.triggers ?? []).map((trigger) => trigger.id));
    await editor.triggers.addTrigger(this.newTriggerKind);
    const added = (editor.get().snapshot?.triggers ?? [])
      .find((trigger) => !before.has(trigger.id));
    if (added) {
      this.selectedTrigger = added.id;
    }
  }

  startTriggerCapture(triggerId: string): void {
    this.capturingTrigger = triggerId;
    this.requestUpdate();
    void this.updateComplete.then(() => {
      const input = this.renderRoot.querySelector<HTMLInputElement>(
        `input[data-trigger-capture="${CSS.escape(triggerId)}"]`);
      input?.focus();
      input?.select();
    });
  }

  keyName(event: KeyboardEvent): string {
    return keyboardChord(event) ?? '';
  }
}

declare global {
  interface HTMLElementTagNameMap {
    'wg-macro-settings': WgMacroSettings;
  }
}