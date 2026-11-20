import { LitElement, css, html } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import { appInfo } from '../../shared/app/app.js';
import { strings, t } from '../../shared/strings/strings.js';
import type { AppInfo } from '../../shared/bridge/protocol.js';

/**
 * 首页：程序与项目的现状，加上迁移进度。这一页就是壳的"能用"证明——版本、语言、项目与
 * 宏数量都从桥上真读。
 */
@customElement('wg-home')
export class WgHomePage extends LitElement {
  @state()
  private info: AppInfo | null = null;

  static styles = css`
    :host {
      display: block;
      padding: var(--spacingVerticalL) var(--spacingHorizontalXXL);
      max-width: 760px;
    }

    h1 {
      font-size: var(--font-size-base-500);
      font-weight: var(--font-weight-semibold);
      margin: 0 0 var(--spacingVerticalS);
    }

    .intro {
      margin: 0 0 var(--spacingVerticalL);
      color: var(--colorNeutralForeground2);
    }

    .grid {
      display: grid;
      grid-template-columns: max-content 1fr;
      gap: var(--spacingVerticalS) var(--spacingHorizontalXXL);
      background: var(--colorNeutralBackground1);
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke1);
      border-radius: var(--borderRadiusLarge);
      padding: var(--spacingVerticalL) var(--spacingHorizontalL);
      margin-bottom: var(--spacingVerticalXXL);
    }

    .label {
      color: var(--colorNeutralForeground3);
    }

    .areas {
      display: grid;
      gap: var(--spacingVerticalS);
    }

    .area {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--spacingHorizontalL);
      padding: var(--spacingVerticalMN) var(--spacingHorizontalL);
      background: var(--colorNeutralBackground1);
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke1);
      border-radius: var(--borderRadiusMedium);
    }
  `;

  private unsubscribers: Array<() => void> = [];

  connectedCallback(): void {
    super.connectedCallback();
    this.unsubscribers = [
      strings.subscribe(() => this.requestUpdate()),
      appInfo.subscribe((info) => {
        this.info = info;
      }),
    ];
  }

  disconnectedCallback(): void {
    for (const unsubscribe of this.unsubscribers) {
      unsubscribe();
    }
    this.unsubscribers = [];
    super.disconnectedCallback();
  }

  render() {
    return html`
      <h1>${t('Shell.Home.Heading')}</h1>
      <p class="intro">${t('Shell.Home.Intro')}</p>
      ${this.renderInfo()}
      ${this.renderAreas()}
    `;
  }

  private renderInfo() {
    if (!this.info) {
      return null;
    }

    return html`
      <div class="grid">
        <span class="label">${t('Shell.Field.Version')}</span>
        <span>${this.info.version}</span>
        <span class="label">${t('Shell.Field.Language')}</span>
        <span>${this.info.language}</span>
        <span class="label">${t('Shell.Field.Project')}</span>
        <span>${this.projectName()}</span>
        <span class="label">${t('Shell.Field.Dirty')}</span>
        <span>${this.info.project.dirty ? t('Shell.Field.Dirty') : t('Shell.Field.Clean')}</span>
        <span class="label">${t('Shell.Field.Revision')}</span>
        <span>${this.info.project.revision}</span>
        <span class="label">${t('Shell.Field.Macros')}</span>
        <span>${this.info.project.macros}</span>
      </div>
    `;
  }

  private renderAreas() {
    return html`
      <div class="areas">
        <div class="area">
          <span>${t('Shell.Home')}</span>
          <fl-badge appearance="success">${t('Shell.Status.Web')}</fl-badge>
        </div>
        <div class="area">
          <span>${t('Shell.Macros')}</span>
          <fl-badge appearance="subtle">${t('Shell.Status.Native')}</fl-badge>
        </div>
        <div class="area">
          <span>${t('Shell.Editor')}</span>
          <fl-badge appearance="subtle">${t('Shell.Status.Native')}</fl-badge>
        </div>
        <div class="area">
          <span>${t('Shell.Run')}</span>
          <fl-badge appearance="subtle">${t('Shell.Status.Native')}</fl-badge>
        </div>
        <div class="area">
          <span>${t('Shell.Settings')}</span>
          <fl-badge appearance="subtle">${t('Shell.Status.Planned')}</fl-badge>
        </div>
      </div>
    `;
  }

  private projectName(): string {
    if (!this.info || this.info.project.untitled) {
      return t('Shell.UntitledProject');
    }
    return this.info.project.name ?? '';
  }
}

declare global {
  interface HTMLElementTagNameMap {
    'wg-home': WgHomePage;
  }
}
