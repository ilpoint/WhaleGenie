import { LitElement, css, html, nothing, type TemplateResult } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import { icon } from '../../shared/icons.js';
import { t } from '../../shared/strings/strings.js';
import type { EditorTrigger } from '../../shared/bridge/protocol.js';
import { editor } from './editor-store.js';
import { editorBase } from './editor-css.js';

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
  private capturingTrigger = '';

  @state()
  private newTriggerKind = 'Key';

  static styles = [editorBase, css`
    :host {
      display: flex;
      flex-direction: column;
      min-height: 0;
      overflow: hidden;
    }

    .title {
      display: flex;
      align-items: center;
      gap: var(--spacingHorizontalXS);
      flex: none;
      height: 36px;
      padding: 0 var(--spacingHorizontalL);
      border-bottom: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
      background: var(--colorNeutralBackground2);
      color: var(--colorNeutralForeground1);
    }

    .title h3 {
      margin: 0;
      font-size: var(--fontSizeBase300);
      font-weight: var(--fontWeightSemibold);
    }

    .tabs {
      display: flex;
      flex: none;
      gap: var(--spacingHorizontalXS);
      padding: 0 var(--spacingHorizontalL);
      border-bottom: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
    }

    .body {
      flex: 1;
      min-height: 0;
      overflow: auto;
      padding: var(--spacingVerticalS) var(--spacingHorizontalL) var(--spacingVerticalL);
    }

    .footer {
      display: flex;
      align-items: center;
      gap: var(--spacingHorizontalXS);
      flex: none;
      padding: var(--spacingVerticalS) var(--spacingHorizontalL);
      border-top: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
      background: var(--colorNeutralBackground2);
    }

    .footer .error {
      flex: 1;
      min-width: 0;
      color: var(--colorStatusDangerForeground1);
      font-size: var(--fontSizeBase100);
      overflow: hidden;
      text-overflow: ellipsis;
    }

    .footer fluent-button {
      min-width: 0;
    }

    .tabs button {
      border: none;
      border-bottom: 2px solid transparent;
      background: transparent;
      color: var(--colorNeutralForeground3);
      padding: var(--spacingVerticalXS) var(--spacingHorizontalS);
      cursor: pointer;
    }

    .tabs button[aria-selected='true'] {
      color: var(--colorNeutralForeground1);
      border-bottom-color: var(--colorBrandBackground);
    }

    .tabs button:focus-visible {
      outline: var(--strokeWidthThicker) solid var(--colorStrokeFocus2);
      outline-offset: -1px;
    }

    .page {
      display: grid;
      gap: var(--spacingVerticalS);
      min-width: 0;
    }

    .group {
      display: grid;
      gap: var(--spacingVerticalS);
      padding: var(--spacingVerticalS) var(--spacingHorizontalS);
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
      border-radius: var(--borderRadiusMedium);
      background: var(--colorNeutralBackground2);
    }

    .group > h3 {
      margin: 0;
      color: var(--colorNeutralForeground2);
      font-size: var(--fontSizeBase200);
      font-weight: var(--fontWeightSemibold);
    }

    .triggers {
      display: grid;
      gap: var(--spacingVerticalXS);
    }

    .trigger {
      display: grid;
      grid-template-columns: 28px minmax(0, 1fr) 24px;
      gap: var(--spacingHorizontalXS);
      align-items: center;
      min-height: 36px;
      padding: var(--spacingVerticalXS) var(--spacingHorizontalS);
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
      border-radius: var(--borderRadiusMedium);
      background: var(--colorNeutralBackground1);
      text-align: left;
      cursor: pointer;
    }

    .trigger:hover {
      background: var(--colorNeutralBackground1Hover);
    }

    .trigger[data-selected='true'] {
      border-color: var(--colorBrandStroke1);
      background: var(--colorBrandBackground2);
    }

    .trigger .body {
      display: grid;
      min-width: 0;
    }

    .trigger .kind {
      font-size: var(--fontSizeBase200);
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .trigger .summary {
      color: var(--colorNeutralForeground3);
      font-size: var(--fontSizeBase100);
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .trigger input {
      accent-color: var(--colorBrandBackground);
    }

    .trigger-fields {
      display: grid;
      gap: var(--spacingVerticalS);
      padding: var(--spacingVerticalS) var(--spacingHorizontalS);
      border-left: 2px solid var(--colorBrandStroke1);
      background: var(--colorNeutralBackground1);
    }

    .two {
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      gap: var(--spacingVerticalS) var(--spacingHorizontalS);
    }

    .inline {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--spacingHorizontalS);
    }

    .inline input[type='checkbox'] {
      accent-color: var(--colorBrandBackground);
    }

    .hint {
      font-size: var(--fontSizeBase100);
    }

    .link-button {
      justify-self: start;
      min-height: 26px;
      padding: 0 var(--spacingHorizontalS);
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke1);
      border-radius: var(--borderRadiusMedium);
      background: var(--colorNeutralBackground1);
      color: var(--colorNeutralForeground1);
      font-size: var(--fontSizeBase100);
      cursor: pointer;
    }

    .link-button:hover {
      background: var(--colorNeutralBackground1Hover);
    }

    .link-button:focus-visible {
      outline: var(--strokeWidthThicker) solid var(--colorStrokeFocus2);
      outline-offset: -1px;
    }

    .declarations {
      display: grid;
      gap: var(--spacingVerticalXS);
    }

    .declaration {
      display: grid;
      gap: var(--spacingVerticalXS);
      padding: var(--spacingVerticalS) var(--spacingHorizontalS);
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
      border-radius: var(--borderRadiusMedium);
      background: var(--colorNeutralBackground1);
    }

    .declaration-head {
      display: grid;
      grid-template-columns: minmax(0, 1fr) 96px 74px 24px 24px 24px;
      gap: 3px;
      align-items: center;
    }

    .declaration-head input,
    .declaration-head select {
      min-width: 0;
      width: 100%;
    }

    .record-fields {
      display: grid;
      gap: 3px;
      padding: var(--spacingVerticalXS) var(--spacingHorizontalS);
      border-left: 2px solid var(--colorNeutralStroke2);
      background: var(--colorNeutralBackground2);
    }

    .record-field {
      display: grid;
      grid-template-columns: minmax(0, 1fr) 100px 24px;
      gap: 3px;
    }

    .empty {
      color: var(--colorNeutralForeground3);
      font-size: var(--fontSizeBase200);
    }

    .saved {
      color: var(--colorStatusSuccessForeground1);
      font-size: var(--fontSizeBase200);
    }
  `];

  private unsubscribe?: () => void;

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
        ${this.page === 'general' ? this.renderGeneral() : this.renderSpecific()}
      </div>
      <div class="footer">
        ${state.error
          ? html`<span class="error" title=${state.error}>${state.error}</span>`
          : html`<span style="flex:1"></span>`}
        <fluent-button appearance="accent"
          ?disabled=${!snapshot.dirty || !snapshot.canSave}
          @click=${() => void editor.save()}>${t('Editor.Save')}</fluent-button>
        <fluent-button
          ?disabled=${snapshot.isSubMacro || snapshot.steps.length === 0 || !snapshot.canSave}
          @click=${() => void editor.run()}>${t('Editor.RunDebug')}</fluent-button>
      </div>
    `;
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
              @input=${(event: Event) => editor.setDraft('name',
                (event.target as HTMLInputElement).value)}
              @change=${() => void editor.commitDrafts()} />
          </label>
          <label class="field">
            <span>${t('Editor.MacroDescription')}</span>
            <textarea .value=${snapshot.description} data-field="description"
              placeholder=${t('Editor.DescriptionPlaceholder')}
              @input=${(event: Event) => editor.setDraft('description',
                (event.target as HTMLTextAreaElement).value)}
              @change=${() => void editor.commitDrafts()}></textarea>
          </label>
          <label class="field">
            <span>${t('Editor.MacroGroup')}</span>
            <input .value=${snapshot.group} data-field="group"
              placeholder=${t('Editor.GroupPlaceholder')}
              @input=${(event: Event) => editor.setDraft('group',
                (event.target as HTMLInputElement).value)}
              @change=${() => void editor.commitDrafts()} />
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
                @change=${(event: Event) => void editor.setTriggerEnabled(
                  trigger.id, (event.target as HTMLInputElement).checked)} />
              <span class="body">
                <span class="kind">${trigger.kindLabel}</span>
                <span class="summary">${trigger.summary}</span>
              </span>
              <button class="ghost" title=${t('Editor.RemoveTrigger')}
                aria-label=${t('Editor.RemoveTrigger')}
                @click=${(event: Event) => {
                  event.stopPropagation();
                  void editor.removeTrigger(trigger.id);
                }}>${icon('close')}</button>
            </div>
          `)}
          ${triggers.length === 0
            ? html`<p class="empty">${t('Editor.NoTriggerSelected')}</p>`
            : nothing}
        </div>
        ${selected ? this.renderTriggerFields(selected) : nothing}
        ${editor.get().snapshot?.triggerProblemSummary
          ? html`<p class="problem">${editor.get().snapshot?.triggerProblemSummary}</p>`
          : nothing}
        <div class="inline">
          <select aria-label=${t('Editor.AddTrigger')} .value=${this.newTriggerKind}
            @change=${(event: Event) => {
              this.newTriggerKind = (event.target as HTMLSelectElement).value;
            }}>
            ${(editor.get().snapshot?.triggerKinds ?? []).map((kind) => html`
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

  private renderTriggerFields(trigger: EditorTrigger): TemplateResult {
    const fields: TemplateResult[] = [];
    if (trigger.kind === 'Key') {
      fields.push(html`
        <div class="field">
          <span>${t('Editor.BindKey')}</span>
          <div class="inline">
            <input
              data-trigger-capture=${trigger.id}
              .value=${trigger.binding}
              placeholder=${t('Editor.BindKey')}
              @keydown=${(event: KeyboardEvent) => {
                if (this.capturingTrigger !== trigger.id) {
                  return;
                }

                if (['Control', 'Shift', 'Alt', 'Meta'].includes(event.key)) {
                  return;
                }

                event.preventDefault();
                const key = this.keyName(event);
                this.capturingTrigger = '';
                void editor.setTriggerBinding(trigger.id, key);
              }}
              @change=${(event: Event) => void editor.setTriggerBinding(
                trigger.id, (event.target as HTMLInputElement).value)}
            />
            <button class="link-button" type="button"
              @click=${() => this.startTriggerCapture(trigger.id)}>
              ${t('Add.SetCombination')}
            </button>
          </div>
        </div>
        <div class="two">
          <label class="field">
            <span>${t('Editor.Loop')}</span>
            <select .value=${trigger.loop}
              @change=${(event: Event) => void editor.setTriggerLoop(
                trigger.id, (event.target as HTMLSelectElement).value)}>
              <option value="Press">${t('Loop.Press')}</option>
              <option value="Hold">${t('Loop.Hold')}</option>
              <option value="Toggle">${t('Loop.Toggle')}</option>
              <option value="Release">${t('Loop.Release')}</option>
            </select>
          </label>
          <label class="inline">
            <input type="checkbox" .checked=${trigger.once}
              @change=${(event: Event) => void editor.setTriggerOnce(
                trigger.id, (event.target as HTMLInputElement).checked)} />
            <span>${t('Editor.TriggerOnce')}</span>
          </label>
        </div>
      `);
    } else if (trigger.kind === 'Timer') {
      fields.push(html`
        <label class="field">
          <span>${t('Editor.Schedule')}</span>
          <select .value=${trigger.timerMode}
            @change=${(event: Event) => void editor.setTriggerTimer(trigger.id, 'mode',
              (event.target as HTMLSelectElement).value)}>
            <option value="Interval">${t('Schedule.Interval')}</option>
            <option value="Daily">${t('Schedule.Daily')}</option>
          </select>
        </label>
        ${trigger.timerMode === 'Daily'
          ? html`
            <label class="field">
              <span>${t('Editor.ScheduleTime')}</span>
              <input .value=${trigger.time} placeholder=${t('Editor.ScheduleTimeHint')}
                @change=${(event: Event) => void editor.setTriggerTimer(trigger.id, 'time',
                  (event.target as HTMLInputElement).value)} />
            </label>
          `
          : html`
            <div class="two">
              <label class="field">
                <span>${t('Editor.Interval')}</span>
                <input type="number" min="1" .value=${trigger.interval}
                  @change=${(event: Event) => void editor.setTriggerTimer(trigger.id, 'interval',
                    (event.target as HTMLInputElement).value)} />
              </label>
              <label class="field">
                <span>${t('Editor.Schedule')}</span>
                <select .value=${trigger.unit}
                  @change=${(event: Event) => void editor.setTriggerTimer(trigger.id, 'unit',
                    (event.target as HTMLSelectElement).value)}>
                  <option value="Seconds">${t('Schedule.Unit.Seconds')}</option>
                  <option value="Minutes">${t('Schedule.Unit.Minutes')}</option>
                  <option value="Hours">${t('Schedule.Unit.Hours')}</option>
                </select>
              </label>
            </div>
          `}
      `);
    } else if (trigger.kind === 'File') {
      fields.push(html`
        <label class="field">
          <span>${t('Editor.WatchPath')}</span>
          <input .value=${trigger.filePath} placeholder=${t('Editor.WatchPathHint')}
            @change=${(event: Event) => void editor.setTriggerFile(trigger.id, 'path',
              (event.target as HTMLInputElement).value)} />
        </label>
        <div class="two">
          <label class="field">
            <span>${t('Editor.WatchChange')}</span>
            <select .value=${trigger.fileChange}
              @change=${(event: Event) => void editor.setTriggerFile(trigger.id, 'change',
                (event.target as HTMLSelectElement).value)}>
              <option value="Any">${t('FileChange.Any')}</option>
              <option value="Created">${t('FileChange.Created')}</option>
              <option value="Changed">${t('FileChange.Changed')}</option>
              <option value="Deleted">${t('FileChange.Deleted')}</option>
            </select>
          </label>
          <label class="field">
            <span>${t('Editor.WatchFilter')}</span>
            <input .value=${trigger.fileFilter} placeholder=${t('Editor.WatchFilter')}
              @change=${(event: Event) => void editor.setTriggerFile(trigger.id, 'filter',
                (event.target as HTMLInputElement).value)} />
          </label>
        </div>
        <label class="inline">
          <input type="checkbox" .checked=${trigger.fileSubfolders}
            @change=${(event: Event) => void editor.setTriggerFile(trigger.id, 'subfolders',
              String((event.target as HTMLInputElement).checked))} />
          <span>${t('Editor.WatchSubfolders')}</span>
        </label>
      `);
    } else if (trigger.kind === 'Process') {
      fields.push(html`
        <label class="field">
          <span>${t('Editor.ProcessName')}</span>
          <input .value=${trigger.processName} placeholder=${t('Editor.ProcessNameHint')}
            @change=${(event: Event) => void editor.setTriggerProcess(trigger.id, 'name',
              (event.target as HTMLInputElement).value)} />
        </label>
        <label class="field">
          <span>${t('Editor.ProcessWhen')}</span>
          <select .value=${trigger.processChange}
            @change=${(event: Event) => void editor.setTriggerProcess(trigger.id, 'change',
              (event.target as HTMLSelectElement).value)}>
            <option value="Any">${t('ProcessChange.Any')}</option>
            <option value="Started">${t('ProcessChange.Started')}</option>
            <option value="Stopped">${t('ProcessChange.Stopped')}</option>
          </select>
        </label>
      `);
    } else if (trigger.kind === 'Window' || trigger.kind === 'Interface') {
      const isWindow = trigger.kind === 'Window';
      fields.push(html`
        <label class="field">
          <span>${t('WindowPart.Title')}</span>
          <input .value=${isWindow ? trigger.windowTitle : trigger.interfaceTitle}
            @change=${(event: Event) => void (isWindow
              ? editor.setTriggerWindow(trigger.id, 'title', (event.target as HTMLInputElement).value)
              : editor.setTriggerInterface(trigger.id, 'title',
                (event.target as HTMLInputElement).value))} />
        </label>
        <label class="field">
          <span>${t('WindowRule.Process')}</span>
          <input .value=${isWindow ? trigger.windowProcess : trigger.interfaceProcess}
            @change=${(event: Event) => void (isWindow
              ? editor.setTriggerWindow(trigger.id, 'process',
                (event.target as HTMLInputElement).value)
              : editor.setTriggerInterface(trigger.id, 'process',
                (event.target as HTMLInputElement).value))} />
        </label>
        <label class="field">
          <span>${t('WindowRule.How')}</span>
          <select .value=${isWindow ? trigger.windowHow : trigger.interfaceHow}
            @change=${(event: Event) => void (isWindow
              ? editor.setTriggerWindow(trigger.id, 'how', (event.target as HTMLSelectElement).value)
              : editor.setTriggerInterface(trigger.id, 'how',
                (event.target as HTMLSelectElement).value))}>
            <option value="TitleContains">${t('WindowRule.How.titleContains')}</option>
            <option value="TitleStartsWith">${t('WindowRule.How.titleStartsWith')}</option>
            <option value="TitleEndsWith">${t('WindowRule.How.titleEndsWith')}</option>
            <option value="TitleEquals">${t('WindowRule.How.titleEquals')}</option>
            <option value="TitleRegex">${t('WindowRule.How.titleRegex')}</option>
            <option value="Process">${t('WindowRule.How.process')}</option>
            <option value="ProcessEquals">${t('WindowRule.How.processEquals')}</option>
            <option value="Class">${t('WindowRule.How.class')}</option>
            <option value="ClassEquals">${t('WindowRule.How.classEquals')}</option>
            ${isWindow ? html`<option value="Handle">${t('WindowRule.How.handle')}</option>` : nothing}
          </select>
        </label>
        ${isWindow
          ? html`
            <label class="field">
              <span>${t('WindowChange.Any')}</span>
              <select .value=${trigger.windowChange}
                @change=${(event: Event) => void editor.setTriggerWindow(trigger.id, 'change',
                  (event.target as HTMLSelectElement).value)}>
                <option value="Appeared">${t('WindowChange.Appeared')}</option>
                <option value="Disappeared">${t('WindowChange.Disappeared')}</option>
                <option value="Any">${t('WindowChange.Any')}</option>
              </select>
            </label>
          `
          : nothing}
      `);
    } else if (trigger.kind === 'Hotstring') {
      fields.push(html`
        <label class="field">
          <span>${t('Editor.Hotstring')}</span>
          <input .value=${trigger.hotstringText} placeholder=${t('Editor.HotstringHint')}
            @change=${(event: Event) => void editor.setTriggerHotstring(trigger.id,
              (event.target as HTMLInputElement).value)} />
        </label>
      `);
    } else if (trigger.kind === 'Color') {
      const colour = /^#[0-9a-f]{6}$/i.test(trigger.colorHex) ? trigger.colorHex : '#000000';
      fields.push(html`
        <div class="two">
          <label class="field">
            <span>${t('Editor.ColorTolerance')}</span>
            <select .value=${trigger.colorMatch}
              @change=${(event: Event) => void editor.setTriggerColor(trigger.id, 'match',
                (event.target as HTMLSelectElement).value)}>
              <option value="ColorMatches">${t('ColorMatch.Matches')}</option>
              <option value="ColorNotMatches">${t('ColorMatch.NotMatches')}</option>
            </select>
          </label>
          <label class="field">
            <span>${t('Editor.HexColor')}</span>
            <div class="inline">
              <input type="color" .value=${colour}
                @input=${(event: Event) => void editor.setTriggerColor(trigger.id, 'hex',
                  (event.target as HTMLInputElement).value)} />
              <input .value=${trigger.colorHex}
                @change=${(event: Event) => void editor.setTriggerColor(trigger.id, 'hex',
                  (event.target as HTMLInputElement).value)} />
            </div>
          </label>
        </div>
        <div class="two">
          <label class="field">
            <span>${t('Editor.ColorTolerance')}</span>
            <input type="number" min="0" .value=${trigger.colorTolerance}
              @change=${(event: Event) => void editor.setTriggerColor(trigger.id, 'tolerance',
                (event.target as HTMLInputElement).value)} />
          </label>
          <label class="inline">
            <input type="checkbox" .checked=${trigger.followCursor}
              @change=${(event: Event) => void editor.setTriggerColor(trigger.id, 'follow',
                String((event.target as HTMLInputElement).checked))} />
            <span>${t('Editor.UseCursorPosition')}</span>
          </label>
        </div>
        ${trigger.followCursor
          ? nothing
          : html`
            <div class="two">
              <label class="field">
                <span>${t('Editor.X')}</span>
                <input type="number" .value=${trigger.colorX}
                  @change=${(event: Event) => void editor.setTriggerColor(trigger.id, 'x',
                    (event.target as HTMLInputElement).value)} />
              </label>
              <label class="field">
                <span>${t('Editor.Y')}</span>
                <input type="number" .value=${trigger.colorY}
                  @change=${(event: Event) => void editor.setTriggerColor(trigger.id, 'y',
                    (event.target as HTMLInputElement).value)} />
              </label>
            </div>
          `}
      `);
    } else if (trigger.kind === 'Idle') {
      fields.push(html`
        <label class="field">
          <span>${t('Editor.IdleSeconds')}</span>
          <input type="number" min="1" .value=${trigger.idleSeconds}
            @change=${(event: Event) => void editor.setTriggerIdle(trigger.id,
              (event.target as HTMLInputElement).value)} />
        </label>
      `);
    } else {
      fields.push(html`<p class="hint">${trigger.summary}</p>`);
    }

    return html`<div class="trigger-fields">${fields}</div>`;
  }

  private renderSpecific(): TemplateResult {
    const snapshot = editor.get().snapshot;
    if (!snapshot) {
      return html``;
    }

    return html`
      <div class="page macro-settings">
        <p class="hint">${t('Editor.SpecificHint')}</p>
        ${snapshot.isSubMacro
          ? nothing
          : html`
            <div class="group">
              <h3>${t('Editor.InputLimit')}</h3>
              <label class="inline">
                <input type="checkbox" data-setting="followInputLimit" .checked=${snapshot.followInputLimit}
                  @change=${(event: Event) => void editor.setMacroSetting('followInputLimit',
                    String((event.target as HTMLInputElement).checked))} />
                <span>${t('Editor.InputLimitFollow')}</span>
              </label>
              <div class="inline">
                <input type="number" min="0" max="999" data-setting="inputLimit" .value=${String(snapshot.inputLimit)}
                  ?disabled=${snapshot.followInputLimit}
                  @change=${(event: Event) => void editor.setMacroSetting('inputLimit',
                    (event.target as HTMLInputElement).value)} />
                <span class="hint">${t('Editor.InputLimitHint')}</span>
              </div>
            </div>
            <div class="group">
              <h3>${t('Editor.ToggleKey')}</h3>
              <div class="inline">
                <input .value=${snapshot.toggleKey} data-setting="toggleKey"
                  @change=${(event: Event) => void editor.setMacroSetting('toggleKey',
                    (event.target as HTMLInputElement).value)} />
                <button class="link-button" type="button"
                  @click=${() => void editor.setMacroSetting('toggleKey', '')}>
                  ${t('Settings.HotkeyClear')}
                </button>
              </div>
              <p class="hint">${t('Editor.ToggleKeyHint')}</p>
              ${snapshot.toggleKeyProblem
                ? html`<p class="problem">${snapshot.toggleKeyProblem}</p>`
                : nothing}
            </div>
          `}
        <div class="group">
          <h3>${t('Editor.MacroInterface')}</h3>
          ${this.renderDeclarations()}
        </div>
      </div>
    `;
  }

  private renderDeclarations(): TemplateResult {
    const snapshot = editor.get().snapshot;
    if (!snapshot) {
      return html``;
    }

    const types = snapshot.variableTypes;
    return html`
      <h3>${t('MacroInterface.Parameters')}</h3>
      <div class="declarations">
        ${snapshot.callableParameters.map((parameter, index) => html`
          <div class="declaration">
            <div class="declaration-head">
              <input .value=${parameter.name} placeholder=${t('MacroInterface.Name')}
                @change=${(event: Event) => void editor.patchCallableParameter(index, 'name',
                  (event.target as HTMLInputElement).value)} />
              <select .value=${parameter.type}
                @change=${(event: Event) => void editor.patchCallableParameter(index, 'type',
                  (event.target as HTMLSelectElement).value)}>
                ${types.map((type) => html`<option value=${type.value}>${type.label}</option>`)}
              </select>
              <label class="inline" title=${t('MacroInterface.Required')}>
                <input type="checkbox" .checked=${parameter.required}
                  @change=${(event: Event) => void editor.patchCallableParameter(index, 'required',
                    String((event.target as HTMLInputElement).checked))} />
                <span class="hint">${t('MacroInterface.Required')}</span>
              </label>
              <button class="ghost" title=${t('MacroInterface.MoveUp')}
                aria-label=${t('MacroInterface.MoveUp')}
                @click=${() => void editor.moveCallableParameter(index, -1)}>${icon('arrow-up')}</button>
              <button class="ghost" title=${t('MacroInterface.MoveDown')}
                aria-label=${t('MacroInterface.MoveDown')}
                @click=${() => void editor.moveCallableParameter(index, 1)}>${icon('arrow-down')}</button>
              <button class="ghost" title=${t('MacroInterface.Remove')}
                aria-label=${t('MacroInterface.Remove')}
                @click=${() => void editor.removeCallableParameter(index)}>${icon('close')}</button>
            </div>
            <label class="field">
              <span>${t('MacroInterface.Default')}</span>
              <input .value=${parameter.default} placeholder=${t('MacroInterface.Default')}
                @change=${(event: Event) => void editor.patchCallableParameter(index, 'default',
                  (event.target as HTMLInputElement).value)} />
            </label>
            <label class="field">
              <span>${t('MacroInterface.Description')}</span>
              <input .value=${parameter.description}
                @change=${(event: Event) => void editor.patchCallableParameter(index, 'description',
                  (event.target as HTMLInputElement).value)} />
            </label>
            ${parameter.type === 'record' ? this.renderRecordFields('parameter', index, parameter.fields) : nothing}
          </div>
        `)}
      </div>
      <button class="link-button" type="button"
        @click=${() => void editor.addCallableParameter()}>
        ${t('MacroInterface.AddParameter')}
      </button>
      <h3>${t('MacroInterface.Results')}</h3>
      <div class="declarations">
        ${snapshot.callableResults.map((result, index) => html`
          <div class="declaration">
            <div class="declaration-head">
              <input .value=${result.name} placeholder=${t('MacroInterface.Name')}
                @change=${(event: Event) => void editor.patchCallableResult(index, 'name',
                  (event.target as HTMLInputElement).value)} />
              <select .value=${result.type}
                @change=${(event: Event) => void editor.patchCallableResult(index, 'type',
                  (event.target as HTMLSelectElement).value)}>
                ${types.map((type) => html`<option value=${type.value}>${type.label}</option>`)}
              </select>
              <span></span>
              <button class="ghost" title=${t('MacroInterface.MoveUp')}
                aria-label=${t('MacroInterface.MoveUp')}
                @click=${() => void editor.moveCallableResult(index, -1)}>${icon('arrow-up')}</button>
              <button class="ghost" title=${t('MacroInterface.MoveDown')}
                aria-label=${t('MacroInterface.MoveDown')}
                @click=${() => void editor.moveCallableResult(index, 1)}>${icon('arrow-down')}</button>
              <button class="ghost" title=${t('MacroInterface.Remove')}
                aria-label=${t('MacroInterface.Remove')}
                @click=${() => void editor.removeCallableResult(index)}>${icon('close')}</button>
            </div>
            <label class="field">
              <span>${t('MacroInterface.Description')}</span>
              <input .value=${result.description}
                @change=${(event: Event) => void editor.patchCallableResult(index, 'description',
                  (event.target as HTMLInputElement).value)} />
            </label>
            ${result.type === 'record' ? this.renderRecordFields('result', index, result.fields) : nothing}
          </div>
        `)}
      </div>
      <button class="link-button" type="button"
        @click=${() => void editor.addCallableResult()}>
        ${t('MacroInterface.AddResult')}
      </button>
    `;
  }

  private renderRecordFields(kind: 'parameter' | 'result', index: number,
    fields: { name: string; type: string }[]): TemplateResult {
    const types = editor.get().snapshot?.variableTypes ?? [];
    return html`
      <div class="record-fields">
        ${fields.map((field, fieldIndex) => html`
          <div class="record-field">
            <input .value=${field.name} placeholder=${t('MacroInterface.FieldName')}
              @change=${(event: Event) => void (kind === 'parameter'
                ? editor.patchCallableParameterField(index, fieldIndex, 'name',
                  (event.target as HTMLInputElement).value)
                : editor.patchCallableResultField(index, fieldIndex, 'name',
                  (event.target as HTMLInputElement).value))} />
            <select .value=${field.type}
              @change=${(event: Event) => void (kind === 'parameter'
                ? editor.patchCallableParameterField(index, fieldIndex, 'type',
                  (event.target as HTMLSelectElement).value)
                : editor.patchCallableResultField(index, fieldIndex, 'type',
                  (event.target as HTMLSelectElement).value))}>
              ${types.map((type) => html`<option value=${type.value}>${type.label}</option>`)}
            </select>
            <button class="ghost" title=${t('MacroInterface.Remove')}
              aria-label=${t('MacroInterface.Remove')}
              @click=${() => void (kind === 'parameter'
                ? editor.removeCallableParameterField(index, fieldIndex)
                : editor.removeCallableResultField(index, fieldIndex))}>${icon('close')}</button>
          </div>
        `)}
        <button class="link-button" type="button"
          @click=${() => void (kind === 'parameter'
            ? editor.addCallableParameterField(index)
            : editor.addCallableResultField(index))}>
          ${t('MacroInterface.AddField')}
        </button>
      </div>
    `;
  }

  private async addTrigger(): Promise<void> {
    const before = new Set((editor.get().snapshot?.triggers ?? []).map((trigger) => trigger.id));
    await editor.addTrigger(this.newTriggerKind);
    const added = (editor.get().snapshot?.triggers ?? [])
      .find((trigger) => !before.has(trigger.id));
    if (added) {
      this.selectedTrigger = added.id;
    }
  }

  private startTriggerCapture(triggerId: string): void {
    this.capturingTrigger = triggerId;
    this.requestUpdate();
    void this.updateComplete.then(() => {
      const input = this.renderRoot.querySelector<HTMLInputElement>(
        `input[data-trigger-capture="${CSS.escape(triggerId)}"]`);
      input?.focus();
      input?.select();
    });
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
    'wg-macro-settings': WgMacroSettings;
  }
}
