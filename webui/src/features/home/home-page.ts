import { LitElement, css, html } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import { appInfo } from '../../shared/app/app.js';
import { project } from '../../shared/project/store.js';
import { go, type Route } from '../../shared/navigation/router.js';
import { strings, t } from '../../shared/strings/strings.js';
import { StoreController } from '../../shared/lit/store-controller.js';
import type { AppInfo, ProjectState } from '../../shared/bridge/protocol.js';

/** The first page: what build and project are open, and where each part of the work lives. */
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
      max-width: 900px;
    }

    h1 {
      font-size: var(--fontSizeBase500);
      font-weight: var(--fontWeightSemibold);
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

    .links {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
      gap: var(--spacingHorizontalS);
    }

    .link {
      display: grid;
      gap: var(--spacingVerticalXS);
      padding: var(--spacingVerticalM);
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke1);
      border-radius: var(--borderRadiusMedium);
      background: var(--colorNeutralBackground1);
      color: inherit;
      font: inherit;
      text-align: left;
      cursor: default;
    }

    .link:hover {
      background: var(--colorNeutralBackground1Hover);
    }

    .link strong {
      font-size: var(--fontSizeBase300);
      font-weight: var(--fontWeightSemibold);
    }

  `;

  protected readonly stringsController = new StoreController(this, strings);

  protected readonly appInfoController = new StoreController(this, appInfo, (info) => {
    this.info = info;
  });

  protected readonly projectController = new StoreController(this, project, (state) => {
    this.document = state;
  });

  render() {
    return html`
      <h1>${t('Shell.Home.Heading')}</h1>
      <p class="intro">${t('Shell.Home.Intro')}</p>
      ${this.renderInfo()}
      <div class="links">
        ${this.link('macros', 'Shell.Macros')}
        ${this.link('editor', 'Shell.Editor')}
        ${this.link('run', 'Shell.Run')}
        ${this.link('library', 'Shell.Library')}
        ${this.link('settings', 'Shell.Settings')}
        ${this.link('about', 'Shell.About')}
      </div>
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

  private link(route: Route, title: string) {
    return html`
      <button type="button" class="link" @click=${() => go(route)}>
        <strong>${t(title)}</strong>
      </button>
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
