import { LitElement, css, html, nothing } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import { t } from '../../shared/strings/strings.js';
import type { EditorValueChoice } from '../../shared/bridge/protocol.js';
import { editor } from './editor-store.js';
import { StoreController } from '../../shared/lit/store-controller.js';
import { editorBase } from './editor-css.js';
import { wallVariables, type VariableGroupFilter } from './editor-model.js';

/**
 * The variable center: every name this macro can read, filtered by the group the C# snapshot
 * assigned. It is a wall to point at, not a second editor; the management dialogs are behind the
 * two buttons at the top.
 */
@customElement('wg-variable-wall')
export class WgVariableWall extends LitElement {
  @state()
  private filter: VariableGroupFilter = 'All';

  @state()
  private search = '';

  @state()
  private looking = '';

  static styles = [editorBase, css`
    :host {
      display: flex;
      flex-direction: column;
      min-height: 0;
      padding: var(--spacingVerticalM) var(--spacingHorizontalL);
    }

    .head {
      display: flex;
      align-items: center;
      gap: var(--spacingHorizontalXS);
      margin-bottom: var(--spacingVerticalS);
    }

    .head h3 {
      margin: 0 auto 0 0;
      font-size: var(--fontSizeBase300);
      font-weight: var(--fontWeightSemibold);
    }

    .search {
      flex: none;
      width: 100%;
      margin-bottom: var(--spacingVerticalS);
      padding: var(--spacingVerticalXS) var(--spacingHorizontalS);
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke1);
      border-bottom: var(--strokeWidthThicker) solid var(--colorNeutralStrokeAccessible);
      border-radius: var(--borderRadiusMedium);
      background: var(--colorNeutralBackground1);
      color: var(--colorNeutralForeground1);
      font: inherit;
      font-size: var(--fontSizeBase200);
    }

    .search:focus-visible {
      outline: var(--strokeWidthThicker) solid var(--colorStrokeFocus2);
      outline-offset: -1px;
    }

    .filters {
      margin-bottom: var(--spacingVerticalS);
    }

    .filters button {
      font-size: var(--fontSizeBase100);
      min-height: 22px;
      padding: 1px 6px;
    }

    .wall {
      display: flex;
      flex: 1;
      flex-wrap: wrap;
      gap: 5px;
      align-content: flex-start;
      min-height: 0;
      overflow: auto;
      padding-bottom: var(--spacingVerticalS);
    }

    .var {
      gap: 4px;
      min-height: 25px;
      padding: 2px 7px;
      font-size: var(--fontSizeBase200);
      text-align: left;
    }

    .var .name {
      color: var(--colorNeutralForeground3);
      font-family: var(--fontFamilyMonospace);
      font-size: var(--fontSizeBase100);
    }

    .var .saved {
      padding: 0 4px;
      border-radius: var(--borderRadiusSmall);
      background: var(--colorNeutralBackground2);
      color: var(--colorNeutralForeground3);
      font-size: var(--fontSizeBase100);
    }

    .empty {
      padding: var(--spacingVerticalL) var(--spacingHorizontalS);
      color: var(--colorNeutralForeground3);
      font-size: var(--fontSizeBase200);
      text-align: center;
    }

    .details {
      flex: none;
      min-height: 32px;
      padding: var(--spacingVerticalXS) 0 0;
      border-top: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
      color: var(--colorNeutralForeground3);
      font-size: var(--fontSizeBase100);
      line-height: 1.4;
      overflow-wrap: anywhere;
    }
  `];

  protected readonly editorController = new StoreController<void>(this, editor);

  render() {
    const snapshot = editor.get().snapshot;
    const choices = wallVariables(snapshot?.valueChoices ?? [], this.filter, this.search);
    const looking = choices.find((choice) => choice.name === this.looking);
    const filters: { value: VariableGroupFilter; label: string; hint: string }[] = [
      { value: 'All', label: t('VarWall.All'), hint: t('VarWall.AllHint') },
      { value: 'Local', label: t('Variable.Tab.Local'), hint: t('VarWall.LocalHint') },
      { value: 'Global', label: t('Variable.Tab.Global'), hint: t('VarWall.GlobalHint') },
      { value: 'System', label: t('VarWall.System'), hint: t('VarWall.SystemHint') },
      { value: 'StepResult', label: t('VarWall.StepResult'), hint: t('VarWall.StepResultHint') },
    ];

    return html`
      <div class="head">
        <h3>${t('Editor.VariableCenter')}</h3>
        <button class="value-chip" type="button" title=${t('Editor.ManageLocalsHint')}
          @click=${() => this.emit('wg-manage-locals')}>
          ${t('Editor.ManageLocals')}
        </button>
        <button class="value-chip" type="button" title=${t('Editor.ManageGlobalsHint')}
          @click=${() => this.emit('wg-manage-globals')}>
          ${t('Editor.ManageGlobals')}
        </button>
      </div>
      <input class="search" type="search" .value=${this.search}
        placeholder=${t('VarWall.Search')} aria-label=${t('VarWall.Search')}
        @input=${(event: Event) => {
          this.search = (event.target as HTMLInputElement).value;
        }} />
      <div class="filters dock" role="group" aria-label=${t('Editor.VariableCenter')}>
        ${filters.map((filter) => html`
          <button type="button" title=${filter.hint}
            aria-pressed=${this.filter === filter.value ? 'true' : 'false'}
            @click=${() => {
              this.filter = filter.value;
              this.looking = '';
            }}>
            ${filter.label}
          </button>
        `)}
      </div>
      <div class="wall">
        ${choices.length === 0
          ? html`<p class="empty">${t('VarWall.Empty')}</p>`
          : choices.map((choice) => this.renderVariable(choice))}
      </div>
      <div class="details">
        ${looking
          ? [looking.description, looking.source].filter((text) => text.length > 0).join(' · ')
          : t('VarWall.Pick')}
      </div>
    `;
  }

  private renderVariable(choice: EditorValueChoice) {
    return html`
      <button
        type="button"
        class="chip var"
        aria-pressed=${this.looking === choice.name ? 'true' : 'false'}
        title=${[choice.description, choice.source].filter((text) => text.length > 0).join(' · ')}
        @pointerenter=${() => {
          this.looking = choice.name;
        }}
        @focus=${() => {
          this.looking = choice.name;
        }}
      >
        <span>${choice.display}</span>
        ${choice.display !== choice.name ? html`<span class="name">(${choice.name})</span>` : nothing}
        ${choice.persistent ? html`<span class="saved">${t('VarWall.Persistent')}</span>` : nothing}
      </button>
    `;
  }

  private emit(name: string): void {
    this.dispatchEvent(new CustomEvent(name, { bubbles: true, composed: true }));
  }
}

declare global {
  interface HTMLElementTagNameMap {
    'wg-variable-wall': WgVariableWall;
  }
}
