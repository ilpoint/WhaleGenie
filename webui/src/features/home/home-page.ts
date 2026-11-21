import { LitElement, css, html } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import { appInfo } from '../../shared/app/app.js';
import { project } from '../../shared/project/store.js';
import { strings, t } from '../../shared/strings/strings.js';
import type { AppInfo, ProjectState } from '../../shared/bridge/protocol.js';

/**
 * 首页：程序与项目的现况，加上迁移进度。这一页就是壳的"能用"证明——版本与语言从 app.info
 * 上读，项目的每一格都跟文档走（打开、另存、MCP 修改都会推 project.changed，与列表同源）。
 */
@customElement('wg-home')
export class WgHomePage extends LitElement {
  @state()
  private info: AppInfo | null = null;

  @state()
  private document: ProjectState | null = null;

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
      project.subscribe((state) => {
        this.document = state;
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
    if (this.info === null) {
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
        <span>${this.document?.dirty ? t('Shell.Field.Dirty') : t('Shell.Field.Clean')}</span>
        <span class="label">${t('Shell.Field.Revision')}</span>
        <span>${this.document?.revision ?? 0}</span>
        <span class="label">${t('Shell.Field.Macros')}</span>
        <span>${this.document?.macros.length ?? 0}</span>
      </div>
    `;
  }

  private renderAreas() {
    return html`
      <div class="areas">
        <div class="area">
          <span>${t('Shell.Home')}</span>
          <fluent-badge appearance="success">${t('Shell.Status.Web')}</fluent-badge>
        </div>
        <div class="area">
          <span>${t('Shell.Macros')}</span>
          <fluent-badge appearance="subtle">${t('Shell.Status.Native')}</fluent-badge>
        </div>
        <div class="area">
          <span>${t('Shell.Editor')}</span>
          <fluent-badge appearance="subtle">${t('Shell.Status.Native')}</fluent-badge>
        </div>
        <div class="area">
          <span>${t('Shell.Run')}</span>
          <fluent-badge appearance="subtle">${t('Shell.Status.Native')}</fluent-badge>
        </div>
        <div class="area">
          <span>${t('Shell.Settings')}</span>
          <fluent-badge appearance="subtle">${t('Shell.Status.Planned')}</fluent-badge>
        </div>
      </div>
    `;
  }

  private projectName(): string {
    if (this.document === null || this.document.untitled) {
      return t('Shell.UntitledProject');
    }
    return this.document.name ?? '';
  }
}

declare global {
  interface HTMLElementTagNameMap {
    'wg-home': WgHomePage;
  }
}
