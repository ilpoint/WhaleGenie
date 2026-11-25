import { html, nothing, type TemplateResult } from 'lit';
import { icon } from '../../../shared/icons.js';
import { t } from '../../../shared/strings/strings.js';
import { editor } from '../editor-store.js';

export function renderDeclarations(): TemplateResult {
    const snapshot = editor.get().snapshot;
    if (!snapshot) {
      return html``;
    }

    const types = editor.get().catalogue?.variableTypes ?? [];
    return html`
      <h3>${t('MacroInterface.Parameters')}</h3>
      <div class="declarations">
        ${snapshot.callableParameters.map((parameter, index) => html`
          <div class="declaration">
            <div class="declaration-head">
              <input .value=${parameter.name} placeholder=${t('MacroInterface.Name')}
                @change=${(event: Event) => void editor.declarations.patchCallableParameter(index, 'name',
                  (event.target as HTMLInputElement).value)} />
              <select .value=${parameter.type}
                @change=${(event: Event) => void editor.declarations.patchCallableParameter(index, 'type',
                  (event.target as HTMLSelectElement).value)}>
                ${types.map((type) => html`<option value=${type.value}>${type.label}</option>`)}
              </select>
              <label class="inline" title=${t('MacroInterface.Required')}>
                <input type="checkbox" .checked=${parameter.required}
                  @change=${(event: Event) => void editor.declarations.patchCallableParameter(index, 'required',
                    String((event.target as HTMLInputElement).checked))} />
                <span class="hint">${t('MacroInterface.Required')}</span>
              </label>
              <button class="ghost" title=${t('MacroInterface.MoveUp')}
                aria-label=${t('MacroInterface.MoveUp')}
                @click=${() => void editor.declarations.moveCallableParameter(index, -1)}>${icon('arrow-up')}</button>
              <button class="ghost" title=${t('MacroInterface.MoveDown')}
                aria-label=${t('MacroInterface.MoveDown')}
                @click=${() => void editor.declarations.moveCallableParameter(index, 1)}>${icon('arrow-down')}</button>
              <button class="ghost" title=${t('MacroInterface.Remove')}
                aria-label=${t('MacroInterface.Remove')}
                @click=${() => void editor.declarations.removeCallableParameter(index)}>${icon('close')}</button>
            </div>
            <label class="field">
              <span>${t('MacroInterface.Default')}</span>
              <input .value=${parameter.default} placeholder=${t('MacroInterface.Default')}
                @change=${(event: Event) => void editor.declarations.patchCallableParameter(index, 'default',
                  (event.target as HTMLInputElement).value)} />
            </label>
            <label class="field">
              <span>${t('MacroInterface.Description')}</span>
              <input .value=${parameter.description}
                @change=${(event: Event) => void editor.declarations.patchCallableParameter(index, 'description',
                  (event.target as HTMLInputElement).value)} />
            </label>
            ${parameter.type === 'record' ? renderRecordFields('parameter', index, parameter.fields) : nothing}
          </div>
        `)}
      </div>
      <button class="link-button" type="button"
        @click=${() => void editor.declarations.addCallableParameter()}>
        ${t('MacroInterface.AddParameter')}
      </button>
      <h3>${t('MacroInterface.Results')}</h3>
      <div class="declarations">
        ${snapshot.callableResults.map((result, index) => html`
          <div class="declaration">
            <div class="declaration-head">
              <input .value=${result.name} placeholder=${t('MacroInterface.Name')}
                @change=${(event: Event) => void editor.declarations.patchCallableResult(index, 'name',
                  (event.target as HTMLInputElement).value)} />
              <select .value=${result.type}
                @change=${(event: Event) => void editor.declarations.patchCallableResult(index, 'type',
                  (event.target as HTMLSelectElement).value)}>
                ${types.map((type) => html`<option value=${type.value}>${type.label}</option>`)}
              </select>
              <span></span>
              <button class="ghost" title=${t('MacroInterface.MoveUp')}
                aria-label=${t('MacroInterface.MoveUp')}
                @click=${() => void editor.declarations.moveCallableResult(index, -1)}>${icon('arrow-up')}</button>
              <button class="ghost" title=${t('MacroInterface.MoveDown')}
                aria-label=${t('MacroInterface.MoveDown')}
                @click=${() => void editor.declarations.moveCallableResult(index, 1)}>${icon('arrow-down')}</button>
              <button class="ghost" title=${t('MacroInterface.Remove')}
                aria-label=${t('MacroInterface.Remove')}
                @click=${() => void editor.declarations.removeCallableResult(index)}>${icon('close')}</button>
            </div>
            <label class="field">
              <span>${t('MacroInterface.Description')}</span>
              <input .value=${result.description}
                @change=${(event: Event) => void editor.declarations.patchCallableResult(index, 'description',
                  (event.target as HTMLInputElement).value)} />
            </label>
            ${result.type === 'record' ? renderRecordFields('result', index, result.fields) : nothing}
          </div>
        `)}
      </div>
      <button class="link-button" type="button"
        @click=${() => void editor.declarations.addCallableResult()}>
        ${t('MacroInterface.AddResult')}
      </button>
    `;
  }

function renderRecordFields(kind: 'parameter' | 'result', index: number,
    fields: { name: string; type: string }[]): TemplateResult {
    const types = editor.get().catalogue?.variableTypes ?? [];
    return html`
      <div class="record-fields">
        ${fields.map((field, fieldIndex) => html`
          <div class="record-field">
            <input .value=${field.name} placeholder=${t('MacroInterface.FieldName')}
              @change=${(event: Event) => void (kind === 'parameter'
                ? editor.declarations.patchCallableParameterField(index, fieldIndex, 'name',
                  (event.target as HTMLInputElement).value)
                : editor.declarations.patchCallableResultField(index, fieldIndex, 'name',
                  (event.target as HTMLInputElement).value))} />
            <select .value=${field.type}
              @change=${(event: Event) => void (kind === 'parameter'
                ? editor.declarations.patchCallableParameterField(index, fieldIndex, 'type',
                  (event.target as HTMLSelectElement).value)
                : editor.declarations.patchCallableResultField(index, fieldIndex, 'type',
                  (event.target as HTMLSelectElement).value))}>
              ${types.map((type) => html`<option value=${type.value}>${type.label}</option>`)}
            </select>
            <button class="ghost" title=${t('MacroInterface.Remove')}
              aria-label=${t('MacroInterface.Remove')}
              @click=${() => void (kind === 'parameter'
                ? editor.declarations.removeCallableParameterField(index, fieldIndex)
                : editor.declarations.removeCallableResultField(index, fieldIndex))}>${icon('close')}</button>
          </div>
        `)}
        <button class="link-button" type="button"
          @click=${() => void (kind === 'parameter'
            ? editor.declarations.addCallableParameterField(index)
            : editor.declarations.addCallableResultField(index))}>
          ${t('MacroInterface.AddField')}
        </button>
      </div>
    `;
  }
