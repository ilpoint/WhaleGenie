import { LitElement, css, html, nothing } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import { bridge } from '../../shared/bridge/bridge.js';
import type { ProjectState, RunLine, RunState, RunStep } from '../../shared/bridge/protocol.js';
import { strings, t } from '../../shared/strings/strings.js';

/** The web run panel: loads one macro and drives the shared RunViewModel through the bridge. */
@customElement('wg-run')
export class WgRun extends LitElement {
  @state()
  private project: ProjectState | null = null;

  @state()
  private run: RunState | null = null;

  @state()
  private error = '';

  private unsubscribe?: () => void;
  private unwatch?: () => void;

  static styles = css`
    :host {
      display: grid;
      grid-template-columns: 280px minmax(0, 1fr);
      gap: var(--spacingHorizontalL);
      padding: var(--spacingVerticalL) var(--spacingHorizontalXXL);
      height: 100%;
      box-sizing: border-box;
    }

    h1 {
      margin: 0 0 var(--spacingVerticalS);
      font-size: var(--fontSizeBase500);
      font-weight: var(--fontWeightSemibold);
    }

    .side {
      min-width: 0;
    }

    .note {
      margin: 0 0 var(--spacingVerticalM);
      color: var(--colorNeutralForeground3);
      font-size: var(--fontSizeBase200);
    }

    .macros, .steps, .log {
      display: grid;
      gap: var(--spacingVerticalXS);
      align-content: start;
    }

    .macros button, .steps button {
      text-align: left;
    }

    .panel {
      display: grid;
      grid-template-rows: auto auto auto minmax(120px, 1fr) minmax(100px, 0.7fr) auto;
      gap: var(--spacingVerticalM);
      min-width: 0;
    }

    .toolbar {
      display: flex;
      flex-wrap: wrap;
      gap: var(--spacingHorizontalS);
      align-items: center;
    }

    .status {
      color: var(--colorNeutralForeground2);
    }

    .args {
      display: grid;
      gap: var(--spacingVerticalS);
      padding: var(--spacingVerticalS) var(--spacingHorizontalM);
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
      border-radius: var(--borderRadiusMedium);
      background: var(--colorNeutralBackground1);
    }

    .arg {
      display: grid;
      gap: var(--spacingVerticalXS);
    }

    .arg .hint {
      color: var(--colorNeutralForeground3);
      font-size: var(--fontSizeBase200);
    }

    .ask {
      display: grid;
      gap: var(--spacingVerticalS);
      padding: var(--spacingVerticalS) var(--spacingHorizontalM);
      border: var(--strokeWidthThin) solid var(--colorBrandStroke1);
      border-radius: var(--borderRadiusMedium);
      background: var(--colorNeutralBackground2);
    }

    .step {
      display: grid;
      grid-template-columns: 22px minmax(0, 1fr);
      gap: var(--spacingHorizontalS);
      align-items: start;
      padding: var(--spacingVerticalXS) var(--spacingHorizontalS);
      border-left: var(--strokeWidthThick) solid transparent;
    }

    .step.current {
      border-left-color: var(--colorBrandStroke1);
      background: var(--colorNeutralBackground2);
    }

    .step .body {
      min-width: 0;
    }

    .rule {
      display: grid;
      grid-template-columns: 72px minmax(0, 1fr);
      gap: var(--spacingHorizontalS);
      margin-top: var(--spacingVerticalXS);
    }

    .step .detail {
      color: var(--colorNeutralForeground3);
      font-size: var(--fontSizeBase200);
      overflow-wrap: anywhere;
    }

    .log-line {
      display: grid;
      grid-template-columns: 72px 64px minmax(0, 1fr);
      gap: var(--spacingHorizontalS);
      font-size: var(--fontSizeBase200);
    }

    .log-line .time {
      color: var(--colorNeutralForeground3);
    }

    .look {
      margin: 0;
      display: grid;
      gap: var(--spacingVerticalXS);
    }

    .look img {
      max-width: 100%;
      max-height: 220px;
      object-fit: contain;
      justify-self: start;
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
    }

    .look figcaption {
      color: var(--colorNeutralForeground3);
      font-size: var(--fontSizeBase200);
    }

    .vars h3 {
      margin: 0 0 var(--spacingVerticalXS);
      font-size: var(--fontSizeBase300);
    }

    .var-grid {
      display: grid;
      grid-template-columns: minmax(120px, 1fr) 110px minmax(0, 1.4fr);
      gap: var(--spacingVerticalXS) var(--spacingHorizontalS);
      font-size: var(--fontSizeBase200);
    }

    .var {
      display: contents;
    }

    .var span:last-child {
      overflow-wrap: anywhere;
    }

    .error {
      color: var(--colorPaletteRedForeground1);
    }
  `;

  connectedCallback(): void {
    super.connectedCallback();
    this.unsubscribe = strings.subscribe(() => this.requestUpdate());
    this.unwatch = bridge.value.on('run.changed', (payload) => {
      this.run = payload as RunState;
    });
    void this.load();
  }

  disconnectedCallback(): void {
    this.unsubscribe?.();
    this.unwatch?.();
    super.disconnectedCallback();
  }

  render() {
    return html`
      <div class="side">
        <h1>${t('Run.Title')}</h1>
        <p class="note">${t('Run.NativeNote')}</p>
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
        <div class="toolbar">
          ${this.button('run', 'Run.Run', this.run?.canRun ?? false)}
          ${this.button('step', 'Run.Step', this.run?.canStep ?? false)}
          ${this.button('continue', 'Run.Continue', this.run?.paused ?? false)}
          ${this.button('skip', 'Run.SkipStep', this.run?.paused ?? false)}
          ${this.button('stop', 'Run.Stop', this.run?.canStop ?? false)}
        </div>
        <div class="status">${this.run?.status ?? ''}</div>
        ${this.arguments()}
        ${this.ask()}
        <div class="steps">
          ${this.run?.steps.map((step) => this.step(step))}
        </div>
        <div class="log">
          ${this.run?.log.map((line) => this.line(line))}
        </div>
        ${this.run?.look ? html`
          <figure class="look">
            <img src=${this.run.look.image} alt="" />
            <figcaption>${this.run.look.stepId}${this.run.look.looking ? ` · ${this.run.look.looking}` : ''}</figcaption>
          </figure>
        ` : nothing}
        <div class="vars">
          <h3>${t('Run.Variables')}</h3>
          <div class="var-grid">
            ${this.run?.globals.map((item) => html`
              <div class="var"><span>${item.name}</span><span>${item.type}</span><span>${item.value}</span></div>
            `)}
            ${this.run?.locals.map((item) => html`
              <div class="var"><span>${item.name}</span><span>${item.type}</span><span>${item.macro}</span></div>
            `)}
          </div>
        </div>
      </div>
    `;
  }

  private button(command: string, label: string, enabled: boolean) {
    return html`
      <fluent-button appearance="subtle" ?disabled=${!enabled}
        @click=${() => void this.send(command)}>${t(label)}</fluent-button>
    `;
  }

  private step(step: RunStep) {
    return html`
      <div class="step ${step.current ? 'current' : ''}" style="padding-left: ${step.depth * 12}px">
        ${step.canBreak
          ? html`<input type="checkbox" .checked=${step.breakpoint}
              @change=${() => void this.breakpoint(step.id)} />`
          : html`<span></span>`}
        <div class="body">
          <div>${step.label}</div>
          ${step.detail ? html`<div class="detail">${step.detail}</div>` : nothing}
          ${step.time ? html`<div class="detail">${step.time}</div>` : nothing}
          ${step.failure ? html`<div class="detail error">${step.failure}</div>` : nothing}
          ${step.condition ? html`<div class="detail">${step.condition}</div>` : nothing}
          ${step.breakpoint ? html`
            <div class="rule">
              <input type="number" min="1" .value=${String(step.hitTimes)}
                @change=${(event: Event) => void this.rule(step.id, event)} />
              <input .value=${step.expression} placeholder=${t('Run.BreakpointExpressionWatermark')}
                @change=${(event: Event) => void this.rule(step.id, event)} />
            </div>
          ` : nothing}
        </div>
      </div>
    `;
  }

  private line(line: RunLine) {
    return html`
      <div class="log-line">
        <span class="time">${line.time}</span>
        <span>${line.level}</span>
        <span>${line.text}</span>
      </div>
    `;
  }

  private arguments() {
    if (!this.run?.arguments.length) {
      return nothing;
    }

    return html`
      <div class="args">
        ${this.run.arguments.map((argument) => html`
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

  private ask() {
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

  private async rule(id: string, event: Event): Promise<void> {
    const target = event.target as HTMLInputElement;
    const row = target.closest('.step');
    if (!row) {
      return;
    }

    const hitTimes = Number(row.querySelector<HTMLInputElement>('input[type="number"]')?.value ?? 1);
    const expression = row.querySelectorAll<HTMLInputElement>('input:not([type])')[0]?.value ?? '';
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
}

declare global {
  interface HTMLElementTagNameMap {
    'wg-run': WgRun;
  }
}
