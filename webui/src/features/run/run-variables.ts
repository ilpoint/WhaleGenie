import { html, nothing, type TemplateResult } from 'lit';
import type { RunState } from '../../shared/bridge/protocol.js';
import { t } from '../../shared/strings/strings.js';
import { section } from './run-sections.js';

/**
 * 变量监视：先摆这次运行正看着的名字和它们此刻的值，再摆全局与各宏的局部变量。三张表共用
 * 一套格线，值一律来自程序，页面不自己算。
 */
export function renderVariables(run: RunState | null): TemplateResult | typeof nothing {
  if (!run) {
    return nothing;
  }

  const watches = run.watches ?? [];
  return section('vars', 'Run.Variables', html`
    ${rate(run)}
    ${watches.length === 0 ? nothing : html`
      <div class="watch-grid">
        ${watches.map((watch) => html`
          <div class="watch">
            <span>${watch.name}</span><span>${watch.type}</span><span>${watch.value}</span>
          </div>
        `)}
      </div>
    `}
    <div class="var-grid">
      ${run.globals.map((item) => html`
        <div class="var"><span>${item.name}</span><span>${item.type}</span><span>${item.value}</span></div>
      `)}
      ${run.locals.map((item) => html`
        <div class="var"><span>${item.name}</span><span>${item.type}</span><span>${item.macro}</span></div>
      `)}
    </div>
  `);
}

/** 输入速率与正在发输入的那次运行：两句都已经由程序写好，页面只负责摆位置。 */
function rate(run: RunState): TemplateResult | typeof nothing {
  if (run.inputRate <= 0 && run.inputRateOwner.length === 0) {
    return nothing;
  }

  return html`
    <div class="rate">
      ${run.inputRate > 0
        ? html`<span class="pair">${t('Settings.InputLimit')} ${String(run.inputRate)} ${t('Settings.InputLimitUnit')}</span>`
        : nothing}
      ${run.inputRateOwner.length > 0 ? html`<span class="owner">${run.inputRateOwner}</span>` : nothing}
    </div>
  `;
}
