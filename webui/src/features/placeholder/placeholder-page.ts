import { LitElement, css, html } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import { strings, t } from '../../shared/strings/strings.js';

/** 路由名到词表键的显式映射，与导航的映射同形：键都是字面量，对账测试扫得到。 */
const areaKeys: Record<string, string> = {
  macros: 'Shell.Macros',
  editor: 'Shell.Editor',
  run: 'Shell.Run',
  settings: 'Shell.Settings',
};

/**
 * 占位页：这一区域还由原生窗口承担。占位页只说明现状，不带任何按钮——等区域真正迁进来，
 * 这个文件连同它的路由一起删除。
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
    return html`
      <h1>${t(areaKeys[this.area] ?? 'Shell.Placeholder.Body')}</h1>
      <p>${t('Shell.Placeholder.Body')}</p>
      <fl-badge appearance="subtle">${t('Shell.Status.Planned')}</fl-badge>
    `;
  }
}

declare global {
  interface HTMLElementTagNameMap {
    'wg-placeholder': WgPlaceholder;
  }
}
