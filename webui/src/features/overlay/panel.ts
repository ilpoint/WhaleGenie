import { LitElement, css, html } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import { bridgeApi } from '../../shared/bridge/bridge.js';
import type { SurfacePanelSnapshot } from '../../shared/bridge/protocol.js';
import { strings } from '../../shared/strings/strings.js';
import { StoreController } from '../../shared/lit/store-controller.js';

/**
 * 界面宏浮层：一组宏按标题排开，横向或纵向。窗口规则、锚定位置与出现时机都在 C#，
 * 页面只负责画出来并把点击交回去。
 */
@customElement('wg-overlay-panel')
export class WgOverlayPanel extends LitElement {
  @state()
  private snapshot: SurfacePanelSnapshot | null = null;

  @state()
  private error = '';

  protected readonly stringsController = new StoreController(this, strings);

  static styles = css`
    :host {
      display: block;
      width: max-content;
      background: transparent;
      font-family: var(--fontFamilyBase);
      color: var(--colorNeutralForeground1);
      user-select: none;
    }

    .panel {
      display: flex;
      flex-direction: column;
      gap: 4px;
      padding: 8px;
      border-radius: 10px;
      background: var(--colorNeutralBackground1);
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
      box-shadow: var(--shadow8);
    }

    .panel.horizontal {
      flex-direction: row;
      align-items: center;
    }

    .title {
      font-size: 11px;
      color: var(--colorNeutralForeground3);
      margin-bottom: 2px;
    }

    button.member {
      padding: 6px 10px;
      border: none;
      border-radius: 6px;
      background: var(--colorNeutralBackground3);
      color: inherit;
      font: inherit;
      text-align: left;
      cursor: pointer;
    }

    button.member:hover {
      background: var(--colorNeutralBackground3Hover);
    }

    button.member.disabled {
      opacity: 0.5;
      cursor: default;
    }

    .note {
      padding: 8px;
      font-size: 12px;
      color: var(--colorNeutralForeground3);
    }
  `;

  protected override async firstUpdated(): Promise<void> {
    await this.reload();
    window.addEventListener('focus', () => void this.reload());
  }

  private async reload(): Promise<void> {
    try {
      this.snapshot = await bridgeApi.call<SurfacePanelSnapshot>('surface.panel.state',
        { groupKey: '', horizontal: false, collapsed: false });
      this.error = '';
    } catch (failure) {
      this.error = failure instanceof Error ? failure.message : String(failure);
    }
  }

  protected override render() {
    if (this.error.length > 0) {
      return html`<p class="note">${this.error}</p>`;
    }

    if (!this.snapshot) {
      return html`<p class="note">…</p>`;
    }

    const snapshot = this.snapshot;
    return html`
      <div class="panel ${snapshot.horizontal ? 'horizontal' : ''}">
        <span class="title">${snapshot.title}</span>
        ${snapshot.items.length === 0
          ? html`<span class="note">${strings.t('Interface.Panel')}</span>`
          : snapshot.items.map((item) => html`
              <button class="member ${item.state === 'visible' ? '' : 'disabled'}"
                title=${item.label}>${item.label}</button>
            `)}
      </div>
    `;
  }
}
