import { html, nothing, type TemplateResult } from 'lit';
import type { RunState, RunTrigger } from '../../shared/bridge/protocol.js';
import { section } from './run-sections.js';

/**
 * 等触发的宏：每个开着、靠定时去看的入口一行，说清等的是什么、现在在不在、上次判定与下次
 * 什么时候看。一个本该跑却没跑的宏，答案通常就在这行字里。
 */
export function renderTriggers(run: RunState | null): TemplateResult | typeof nothing {
  const triggers = run?.triggers ?? [];
  if (triggers.length === 0) {
    return nothing;
  }

  return section('triggers', 'Run.Triggers', html`
    ${triggers.map((trigger) => row(trigger))}
  `, 'Run.TriggersHint');
}

function row(trigger: RunTrigger): TemplateResult {
  return html`
    <div class="trigger-row">
      <span class="name">${trigger.name}</span>
      <span class="kind">${trigger.kind}</span>
      ${trigger.status ? html`<span class="status">${trigger.status}</span>` : nothing}
      <span class="checked">${trigger.checked}</span>
      ${trigger.detail ? html`<span class="detail">${trigger.detail}</span>` : nothing}
    </div>
  `;
}
