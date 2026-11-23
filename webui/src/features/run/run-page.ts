import { LitElement, html, nothing, type TemplateResult } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import { bridge } from '../../shared/bridge/bridge.js';
import type { ProjectState, RunLook, RunState } from '../../shared/bridge/protocol.js';
import { strings, t } from '../../shared/strings/strings.js';
import { StoreController } from '../../shared/lit/store-controller.js';
import { SubscriptionController } from '../../shared/lit/subscription-controller.js';
import { renderLog } from './run-log.js';
import { LOOKS_KEPT, renderLooks } from './run-look.js';
import { runPageCss } from './run-page-css.js';
import { renderSteps, type StepActions } from './run-steps.js';
import { renderTriggers } from './run-triggers.js';
import { renderVariables } from './run-variables.js';

/**
 * 运行面板的装配：装一个宏、经桥驱动程序里那一个 RunViewModel，再把状态交给各区块画。
 * 页面只做视图——草稿、校验与历史都在程序里；这里记的只是推来的快照与命令的回复。
 */
@customElement('wg-run')
export class WgRun extends LitElement {
  @state()
  private project: ProjectState | null = null;

  @state()
  private run: RunState | null = null;

  @state()
  private error = '';

  protected readonly stringsController = new StoreController(this, strings);

  /** 推送来的整份状态。行、日志与取景走各自的差量，所以带着上一次的副本合并。 */
  protected readonly runController = new SubscriptionController(this, () =>
    bridge.value.on('run.changed', (payload) => {
      const next = payload as RunState;
      this.run = {
        ...next,
        steps: this.run?.steps ?? [],
        log: this.run?.log ?? [],
        look: this.run?.look ?? null,
        looks: this.run?.looks ?? [],
      };
    }));

  protected readonly stepsController = new SubscriptionController(this, () =>
    bridge.value.on('run.steps', (steps) => {
      if (this.run) {
        this.run = { ...this.run, steps };
      }
    }));

  protected readonly logController = new SubscriptionController(this, () =>
    bridge.value.on('run.log', (batch) => {
      if (!this.run) {
        return;
      }

      const seen = this.run.log.length;
      this.run = {
        ...this.run,
        log: batch.logFrom === 0 || batch.logFrom > seen
          ? batch.lines
          : [...this.run.log, ...batch.lines],
      };
    }));

  protected readonly lookController = new SubscriptionController(this, () =>
    bridge.value.on('run.look', (look) => this.keepLook(look)));

  static styles = runPageCss;

  connectedCallback(): void {
    super.connectedCallback();
    void this.load();
  }

  disconnectedCallback(): void {
    super.disconnectedCallback();
    void bridge.value.call<RunState>('run.unload');
  }

  render(): TemplateResult {
    return html`
      <div class="side">
        <h1>${t('Run.Title')}</h1>
        ${this.error ? html`<p class="error">${this.error}</p>` : nothing}
        <div class="macros">
          ${this.project?.macros.map((macro) => html`
            <fluent-button appearance="subtle" @click=${() => void this.loadMacro(macro.name)}>
              ${macro.name}
            </fluent-button>
          `)}
        </div>
      </div>
      <div class="panel">
        ${this.toolbar()}
        <div class="status">${this.run?.status ?? ''}</div>
        ${this.swapNote()}
        ${renderTriggers(this.run)}
        ${this.arguments()}
        ${this.ask()}
        ${renderSteps(this.run, this.stepActions())}
        ${renderLog(this.run?.log ?? [])}
        ${renderLooks(this.run, (stepId) => void this.lookSelect(stepId))}
        ${renderVariables(this.run)}
      </div>
    `;
  }

  private toolbar(): TemplateResult {
    return html`
      <div class="toolbar">
        ${this.button('run', 'Run.Run', this.run?.canRun ?? false)}
        ${this.button('step', 'Run.Step', this.run?.canStep ?? false)}
        ${this.button('continue', 'Run.Continue', this.run?.paused ?? false)}
        ${this.button('skip', 'Run.SkipStep', this.run?.paused ?? false)}
        ${this.button('stop', 'Run.Stop', this.run?.canStop ?? false)}
      </div>
    `;
  }

  /** 换宏的说明：程序在切换时写好的那句话，读过一次运行就把它挪进日志。 */
  private swapNote(): TemplateResult | typeof nothing {
    const note = this.run?.swapNote ?? '';
    return note.length === 0 ? nothing : html`<p class="swap">${note}</p>`;
  }

  private arguments(): TemplateResult | typeof nothing {
    const arguments_ = this.run?.arguments ?? [];
    if (arguments_.length === 0) {
      return nothing;
    }

    return html`
      <div class="args">
        ${arguments_.map((argument) => html`
          <label class="arg">
            <span>${argument.label}${argument.required ? ' *' : ''}</span>
            <input .value=${argument.value}
              @change=${(event: Event) => void this.argument(argument.name, event)} />
            <span class="hint">${argument.hint}</span>
          </label>
        `)}
      </div>
    `;
  }

  private ask(): TemplateResult | typeof nothing {
    if (!this.run) {
      return nothing;
    }

    if (this.run.isAskingUser) {
      return html`
        <div class="ask">
          <div>${this.run.question}</div>
          ${this.run.wantsTypedAnswer ? html`
            <input id="run-answer" .value=${this.run.answerText} />
          ` : nothing}
          <div class="toolbar">
            <fluent-button appearance="accent"
              @click=${() => void this.answer('user', 'ok')}>${t('Common.Ok')}</fluent-button>
            <fluent-button appearance="subtle"
              @click=${() => void this.answer('user', 'cancel')}>${t('Common.Cancel')}</fluent-button>
          </div>
        </div>
      `;
    }

    if (this.run.isAsking) {
      return html`
        <div class="ask">
          <div>${this.run.question}</div>
          <div class="toolbar">
            <fluent-button appearance="accent"
              @click=${() => void this.answer('failure', 'retry')}>${t('Run.AskRetry')}</fluent-button>
            <fluent-button appearance="subtle"
              @click=${() => void this.answer('failure', 'skip')}>${t('Run.AskSkip')}</fluent-button>
            <fluent-button appearance="subtle"
              @click=${() => void this.answer('failure', 'stop')}>${t('Run.AskStop')}</fluent-button>
          </div>
        </div>
      `;
    }

    return nothing;
  }

  private button(command: string, label: string, enabled: boolean): TemplateResult {
    return html`
      <fluent-button appearance="subtle" ?disabled=${!enabled}
        @click=${() => void this.send(command)}>${t(label)}</fluent-button>
    `;
  }

  /** 步骤列表要发回去的两件事，接到桥上。 */
  private stepActions(): StepActions {
    return {
      breakpoint: (id: string) => void this.breakpoint(id),
      rule: (id: string, hitTimes: number, expression: string) => void this.rule(id, hitTimes, expression),
    };
  }

  /** 推来的取景：历史按步骤去重、只留最近几张，与程序侧同一条规则。 */
  private keepLook(look: RunLook | null): void {
    if (!this.run || !look) {
      return;
    }

    const kept = [...(this.run.looks ?? []).filter((item) => item.stepId !== look.stepId), look];
    this.run = {
      ...this.run,
      look,
      activeLook: look.stepId,
      looks: kept.slice(-LOOKS_KEPT),
    };
  }

  private async load(): Promise<void> {
    try {
      this.project = await bridge.value.call<ProjectState>('project.state');
      this.run = await bridge.value.call<RunState>('run.state');
    } catch (error) {
      this.error = String(error);
    }
  }

  private async argument(name: string, event: Event): Promise<void> {
    try {
      this.run = await bridge.value.call<RunState>('run.argument',
        { name, value: (event.target as HTMLInputElement).value });
      this.error = '';
    } catch (error) {
      this.error = String(error);
    }
  }

  private async rule(id: string, hitTimes: number, expression: string): Promise<void> {
    try {
      this.run = await bridge.value.call<RunState>('run.breakpointRule',
        { id, hitTimes, expression });
      this.error = '';
    } catch (error) {
      this.error = String(error);
    }
  }

  private async answer(kind: string, choice: string): Promise<void> {
    try {
      const text = this.renderRoot.querySelector<HTMLInputElement>('#run-answer')?.value;
      this.run = await bridge.value.call<RunState>('run.answer', { kind, choice, text });
      this.error = '';
    } catch (error) {
      this.error = String(error);
    }
  }

  private async loadMacro(name: string): Promise<void> {
    try {
      this.run = await bridge.value.call<RunState>('run.load', { name });
      this.error = '';
    } catch (error) {
      this.error = String(error);
    }
  }

  private async send(command: string): Promise<void> {
    try {
      this.run = await bridge.value.call<RunState>('run.command', { command });
      this.error = '';
    } catch (error) {
      this.error = String(error);
    }
  }

  private async breakpoint(id: string): Promise<void> {
    try {
      this.run = await bridge.value.call<RunState>('run.breakpoint', { id });
      this.error = '';
    } catch (error) {
      this.error = String(error);
    }
  }

  /** 换一张取景来看：只切正在看哪张，不重跑、也不重新抓屏。 */
  private async lookSelect(stepId: string): Promise<void> {
    try {
      this.run = await bridge.value.call<RunState>('run.lookSelect', { stepId });
      this.error = '';
    } catch (error) {
      this.error = String(error);
    }
  }
}

declare global {
  interface HTMLElementTagNameMap {
    'wg-run': WgRun;
  }
}
