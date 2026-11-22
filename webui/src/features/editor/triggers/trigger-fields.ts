import { html, nothing, type TemplateResult } from 'lit';
import type { EditorTrigger } from '../../../shared/bridge/protocol.js';
import { t } from '../../../shared/strings/strings.js';
import { editor } from '../editor-store.js';
import type { TriggerFieldHost } from './trigger-field-host.js';
import { renderIdleFields } from './trigger-idle.js';

export function renderTriggerFields(host: TriggerFieldHost, trigger: EditorTrigger): TemplateResult {
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
                if (host.capturingTrigger !== trigger.id) {
                  return;
                }

                if (['Control', 'Shift', 'Alt', 'Meta'].includes(event.key)) {
                  return;
                }

                event.preventDefault();
                const key = host.keyName(event);
                host.capturingTrigger = '';
                void editor.triggers.setTriggerBinding(trigger.id, key);
              }}
              @change=${(event: Event) => void editor.triggers.setTriggerBinding(
                trigger.id, (event.target as HTMLInputElement).value)}
            />
            <button class="link-button" type="button"
              @click=${() => host.startTriggerCapture(trigger.id)}>
              ${t('Add.SetCombination')}
            </button>
          </div>
        </div>
      `);
    } else if (trigger.kind === 'Timer') {
      fields.push(html`
        <label class="field">
          <span>${t('Editor.Schedule')}</span>
          <select .value=${trigger.timerMode}
            @change=${(event: Event) => void editor.triggers.setTriggerTimer(trigger.id, 'mode',
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
                @change=${(event: Event) => void editor.triggers.setTriggerTimer(trigger.id, 'time',
                  (event.target as HTMLInputElement).value)} />
            </label>
          `
          : html`
            <div class="two">
              <label class="field">
                <span>${t('Editor.Interval')}</span>
                <input type="number" min="1" .value=${trigger.interval}
                  @change=${(event: Event) => void editor.triggers.setTriggerTimer(trigger.id, 'interval',
                    (event.target as HTMLInputElement).value)} />
              </label>
              <label class="field">
                <span>${t('Editor.Schedule')}</span>
                <select .value=${trigger.unit}
                  @change=${(event: Event) => void editor.triggers.setTriggerTimer(trigger.id, 'unit',
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
            @change=${(event: Event) => void editor.triggers.setTriggerFile(trigger.id, 'path',
              (event.target as HTMLInputElement).value)} />
        </label>
        <div class="two">
          <label class="field">
            <span>${t('Editor.WatchChange')}</span>
            <select .value=${trigger.fileChange}
              @change=${(event: Event) => void editor.triggers.setTriggerFile(trigger.id, 'change',
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
              @change=${(event: Event) => void editor.triggers.setTriggerFile(trigger.id, 'filter',
                (event.target as HTMLInputElement).value)} />
          </label>
        </div>
        <label class="inline">
          <input type="checkbox" .checked=${trigger.fileSubfolders}
            @change=${(event: Event) => void editor.triggers.setTriggerFile(trigger.id, 'subfolders',
              String((event.target as HTMLInputElement).checked))} />
          <span>${t('Editor.WatchSubfolders')}</span>
        </label>
      `);
    } else if (trigger.kind === 'Process') {
      fields.push(html`
        <label class="field">
          <span>${t('Editor.ProcessName')}</span>
          <input .value=${trigger.processName} placeholder=${t('Editor.ProcessNameHint')}
            @change=${(event: Event) => void editor.triggers.setTriggerProcess(trigger.id, 'name',
              (event.target as HTMLInputElement).value)} />
        </label>
        <label class="field">
          <span>${t('Editor.ProcessWhen')}</span>
          <select .value=${trigger.processChange}
            @change=${(event: Event) => void editor.triggers.setTriggerProcess(trigger.id, 'change',
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
              ? editor.triggers.setTriggerWindow(trigger.id, 'title', (event.target as HTMLInputElement).value)
              : editor.triggers.setTriggerInterface(trigger.id, 'title',
                (event.target as HTMLInputElement).value))} />
        </label>
        <label class="field">
          <span>${t('WindowRule.Process')}</span>
          <input .value=${isWindow ? trigger.windowProcess : trigger.interfaceProcess}
            @change=${(event: Event) => void (isWindow
              ? editor.triggers.setTriggerWindow(trigger.id, 'process',
                (event.target as HTMLInputElement).value)
              : editor.triggers.setTriggerInterface(trigger.id, 'process',
                (event.target as HTMLInputElement).value))} />
        </label>
        <label class="field">
          <span>${t('WindowRule.How')}</span>
          <select .value=${isWindow ? trigger.windowHow : trigger.interfaceHow}
            @change=${(event: Event) => void (isWindow
              ? editor.triggers.setTriggerWindow(trigger.id, 'how', (event.target as HTMLSelectElement).value)
              : editor.triggers.setTriggerInterface(trigger.id, 'how',
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
                @change=${(event: Event) => void editor.triggers.setTriggerWindow(trigger.id, 'change',
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
          <div class="inline">
            <input .value=${trigger.hotstringText} placeholder=${t('Editor.HotstringHint')}
              @change=${(event: Event) => void editor.triggers.setTriggerHotstring(trigger.id,
                (event.target as HTMLInputElement).value)} />
            <button class="keycap" type="button" @click=${() => void host.openCharacterPad(trigger)}>
              ${t('HotstringKeyboard.Choose')}</button>
          </div>
          <span class="hint">${trigger.hotstringText.length}/${trigger.hotstringMaximumLength}</span>
        </label>
      `);
    } else if (trigger.kind === 'Color') {
      const colour = /^#[0-9a-f]{6}$/i.test(trigger.colorHex) ? trigger.colorHex : '#000000';
      fields.push(html`
        <div class="two">
          <label class="field">
            <span>${t('Editor.ColorTolerance')}</span>
            <select .value=${trigger.colorMatch}
              @change=${(event: Event) => void editor.triggers.setTriggerColor(trigger.id, 'match',
                (event.target as HTMLSelectElement).value)}>
              <option value="ColorMatches">${t('ColorMatch.Matches')}</option>
              <option value="ColorNotMatches">${t('ColorMatch.NotMatches')}</option>
            </select>
          </label>
          <label class="field">
            <span>${t('Editor.HexColor')}</span>
            <div class="inline">
              <input type="color" .value=${colour}
                @input=${(event: Event) => void editor.triggers.setTriggerColor(trigger.id, 'hex',
                  (event.target as HTMLInputElement).value)} />
              <input .value=${trigger.colorHex}
                @change=${(event: Event) => void editor.triggers.setTriggerColor(trigger.id, 'hex',
                  (event.target as HTMLInputElement).value)} />
            </div>
          </label>
        </div>
        <div class="two">
          <label class="field">
            <span>${t('Editor.ColorTolerance')}</span>
            <input type="number" min="0" .value=${trigger.colorTolerance}
              @change=${(event: Event) => void editor.triggers.setTriggerColor(trigger.id, 'tolerance',
                (event.target as HTMLInputElement).value)} />
          </label>
          <label class="inline">
            <input type="checkbox" .checked=${trigger.followCursor}
              @change=${(event: Event) => void editor.triggers.setTriggerColor(trigger.id, 'follow',
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
                  @change=${(event: Event) => void editor.triggers.setTriggerColor(trigger.id, 'x',
                    (event.target as HTMLInputElement).value)} />
              </label>
              <label class="field">
                <span>${t('Editor.Y')}</span>
                <input type="number" .value=${trigger.colorY}
                  @change=${(event: Event) => void editor.triggers.setTriggerColor(trigger.id, 'y',
                    (event.target as HTMLInputElement).value)} />
              </label>
            </div>
          `}
      `);
    } else if (trigger.kind === 'Idle') {
      fields.push(renderIdleFields(trigger));
    } else {
      fields.push(html`<p class="hint">${trigger.summary}</p>`);
    }

    if (trigger.kind === 'Key' || trigger.kind === 'Color') {
      fields.push(html`
        <label class="field">
          <span>${t('Editor.Loop')}</span>
          <select .value=${trigger.loop}
            @change=${(event: Event) => void editor.triggers.setTriggerLoop(
              trigger.id, (event.target as HTMLSelectElement).value)}>
            <option value="Press">${t('Loop.Press')}</option>
            <option value="Hold">${t('Loop.Hold')}</option>
            <option value="Toggle">${t('Loop.Toggle')}</option>
            <option value="Release">${t('Loop.Release')}</option>
          </select>
        </label>
      `);
    }

    if (['Key', 'Color', 'Timer', 'File', 'Process', 'Window', 'Idle'].includes(trigger.kind)) {
      fields.push(html`
        <label class="inline">
          <input type="checkbox" .checked=${trigger.once}
            @change=${(event: Event) => void editor.triggers.setTriggerOnce(
              trigger.id, (event.target as HTMLInputElement).checked)} />
          <span>${t('Editor.TriggerOnce')}</span>
        </label>
      `);
    }

    if (trigger.kind === 'Hotstring') {
      fields.push(html`
        <label class="field">
          <span>${t('Editor.HotstringEndChars')}</span>
          <div class="end-grid">
            ${trigger.hotstringEndKeys.map((key) => html`
              <button class="keycap ${key.checked ? 'on' : ''} ${key.isCustom ? 'custom' : ''}"
                type="button" aria-pressed=${key.checked ? 'true' : 'false'}
                @click=${() => void editor.triggers.setTriggerHotstringEndKey(trigger.id, key.value, !key.checked)}>
                ${key.label}
              </button>
            `)}
          </div>
          <div class="key-row">
            <button class="keycap" type="button" @click=${() => host.beginEndCapture(trigger)}>
              ${t('Editor.HotstringEndCapture')}
            </button>
            <button class="keycap" type="button"
              @click=${() => void editor.triggers.resetTriggerHotstringEndKeys(trigger.id)}>
              ${t('Editor.HotstringEndCharsReset')}
            </button>
          </div>
          ${trigger.hotstringEndCaptureProblem
            ? html`<span class="problem">${trigger.hotstringEndCaptureProblem}</span>`
            : nothing}
          <span class="hint">${t('Editor.HotstringEndCharsHint')}</span>
        </label>
        <div class="two">
          <label class="inline">
            <input type="checkbox" .checked=${trigger.hotstringCaseSensitive}
              @change=${(event: Event) => void editor.triggers.setTriggerHotstringOption(trigger.id,
                'caseSensitive', String((event.target as HTMLInputElement).checked))} />
            <span>${t('Editor.HotstringCase')}</span>
          </label>
          <label class="inline">
            <input type="checkbox" .checked=${trigger.hotstringInsideWord}
              @change=${(event: Event) => void editor.triggers.setTriggerHotstringOption(trigger.id,
                'insideWord', String((event.target as HTMLInputElement).checked))} />
            <span>${t('Editor.HotstringInsideWord')}</span>
          </label>
        </div>
        <div class="two">
          <label class="inline">
            <input type="checkbox" .checked=${trigger.hotstringRequireEnd}
              @change=${(event: Event) => void editor.triggers.setTriggerHotstringOption(trigger.id,
                'requireEnd', String((event.target as HTMLInputElement).checked))} />
            <span>${t('Editor.HotstringRequireEnd')}</span>
          </label>
          <label class="inline">
            <input type="checkbox" .checked=${trigger.hotstringEraseTyped}
              @change=${(event: Event) => void editor.triggers.setTriggerHotstringOption(trigger.id,
                'eraseTyped', String((event.target as HTMLInputElement).checked))} />
            <span>${t('Editor.HotstringErase')}</span>
          </label>
        </div>
        <label class="inline">
          <input type="checkbox" .checked=${trigger.hotstringKeepEnd}
            @change=${(event: Event) => void editor.triggers.setTriggerHotstringOption(trigger.id,
              'keepEnd', String((event.target as HTMLInputElement).checked))} />
          <span>${t('Editor.HotstringKeepEnd')}</span>
        </label>
      `);
    }

    if (trigger.kind === 'Window' || trigger.kind === 'Interface') {
      fields.push(html`
        <label class="field">
          <span>${t('WindowRule.Class')}</span>
          <input .value=${trigger.windowClass}
            @change=${(event: Event) => void (trigger.kind === 'Window'
              ? editor.triggers.setTriggerWindow(trigger.id, 'class', (event.target as HTMLInputElement).value)
              : editor.triggers.setTriggerInterface(trigger.id, 'class',
                (event.target as HTMLInputElement).value))} />
        </label>
      `);
    }

    if (trigger.kind === 'Interface') {
      fields.push(html`
        <label class="field">
          <span>${t('Editor.InterfaceHotkey')}</span>
          <div class="key-row">
            <input .value=${trigger.interfaceHotkey} readonly placeholder=${t('Common.None')} />
            <button class="keycap" type="button" @click=${() => host.beginInterfaceCapture(trigger)}>
              ${t('Settings.HotkeySet')}</button>
            <button class="keycap" type="button"
              @click=${() => void editor.triggers.clearTriggerInterfaceHotkey(trigger.id)}>
              ${t('Settings.HotkeyClear')}</button>
          </div>
          <span class="hint">${t('Editor.InterfaceHotkeySupport')}</span>
          ${trigger.interfaceHotkeyProblem
            ? html`<span class="problem">${trigger.interfaceHotkeyProblem}</span>`
            : nothing}
          ${trigger.interfaceGroupMembers.length > 0
            ? html`<ul class="member-list">${trigger.interfaceGroupMembers.map((member) =>
              html`<li>${member}</li>`)}</ul>`
            : nothing}
        </label>
      `);
    }

    return html`<div class="trigger-fields">${fields}</div>`;
  }
