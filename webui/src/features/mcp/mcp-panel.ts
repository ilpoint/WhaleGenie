import { LitElement, css, html, nothing } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import { bridge } from '../../shared/bridge/bridge.js';
import { strings, t } from '../../shared/strings/strings.js';
import { StoreController } from '../../shared/lit/store-controller.js';
import { SubscriptionController } from '../../shared/lit/subscription-controller.js';
import type { McpState } from '../../shared/bridge/protocol.js';

/**
 * MCP 浮层：两个授权开关、给客户端配置用的那段 JSON、审计入口。与旧主窗的 MCP 按钮同职责；
 * 开关只经桥写设置——服务端每次调用前重读，客户端已经在跑的会话立即受新答案约束。
 */
@customElement('wg-mcp-panel')
export class WgMcpPanel extends LitElement {
  @state()
  private open = false;

  @state()
  private state: McpState | null = null;

  static styles = css`
    :host {
      display: contents;
    }

    .overlay {
      position: fixed;
      inset: 0;
      background: var(--shell-scrim);
      z-index: 100;
    }

    .card {
      position: fixed;
      top: 48px;
      right: var(--spacingHorizontalL);
      width: 460px;
      max-height: calc(100vh - 96px);
      overflow: auto;
      background: var(--colorNeutralBackground1);
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke1);
      border-radius: var(--borderRadiusLarge);
      padding: var(--spacingVerticalL) var(--spacingHorizontalXL);
      box-shadow: var(--shadow16);
      z-index: 101;
    }

    h2 {
      font-size: var(--fontSizeBase400);
      font-weight: var(--fontWeightSemibold);
      margin: 0 0 var(--spacingVerticalM);
    }

    .intro {
      color: var(--colorNeutralForeground2);
      margin: 0 0 var(--spacingVerticalL);
    }

    .switch {
      display: flex;
      align-items: center;
      gap: var(--spacingHorizontalM);
      margin-bottom: var(--spacingVerticalM);
    }

    .switch span {
      color: var(--colorNeutralForeground1);
    }

    .switch .hint {
      color: var(--colorNeutralForeground3);
      font-size: var(--fontSizeBase200);
    }

    h3 {
      font-size: var(--fontSizeBase300);
      font-weight: var(--fontWeightSemibold);
      margin: var(--spacingVerticalL) 0 var(--spacingVerticalS);
    }

    pre {
      background: var(--colorNeutralBackground2);
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke1);
      border-radius: var(--borderRadiusMedium);
      padding: var(--spacingVerticalM) var(--spacingHorizontalM);
      font-family: var(--fontFamilyMonospace);
      font-size: var(--fontSizeBase200);
      white-space: pre-wrap;
      word-break: break-all;
      margin: 0 0 var(--spacingVerticalM);
    }

    .actions {
      display: flex;
      gap: var(--spacingHorizontalS);
      align-items: center;
      flex-wrap: wrap;
    }
  `;

  protected readonly stringsController = new StoreController(this, strings);

  protected readonly mcpController = new SubscriptionController(this, () =>
    bridge.value.on('mcp.changed', (state) => {
      this.state = state;
    }));

  /** filebar 的入口按钮调这个：打开浮层并把当前授权拿回来。 */
  async show(): Promise<void> {
    this.open = true;
    this.load(await bridge.value.call<McpState>('mcp.state'));
  }

  /** 启动装配与事件接线共用的装载口。 */
  load(state: McpState): void {
    this.state = state;
  }

  render() {
    if (!this.open) {
      return nothing;
    }

    return html`
      <div class="overlay" @click=${this.onDismiss}></div>
      <div class="card" @click=${(event: Event) => event.stopPropagation()}>
        <h2>${t('Mcp.Title')}</h2>
        <p class="intro">${t('Mcp.Intro')}</p>
        ${this.state === null
          ? nothing
          : html`
              <div class="switch">
                <fluent-switch
                  ?checked=${this.state.allowControl}
                  @change=${() => this.onGrants('control', !this.state!.allowControl)}
                ></fluent-switch>
                <span>
                  ${t('Mcp.Control')}
                  <span class="hint">${t('Mcp.SwitchOn')} / ${t('Mcp.SwitchOff')}</span>
                </span>
              </div>
              <div class="switch">
                <fluent-switch
                  ?checked=${this.state.allowRun}
                  @change=${() => this.onGrants('run', !this.state!.allowRun)}
                ></fluent-switch>
                <span>${t('Mcp.Run')}</span>
              </div>
              <h3>${t('Mcp.CopyConfig')}</h3>
              <pre>${this.state.config}</pre>
              <p class="intro">${t('Mcp.GrantsNote')}</p>
              <div class="actions">
                <fluent-button appearance="accent" @click=${this.onCopy}>${t('Mcp.CopyConfig')}</fluent-button>
                <fluent-button appearance="subtle" @click=${this.onOpenAudit}>${t('Mcp.OpenAudit')}</fluent-button>
              </div>
            `}
      </div>
    `;
  }

  private onGrants(kind: 'control' | 'run', value: boolean): void {
    if (this.state === null) {
      return;
    }

    // 开关不自己翻转：设置由程序写入，mcp.changed 带着新的两个开关回来。
    void bridge.value.call('mcp.setGrants', {
      control: kind === 'control' ? value : this.state.allowControl,
      run: kind === 'run' ? value : this.state.allowRun,
    });
  }

  private async onCopy(): Promise<void> {
    await bridge.value.call('mcp.copyConfig');
    this.state = this.state === null ? null : { ...this.state };
  }

  private onOpenAudit(): void {
    void bridge.value.call('mcp.openAudit');
  }

  private onDismiss(): void {
    this.open = false;
  }
}

declare global {
  interface HTMLElementTagNameMap {
    'wg-mcp-panel': WgMcpPanel;
  }
}
