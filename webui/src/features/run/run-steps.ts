import { html, nothing, type TemplateResult } from 'lit';
import type { RunState, RunStep } from '../../shared/bridge/protocol.js';
import { t } from '../../shared/strings/strings.js';
import { section } from './run-sections.js';

/** 步骤列表要做的事：勾断点、改断点条件。页面把它们接到桥上，这个模块只管画和读输入。 */
export interface StepActions {
  breakpoint(id: string): void;
  rule(id: string, hitTimes: number, expression: string): void;
}

/** 条件里最多摆出几个候选名字：名字通常几十个，全摆出来会把这行撑成一片。 */
const SHOWN_CHOICES = 12;

/**
 * 步骤列表，连同断点条件的候选名字。候选来自程序给的 breakpointChoices：条件里写错一个名字
 * 不会让运行停下，所以名字要能直接点到，同时保留 datalist 让习惯手打的人也能补全。
 */
export function renderSteps(run: RunState | null, actions: StepActions): TemplateResult {
  const steps = (run?.steps ?? []).filter((step) => step.shown);
  const names = (run?.breakpointChoices ?? []).slice(0, SHOWN_CHOICES);
  return section('steps', 'Run.Steps', html`
    <datalist id="run-choices">
      ${names.map((name) => html`<option value=${name}></option>`)}
    </datalist>
    ${steps.map((step) => stepRow(step, names, actions))}
  `);
}

function stepRow(step: RunStep, names: string[], actions: StepActions): TemplateResult {
  return html`
    <div class="step ${step.current ? 'current' : ''}" style="padding-left: ${String(step.depth * 12)}px">
      ${step.canBreak
        ? html`<input type="checkbox" class="break" .checked=${step.breakpoint}
            @change=${() => actions.breakpoint(step.id)} />`
        : html`<span></span>`}
      <div class="body">
        <div class="label">${step.label}</div>
        ${step.detail ? html`<div class="detail">${step.detail}</div>` : nothing}
        ${step.time ? html`<div class="detail">${step.time}</div>` : nothing}
        ${step.outcome ? html`<div class="outcome">${step.outcome}</div>` : nothing}
        ${step.result ? html`<div class="result">${step.result}</div>` : nothing}
        ${step.failure ? html`<div class="detail error">${step.failure}</div>` : nothing}
        ${step.condition ? html`<div class="detail">${step.condition}</div>` : nothing}
        ${step.breakpoint ? rule(step, names, actions) : nothing}
      </div>
    </div>
  `;
}

/** 断点的两格：第几次经过时停，以及停下来的条件（可以空着手打，也可以点候选）。 */
function rule(step: RunStep, names: string[], actions: StepActions): TemplateResult {
  return html`
    <div class="rule">
      <input type="number" class="hits" min="1" .value=${String(step.hitTimes)}
        @change=${(event: Event) => send(step, event, actions)} />
      <input class="expression" list="run-choices" .value=${step.expression}
        placeholder=${t('Run.BreakpointExpressionWatermark')}
        @change=${(event: Event) => send(step, event, actions)} />
    </div>
    <div class="choices">
      ${names.map((name) => html`
        <button type="button" class="choice" data-name=${name}
          @click=${(event: Event) => choose(step, event, actions)}>${name}</button>
      `)}
    </div>
  `;
}

function send(step: RunStep, event: Event, actions: StepActions): void {
  const row = (event.target as HTMLElement).closest('.step');
  const expression = row?.querySelector<HTMLInputElement>('input.expression')?.value ?? '';
  actions.rule(step.id, hits(row), expression);
}

/** 点一个候选名字：接在已写的内容后面，再把这一行交回去。 */
function choose(step: RunStep, event: Event, actions: StepActions): void {
  const button = event.target as HTMLElement;
  const row = button.closest('.step');
  const expression = row?.querySelector<HTMLInputElement>('input.expression');
  if (!expression) {
    return;
  }

  const name = button.dataset['name'] ?? '';
  const written = expression.value.trim();
  expression.value = written.length === 0 ? name : `${written} ${name}`;
  actions.rule(step.id, hits(row), expression.value);
}

function hits(row: Element | null | undefined): number {
  const value = Number(row?.querySelector<HTMLInputElement>('input.hits')?.value ?? 1);
  return Number.isFinite(value) ? Math.max(1, Math.trunc(value)) : 1;
}
