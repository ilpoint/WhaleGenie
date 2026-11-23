import { html, nothing, type TemplateResult } from 'lit';
import { t } from '../../shared/strings/strings.js';

/**
 * 运行页的一块：标题、可选的一句说明、内容。这些块的标题行在页面里反复出现，写一次就够，
 * 免得每个模块各画各的间距与字号。
 *
 * 词表里的标题有的带一个数字占位（如「步骤状态（{0}）」），count 大于零时把它填上；没有占位的
 * 标题原样取词。
 */
export function section(
  className: string,
  titleKey: string,
  body: TemplateResult | typeof nothing,
  hintKey = '',
  count = 0,
): TemplateResult {
  return html`
    <section class=${className}>
      <h3>${count > 0 ? t(titleKey).replace('{0}', String(count)) : t(titleKey)}</h3>
      ${hintKey ? html`<p class="hint">${t(hintKey)}</p>` : nothing}
      ${body}
    </section>
  `;
}
