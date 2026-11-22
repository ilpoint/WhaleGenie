import { html, type TemplateResult } from 'lit';
import type { EditorTrigger } from '../../../shared/bridge/protocol.js';
import { t } from '../../../shared/strings/strings.js';
import { editor } from '../editor-store.js';

export function renderIdleFields(trigger: EditorTrigger): TemplateResult {
  return html`
    <label class="field">
      <span>${t('Editor.IdleSeconds')}</span>
      <input type="number" min="1" .value=${trigger.idleSeconds}
        @change=${(event: Event) => void editor.triggers.setTriggerIdle(trigger.id,
          (event.target as HTMLInputElement).value)} />
    </label>
  `;
}
