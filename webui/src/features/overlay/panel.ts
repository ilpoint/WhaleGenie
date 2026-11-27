import { LitElement, css, html } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import { bridgeApi } from '../../shared/bridge/bridge.js';
import type { SurfacePanelSnapshot } from '../../shared/bridge/protocol.js';
import { strings, t } from '../../shared/strings/strings.js';
import { StoreController } from '../../shared/lit/store-controller.js';
import '../../shared/icons/icon.js';

/**
 * 界面宏浮层：一组宏按标题排开，横向或纵向、折叠或展开。窗口规则、锚定位置与出现时机都在 C#，
 * 页面只负责画出来并把点击与折叠交回去。
 *
 * 尺寸与 `PanelGeometry` 是同一组数：面板 86 宽、标题 20、成员 80×24、间距 2、最多 6 行。
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
      box-sizing: border-box;
      width: 86px;
      display: flex;
      flex-direction: column;
      border-radius: 6px;
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
      background: var(--colorNeutralBackground1);
      overflow: hidden;
    }

    .panel.horizontal {
      width: max-content;
      max-width: 500px;
    }

    /* The folded panel is one header, whichever way the members were arranged: the host keeps it
       86 wide, so the header must not grow to its content and push the two buttons out of the
       window where they cannot be clicked. */
    .panel.collapsed {
      width: 86px;
    }

    .header {
      box-sizing: border-box;
      height: 20px;
      flex: none;
      display: flex;
      align-items: center;
      gap: 2px;
      padding: 0 4px;
      background: var(--colorNeutralBackground2);
      cursor: move;
    }

    .title {
      flex: 1;
      min-width: 0;
      font-size: 10.5px;
      line-height: 1;
      color: var(--colorNeutralForeground2);
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .header button {
      width: 16px;
      height: 16px;
      padding: 0;
      flex: none;
      display: inline-flex;
      align-items: center;
      justify-content: center;
      border: none;
      border-radius: 3px;
      background: transparent;
      color: var(--colorNeutralForeground2);
      cursor: pointer;
    }

    .header button:hover {
      background: var(--colorNeutralBackground3Hover);
    }

    .members {
      display: flex;
      flex-direction: column;
      gap: 2px;
      padding: 2px;
      max-height: 154px;
      overflow-y: auto;
      scrollbar-width: none;
    }

    .members.horizontal {
      flex-direction: row;
      max-height: none;
      overflow-x: auto;
      overflow-y: hidden;
    }

    button.member {
      box-sizing: border-box;
      width: 80px;
      height: 24px;
      flex: none;
      padding: 0 4px;
      border: none;
      border-radius: 4px;
      background: var(--colorNeutralBackground3);
      color: inherit;
      font: inherit;
      font-size: 11px;
      line-height: 1;
      text-align: left;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
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

  private startDrag(event: PointerEvent): void {
    if (event.button !== 0 || (event.target as HTMLElement).closest('button')) {
      return;
    }

    const snapshot = this.snapshot;
    if (snapshot) {
      void bridgeApi.call('surface.panel.drag', { groupKey: snapshot.groupKey });
    }
  }

  private renderHeader(snapshot: SurfacePanelSnapshot) {
    return html`
      <div class="header" @pointerdown=${(event: PointerEvent) => this.startDrag(event)}>
        <span class="title" title=${snapshot.title}>${snapshot.title}</span>
        <button aria-label=${t('Interface.Collapse')}
          title=${snapshot.collapsed ? t('Interface.Expand') : t('Interface.Collapse')}
          @pointerdown=${(event: Event) => event.stopPropagation()}
          @click=${() => void this.toggle(!snapshot.collapsed, snapshot.horizontal)}>
          <wg-icon name=${snapshot.collapsed ? 'ChevronRight' : 'ChevronDown'} .size=${11}></wg-icon>
        </button>
        <button aria-label=${t('Interface.Layout')} title=${t('Interface.Layout')}
          @pointerdown=${(event: Event) => event.stopPropagation()}
          @click=${() => void this.toggle(snapshot.collapsed, !snapshot.horizontal)}>
          <wg-icon name=${snapshot.horizontal ? 'Rows3' : 'Columns3'} .size=${11}></wg-icon>
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
      <div class="panel ${snapshot.horizontal ? 'horizontal' : ''} ${snapshot.collapsed ? 'collapsed' : ''}">
        ${this.renderHeader(snapshot)}
        ${snapshot.collapsed ? '' : html`
          <div class="members ${snapshot.horizontal ? 'horizontal' : ''}">
            ${snapshot.items.map((item) => html`
              <button class="member ${item.state === 'visible' ? '' : 'disabled'}"
                title=${item.label}
                @click=${() => {
                  if (item.state === 'visible') {
                    this.run(item.label);
                  }
                }}>${item.label}</button>
            `)}
          </div>
        `}
      </div>
    `;
  }
}
