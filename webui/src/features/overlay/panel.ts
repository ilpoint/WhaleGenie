import { LitElement, css, html } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import { bridgeApi } from '../../shared/bridge/bridge.js';
import type { SurfacePanelSnapshot } from '../../shared/bridge/protocol.js';
import { strings, t } from '../../shared/strings/strings.js';
import { StoreController } from '../../shared/lit/store-controller.js';

/**
 * 界面宏浮层：一组宏按标题排开，横向或纵向、折叠或展开。窗口规则、锚定位置与出现时机都在 C#，
 * 页面只负责画出来并把点击与折叠交回去。
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

    .header {
      display: flex;
      align-items: center;
      gap: 6px;
    }

    .title {
      font-size: 11px;
      color: var(--colorNeutralForeground3);
      flex: 1;
    }

    .header button {
      width: 20px;
      height: 18px;
      padding: 0;
      border: none;
      border-radius: 4px;
      background: var(--colorNeutralBackground3);
      color: inherit;
      font-size: 11px;
      cursor: pointer;
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

  /** The group this window was opened for; an empty key means the first group with members. */
  private readonly groupKey = new URLSearchParams(window.location.search).get('group') ?? '';

  protected override async firstUpdated(): Promise<void> {
    await this.reload();
    window.addEventListener('focus', () => void this.reload());
    bridgeApi.on('surface.panel.changed', (next) => {
      this.snapshot = next;
    });
  }

  private async reload(): Promise<void> {
    try {
      this.snapshot = await bridgeApi.call<SurfacePanelSnapshot>('surface.panel.state',
        { groupKey: this.groupKey, horizontal: false, collapsed: false });
      this.error = '';
    } catch (failure) {
      this.error = failure instanceof Error ? failure.message : String(failure);
    }
  }

  private run(label: string): void {
    void bridgeApi.call('surface.panel.run', { name: label });
  }

  private async toggle(collapsed: boolean, horizontal: boolean): Promise<void> {
    this.snapshot = await bridgeApi.call<SurfacePanelSnapshot>('surface.panel.view',
      { groupKey: this.snapshot?.groupKey || this.groupKey, collapsed, horizontal });
  }

  private renderHeader(snapshot: SurfacePanelSnapshot) {
    return html`
      <div class="header" @pointerdown=${() => void bridgeApi.call('surface.panel.drag', { groupKey: snapshot.groupKey })}>
        <span class="title">${snapshot.title}</span>
        <button aria-label=${t('Interface.Collapse')}
          title=${snapshot.collapsed ? t('Interface.Expand') : t('Interface.Collapse')}
          @click=${() => void this.toggle(!snapshot.collapsed, snapshot.horizontal)}>
          ${snapshot.collapsed ? '▸' : '▾'}
        </button>
        <button aria-label=${t('Interface.Layout')} title=${t('Interface.Layout')}
          @click=${() => void this.toggle(snapshot.collapsed, !snapshot.horizontal)}>
          ${snapshot.horizontal ? '⇅' : '⇆'}
        </button>
      </div>
    `;
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
        ${this.renderHeader(snapshot)}
        ${snapshot.collapsed ? '' : snapshot.items.map((item) => html`
          <button class="member ${item.state === 'visible' ? '' : 'disabled'}"
            title=${item.label}
            @click=${() => {
              if (item.state === 'visible') {
                this.run(item.label);
              }
            }}>${item.label}</button>
        `)}
      </div>
    `;
  }
}
