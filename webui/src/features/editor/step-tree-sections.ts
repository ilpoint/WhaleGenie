import { html, type TemplateResult } from 'lit';
import { repeat } from 'lit/directives/repeat.js';
import type { EditorStep } from '../../shared/bridge/protocol.js';
import { icon } from '../../shared/icons.js';
import { t } from '../../shared/strings/strings.js';
import { editor } from './editor-store.js';

export interface StepTreeSectionsHost {
  renderStep(step: EditorStep, depth: number): TemplateResult;
}

export function renderStepSections(host: StepTreeSectionsHost, step: EditorStep, depth: number): TemplateResult {
    const state = editor.get();
    return html`
      <div class="sections" data-depth=${String(depth + 1)}>
        ${step.sections.map((section) => {
          const cases = section.name === 'cases';
          const disabled = !cases && state.selectedActionKey.length === 0;
          return html`
            <div class="section-head" data-depth=${String(depth + 1)}>
              <span class="label">${section.label}</span>
              <span class="count">${t('Editor.StepCount').replace('{0}', String(section.steps.length))}</span>
              <button
                class="ghost section-add"
                ?disabled=${disabled}
                title=${cases ? t('Editor.AddBranchHint') : t('Editor.AddStep')}
                aria-label=${cases ? t('Editor.AddBranch') : t('Editor.AddStep')}
                @click=${(event: Event) => {
                  event.stopPropagation();
                  void (cases
                    ? editor.steps.addBranch(step.id)
                    : editor.action.addSelectedStep(step.id, section.name));
                }}
              >${icon('plus')}${cases ? t('Editor.AddBranch') : t('Editor.AddStep')}</button>
            </div>
            ${section.steps.length === 0
              ? html`<div class="section-empty" data-depth=${String(depth + 1)}>${cases
                ? t('Editor.EmptyBranchList')
                : t('Editor.NoStepsHint')}</div>`
              : html`
                <div class="list" role="list">
                  ${repeat(section.steps, (child) => child.id,
                    (child) => host.renderStep(child, depth + 1))}
                </div>
              `}
          `;
        })}
      </div>
    `;
  }
