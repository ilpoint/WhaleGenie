import { LitElement, css, html, nothing } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import { bridgeApi } from '../bridge/bridge.js';
import { t } from '../strings/strings.js';
import { clampPage, filterIcons, pageCount, pageIcons } from './icon-picker-model.js';
import './icon.js';

let catalogue: Promise<string[]> | null = null;

function iconNames(): Promise<string[]> {
  catalogue ??= bridgeApi.call<string[]>('icon.names').catch(() => []);
  return catalogue;
}

/** The Lucide/image chooser shared by menu cells and settings wheel slots. */
@customElement('wg-icon-picker')
export class WgIconPicker extends LitElement {
  @property({ type: Boolean })
  open = false;

  @property({ type: String })
  selectedKind = '';

  @property({ type: String })
  selectedValue = '';

  @property({ attribute: false })
  imagePicker?: () => Promise<string | null>;

  @state()
  private names: string[] = [];

  @state()
  private query = '';

  @state()
  private page = 0;

  @state()
  private loading = false;

  @state()
  private error = '';

  static styles = css`
    :host { display: contents; }
    .backdrop {
      position: fixed; inset: 0; z-index: 100;
      display: flex; align-items: center; justify-content: center;
      background: color-mix(in srgb, var(--colorNeutralBackground1) 55%, transparent);
    }
    .card {
      box-sizing: border-box; width: min(320px, calc(100vw - 24px));
      max-height: calc(100vh - 32px); display: flex; flex-direction: column; gap: 10px;
      padding: 12px; border: 1px solid var(--colorNeutralStroke1);
      border-radius: var(--borderRadiusXLarge); background: var(--colorNeutralBackground1);
      box-shadow: var(--shadow16); color: var(--colorNeutralForeground1);
    }
    header { display: flex; align-items: center; gap: 8px; font-weight: 600; }
    header .title { flex: 1; }
    input {
      box-sizing: border-box; width: 100%; height: 30px; padding: 0 8px;
      border: 1px solid var(--colorNeutralStroke1); border-radius: var(--borderRadiusMedium);
      background: var(--colorNeutralBackground1); color: inherit; font: inherit;
    }
    .grid {
      display: grid; grid-template-columns: repeat(7, minmax(0, 1fr)); gap: 2px;
      overflow-y: auto; min-height: 120px; flex: 1; scrollbar-width: thin;
    }
    .icon {
      height: 38px; border: 1px solid transparent; border-radius: var(--borderRadiusMedium);
      background: transparent; color: inherit; display: flex; align-items: center;
      justify-content: center; cursor: pointer; padding: 0;
    }
    .icon:hover { background: var(--colorNeutralBackground1Hover); }
    .icon.on { background: var(--colorBrandBackground2); border-color: var(--colorBrandStroke1); }
    .footer {
      display: flex; align-items: center; gap: 4px; flex-wrap: nowrap; white-space: nowrap;
    }
    .footer .spacer { flex: 1; }
    .footer button { flex: none; padding: 0 6px; }
    .footer .note { flex: none; white-space: nowrap; }
    button {
      height: 28px; padding: 0 10px; border: 1px solid var(--colorNeutralStroke1);
      border-radius: var(--borderRadiusMedium); background: var(--colorNeutralBackground1);
      color: inherit; font: inherit; cursor: pointer;
    }
    button:hover { background: var(--colorNeutralBackground1Hover); }
    button:disabled { opacity: 0.5; cursor: default; }
    .note { color: var(--colorNeutralForeground3); font-size: 12px; }
  `;

  protected override async updated(changed: Map<string, unknown>): Promise<void> {
    if (!changed.has('open') || !this.open || this.names.length > 0) {
      return;
    }

    this.loading = true;
    this.names = await iconNames();
    this.loading = false;
    this.error = this.names.length === 0 ? t('Menu.IconNoResults') : '';
    setTimeout(() => this.renderRoot.querySelector<HTMLInputElement>('input')?.focus(), 0);
  }

  private close(): void {
    this.dispatchEvent(new CustomEvent('icon-cancel', { bubbles: true, composed: true }));
  }

  private choose(name: string): void {
    this.dispatchEvent(new CustomEvent('icon-picked', {
      detail: { kind: 'builtin', value: name },
      bubbles: true,
      composed: true,
    }));
  }

  private async chooseImage(): Promise<void> {
    if (!this.imagePicker) {
      return;
    }

    const path = await this.imagePicker();
    if (path) {
      this.dispatchEvent(new CustomEvent('icon-picked', {
        detail: { kind: 'image', value: path },
        bubbles: true,
        composed: true,
      }));
    }
  }

  protected override render() {
    if (!this.open) {
      return nothing;
    }

    const matches = filterIcons(this.names, this.query);
    const count = pageCount(matches.length);
    const page = clampPage(this.page, count);
    const visible = pageIcons(matches, page);
    return html`
      <div class="backdrop icon-backdrop" @pointerdown=${(event: PointerEvent) => {
        if (event.target === event.currentTarget) {
          this.close();
        }
      }}>
        <div class="card" role="dialog" aria-modal="true">
          <header>
            <span class="title">${t('Menu.PickIcon')}</span>
            <button @click=${() => this.close()}>${t('Common.Cancel')}</button>
          </header>
          <input type="search" placeholder=${t('Menu.IconSearch')} .value=${this.query}
            @input=${(event: Event) => {
              this.query = (event.target as HTMLInputElement).value;
              this.page = 0;
            }} />
          <div class="grid">
            ${visible.map((name) => html`
              <button class="icon ${this.selectedKind === 'builtin' && this.selectedValue === name ? 'on' : ''}"
                title=${name} @click=${() => this.choose(name)}>
                <wg-icon .name=${name} .size=${20}></wg-icon>
              </button>`)}
          </div>
          ${this.loading ? html`<span class="note">…</span>` : nothing}
          ${!this.loading && matches.length === 0 ? html`<span class="note">${this.error || t('Menu.IconNoResults')}</span>` : nothing}
          <div class="footer">
            <button ?disabled=${page === 0} @click=${() => { this.page = page - 1; }}>${t('Menu.IconPreviousPage')}</button>
            <span class="note">${t('Menu.IconPage').replace('{0}', String(page + 1)).replace('{1}', String(count))}</span>
            <button ?disabled=${page >= count - 1} @click=${() => { this.page = page + 1; }}>${t('Menu.IconNextPage')}</button>
            <span class="spacer"></span>
            ${this.imagePicker ? html`<button @click=${() => void this.chooseImage()}>${t('Menu.PickImage')}</button>` : nothing}
          </div>
        </div>
      </div>
    `;
  }
}

declare global {
  interface HTMLElementTagNameMap {
    'wg-icon-picker': WgIconPicker;
  }
}