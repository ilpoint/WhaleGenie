import { html, type TemplateResult } from 'lit';
import type { RunLine } from '../../shared/bridge/protocol.js';
import { section } from './run-sections.js';

/** 运行日志：时间、级别、原文一行。行已经由程序写成读者的话，页面只排格子。 */
export function renderLog(lines: RunLine[]): TemplateResult {
  return section('log', 'Run.Log', html`
    ${lines.map((line) => html`
      <div class="log-line">
        <span class="time">${line.time}</span>
        <span class="level">${line.level}</span>
        <span class="text">${line.text}</span>
      </div>
    `)}
  `);
}
