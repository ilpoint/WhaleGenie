import { LitElement, css, html, nothing } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import { bridge } from '../../shared/bridge/bridge.js';
import { strings } from '../../shared/strings/strings.js';
import { StoreController } from '../../shared/lit/store-controller.js';
import { SubscriptionController } from '../../shared/lit/subscription-controller.js';
import type { DialogRequest } from '../../shared/bridge/protocol.js';

/**
 * 程序向页面问话的窗口。旧界面弹 ConfirmDialog；网页壳里程序推 dialog.request，这里画出来，
 * 答案经 dialog.respond 回去——问话的人在等，页面不画的时候答案永远是"否"（桥的重置会把
 * 悬着的问话全部按"否"收掉）。
 */
@customElement('wg-confirm')
export class WgConfirm extends LitElement {
  @state()
  private request: DialogRequest | null = null;

  static styles = css`
    :host {
      display: contents;
    }

    .overlay {
      position: fixed;
      inset: 0;
      background: var(--shell-scrim);
      display: grid;
      place-items: center;
      z-index: 100;
    }

    .card {
      background: var(--colorNeutralBackground1);
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke1);
      border-radius: var(--borderRadiusLarge);
      padding: var(--spacingVerticalL) var(--spacingHorizontalXL);
      max-width: 440px;
      box-shadow: var(--shadow16);
    }

    h2 {
      font-size: var(--fontSizeBase400);
      font-weight: var(--fontWeightSemibold);
      margin: 0 0 var(--spacingVerticalM);
    }

    p {
      color: var(--colorNeutralForeground1);
      white-space: pre-line;
      margin: 0 0 var(--spacingVerticalL);
    }

    .actions {
      display: flex;
      justify-content: flex-end;
      gap: var(--spacingHorizontalS);
    }
  `;

  protected readonly stringsController = new StoreController(this, strings);

  protected readonly dialogController = new SubscriptionController(this, () =>
    bridge.value.on('dialog.request', (request) => {
      this.request = request;
    }));

  render() {
    if (this.request === null) {
      return nothing;
    }

    return html`
      <div class="overlay" @click=${this.onDismiss}>
        <div class="card" @click=${(event: Event) => event.stopPropagation()}>
          <h2>${this.request.title}</h2>
          <p>${this.request.message}</p>
          <div class="actions">
            ${this.request.cancelLabel
              ? html`
                  <fluent-button appearance="subtle" @click=${this.onCancel}>
                    ${this.request.cancelLabel}
                  </fluent-button>
                `
              : nothing}
            <fluent-button appearance="accent" @click=${this.onConfirm}>
              ${this.request.confirmLabel}
            </fluent-button>
          </div>
        </div>
      </div>
    `;
  }

  private answer(accepted: boolean): void {
    if (this.request === null) {
      return;
    }

    void bridge.value.call('dialog.respond', { requestId: this.request.requestId, accepted });
    this.request = null;
  }

  private onConfirm(): void {
    this.answer(true);
  }

  private onCancel(): void {
    this.answer(false);
  }

  private onDismiss(): void {
    // 点在卡片外面是取消：有取消键的问题答"否"，纯报告就当看过收掉。
    this.answer(false);
  }
}

declare global {
  interface HTMLElementTagNameMap {
    'wg-confirm': WgConfirm;
  }
}
