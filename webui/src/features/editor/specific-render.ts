import { html, nothing, type TemplateResult } from 'lit';
import { t } from '../../shared/strings/strings.js';
import { keyboardChord } from '../../shared/input/capture.js';
import { editor } from './editor-store.js';
import { renderDeclarations } from './declarations/declarations.js';

export function renderSpecific(): TemplateResult {
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
                  @change=${(event: Event) => void editor.session.setMacroSetting('followInputLimit',
                    String((event.target as HTMLInputElement).checked))} />
                <span>${t('Editor.InputLimitFollow')}</span>
              </label>
              <div class="inline">
                <input type="number" min="0" max="999" data-setting="inputLimit" .value=${String(snapshot.inputLimit)}
                  ?disabled=${snapshot.followInputLimit}
                  @change=${(event: Event) => void editor.session.setMacroSetting('inputLimit',
                    (event.target as HTMLInputElement).value)} />
                <span class="hint">${t('Editor.InputLimitHint')}</span>
              </div>
            </div>
            <div class="group">
              <h3>${t('Editor.ToggleKey')}</h3>
              <div class="inline">
                <input .value=${snapshot.toggleKey} data-setting="toggleKey" readonly
                  title=${t('Add.SetCombination')}
                  placeholder=${t('Common.None')}
                  @keydown=${(event: KeyboardEvent) => {
                    if (['Control', 'Shift', 'Alt', 'Meta'].includes(event.key)) {
                      return;
                    }

                    const binding = keyboardChord(event);
                    if (binding) {
                      event.preventDefault();
                      void editor.session.setMacroSetting('toggleKey', binding);
                    }
                  }} />
                <button class="link-button" type="button"
                  @click=${() => void editor.session.setMacroSetting('toggleKey', '')}>
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
          <h3>${t('Editor.RecordActions')}</h3>
          <label class="field">
            <span>${t('Editor.PositionCapture')}</span>
            <select .value=${String(snapshot.recording.positionIndex)}
              @change=${(event: Event) => void editor.session.setRecordOption('positionCapture',
                (event.target as HTMLSelectElement).value)}>
              ${snapshot.recording.positionOptions.map((option, index) => html`
                <option value=${String(index)}>${option}</option>`)}
            </select>
          </label>
          <div class="two">
            ${recordToggle('mouseMovement', t('Editor.MouseMovement'), snapshot.recording.mouseMovement)}
            ${recordToggle('mouseButtons', t('Editor.MouseButtons'), snapshot.recording.mouseButtons)}
          </div>
          <div class="two">
            ${recordToggle('keyboardKeys', t('Editor.KeyboardKeys'), snapshot.recording.keyboardKeys)}
            ${recordToggle('delays', t('Editor.Delays'), snapshot.recording.delays)}
          </div>
          <div class="two">
            ${recordToggle('gamepadButtons', t('Editor.GamepadButtons'), snapshot.recording.gamepadButtons)}
            ${recordToggle('ignoreInjected', t('Editor.IgnoreInjected'), snapshot.recording.ignoreInjected)}
          </div>
          <label class="field">
            <span>${t('Editor.IgnoreDelays')}</span>
            <input type="number" min="0" max="60000" .value=${String(snapshot.recording.ignoreDelaysBelowMs)}
              @change=${(event: Event) => void editor.session.setRecordOption('ignoreDelaysBelowMs',
                (event.target as HTMLInputElement).value)} />
          </label>
        </div>
        <div class="group">
          <h3>${t('Speed.Title')}</h3>
          <p class="hint">${t('Speed.Explanation')}</p>
          <div class="inline">
            <input type="number" min="0.1" max="10" step="0.1" .value=${String(snapshot.recording.delayScale)}
              @change=${(event: Event) => void editor.session.setDelayScale(
                Number((event.target as HTMLInputElement).value))} />
            <span class="hint">${snapshot.recording.delayScaleDisplay}</span>
            ${snapshot.recording.hasDelayScale
              ? html`<button class="link-button" type="button"
                  @click=${() => void editor.session.setDelayScale(1)}>${t('Common.Restore')}</button>`
              : nothing}
          </div>
        </div>
        <div class="group">
          <h3>${t('Editor.MacroInterface')}</h3>
          ${renderDeclarations()}
        </div>
      </div>
    `;
  }

function recordToggle(field: string, label: string, checked: boolean): TemplateResult {
    return html`
      <label class="inline">
        <input type="checkbox" .checked=${checked}
          @change=${(event: Event) => void editor.session.setRecordOption(field,
            String((event.target as HTMLInputElement).checked))} />
        <span>${label}</span>
      </label>
    `;
  }
