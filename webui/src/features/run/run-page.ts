import { LitElement, css, html, nothing } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import { bridge } from '../../shared/bridge/bridge.js';
import type { ProjectState } from '../../shared/bridge/protocol.js';
import { strings, t } from '../../shared/strings/strings.js';

/** Selects the macro the native run-and-debug window should load. */
@customElement('wg-run')
export class WgRun extends LitElement {
  @state()
  private project: ProjectState | null = null;

  @state()
  private error = '';

  private unsubscribe?: () => void;

  static styles = css`
    :host {
      display: block;
      padding: var(--spacingVerticalL) var(--spacingHorizontalXXL);
      max-width: 900px;
    }

    h1 {
      margin: 0 0 var(--spacingVerticalS);
      font-size: var(--font-size-base-500);
      font-weight: var(--font-weight-semibold);
    }

    .note {
      margin: 0 0 var(--spacingVerticalL);
      color: var(--colorNeutralForeground3);
    }

    .macros {
      display: grid;
      gap: var(--spacingVerticalS);
    }

    .macro {
      display: grid;
      grid-template-columns: minmax(0, 1fr) auto;
      align-items: center;
      gap: var(--spacingHorizontalM);
      padding: var(--spacingVerticalS) var(--spacingHorizontalM);
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
      border-radius: var(--borderRadiusMedium);
      background: var(--colorNeutralBackground1);
    }

    .macro small {
      color: var(--colorNeutralForeground3);
    }
  `;

  connectedCallback(): void {
    super.connectedCallback();
    this.unsubscribe = strings.subscribe(() => this.requestUpdate());
    void this.load();
  }

  disconnectedCallback(): void {
    this.unsubscribe?.();
    super.disconnectedCallback();
  }

  render() {
    if (!this.project) {
      return html`<h1>${t('Run.Title')}</h1>
        ${this.error ? html`<p class="note">${this.error}</p>` : nothing}`;
    }

    return html`
      <h1>${t('Run.Title')}</h1>
      <p class="note">${t('Run.NativeNote')}</p>
      ${this.error ? html`<p class="note">${this.error}</p>` : nothing}
      <div class="macros">
        ${this.project.macros.map((macro) => html`
          <div class="macro">
            <div>
              <strong>${macro.name}</strong>
              <div><small>${macro.group || ''}</small></div>
            </div>
            <fluent-button appearance="accent" @click=${() => void this.run(macro.name)}>
              ${t('Run.Run')}
            </fluent-button>
          </div>
        `)}
      </div>
    `;
  }

  private async load(): Promise<void> {
    try {
      this.project = await bridge.value.call<ProjectState>('project.state');
    } catch (error) {
      this.error = String(error);
    }
  }

  private async run(name: string): Promise<void> {
    try {
      await bridge.value.call('app.openRun', { name });
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
