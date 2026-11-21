import { LitElement, css, html, nothing, type TemplateResult } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import { bridge } from '../../shared/bridge/bridge.js';
import type {
  EditorAction,
  EditorConditionNode,
  EditorParameter,
  EditorSnapshot,
  EditorStep,
  EditorTab,
  EditorValueChoice,
  EditorValueFunction,
  ProjectState,
} from '../../shared/bridge/protocol.js';
import { icon } from '../../shared/icons.js';
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
  private actionSearch = '';

  @state()
  private recording = false;

  @state()
  private captured = 0;

  private recordTimer = 0;

  @state()
  private valueTarget: { step: EditorStep; parameter: EditorParameter } | null = null;

  @state()
  private valueDraft = '';

  @state()
  private valueSearch = '';

  @state()
  private selectedId = '';

  @state()
  private tabs: EditorTab[] = [];

  @state()
  private activeKey = '';

  @state()
  private macros: string[] = [];

  @state()
  private openName = '';

  @state()
  private capturingKey = '';

  @state()
  private triggerKind = 'Key';

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

    .open-macro {
      display: flex;
      align-items: center;
      gap: var(--spacingHorizontalS);
      margin-bottom: var(--spacingVerticalM);
    }

    .open-macro select {
      font: inherit;
      padding: var(--spacingVerticalSN) var(--spacingHorizontalM);
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke1);
      border-radius: var(--borderRadiusMedium);
      background: var(--colorNeutralBackground1);
      color: var(--colorNeutralForeground1);
    }

    .tabs {
      display: flex;
      gap: var(--spacingHorizontalXS);
      border-bottom: var(--strokeWidthThin) solid var(--colorNeutralStroke1);
      margin-bottom: var(--spacingVerticalM);
      overflow-x: auto;
    }

    .tab-item {
      display: flex;
      align-items: center;
    }

    .tabs button {
      border: none;
      border-bottom: 2px solid transparent;
      background: transparent;
      color: var(--colorNeutralForeground2);
      font: inherit;
      padding: var(--spacingVerticalSN) var(--spacingHorizontalM);
      cursor: default;
      white-space: nowrap;
    }

    .tabs button[aria-current='page'] {
      color: var(--colorNeutralForeground1);
      border-bottom-color: var(--colorBrandBackground);
    }

    .tabs .tab-close {
      border-bottom: none;
      padding: 0 var(--spacingHorizontalXS);
      color: var(--colorNeutralForeground2);
    }

    .tabs .tab-close:hover {
      color: var(--colorNeutralForeground1);
    }

    .declarations {
      width: 100%;
      border-collapse: collapse;
    }

    .declarations th,
    .declarations td {
      padding: var(--spacingVerticalXS);
      text-align: left;
      font-size: var(--font-size-base-200);
    }

    .declarations input,
    .declarations select {
      width: 100%;
    }

    .declarations button {
      border: none;
      background: transparent;
      color: var(--colorNeutralForeground2);
      font: inherit;
      cursor: pointer;
    }

    .record-fields td {
      padding-left: var(--spacingHorizontalL);
      background: var(--colorNeutralBackground2);
    }

    .record-field-list {
      display: grid;
      gap: var(--spacingVerticalXS);
    }

    .record-field-row {
      display: grid;
      grid-template-columns: minmax(0, 1fr) 150px auto;
      gap: var(--spacingHorizontalXS);
      align-items: center;
    }

    .macro-settings {
      display: grid;
      grid-template-columns: auto minmax(0, 1fr) minmax(0, 1fr);
      gap: var(--spacingHorizontalM);
      align-items: end;
    }

    .macro-settings label {
      display: grid;
      gap: var(--spacingVerticalXS);
      color: var(--colorNeutralForeground3);
      font-size: var(--font-size-base-200);
    }

    .submacros {
      display: flex;
      flex-wrap: wrap;
      gap: var(--spacingHorizontalXS);
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

    .variables {
      display: grid;
      gap: var(--spacingVerticalS);
      margin-bottom: var(--spacingVerticalM);
    }

    .variable-row {
      display: grid;
      grid-template-columns: 1fr 120px 1fr 1fr auto;
      gap: var(--spacingHorizontalS);
      align-items: center;
    }

    .variable-source {
      color: var(--colorNeutralForeground3);
      font-size: var(--font-size-base-200);
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .globals {
      display: grid;
      gap: var(--spacingVerticalS);
      margin-bottom: var(--spacingVerticalM);
    }

    .global-row {
      display: grid;
      grid-template-columns: 1fr 120px 1fr 1fr auto auto;
      gap: var(--spacingHorizontalS);
      align-items: center;
    }

    .global-row button,
    .variable-row button {
      border: none;
      background: transparent;
      color: var(--colorNeutralForeground2);
      font: inherit;
      cursor: pointer;
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
      flex-wrap: wrap;
      gap: var(--spacingHorizontalS);
    }

    .triggers .once,
    .triggers .enabled {
      display: flex;
      align-items: center;
      gap: var(--spacingHorizontalXS);
      color: var(--colorNeutralForeground2);
    }

    .trigger-kind {
      min-width: 96px;
      color: var(--colorNeutralForeground3);
    }

    .timer-fields {
      display: flex;
      align-items: center;
      gap: var(--spacingHorizontalS);
    }

    .timer-fields input[type='number'] {
      width: 80px;
    }

    .file-fields,
    .process-fields,
    .color-fields,
    .window-fields {
      display: flex;
      align-items: center;
      flex-wrap: wrap;
      gap: var(--spacingHorizontalS);
    }

    .color-fields input[type='color'] {
      width: 48px;
      padding: var(--spacingVerticalXS);
    }

    .idle-field {
      width: 96px;
    }

    .hotstring-field {
      flex: 1;
      min-width: 200px;
    }

    .triggers button {
      border: none;
      background: transparent;
      color: var(--colorNeutralForeground2);
      font: inherit;
      cursor: pointer;
    }

    .trigger-add {
      display: flex;
      align-items: center;
      gap: var(--spacingHorizontalS);
      margin-bottom: var(--spacingVerticalM);
    }

    .trigger-add select {
      font: inherit;
      padding: var(--spacingVerticalSN) var(--spacingHorizontalM);
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke1);
      border-radius: var(--borderRadiusMedium);
      background: var(--colorNeutralBackground1);
      color: var(--colorNeutralForeground1);
    }

    .toolbar {
      display: flex;
      align-items: center;
      gap: var(--spacingHorizontalS);
      margin-bottom: var(--spacingVerticalM);
    }

    .value-backdrop {
      position: fixed;
      inset: 0;
      z-index: 20;
      display: grid;
      place-items: center;
      background: color-mix(in srgb, var(--colorNeutralBackgroundInverted) 35%, transparent);
    }

    .value-dialog {
      width: min(720px, calc(100vw - 48px));
      max-height: calc(100vh - 96px);
      overflow: auto;
      display: grid;
      gap: var(--spacingVerticalM);
      padding: var(--spacingVerticalL);
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke1);
      border-radius: var(--borderRadiusXLarge);
      background: var(--colorNeutralBackground1);
      box-shadow: var(--shadow64);
    }

    .value-draft {
      min-height: 90px;
      font-family: var(--fontFamilyMonospace);
    }

    .value-groups {
      display: grid;
      grid-template-columns: minmax(0, 1fr) minmax(0, 2fr);
      gap: var(--spacingHorizontalL);
    }

    .value-groups h3 {
      margin: 0 0 var(--spacingVerticalS);
      font-size: var(--font-size-base-300);
    }

    .value-buttons {
      display: flex;
      flex-wrap: wrap;
      gap: var(--spacingHorizontalXS);
    }

    .value-buttons button {
      padding: var(--spacingVerticalXS) var(--spacingHorizontalS);
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
      border-radius: var(--borderRadiusMedium);
      background: var(--colorNeutralBackground2);
      color: var(--colorNeutralForeground1);
      font: inherit;
      font-size: var(--font-size-base-200);
      cursor: pointer;
    }

    .value-actions {
      display: flex;
      justify-content: flex-end;
      gap: var(--spacingHorizontalS);
    }

    .toolbar .record-status {
      align-self: center;
      color: var(--colorPaletteRedForeground1);
      font-size: var(--font-size-base-200);
    }

    .toolbar .action-search {
      font: inherit;
      padding: var(--spacingVerticalSN) var(--spacingHorizontalM);
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke1);
      border-radius: var(--borderRadiusMedium);
      background: var(--colorNeutralBackground1);
      color: var(--colorNeutralForeground1);
      min-width: 160px;
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

    .step-meta {
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      gap: var(--spacingVerticalS);
      padding-top: var(--spacingVerticalS);
      border-top: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
    }

    .step-meta label {
      display: grid;
      gap: var(--spacingVerticalXS);
      color: var(--colorNeutralForeground3);
      font-size: var(--font-size-base-200);
    }

    .param {
      display: grid;
      gap: var(--spacingVerticalXS);
    }

    .param label {
      color: var(--colorNeutralForeground3);
      font-size: var(--font-size-base-200);
    }

    .key-row,
    .color-row,
    .file-row {
      display: flex;
      gap: var(--spacingHorizontalS);
      align-items: center;
    }

    .color-row input[type='color'] {
      width: 48px;
      padding: var(--spacingVerticalXS);
    }

    .color-row input:last-child {
      flex: 1;
      min-width: 0;
    }

    .condition-join {
      margin-bottom: var(--spacingVerticalS);
    }

    .condition-group {
      display: grid;
      gap: var(--spacingVerticalXS);
    }

    .condition-row {
      display: grid;
      grid-template-columns: minmax(0, 1fr) 150px minmax(0, 1fr) auto;
      gap: var(--spacingHorizontalXS);
      align-items: center;
    }

    .condition-nested {
      padding-left: var(--spacingHorizontalM);
      border-left: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
    }

    .condition-nested-head {
      display: flex;
      justify-content: flex-end;
    }

    .condition-add {
      display: flex;
      gap: var(--spacingHorizontalS);
    }

    .condition-row button,
    .condition-nested-head button {
      border: none;
      background: transparent;
      color: var(--colorNeutralForeground2);
      font: inherit;
      cursor: pointer;
    }

    .row-list {
      width: 100%;
      border-collapse: collapse;
    }

    .row-list th,
    .row-list td {
      padding: var(--spacingVerticalXS);
      text-align: left;
      font-size: var(--font-size-base-200);
    }

    .row-list th {
      color: var(--colorNeutralForeground3);
      font-weight: var(--font-weight-regular);
    }

    .row-list input {
      width: 100%;
    }

    .row-list button {
      border: none;
      background: transparent;
      color: var(--colorNeutralForeground2);
      font: inherit;
      cursor: pointer;
    }

    .key-row input {
      flex: 1;
      min-width: 0;
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
    this.unsubscribeProject = project.subscribe((state) => this.projectChanged(state));
    window.addEventListener('keydown', this.onKeydown);
    void this.loadActions();
  }

  disconnectedCallback(): void {
    this.unsubscribeStrings?.();
    this.unsubscribeProject?.();
    window.removeEventListener('keydown', this.onKeydown);
    window.clearInterval(this.recordTimer);
    if (this.recording) {
      void this.stopRecording();
    }
    super.disconnectedCallback();
  }

  render() {
    if (this.snapshot === null) {
      return html`<h1>${t('Shell.Editor')}</h1><p>${t('Editor.NoSteps')}</p>`;
    }

    return html`
      <h1>${t('Shell.Editor')}</h1>
      ${this.error ? html`<p class="error">${this.error}</p>` : nothing}
      ${this.tabs.length > 0
        ? html`<div class="tabs" role="tablist">
            ${this.tabs.map((tab) => html`
              <div class="tab-item">
                <button
                  role="tab"
                  aria-current=${tab.key === this.activeKey ? 'page' : 'false'}
                  @click=${() => void this.activateTab(tab.key)}
                >${tab.name}${tab.dirty ? ' •' : ''}</button>
                <button
                  class="tab-close"
                  title=${t('Common.Close')}
                  aria-label=${t('Common.Close')}
                  @click=${(event: Event) => { event.stopPropagation(); void this.closeTab(tab.key); }}
                >×</button>
              </div>
            `)}
          </div>`
        : nothing}
      ${this.macros.length > 0
        ? html`<div class="open-macro">
            <select .value=${this.openName} @change=${this.selectOpen}>
              ${this.macros.map((name) => html`<option value=${name}>${name}</option>`)}
            </select>
            <fluent-button appearance="subtle" @click=${this.openSelected}>
              ${t('Editor.OpenMacro')}
            </fluent-button>
            <fluent-button appearance="subtle" @click=${this.newMacro}>
              ${t('Main.AddMacro')}
            </fluent-button>
          </div>`
        : nothing}
      <div class="fields">
        <input
          .value=${this.snapshot.name}
          placeholder=${t('Editor.NamePlaceholder')}
          aria-label=${t('Editor.NamePlaceholder')}
          @input=${(event: Event) => this.editName(event)}
        />
        <input
          .value=${this.snapshot.group}
          placeholder=${t('Editor.GroupPlaceholder')}
          aria-label=${t('Editor.GroupPlaceholder')}
          @input=${(event: Event) => this.editGroup(event)}
        />
        <textarea
          .value=${this.snapshot.description}
          placeholder=${t('Editor.DescriptionPlaceholder')}
          aria-label=${t('Editor.DescriptionPlaceholder')}
          @input=${(event: Event) => this.editDescription(event)}
        ></textarea>
      </div>
      ${this.snapshot.subMacros.length > 0
        ? html`
          <h2>${t('Editor.SubMacros')}</h2>
          <div class="submacros">
            ${this.snapshot.subMacros.map((name) => html`
              <fluent-button appearance="subtle" @click=${() => void this.openSubmacro(name)}>
                ${name}
              </fluent-button>
            `)}
          </div>
        `
        : nothing}
      <h2>${t('MacroInterface.Parameters')}</h2>
      <table class="declarations">
        <thead>
          <tr>
            <th>${t('MacroInterface.Name')}</th>
            <th>${t('MacroInterface.Type')}</th>
            <th>${t('MacroInterface.Required')}</th>
            <th>${t('MacroInterface.Default')}</th>
            <th>${t('MacroInterface.Description')}</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          ${this.snapshot.callableParameters.map((parameter, index) => html`
            <tr>
              <td><input .value=${parameter.name} @change=${(event: Event) =>
                void this.patchCallableParameter(index, 'name', event)} /></td>
              <td>
                <select .value=${parameter.type} @change=${(event: Event) =>
                  void this.patchCallableParameter(index, 'type', event)}>
                  ${this.snapshot!.variableTypes.map((type) => html`
                    <option value=${type.value}>${type.label}</option>
                  `)}
                </select>
              </td>
              <td><input type="checkbox" .checked=${parameter.required}
                @change=${(event: Event) => void this.patchCallableParameter(index, 'required', event)} /></td>
              <td><input .value=${parameter.default} @change=${(event: Event) =>
                void this.patchCallableParameter(index, 'default', event)} /></td>
              <td><input .value=${parameter.description} @change=${(event: Event) =>
                void this.patchCallableParameter(index, 'description', event)} /></td>
              <td><button aria-label=${t('MacroInterface.Remove')} @click=${() =>
                void this.removeCallableParameter(index)}>×</button></td>
            </tr>
            ${parameter.type === 'record'
              ? html`<tr class="record-fields"><td colspan="6">
                  ${this.renderRecordFields('parameter', index, parameter.fields)}
                </td></tr>`
              : nothing}
          `)}
        </tbody>
      </table>
      <fluent-button appearance="subtle" @click=${() => void this.addCallableParameter()}>
        ${t('MacroInterface.AddParameter')}
      </fluent-button>
      <h2>${t('MacroInterface.Results')}</h2>
      <table class="declarations">
        <thead>
          <tr>
            <th>${t('MacroInterface.Name')}</th>
            <th>${t('MacroInterface.Type')}</th>
            <th>${t('MacroInterface.Description')}</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          ${this.snapshot.callableResults.map((result, index) => html`
            <tr>
              <td><input .value=${result.name} @change=${(event: Event) =>
                void this.patchCallableResult(index, 'name', event)} /></td>
              <td>
                <select .value=${result.type} @change=${(event: Event) =>
                  void this.patchCallableResult(index, 'type', event)}>
                  ${this.snapshot!.variableTypes.map((type) => html`
                    <option value=${type.value}>${type.label}</option>
                  `)}
                </select>
              </td>
              <td><input .value=${result.description} @change=${(event: Event) =>
                void this.patchCallableResult(index, 'description', event)} /></td>
              <td><button aria-label=${t('MacroInterface.Remove')} @click=${() =>
                void this.removeCallableResult(index)}>×</button></td>
            </tr>
            ${result.type === 'record'
              ? html`<tr class="record-fields"><td colspan="4">
                  ${this.renderRecordFields('result', index, result.fields)}
                </td></tr>`
              : nothing}
          `)}
        </tbody>
      </table>
      <fluent-button appearance="subtle" @click=${() => void this.addCallableResult()}>
        ${t('MacroInterface.AddResult')}
      </fluent-button>
      <h2>${t('Editor.MacroSettings')}</h2>
      <div class="macro-settings">
        <label class="enabled">
          <input
            type="checkbox"
            .checked=${this.snapshot.followInputLimit}
            @change=${(event: Event) => void this.setMacroSetting('followInputLimit', event)}
          />
          ${t('Editor.InputLimitFollow')}
        </label>
        <label>
          ${t('Editor.InputLimit')}
          <input
            type="number"
            min="0"
            max="999"
            ?disabled=${this.snapshot.followInputLimit}
            .value=${String(this.snapshot.inputLimit)}
            @change=${(event: Event) => void this.setMacroSetting('inputLimit', event)}
          />
        </label>
        <label>
          ${t('Editor.ToggleKey')}
          <input
            .value=${this.snapshot.toggleKey}
            @change=${(event: Event) => void this.setMacroSetting('toggleKey', event)}
          />
        </label>
      </div>
      <h2>${t('Editor.TriggerEntries')}</h2>
      <ul class="triggers">
        ${this.snapshot.triggers.map((trigger) => html`
          <li>
            <span class="trigger-kind">${trigger.kindLabel}</span>
            <label class="enabled">
              <input
                type="checkbox"
                .checked=${trigger.enabled}
                @change=${(event: Event) => void this.setTriggerEnabled(trigger.id, event)}
              />
              ${t('Editor.TriggerEnabled')}
            </label>
            ${trigger.kind === 'Key'
              ? html`<input
                  data-capture=${'trigger:' + trigger.id}
                  .value=${trigger.binding}
                  placeholder=${t('Editor.BindKey')}
                  @keydown=${(event: KeyboardEvent) => void this.captureTriggerKey(trigger.id, event)}
                  @change=${(event: Event) => void this.setBinding(trigger.id, event)}
                />
                <fluent-button
                  appearance="subtle"
                  @click=${() => this.startKeyCapture('trigger:' + trigger.id)}
                >${t('Add.SetCombination')}</fluent-button>
                <select
                  .value=${trigger.loop}
                  aria-label=${t('Editor.Loop')}
                  @change=${(event: Event) => void this.setTriggerLoop(trigger.id, event)}
                >
                  <option value="Press">${t('Loop.Press')}</option>
                  <option value="Hold">${t('Loop.Hold')}</option>
                  <option value="Toggle">${t('Loop.Toggle')}</option>
                  <option value="Release">${t('Loop.Release')}</option>
                </select>
                <label class="once">
                  <input
                    type="checkbox"
                    .checked=${trigger.once}
                    @change=${(event: Event) => void this.setTriggerOnce(trigger.id, event)}
                  />
                  ${t('Editor.TriggerOnce')}
                </label>`
              : trigger.kind === 'Timer'
                ? html`<div class="timer-fields">
                    <select
                      .value=${trigger.timerMode}
                      aria-label=${t('Editor.Schedule')}
                      @change=${(event: Event) => void this.setTriggerTimer(trigger.id, 'mode', event)}
                    >
                      <option value="Interval">${t('Schedule.Interval')}</option>
                      <option value="Daily">${t('Schedule.Daily')}</option>
                    </select>
                    ${trigger.timerMode === 'Daily'
                      ? html`<input
                          .value=${trigger.time}
                          placeholder=${t('Editor.ScheduleTimeHint')}
                          aria-label=${t('Editor.ScheduleTime')}
                          @change=${(event: Event) => void this.setTriggerTimer(trigger.id, 'time', event)}
                        />`
                      : html`<input
                          type="number"
                          min="1"
                          .value=${trigger.interval}
                          aria-label=${t('Editor.Schedule')}
                          @change=${(event: Event) => void this.setTriggerTimer(trigger.id, 'interval', event)}
                        />
                        <select
                          .value=${trigger.unit}
                          aria-label=${t('Editor.Schedule')}
                          @change=${(event: Event) => void this.setTriggerTimer(trigger.id, 'unit', event)}
                        >
                          <option value="Seconds">${t('Schedule.Unit.Seconds')}</option>
                          <option value="Minutes">${t('Schedule.Unit.Minutes')}</option>
                          <option value="Hours">${t('Schedule.Unit.Hours')}</option>
                        </select>`}
                  </div>`
              : trigger.kind === 'File'
                ? html`<div class="file-fields">
                    <input
                      .value=${trigger.filePath}
                      placeholder=${t('Editor.WatchPathHint')}
                      aria-label=${t('Editor.WatchPath')}
                      @change=${(event: Event) => void this.setTriggerFile(trigger.id, 'path', event)}
                    />
                    <select
                      .value=${trigger.fileChange}
                      aria-label=${t('Editor.WatchChange')}
                      @change=${(event: Event) => void this.setTriggerFile(trigger.id, 'change', event)}
                    >
                      <option value="Any">${t('FileChange.Any')}</option>
                      <option value="Created">${t('FileChange.Created')}</option>
                      <option value="Changed">${t('FileChange.Changed')}</option>
                      <option value="Deleted">${t('FileChange.Deleted')}</option>
                    </select>
                    <input
                      .value=${trigger.fileFilter}
                      placeholder=${t('Editor.WatchFilter')}
                      aria-label=${t('Editor.WatchFilter')}
                      @change=${(event: Event) => void this.setTriggerFile(trigger.id, 'filter', event)}
                    />
                    <label class="once">
                      <input
                        type="checkbox"
                        .checked=${trigger.fileSubfolders}
                        @change=${(event: Event) => void this.setTriggerFile(trigger.id, 'subfolders', event)}
                      />
                      ${t('Editor.WatchSubfolders')}
                    </label>
                  </div>`
              : trigger.kind === 'Process'
                ? html`<div class="process-fields">
                    <input
                      .value=${trigger.processName}
                      placeholder=${t('Editor.ProcessNameHint')}
                      aria-label=${t('Editor.ProcessName')}
                      @change=${(event: Event) => void this.setTriggerProcess(trigger.id, 'name', event)}
                    />
                    <select
                      .value=${trigger.processChange}
                      aria-label=${t('Editor.ProcessWhen')}
                      @change=${(event: Event) => void this.setTriggerProcess(trigger.id, 'change', event)}
                    >
                      <option value="Any">${t('ProcessChange.Any')}</option>
                      <option value="Started">${t('ProcessChange.Started')}</option>
                      <option value="Stopped">${t('ProcessChange.Stopped')}</option>
                    </select>
                  </div>`
              : trigger.kind === 'Window'
                ? html`<div class="window-fields">
                    <input
                      .value=${trigger.windowTitle}
                      placeholder=${t('WindowPart.Title')}
                      aria-label=${t('WindowPart.Title')}
                      @change=${(event: Event) => void this.setTriggerWindow(trigger.id, 'title', event)}
                    />
                    <input
                      .value=${trigger.windowProcess}
                      placeholder=${t('WindowRule.Process')}
                      aria-label=${t('WindowRule.Process')}
                      @change=${(event: Event) => void this.setTriggerWindow(trigger.id, 'process', event)}
                    />
                    <select
                      .value=${trigger.windowHow}
                      aria-label=${t('WindowRule.How')}
                      @change=${(event: Event) => void this.setTriggerWindow(trigger.id, 'how', event)}
                    >
                      <option value="TitleContains">${t('WindowRule.How.titleContains')}</option>
                      <option value="TitleStartsWith">${t('WindowRule.How.titleStartsWith')}</option>
                      <option value="TitleEndsWith">${t('WindowRule.How.titleEndsWith')}</option>
                      <option value="TitleEquals">${t('WindowRule.How.titleEquals')}</option>
                      <option value="TitleRegex">${t('WindowRule.How.titleRegex')}</option>
                      <option value="Process">${t('WindowRule.How.process')}</option>
                      <option value="ProcessEquals">${t('WindowRule.How.processEquals')}</option>
                      <option value="Class">${t('WindowRule.How.class')}</option>
                      <option value="ClassEquals">${t('WindowRule.How.classEquals')}</option>
                      <option value="Handle">${t('WindowRule.How.handle')}</option>
                    </select>
                    <select
                      .value=${trigger.windowChange}
                      aria-label=${t('WindowChange.Any')}
                      @change=${(event: Event) => void this.setTriggerWindow(trigger.id, 'change', event)}
                    >
                      <option value="Appeared">${t('WindowChange.Appeared')}</option>
                      <option value="Disappeared">${t('WindowChange.Disappeared')}</option>
                      <option value="Any">${t('WindowChange.Any')}</option>
                    </select>
                  </div>`
              : trigger.kind === 'Interface'
                ? html`<div class="window-fields">
                    <input
                      .value=${trigger.interfaceTitle}
                      placeholder=${t('WindowPart.Title')}
                      aria-label=${t('WindowPart.Title')}
                      @change=${(event: Event) => void this.setTriggerInterface(trigger.id, 'title', event)}
                    />
                    <input
                      .value=${trigger.interfaceProcess}
                      placeholder=${t('WindowRule.Process')}
                      aria-label=${t('WindowRule.Process')}
                      @change=${(event: Event) => void this.setTriggerInterface(trigger.id, 'process', event)}
                    />
                    <select
                      .value=${trigger.interfaceHow}
                      aria-label=${t('WindowRule.How')}
                      @change=${(event: Event) => void this.setTriggerInterface(trigger.id, 'how', event)}
                    >
                      <option value="TitleContains">${t('WindowRule.How.titleContains')}</option>
                      <option value="TitleStartsWith">${t('WindowRule.How.titleStartsWith')}</option>
                      <option value="TitleEndsWith">${t('WindowRule.How.titleEndsWith')}</option>
                      <option value="TitleEquals">${t('WindowRule.How.titleEquals')}</option>
                      <option value="TitleRegex">${t('WindowRule.How.titleRegex')}</option>
                      <option value="Process">${t('WindowRule.How.process')}</option>
                      <option value="ProcessEquals">${t('WindowRule.How.processEquals')}</option>
                      <option value="Class">${t('WindowRule.How.class')}</option>
                      <option value="ClassEquals">${t('WindowRule.How.classEquals')}</option>
                    </select>
                  </div>`
                : trigger.kind === 'Hotstring'
                  ? html`<input
                      class="hotstring-field"
                      .value=${trigger.hotstringText}
                      placeholder=${t('Editor.HotstringHint')}
                      aria-label=${t('Editor.Hotstring')}
                      @change=${(event: Event) => void this.setTriggerHotstring(trigger.id, event)}
                    />`
                : trigger.kind === 'Color'
                  ? html`<div class="color-fields">
                      <select
                        .value=${trigger.colorMatch}
                        aria-label=${t('Editor.ColorTolerance')}
                        @change=${(event: Event) => void this.setTriggerColor(trigger.id, 'match', event)}
                      >
                        <option value="ColorMatches">${t('ColorMatch.Matches')}</option>
                        <option value="ColorNotMatches">${t('ColorMatch.NotMatches')}</option>
                      </select>
                      <input
                        type="color"
                        .value=${trigger.colorHex}
                        aria-label=${t('Editor.ColorPosition')}
                        @input=${(event: Event) => void this.setTriggerColor(trigger.id, 'hex', event)}
                      />
                      <input
                        type="number"
                        min="0"
                        .value=${trigger.colorTolerance}
                        aria-label=${t('Editor.ColorTolerance')}
                        @change=${(event: Event) => void this.setTriggerColor(trigger.id, 'tolerance', event)}
                      />
                      <label class="once">
                        <input
                          type="checkbox"
                          .checked=${trigger.followCursor}
                          @change=${(event: Event) => void this.setTriggerColor(trigger.id, 'follow', event)}
                        />
                        ${t('Editor.ColorPosition')}
                      </label>
                      ${trigger.followCursor
                        ? nothing
                        : html`<input
                            type="number"
                            .value=${trigger.colorX}
                            aria-label=${t('Editor.ColorPosition')}
                            @change=${(event: Event) => void this.setTriggerColor(trigger.id, 'x', event)}
                          />
                          <input
                            type="number"
                            .value=${trigger.colorY}
                            aria-label=${t('Editor.ColorPosition')}
                            @change=${(event: Event) => void this.setTriggerColor(trigger.id, 'y', event)}
                          />`}
                    </div>`
                  : trigger.kind === 'Idle'
                    ? html`<input
                        class="idle-field"
                        type="number"
                        min="1"
                        .value=${trigger.idleSeconds}
                        aria-label=${t('Editor.IdleSeconds')}
                        @change=${(event: Event) => void this.setTriggerIdle(trigger.id, event)}
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
      <div class="trigger-add">
        <select .value=${this.triggerKind} @change=${this.selectTriggerKind}>
          ${this.snapshot.triggerKinds.map((kind) => html`
            <option value=${kind.value}>${kind.label}</option>
          `)}
        </select>
        <fluent-button appearance="subtle" @click=${this.addTrigger}>
          ${t('Editor.AddTrigger')}
        </fluent-button>
      </div>
      <h2>${t('LocalVariables.Title')}</h2>
      <div class="variables">
        ${this.snapshot.variables.map((variable) => html`
          <div class="variable-row">
            <input
              .value=${variable.name}
              ?readonly=${!variable.isDeclared}
              placeholder=${t('LocalVariables.New')}
              aria-label=${t('LocalVariables.Title')}
              @change=${(event: Event) => void this.patchVariable(variable.name, 'name', event)}
            />
            <select
              .value=${variable.type}
              ?disabled=${!variable.isDeclared}
              aria-label=${t('LocalVariables.Title')}
              @change=${(event: Event) => void this.patchVariable(variable.name, 'type', event)}
            >
              ${this.snapshot!.variableTypes.map((type) => html`
                <option value=${type.value}>${type.label}</option>
              `)}
            </select>
            ${variable.isDeclared
              ? html`<input
                  .value=${variable.initial}
                  placeholder=${t('LocalVariables.InitialPlaceholder')}
                  aria-label=${t('LocalVariables.Initial')}
                  @change=${(event: Event) => void this.patchVariable(variable.name, 'initial', event)}
                />`
              : html`<span class="variable-source">${variable.source}</span>`}
            <input
              .value=${variable.description}
              ?readonly=${!variable.isDeclared}
              aria-label=${t('LocalVariables.Title')}
              @change=${(event: Event) => void this.patchVariable(variable.name, 'description', event)}
            />
            ${variable.isDeclared
              ? html`<button
                  title=${t('LocalVariables.Delete')}
                  aria-label=${t('LocalVariables.Delete')}
                  @click=${() => void this.removeVariable(variable.name)}
                >×</button>`
              : nothing}
          </div>
        `)}
      </div>
      <fluent-button appearance="subtle" @click=${this.addVariable}>
        ${t('LocalVariables.New')}
      </fluent-button>
      <h2>${t('GlobalVariables.Title')}</h2>
      <div class="globals">
        ${this.snapshot.globals.map((global) => html`
          <div class="global-row">
            <input
              .value=${global.name}
              placeholder=${t('GlobalVariables.New')}
              aria-label=${t('GlobalVariables.Title')}
              @change=${(event: Event) => void this.patchGlobal(global.name, 'name', event)}
            />
            <select
              .value=${global.type}
              aria-label=${t('GlobalVariables.Title')}
              @change=${(event: Event) => void this.patchGlobal(global.name, 'type', event)}
            >
              ${this.snapshot!.variableTypes.map((type) => html`
                <option value=${type.value}>${type.label}</option>
              `)}
            </select>
            <input
              .value=${global.default}
              placeholder=${t('GlobalVariables.DefaultPlaceholder')}
              aria-label=${t('GlobalVariables.Definition')}
              @change=${(event: Event) => void this.patchGlobal(global.name, 'default', event)}
            />
            <input
              .value=${global.description}
              aria-label=${t('GlobalVariables.Definition')}
              @change=${(event: Event) => void this.patchGlobal(global.name, 'description', event)}
            />
            <label class="once">
              <input
                type="checkbox"
                .checked=${global.persisted}
                @change=${(event: Event) => void this.patchGlobal(global.name, 'persist', event)}
              />
              ${t('Variable.Persist')}
            </label>
            <button
              title=${t('GlobalVariables.Remove')}
              aria-label=${t('GlobalVariables.Remove')}
              @click=${() => void this.removeGlobal(global.name)}
            >×</button>
          </div>
        `)}
      </div>
      <fluent-button appearance="subtle" @click=${this.addGlobal}>
        ${t('GlobalVariables.New')}
      </fluent-button>
      <h2>${t('Run.Steps')}</h2>
      <div class="toolbar">
        <input
          class="action-search"
          .value=${this.actionSearch}
          placeholder=${t('Main.SearchPlaceholder')}
          aria-label=${t('Main.SearchPlaceholder')}
          @input=${this.searchActions}
        />
        <select .value=${this.actionKey} @change=${this.selectAction}>
          ${this.filteredActions().map((action) => html`
            <option value=${action.key} title=${action.categoryLabel}>${action.name}</option>
          `)}
        </select>
        <fluent-button appearance=${this.recording ? 'accent' : 'neutral'} @click=${() => void this.toggleRecording()}>
          ${this.recording ? t('Editor.StopRecording') : t('Editor.Record')}
        </fluent-button>
        ${this.recording
          ? html`<span class="record-status">${t('Editor.RecordActive').replace('{0}', String(this.captured))}</span>`
          : nothing}
        <fluent-button appearance="subtle" @click=${() => void this.addStep(false)}>
          ${t('Editor.AddStep')}
        </fluent-button>
        ${this.selectedStep()?.acceptsSteps
          ? html`<fluent-button appearance="subtle" @click=${() => void this.addStep(true)}>
              ${t('Editor.AddChildStep')}
            </fluent-button>`
          : nothing}
      </div>
      ${this.snapshot.steps.length === 0
        ? html`<p class="steps">${t('Editor.NoSteps')}</p>`
        : html`<ul class="tree">${this.renderSteps(this.snapshot.steps)}</ul>`}
      ${this.renderInspector()}
      <p class="steps">${t('Editor.GateActions').replace('{0}', String(this.actions.length))}</p>
      <div class="actions">
        <fluent-button
          appearance="subtle"
          title=${t('Palette.Undo')}
          aria-label=${t('Palette.Undo')}
          ?disabled=${!this.snapshot.canUndo}
          @click=${this.undo}
        >${icon('undo')}</fluent-button>
        <fluent-button
          appearance="subtle"
          title=${t('Palette.Redo')}
          aria-label=${t('Palette.Redo')}
          ?disabled=${!this.snapshot.canRedo}
          @click=${this.redo}
        >${icon('redo')}</fluent-button>
        <fluent-button appearance="accent" @click=${this.save}>${t('Common.Save')}</fluent-button>
        <fluent-button appearance="subtle" @click=${this.close}>${t('Common.Close')}</fluent-button>
      </div>
      ${this.valueTarget ? html`
        <div class="value-backdrop" @click=${() => this.closeValueBuilder()}>
          <div class="value-dialog" role="dialog" aria-modal="true" @click=${(event: Event) => event.stopPropagation()}>
            <h2>${t('Editor.ValueBuilder')}</h2>
            <textarea
              class="value-draft"
              .value=${this.valueDraft}
              @input=${(event: Event) => { this.valueDraft = (event.target as HTMLTextAreaElement).value; }}
            ></textarea>
            <input
              class="value-search"
              .value=${this.valueSearch}
              placeholder=${t('Main.SearchPlaceholder')}
              @input=${(event: Event) => { this.valueSearch = (event.target as HTMLInputElement).value; }}
            />
            <div class="value-groups">
              <section>
                <h3>${t('Editor.InsertValue')}</h3>
                <div class="value-buttons">
                  ${this.filteredValueChoices().map((choice) => html`
                    <button @click=${() => this.appendValue('$' + choice.name)}>${choice.name}</button>
                  `)}
                </div>
              </section>
              <section>
                <h3>${t('Editor.Functions')}</h3>
                <div class="value-buttons">
                  ${this.filteredValueFunctions().map((fn) => html`
                    <button title=${fn.description} @click=${() => this.appendValue(fn.name + '(')}>
                      ${fn.signature}
                    </button>
                  `)}
                </div>
              </section>
            </div>
            <div class="value-actions">
              <fluent-button appearance="subtle" @click=${() => this.closeValueBuilder()}>${t('Common.Cancel')}</fluent-button>
              <fluent-button appearance="accent" @click=${() => void this.applyValueBuilder()}>${t('Common.Ok')}</fluent-button>
            </div>
          </div>
        </div>
      ` : nothing}
    `;
  }

  private readonly onKeydown = (event: KeyboardEvent): void => {
    if (!event.ctrlKey || event.altKey) {
      return;
    }

    const typing = event.target instanceof HTMLInputElement
      || event.target instanceof HTMLTextAreaElement
      || event.target instanceof HTMLSelectElement;
    switch (event.key.toLowerCase()) {
      case 's':
        event.preventDefault();
        void this.save();
        break;
      case 'w':
        event.preventDefault();
        if (this.activeKey.length > 0) {
          void this.closeTab(this.activeKey);
        }
        break;
      case 'z':
        if (!typing) {
          event.preventDefault();
          void (event.shiftKey ? this.redo() : this.undo());
        }
        break;
      case 'y':
        if (!typing) {
          event.preventDefault();
          void this.redo();
        }
        break;
      default:
        break;
    }
  };

  private async toggleRecording(): Promise<void> {
    if (this.recording) {
      await this.stopRecording();
    } else {
      await this.startRecording();
    }
  }

  private async startRecording(): Promise<void> {
    try {
      const state = await bridge.value.call<{ recording: boolean; captured: number }>(
        'editor.sessionRecordStart');
      this.recording = state.recording;
      this.captured = state.captured;
      this.error = '';
      window.clearInterval(this.recordTimer);
      if (this.recording) {
        this.recordTimer = window.setInterval(() => void this.pollRecording(), 500);
      }
    } catch (error) {
      this.error = String(error);
    }
  }

  private async pollRecording(): Promise<void> {
    try {
      const state = await bridge.value.call<{
        recording?: boolean;
        captured: number;
        snapshot?: EditorSnapshot;
        steps?: number;
      }>('editor.sessionRecordState');
      this.captured = state.captured;
      if (state.snapshot) {
        this.snapshot = state.snapshot;
        await this.loadTabs();
      }

      if (!state.recording) {
        window.clearInterval(this.recordTimer);
        this.recording = false;
      }
    } catch (error) {
      this.recording = false;
      window.clearInterval(this.recordTimer);
      this.error = String(error);
    }
  }

  private async stopRecording(): Promise<void> {
    window.clearInterval(this.recordTimer);
    try {
      const result = await bridge.value.call<{
        captured: number;
        steps: number;
        snapshot: EditorSnapshot;
      }>('editor.sessionRecordStop');
      this.recording = false;
      this.captured = result.captured;
      this.snapshot = result.snapshot;
      if (result.steps === 0) {
        this.error = t('Editor.RecordEmpty');
      }

      await this.loadTabs();
    } catch (error) {
      this.recording = false;
      this.error = String(error);
    }
  }

  private filteredActions(): EditorAction[] {
    const needle = this.actionSearch.trim().toLocaleLowerCase();
    const visible = this.actions.filter((action) => !action.hidden);
    if (needle.length === 0) {
      return visible;
    }

    return visible.filter((action) =>
      action.name.toLocaleLowerCase().includes(needle)
      || action.key.toLocaleLowerCase().includes(needle)
      || action.categoryLabel.toLocaleLowerCase().includes(needle));
  }

  private searchActions(event: Event): void {
    this.actionSearch = (event.target as HTMLInputElement).value;
    const visible = this.filteredActions();
    if (!visible.some((action) => action.key === this.actionKey)) {
      this.actionKey = visible[0]?.key ?? '';
    }
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

  private selectOpen(event: Event): void {
    this.openName = (event.target as HTMLSelectElement).value;
  }

  private renderRecordFields(
    kind: 'parameter' | 'result',
    index: number,
    fields: { name: string; type: string }[],
  ): TemplateResult {
    return html`
      <div class="record-field-list">
        ${fields.map((field, fieldIndex) => html`
          <div class="record-field-row">
            <input
              .value=${field.name}
              placeholder=${t('MacroInterface.FieldName')}
              @change=${(event: Event) => kind === 'parameter'
                ? void this.patchCallableParameterField(index, fieldIndex, 'name', event)
                : void this.patchCallableResultField(index, fieldIndex, 'name', event)}
            />
            <select
              .value=${field.type}
              @change=${(event: Event) => kind === 'parameter'
                ? void this.patchCallableParameterField(index, fieldIndex, 'type', event)
                : void this.patchCallableResultField(index, fieldIndex, 'type', event)}
            >
              ${this.snapshot!.variableTypes.map((type) => html`
                <option value=${type.value}>${type.label}</option>
              `)}
            </select>
            <button @click=${() => kind === 'parameter'
              ? void this.removeCallableParameterField(index, fieldIndex)
              : void this.removeCallableResultField(index, fieldIndex)}>×</button>
          </div>
        `)}
        <fluent-button
          appearance="subtle"
          @click=${() => kind === 'parameter'
            ? void this.addCallableParameterField(index)
            : void this.addCallableResultField(index)}
        >${t('MacroInterface.AddField')}</fluent-button>
      </div>
    `;
  }

  private async addCallableParameterField(index: number): Promise<void> {
    await this.declarationCommand('editor.sessionParameterFieldAdd', { index });
  }

  private async patchCallableParameterField(
    index: number,
    fieldIndex: number,
    field: string,
    event: Event,
  ): Promise<void> {
    await this.declarationCommand('editor.sessionParameterFieldPatch', {
      index,
      fieldIndex,
      field,
      value: (event.target as HTMLInputElement | HTMLSelectElement).value,
    });
  }

  private async removeCallableParameterField(index: number, fieldIndex: number): Promise<void> {
    await this.declarationCommand('editor.sessionParameterFieldRemove', { index, fieldIndex });
  }

  private async addCallableResultField(index: number): Promise<void> {
    await this.declarationCommand('editor.sessionResultFieldAdd', { index });
  }

  private async patchCallableResultField(
    index: number,
    fieldIndex: number,
    field: string,
    event: Event,
  ): Promise<void> {
    await this.declarationCommand('editor.sessionResultFieldPatch', {
      index,
      fieldIndex,
      field,
      value: (event.target as HTMLInputElement | HTMLSelectElement).value,
    });
  }

  private async removeCallableResultField(index: number, fieldIndex: number): Promise<void> {
    await this.declarationCommand('editor.sessionResultFieldRemove', { index, fieldIndex });
  }

  private async addCallableParameter(): Promise<void> {
    await this.declarationCommand('editor.sessionParameterDeclAdd', {});
  }

  private async patchCallableParameter(index: number, field: string, event: Event): Promise<void> {
    const target = event.target as HTMLInputElement | HTMLSelectElement;
    await this.declarationCommand('editor.sessionParameterDeclPatch', {
      index,
      field,
      value: target instanceof HTMLInputElement && target.type === 'checkbox'
        ? String(target.checked)
        : target.value,
    });
  }

  private async removeCallableParameter(index: number): Promise<void> {
    await this.declarationCommand('editor.sessionParameterDeclRemove', { index });
  }

  private async addCallableResult(): Promise<void> {
    await this.declarationCommand('editor.sessionResultDeclAdd', {});
  }

  private async patchCallableResult(index: number, field: string, event: Event): Promise<void> {
    await this.declarationCommand('editor.sessionResultDeclPatch', {
      index,
      field,
      value: (event.target as HTMLInputElement | HTMLSelectElement).value,
    });
  }

  private async removeCallableResult(index: number): Promise<void> {
    await this.declarationCommand('editor.sessionResultDeclRemove', { index });
  }

  private async declarationCommand(
    method: string,
    extra: Record<string, unknown>,
  ): Promise<void> {
    try {
      this.snapshot = await bridge.value.call<EditorSnapshot>(method, extra);
      await this.loadTabs();
    } catch (error) {
      this.error = String(error);
    }
  }

  private async setMacroSetting(field: string, event: Event): Promise<void> {
    const target = event.target as HTMLInputElement;
    const value = target.type === 'checkbox' ? String(target.checked) : target.value;
    try {
      this.snapshot = await bridge.value.call<EditorSnapshot>('editor.sessionMacroSetting', {
        field,
        value,
      });
      await this.loadTabs();
    } catch (error) {
      this.error = String(error);
    }
  }

  private async openSubmacro(name: string): Promise<void> {
    try {
      const opened = await bridge.value.call<EditorSnapshot>('editor.sessionSubOpen', { name });
      this.snapshot = opened;
      this.selectedId = '';
      await this.loadTabs();
    } catch (error) {
      this.error = String(error);
    }
  }

  private async newMacro(): Promise<void> {
    try {
      const opened = await bridge.value.call<EditorSnapshot>('editor.sessionNew');
      this.snapshot = opened;
      this.selectedId = '';
      await this.loadTabs();
    } catch (error) {
      this.error = String(error);
    }
  }

  private async openSelected(): Promise<void> {
    if (this.openName.length > 0) {
      await this.open(this.openName);
    }
  }

  private async loadTabs(): Promise<void> {
    try {
      const response = await bridge.value.call<{ activeKey: string | null; tabs: EditorTab[] }>(
        'editor.sessionTabs');
      this.tabs = response.tabs;
      this.activeKey = response.activeKey ?? '';
    } catch (error) {
      this.error = String(error);
    }
  }

  private async closeTab(key: string): Promise<void> {
    try {
      const next = await bridge.value.call<EditorSnapshot | null>('editor.sessionClose', { key });
      this.snapshot = next;
      this.selectedId = next?.steps[0]?.id ?? '';
      await this.loadTabs();
    } catch (error) {
      this.error = String(error);
    }
  }

  private async activateTab(key: string): Promise<void> {
    try {
      const activated = await bridge.value.call<EditorSnapshot>('editor.sessionActivate', { key });
      this.snapshot = activated;
      this.selectedId = activated.steps[0]?.id ?? '';
      await this.loadTabs();
    } catch (error) {
      this.error = String(error);
    }
  }

  private selectAction(event: Event): void {
    this.actionKey = (event.target as HTMLSelectElement).value;
  }

  private async addStep(inside: boolean): Promise<void> {
    if (this.snapshot === null || this.actionKey.length === 0) {
      return;
    }

    try {
      this.snapshot = await bridge.value.call<EditorSnapshot>('editor.sessionStep', {
        type: this.actionKey,
        parentId: inside ? this.selectedId : undefined,
      });
      await this.loadTabs();
    } catch (error) {
      this.error = String(error);
    }
  }

  private selectTriggerKind(event: Event): void {
    this.triggerKind = (event.target as HTMLSelectElement).value;
  }

  private async addTrigger(): Promise<void> {
    try {
      this.snapshot = await bridge.value.call<EditorSnapshot>('editor.sessionTriggerAdd',
        { kind: this.triggerKind });
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
    await this.setBindingValue(triggerId, (event.target as HTMLInputElement).value);
  }

  private async captureTriggerKey(triggerId: string, event: KeyboardEvent): Promise<void> {
    const capture = 'trigger:' + triggerId;
    if (this.capturingKey !== capture) {
      return;
    }

    const binding = this.keyName(event);
    if (binding === null) {
      return;
    }

    event.preventDefault();
    event.stopPropagation();
    this.capturingKey = '';
    await this.setBindingValue(triggerId, binding);
  }

  private async setBindingValue(triggerId: string, binding: string): Promise<void> {
    try {
      this.snapshot = await bridge.value.call<EditorSnapshot>('editor.sessionTriggerBinding', {
        triggerId,
        binding,
      });
    } catch (error) {
      this.error = String(error);
    }
  }

  private async setTriggerInterface(triggerId: string, field: string, event: Event): Promise<void> {
    try {
      this.snapshot = await bridge.value.call<EditorSnapshot>('editor.sessionTriggerInterface', {
        triggerId,
        field,
        value: (event.target as HTMLInputElement | HTMLSelectElement).value,
      });
    } catch (error) {
      this.error = String(error);
    }
  }

  private async setTriggerWindow(triggerId: string, field: string, event: Event): Promise<void> {
    try {
      this.snapshot = await bridge.value.call<EditorSnapshot>('editor.sessionTriggerWindow', {
        triggerId,
        field,
        value: (event.target as HTMLInputElement | HTMLSelectElement).value,
      });
    } catch (error) {
      this.error = String(error);
    }
  }

  private async setTriggerColor(triggerId: string, field: string, event: Event): Promise<void> {
    const target = event.target as HTMLInputElement | HTMLSelectElement;
    const value = target instanceof HTMLInputElement && target.type === 'checkbox'
      ? String(target.checked)
      : target.value;
    try {
      this.snapshot = await bridge.value.call<EditorSnapshot>('editor.sessionTriggerColor', {
        triggerId,
        field,
        value,
      });
    } catch (error) {
      this.error = String(error);
    }
  }

  private async setTriggerIdle(triggerId: string, event: Event): Promise<void> {
    try {
      this.snapshot = await bridge.value.call<EditorSnapshot>('editor.sessionTriggerIdle', {
        triggerId,
        value: (event.target as HTMLInputElement).value,
      });
    } catch (error) {
      this.error = String(error);
    }
  }

  private async setTriggerProcess(triggerId: string, field: string, event: Event): Promise<void> {
    try {
      this.snapshot = await bridge.value.call<EditorSnapshot>('editor.sessionTriggerProcess', {
        triggerId,
        field,
        value: (event.target as HTMLInputElement | HTMLSelectElement).value,
      });
    } catch (error) {
      this.error = String(error);
    }
  }

  private async setTriggerHotstring(triggerId: string, event: Event): Promise<void> {
    try {
      this.snapshot = await bridge.value.call<EditorSnapshot>('editor.sessionTriggerHotstring', {
        triggerId,
        value: (event.target as HTMLInputElement).value,
      });
    } catch (error) {
      this.error = String(error);
    }
  }

  private async setTriggerFile(triggerId: string, field: string, event: Event): Promise<void> {
    const target = event.target as HTMLInputElement | HTMLSelectElement;
    const value = target instanceof HTMLInputElement && target.type === 'checkbox'
      ? String(target.checked)
      : target.value;
    try {
      this.snapshot = await bridge.value.call<EditorSnapshot>('editor.sessionTriggerFile', {
        triggerId,
        field,
        value,
      });
    } catch (error) {
      this.error = String(error);
    }
  }

  private async setTriggerTimer(triggerId: string, field: string, event: Event): Promise<void> {
    try {
      this.snapshot = await bridge.value.call<EditorSnapshot>('editor.sessionTriggerTimer', {
        triggerId,
        field,
        value: (event.target as HTMLInputElement | HTMLSelectElement).value,
      });
    } catch (error) {
      this.error = String(error);
    }
  }

  private async setTriggerEnabled(triggerId: string, event: Event): Promise<void> {
    try {
      this.snapshot = await bridge.value.call<EditorSnapshot>('editor.sessionTriggerEnabled', {
        triggerId,
        enabled: (event.target as HTMLInputElement).checked,
      });
      await this.loadTabs();
    } catch (error) {
      this.error = String(error);
    }
  }

  private async setTriggerLoop(triggerId: string, event: Event): Promise<void> {
    try {
      this.snapshot = await bridge.value.call<EditorSnapshot>('editor.sessionTriggerLoop', {
        triggerId,
        loop: (event.target as HTMLSelectElement).value,
      });
    } catch (error) {
      this.error = String(error);
    }
  }

  private async setTriggerOnce(triggerId: string, event: Event): Promise<void> {
    try {
      this.snapshot = await bridge.value.call<EditorSnapshot>('editor.sessionTriggerOnce', {
        triggerId,
        once: (event.target as HTMLInputElement).checked,
      });
    } catch (error) {
      this.error = String(error);
    }
  }

  private async addGlobal(): Promise<void> {
    try {
      this.snapshot = await bridge.value.call<EditorSnapshot>('editor.sessionGlobalAdd');
    } catch (error) {
      this.error = String(error);
    }
  }

  private async patchGlobal(originalName: string, field: string, event: Event): Promise<void> {
    const target = event.target as HTMLInputElement | HTMLSelectElement;
    const value = target instanceof HTMLInputElement && target.type === 'checkbox'
      ? String(target.checked)
      : target.value;
    try {
      this.snapshot = await bridge.value.call<EditorSnapshot>('editor.sessionGlobalPatch', {
        originalName,
        field,
        value,
      });
    } catch (error) {
      this.error = String(error);
    }
  }

  private async removeGlobal(name: string): Promise<void> {
    try {
      this.snapshot = await bridge.value.call<EditorSnapshot>('editor.sessionGlobalRemove', { name });
    } catch (error) {
      this.error = String(error);
    }
  }

  private async addVariable(): Promise<void> {
    try {
      this.snapshot = await bridge.value.call<EditorSnapshot>('editor.sessionVariableAdd');
    } catch (error) {
      this.error = String(error);
    }
  }

  private async patchVariable(originalName: string, field: string, event: Event): Promise<void> {
    try {
      this.snapshot = await bridge.value.call<EditorSnapshot>('editor.sessionVariablePatch', {
        originalName,
        field,
        value: (event.target as HTMLInputElement | HTMLSelectElement).value,
      });
    } catch (error) {
      this.error = String(error);
    }
  }

  private async removeVariable(name: string): Promise<void> {
    try {
      this.snapshot = await bridge.value.call<EditorSnapshot>('editor.sessionVariableRemove', { name });
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

  private async duplicateStep(stepId: string): Promise<void> {
    try {
      this.snapshot = await bridge.value.call<EditorSnapshot>('editor.sessionStepDuplicate', { stepId });
      await this.loadTabs();
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
    await this.patchValue(stepId, name, value);
  }

  private startKeyCapture(capture: string): void {
    this.capturingKey = capture;
    void this.updateComplete.then(() => {
      this.renderRoot
        .querySelector<HTMLInputElement>(`input[data-capture="${CSS.escape(capture)}"]`)
        ?.focus();
    });
  }

  private async captureKey(
    step: EditorStep,
    parameter: EditorParameter,
    event: KeyboardEvent,
  ): Promise<void> {
    const capture = step.id + ':' + parameter.name;
    if (this.capturingKey !== capture) {
      return;
    }

    const value = this.keyName(event);
    if (value === null) {
      return;
    }

    event.preventDefault();
    event.stopPropagation();
    this.capturingKey = '';
    await this.patchValue(step.id, parameter.name, value);
  }

  private keyName(event: KeyboardEvent): string | null {
    if (['Control', 'Shift', 'Alt', 'Meta'].includes(event.key)) {
      return null;
    }

    let key = event.key === ' ' ? 'Space' : event.key;
    if (key.length === 1) {
      key = key.toUpperCase();
    }

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
    parts.push(key);
    return parts.join('+');
  }

  private async pickImage(step: EditorStep, parameter: EditorParameter): Promise<void> {
    try {
      const picked = await bridge.value.call<{ path: string | null }>('dialog.pickImage');
      if (picked.path) {
        await this.patchValue(step.id, parameter.name, picked.path);
      }
    } catch (error) {
      this.error = String(error);
    }
  }

  private async patchStepMeta(step: EditorStep, field: string, event: Event): Promise<void> {
    try {
      this.snapshot = await bridge.value.call<EditorSnapshot>('editor.sessionStepMeta', {
        stepId: step.id,
        field,
        value: (event.target as HTMLInputElement | HTMLSelectElement).value,
      });
      await this.loadTabs();
    } catch (error) {
      this.error = String(error);
    }
  }

  private async setStepEnabled(stepId: string, event: Event): Promise<void> {
    try {
      this.snapshot = await bridge.value.call<EditorSnapshot>('editor.sessionStepEnabled', {
        stepId,
        enabled: (event.target as HTMLInputElement).checked,
      });
      await this.loadTabs();
    } catch (error) {
      this.error = String(error);
    }
  }

  private async addParameterRow(step: EditorStep, parameter: EditorParameter): Promise<void> {
    try {
      this.snapshot = await bridge.value.call<EditorSnapshot>('editor.sessionParameterRowAdd', {
        stepId: step.id,
        name: parameter.name,
      });
      await this.loadTabs();
    } catch (error) {
      this.error = String(error);
    }
  }

  private async removeParameterRow(
    step: EditorStep,
    parameter: EditorParameter,
    index: number,
  ): Promise<void> {
    try {
      this.snapshot = await bridge.value.call<EditorSnapshot>('editor.sessionParameterRowRemove', {
        stepId: step.id,
        name: parameter.name,
        index,
      });
      await this.loadTabs();
    } catch (error) {
      this.error = String(error);
    }
  }

  private async patchRowCell(
    step: EditorStep,
    parameter: EditorParameter,
    index: number,
    column: string,
    event: Event,
  ): Promise<void> {
    try {
      this.snapshot = await bridge.value.call<EditorSnapshot>('editor.sessionParameterRowCell', {
        stepId: step.id,
        name: parameter.name,
        index,
        column,
        value: (event.target as HTMLInputElement).value,
      });
      await this.loadTabs();
    } catch (error) {
      this.error = String(error);
    }
  }

  private async patchValue(stepId: string, name: string, value: string): Promise<void> {
    try {
      this.snapshot = await bridge.value.call<EditorSnapshot>('editor.sessionParameter', {
        stepId,
        name,
        value,
      });
      await this.loadTabs();
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
            <input
              type="checkbox"
              .checked=${step.enabled}
              title=${t('Editor.StepEnabled')}
              aria-label=${t('Editor.StepEnabled')}
              @click=${(event: Event) => event.stopPropagation()}
              @change=${(event: Event) => void this.setStepEnabled(step.id, event)}
            />
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
                title=${t('Editor.DuplicateStep')}
                aria-label=${t('Editor.DuplicateStep')}
                @click=${(event: Event) => { event.stopPropagation(); void this.duplicateStep(step.id); }}
              >⧉</button>
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

  private async addConditionRow(
    step: EditorStep,
    parameter: EditorParameter,
    path: number[] = [],
  ): Promise<void> {
    await this.conditionCommand('editor.sessionConditionRowAdd', step, parameter, { path });
  }

  private async addConditionGroup(
    step: EditorStep,
    parameter: EditorParameter,
    path: number[] = [],
  ): Promise<void> {
    await this.conditionCommand('editor.sessionConditionGroupAdd', step, parameter, { path });
  }

  private async removeConditionNode(
    step: EditorStep,
    parameter: EditorParameter,
    path: number[],
  ): Promise<void> {
    await this.conditionCommand('editor.sessionConditionRowRemove', step, parameter, { path });
  }

  private async setConditionRow(
    step: EditorStep,
    parameter: EditorParameter,
    path: number[],
    field: string,
    event: Event,
  ): Promise<void> {
    await this.conditionCommand('editor.sessionConditionRow', step, parameter, {
      path,
      field,
      value: (event.target as HTMLInputElement | HTMLSelectElement).value,
    });
  }

  private async setConditionJoin(
    step: EditorStep,
    parameter: EditorParameter,
    event: Event,
    path: number[],
  ): Promise<void> {
    await this.conditionCommand('editor.sessionConditionJoin', step, parameter, {
      path,
      join: (event.target as HTMLSelectElement).value,
    });
  }

  private async conditionCommand(
    method: string,
    step: EditorStep,
    parameter: EditorParameter,
    extra: Record<string, unknown>,
  ): Promise<void> {
    try {
      this.snapshot = await bridge.value.call<EditorSnapshot>(method, {
        stepId: step.id,
        name: parameter.name,
        ...extra,
      });
      await this.loadTabs();
    } catch (error) {
      this.error = String(error);
    }
  }

  private openValueBuilder(step: EditorStep, parameter: EditorParameter): void {
    this.valueTarget = { step, parameter };
    this.valueDraft = parameter.value;
    this.valueSearch = '';
  }

  private closeValueBuilder(): void {
    this.valueTarget = null;
    this.valueDraft = '';
    this.valueSearch = '';
  }

  private appendValue(token: string): void {
    this.valueDraft = this.valueDraft.trim().length > 0
      ? this.valueDraft + ' ' + token
      : token;
  }

  private filteredValueChoices(): EditorValueChoice[] {
    const needle = this.valueSearch.trim().toLocaleLowerCase();
    return this.snapshot!.valueChoices.filter((choice) =>
      needle.length === 0
      || choice.name.toLocaleLowerCase().includes(needle)
      || choice.source.toLocaleLowerCase().includes(needle));
  }

  private filteredValueFunctions(): EditorValueFunction[] {
    const needle = this.valueSearch.trim().toLocaleLowerCase();
    return this.snapshot!.valueFunctions.filter((fn) =>
      needle.length === 0
      || fn.name.toLocaleLowerCase().includes(needle)
      || fn.signature.toLocaleLowerCase().includes(needle)
      || fn.group.toLocaleLowerCase().includes(needle));
  }

  private async applyValueBuilder(): Promise<void> {
    const target = this.valueTarget;
    if (!target) {
      return;
    }

    const value = this.valueDraft;
    this.closeValueBuilder();
    await this.patchValue(target.step.id, target.parameter.name, value);
  }

  private renderValuePicker(step: EditorStep, parameter: EditorParameter): TemplateResult {
    return html`
      <select
        class="value-picker"
        aria-label=${t('Editor.InsertValue')}
        @change=${(event: Event) => void this.insertValue(step, parameter, event)}
      >
        <option value="">${t('Editor.InsertValue')}</option>
        ${this.snapshot!.valueChoices.map((choice) => html`
          <option value=${choice.name} title=${choice.source}>${choice.name}</option>
        `)}
      </select>
      <fluent-button
        appearance="subtle"
        @click=${() => this.openValueBuilder(step, parameter)}
      >${t('Editor.ValueBuilder')}</fluent-button>
    `;
  }

  private async insertValue(step: EditorStep, parameter: EditorParameter, event: Event): Promise<void> {
    const name = (event.target as HTMLSelectElement).value;
    if (name.length === 0) {
      return;
    }

    const value = parameter.value.trim().length > 0
      ? parameter.value + ' $' + name
      : '$' + name;
    (event.target as HTMLSelectElement).value = '';
    await this.patchValue(step.id, parameter.name, value);
  }

  private renderConditionGroup(
    step: EditorStep,
    parameter: EditorParameter,
    join: string,
    nodes: EditorConditionNode[],
    path: number[],
  ): TemplateResult {
    return html`
      <div class="condition-group">
        <div class="condition-join">
          <select
            .value=${join}
            aria-label=${t('Editor.InsertValue')}
            @change=${(event: Event) => void this.setConditionJoin(step, parameter, event, path)}
          >
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
                <button aria-label=${t('Editor.RemoveRow')} @click=${() =>
                  void this.removeConditionNode(step, parameter, [...path, index])}>×</button>
              </div>
              ${this.renderConditionGroup(step, parameter, node.join, node.children,
                [...path, index])}
            </div>
          `)}
        <div class="condition-add">
          <fluent-button appearance="subtle" @click=${() =>
            void this.addConditionRow(step, parameter, path)}>${t('Editor.AddRow')}</fluent-button>
          <fluent-button appearance="subtle" @click=${() =>
            void this.addConditionGroup(step, parameter, path)}>${t('Editor.AddGroup')}</fluent-button>
        </div>
      </div>
    `;
  }

  private renderConditionRow(
    step: EditorStep,
    parameter: EditorParameter,
    row: EditorConditionNode,
    path: number[],
  ): TemplateResult {
    return html`
      <div class="condition-row">
        <input .value=${row.left} @change=${(event: Event) =>
          void this.setConditionRow(step, parameter, path, 'left', event)} />
        <select .value=${row.compare} @change=${(event: Event) =>
          void this.setConditionRow(step, parameter, path, 'compare', event)}>
          ${this.snapshot!.conditionCompares.map((compare) => html`
            <option value=${compare.value}>${compare.label}</option>
          `)}
        </select>
        <input .value=${row.right} @change=${(event: Event) =>
          void this.setConditionRow(step, parameter, path, 'right', event)} />
        <button aria-label=${t('Editor.RemoveRow')} @click=${() =>
          void this.removeConditionNode(step, parameter, path)}>×</button>
      </div>
    `;
  }

  private renderParameter(step: EditorStep, parameter: EditorParameter): TemplateResult {
    const change = (event: Event) => void this.patchParameter(step.id, parameter.name, event);
    if (parameter.columns.length > 0) {
      return html`
        <div class="param row-param">
          <label>${parameter.label}</label>
          <table class="row-list">
            <thead>
              <tr>
                ${parameter.columns.map((column) => html`<th>${column.name}</th>`)}
                <th></th>
              </tr>
            </thead>
            <tbody>
              ${parameter.rows.map((row, index) => html`
                <tr>
                  ${parameter.columns.map((column, cell) => html`
                    <td>
                      <input
                        .value=${row.cells[cell] ?? ''}
                        aria-label=${column.name}
                        @change=${(event: Event) => void this.patchRowCell(
                          step, parameter, index, column.name, event)}
                      />
                    </td>
                  `)}
                  <td>
                    <button
                      title=${t('Editor.RemoveRow')}
                      aria-label=${t('Editor.RemoveRow')}
                      @click=${() => void this.removeParameterRow(step, parameter, index)}
                    >×</button>
                  </td>
                </tr>
              `)}
            </tbody>
          </table>
          <fluent-button appearance="subtle" @click=${() => void this.addParameterRow(step, parameter)}>
            ${t('Editor.AddRow')}
          </fluent-button>
        </div>
      `;
    }

    if (parameter.kind === 'Key') {
      const capture = step.id + ':' + parameter.name;
      const capturing = this.capturingKey === capture;
      return html`
        <div class="param">
          <label>${parameter.label}</label>
          <div class="key-row">
            <input
              data-capture=${capture}
              .value=${parameter.value}
              placeholder=${capturing ? t('Add.PressKey') : ''}
              @keydown=${(event: KeyboardEvent) => void this.captureKey(step, parameter, event)}
              @change=${change}
            />
            <fluent-button appearance="subtle" @click=${() => this.startKeyCapture(capture)}>
              ${t('Add.SetCombination')}
            </fluent-button>
          </div>
        </div>
      `;
    }

    if (parameter.kind === 'Image') {
      return html`
        <div class="param">
          <label>${parameter.label}</label>
          <div class="file-row">
            <input .value=${parameter.value} @change=${change} />
            <fluent-button appearance="subtle" @click=${() => void this.pickImage(step, parameter)}>
              ${t('Editor.Browse')}
            </fluent-button>
          </div>
        </div>
      `;
    }

    if (parameter.kind === 'Color') {
      const color = /^#[0-9a-f]{6}$/i.test(parameter.value) ? parameter.value : '#000000';
      return html`
        <div class="param">
          <label>${parameter.label}</label>
          <div class="color-row">
            <input type="color" .value=${color} @input=${change} />
            <input .value=${parameter.value} @change=${change} />
          </div>
        </div>
      `;
    }

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

    if (parameter.kind === 'Condition' && parameter.condition?.editable) {
      return html`
        <div class="param condition-param">
          <label>${parameter.label}</label>
          ${this.renderConditionGroup(step, parameter, parameter.condition.join,
            parameter.condition.nodes, [])}
        </div>
      `;
    }

    if (parameter.kind === 'Condition') {
      return html`
        <div class="param">
          <label>${parameter.label}</label>
          <textarea class="condition-field" .value=${parameter.value} @change=${change}></textarea>
          ${this.renderValuePicker(step, parameter)}
        </div>
      `;
    }

    if (parameter.kind === 'MultilineText') {
      return html`
        <div class="param">
          <label>${parameter.label}</label>
          <textarea .value=${parameter.value} @change=${change}></textarea>
          ${this.renderValuePicker(step, parameter)}
        </div>
      `;
    }

    return html`
      <div class="param">
        <label>${parameter.label}</label>
        <input .value=${parameter.value} @change=${change} />
        ${this.renderValuePicker(step, parameter)}
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
        <div class="step-meta">
          <label>
            ${t('Add.StepComment')}
            <input
              .value=${step.comment}
              @change=${(event: Event) => void this.patchStepMeta(step, 'comment', event)}
            />
          </label>
          <label>
            ${t('Add.StepTimeout')}
            <input
              type="number"
              min="0"
              .value=${step.timeoutMs}
              @change=${(event: Event) => void this.patchStepMeta(step, 'timeoutMs', event)}
            />
          </label>
          <label>
            ${t('Add.StepRetry')}
            <input
              type="number"
              min="0"
              .value=${step.retryCount}
              @change=${(event: Event) => void this.patchStepMeta(step, 'retryCount', event)}
            />
          </label>
          <label>
            ${t('Add.StepRetryDelay')}
            <input
              type="number"
              min="0"
              .value=${step.retryDelayMs}
              @change=${(event: Event) => void this.patchStepMeta(step, 'retryDelayMs', event)}
            />
          </label>
          <label>
            ${t('Add.StepOnError')}
            <select
              .value=${step.onError}
              @change=${(event: Event) => void this.patchStepMeta(step, 'onError', event)}
            >
              <option value="Stop">${t('Add.OnError.Stop')}</option>
              <option value="Continue">${t('Add.OnError.Continue')}</option>
              <option value="NextIteration">${t('Add.OnError.NextIteration')}</option>
              <option value="AskUser">${t('Add.OnError.AskUser')}</option>
            </select>
          </label>
        </div>
        ${step.parameters.map((parameter) => this.renderParameter(step, parameter))}
      </div>
    `;
  }

  private projectChanged(state: ProjectState | null): void {
    this.macros = state?.macros.map((macro) => macro.name) ?? [];
    if (this.openName.length === 0) {
      this.openName = this.macros[0] ?? '';
    }
    this.openFirst(state);
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
      await this.loadTabs();
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

  private editGroup(event: Event): void {
    this.snapshot = { ...this.snapshot!, group: (event.target as HTMLInputElement).value };
  }

  private async undo(): Promise<void> {
    try {
      this.snapshot = await bridge.value.call<EditorSnapshot>('editor.sessionUndo');
      await this.loadTabs();
    } catch (error) {
      this.error = String(error);
    }
  }

  private async redo(): Promise<void> {
    try {
      this.snapshot = await bridge.value.call<EditorSnapshot>('editor.sessionRedo');
      await this.loadTabs();
    } catch (error) {
      this.error = String(error);
    }
  }

  private async save(): Promise<void> {
    if (this.snapshot === null) {
      return;
    }

    try {
      this.snapshot = await bridge.value.call<EditorSnapshot>('editor.sessionPatch', {
        name: this.snapshot.name,
        description: this.snapshot.description,
        group: this.snapshot.group,
      });
      this.snapshot = await bridge.value.call<EditorSnapshot>('editor.sessionSave');
      await this.loadTabs();
      this.error = '';
    } catch (error) {
      this.error = String(error);
    }
  }

  private async close(): Promise<void> {
    if (this.activeKey.length > 0) {
      await this.closeTab(this.activeKey);
      return;
    }

    this.snapshot = null;
    await this.loadTabs();
  }
}

declare global {
  interface HTMLElementTagNameMap {
    'wg-editor-gate': WgEditorGate;
  }
}
