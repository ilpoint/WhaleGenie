import { LitElement, css, html, nothing } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import { bridge } from '../../shared/bridge/bridge.js';
import { strings, t } from '../../shared/strings/strings.js';

/** 路由名到词表键的显式映射，与导航的映射同形：键都是字面量，对账测试扫得到。 */
const areaKeys: Record<string, string> = {
  macros: 'Shell.Macros',
  editor: 'Shell.Editor',
  run: 'Shell.Run',
  settings: 'Shell.Settings',
};

/** Route to the temporary bridge command that opens its native window. */
const nativeMethods: Record<string, string> = {
  run: 'app.openRun',
  settings: 'app.openSettings',
};

/**
 * 占位页：这一区域还由原生窗口承担。B1 期间带一个“打开原生窗口”的临时按钮；页面真正迁进来
 * 时，这个按钮、这份映射和这个文件连同它的路由一起删除。
 */
@customElement('wg-placeholder')
export class WgPlaceholder extends LitElement {
  /** 路由名，标题取对应的 Shell.* 键。 */
  @property()
  area = '';

  static styles = css`
    :host {
      display: block;
      padding: var(--spacingVerticalL) var(--spacingHorizontalXXL);
      max-width: 640px;
    }

    h1 {
      font-size: var(--font-size-base-500);
      font-weight: var(--font-weight-semibold);
      margin: 0 0 var(--spacingVerticalM);
    }

    p {
      color: var(--colorNeutralForeground2);
      margin: 0 0 var(--spacingVerticalL);
    }

    fluent-button {
      margin-top: var(--spacingVerticalS);
    }
  `;

  private unsubscribe?: () => void;

  connectedCallback(): void {
    super.connectedCallback();
    this.unsubscribe = strings.subscribe(() => this.requestUpdate());
  }

  disconnectedCallback(): void {
    this.unsubscribe?.();
    super.disconnectedCallback();
  }

  render() {
    const native = nativeMethods[this.area];
    return html`
      <h1>${t(areaKeys[this.area] ?? 'Shell.Placeholder.Body')}</h1>
      <p>${t('Shell.Placeholder.Body')}</p>
      <fluent-badge appearance="subtle">${t('Shell.Status.Planned')}</fluent-badge>
      ${native
        ? html`<fluent-button appearance="accent" @click=${() => this.openNative(native)}>
            ${t('Shell.OpenNative')}
          </fluent-button>`
        : nothing}
    `;
  }

  private async openNative(method: string): Promise<void> {
    try {
      await bridge.value.call(method);
    } catch (error) {
      // The native window is a temporary door; a failure is left to the console until the page
      // that replaces this placeholder owns its own error state.
      console.error('native window request failed', error);
    }
  }
}

declare global {
  interface HTMLElementTagNameMap {
    'wg-placeholder': WgPlaceholder;
  }
}
