import { html, nothing, type TemplateResult } from 'lit';
import type { RunLook, RunState } from '../../shared/bridge/protocol.js';
import { t } from '../../shared/strings/strings.js';

/**
 * 页面自己也记一份取景历史时用的条数，与程序侧 LookHistory.Kept 相同：运行中不断有新画面，
 * 整份状态却要等下一次回复才来，这中间的列表由页面自己长。
 */
export const LOOKS_KEPT = 20;

/**
 * 这一步看到了什么：上面是正在看的那张大图，下面是全部取景（按步骤去重，最新的在最后）。
 * 点缩略图只换正在看哪一张，既不重跑，也不重新取景。
 */
export function renderLooks(
  run: RunState | null,
  select: (stepId: string) => void,
): TemplateResult | typeof nothing {
  if (!run) {
    return nothing;
  }

  const looks = run.looks ?? [];
  if (!run.look && looks.length === 0) {
    return nothing;
  }

  return html`
    <section class="looks">
      <h3>${t('Look.Title')}</h3>
      ${run.look ? html`
        <figure class="look">
          <img src=${run.look.image} alt="" />
          <figcaption>${caption(run.look)}</figcaption>
        </figure>
      ` : nothing}
      ${looks.length === 0 ? nothing : html`
        <div class="look-thumbs">
          ${looks.map((look) => html`
            <button type="button" class=${look.stepId === run.activeLook ? 'look-thumb active' : 'look-thumb'}
              title=${t('Run.OpenLook')} data-step=${look.stepId}
              @click=${() => select(look.stepId)}>
              <img src=${look.image} alt="" />
              <span>${caption(look)}</span>
            </button>
          `)}
        </div>
      `}
    </section>
  `;
}

/** 缩略图下面那一行：哪一步的、用哪种动作看的、找的是什么。 */
function caption(look: RunLook): string {
  return `${look.stepId} · ${look.type}${look.looking ? ' · ' + look.looking : ''}`;
}
