import { LitElement, css, html, nothing } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import { appInfo } from '../../shared/app/app.js';
import { bridge } from '../../shared/bridge/bridge.js';
import type { AboutState, AppInfo } from '../../shared/bridge/protocol.js';
import { strings, t } from '../../shared/strings/strings.js';
import wechat from '../../assets/donate-wechat.png';
import alipay from '../../assets/donate-alipay.png';

/** About, the project link, the support codes and the packed third-party notices. */
@customElement('wg-about')
export class WgAbout extends LitElement {
  @state()
  private info: AppInfo | null = null;

  @state()
  private about: AboutState | null = null;

  @state()
  private notices = false;

  @state()
  private error = '';

  private readonly unsubscribers: Array<() => void> = [];

  static styles = css`
    :host {
      display: block;
      padding: var(--spacingVerticalL) var(--spacingHorizontalXXL);
      max-width: 820px;
    }

    h1 {
      margin: 0 0 var(--spacingVerticalS);
      font-size: var(--font-size-base-500);
      font-weight: var(--font-weight-semibold);
    }

    .version {
      color: var(--colorNeutralForeground3);
      margin-bottom: var(--spacingVerticalL);
    }

    .card {
      display: grid;
      gap: var(--spacingVerticalM);
      padding: var(--spacingVerticalL) var(--spacingHorizontalL);
      margin-bottom: var(--spacingVerticalM);
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
      border-radius: var(--borderRadiusLarge);
      background: var(--colorNeutralBackground1);
    }

    .about-text,
    .support-text,
    .notices {
      white-space: pre-wrap;
      overflow-wrap: anywhere;
      line-height: 1.55;
    }

    .about-text {
      margin: 0;
    }

    h2 {
      margin: 0;
      font-size: var(--font-size-base-400);
    }

    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--spacingHorizontalS);
    }

    .codes {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
      gap: var(--spacingHorizontalM);
    }

    .code {
      display: grid;
      justify-items: center;
      gap: var(--spacingVerticalS);
      padding: var(--spacingVerticalM);
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
      border-radius: var(--borderRadiusMedium);
      background: var(--colorNeutralBackground2);
    }

    .code img {
      width: 190px;
      height: 190px;
      background: #fff;
      padding: 6px;
      border-radius: var(--borderRadiusSmall);
    }

    .code span {
      font-size: var(--font-size-base-200);
    }

    .notices {
      margin: 0;
      font-family: var(--fontFamilyMonospace);
      font-size: var(--font-size-base-200);
      color: var(--colorNeutralForeground2);
    }

    .error {
      color: var(--colorPaletteRedForeground1);
    }
  `;

  connectedCallback(): void {
    super.connectedCallback();
    this.unsubscribers.push(
      strings.subscribe(() => this.requestUpdate()),
      appInfo.subscribe((info) => {
        this.info = info;
      }),
    );
    void this.load();
  }

  disconnectedCallback(): void {
    for (const unsubscribe of this.unsubscribers.splice(0)) {
      unsubscribe();
    }
    super.disconnectedCallback();
  }

  render() {
    const version = this.info?.version || t('Main.AboutUnknownVersion');
    const text = t('Main.AboutText').replace('{0}', version);
    return html`
      <h1>${t('Main.About')}</h1>
      <div class="version">${t('Shell.Field.Version')} ${version}</div>
      ${this.error ? html`<p class="error">${this.error}</p>` : nothing}
      <section class="card">
        <p class="about-text">${text}</p>
        <div class="actions">
          <fluent-button appearance="accent" @click=${() => void this.openProject()}>
            ${t('Main.AboutOpenPage')}
          </fluent-button>
          <fluent-button appearance="subtle" @click=${() => { this.notices = !this.notices; }}>
            ${t('Main.AboutNotices')}
          </fluent-button>
        </div>
      </section>
      <section class="card">
        <h2>${t('About.SupportTitle')}</h2>
        <p class="support-text">${t('About.SupportText')}</p>
        <div class="codes">
          <div class="code">
            <img src=${wechat} alt=${t('About.WeChat')} />
            <span>${t('About.WeChat')}</span>
          </div>
          <div class="code">
            <img src=${alipay} alt=${t('About.Alipay')} />
            <span>${t('About.Alipay')}</span>
          </div>
        </div>
      </section>
      ${this.notices
        ? html`<section class="card" id="notices">
            <h2>${t('Main.AboutNotices')}</h2>
            <pre class="notices">${this.about?.notices ?? ''}</pre>
          </section>`
        : nothing}
    `;
  }

  private async load(): Promise<void> {
    try {
      this.about = await bridge.value.call<AboutState>('app.about');
    } catch (error) {
      this.error = String(error);
    }
  }

  private async openProject(): Promise<void> {
    if (!this.about) {
      return;
    }
    try {
      await bridge.value.call('app.openExternal', { url: this.about.projectUrl });
    } catch (error) {
      this.error = String(error);
    }
  }
}

declare global {
  interface HTMLElementTagNameMap {
    'wg-about': WgAbout;
  }
}