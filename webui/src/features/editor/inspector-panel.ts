import { LitElement, css, html } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import { t } from '../../shared/strings/strings.js';
import { editor } from './editor-store.js';
import { StoreController } from '../../shared/lit/store-controller.js';
import { editorBase } from './editor-css.js';
import './variable-wall.js';
import './macro-settings.js';
import './editor-variable-dialogs.js';

/**
 * The right side of the workbench, in the shape the native window had: the variable center on
 * top and the macro settings below it, both visible at once. The step form is a dialog of its
 * own, so it no longer competes with these two for the column.
 */
@customElement('wg-editor-inspector')
export class WgEditorInspector extends LitElement {
  @state()
  private localsOpen = false;

  @state()
  private globalsOpen = false;

  @state()
  private split = 0.66;

  static styles = [editorBase, css`
    :host {
      display: flex;
      flex-direction: column;
      height: 100%;
      min-height: 0;
      min-width: 0;
      background: var(--colorNeutralBackground1);
      border-left: var(--strokeWidthThin) solid var(--colorNeutralStroke1);
    }

    .panes {
      display: grid;
      grid-template-rows: minmax(180px, var(--variable-fr, 2fr)) 6px minmax(240px, 1fr);
      flex: 1;
      min-height: 0;
    }

    .pane {
      min-height: 0;
      min-width: 0;
      overflow: hidden;
    }

    .pane.variable {
      background: var(--colorNeutralBackground2);
      border-bottom: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
    }

    .pane.macro {
      background: var(--colorNeutralBackground1);
      border-top: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
    }

    .pane wg-variable-wall,
    .pane wg-macro-settings {
      height: 100%;
      min-height: 0;
    }

    .splitter {
      cursor: row-resize;
      background: var(--colorNeutralBackground1);
      border-top: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
      border-bottom: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
    }

    .splitter:hover,
    .splitter:focus-visible {
      background: var(--colorBrandBackground2);
      outline: none;
    }

    .empty {
      padding: var(--spacingVerticalXXL) var(--spacingHorizontalL);
      color: var(--colorNeutralForeground3);
      text-align: center;
      font-size: var(--fontSizeBase200);
    }
  `];

  protected readonly editorController = new StoreController<void>(this, editor);

  private dragStartY = 0;

  private dragStartRatio = 0.66;

  render() {
    const state = editor.get();
    if (!state.snapshot) {
      return html`<div class="empty">${t('Editor.NoDocument')}</div>`;
    }

    return html`
      <div class="panes" style=${'--variable-fr: ' + String(this.split) + 'fr'}>
        <div class="pane variable">
          <wg-variable-wall
            @wg-manage-locals=${() => { this.localsOpen = true; }}
            @wg-manage-globals=${() => { this.globalsOpen = true; }}
          ></wg-variable-wall>
        </div>
        <div class="splitter" role="separator" tabindex="0"
          aria-orientation="horizontal" aria-label=${t('Editor.VariableCenter')}
          @pointerdown=${this.onSplitDown}
          @pointermove=${this.onSplitMove}
          @pointerup=${this.onSplitUp}
          @dblclick=${() => { this.split = 0.66; }}
          @keydown=${(event: KeyboardEvent) => {
            if (event.key === 'ArrowUp') {
              this.split = Math.max(0.3, this.split - 0.04);
            } else if (event.key === 'ArrowDown') {
              this.split = Math.min(0.78, this.split + 0.04);
            }
          }}></div>
        <div class="pane macro">
          <wg-macro-settings></wg-macro-settings>
        </div>
      </div>
      <wg-local-variables .open=${this.localsOpen}
        @wg-close=${() => { this.localsOpen = false; }}></wg-local-variables>
      <wg-global-variables .open=${this.globalsOpen}
        @wg-close=${() => { this.globalsOpen = false; }}></wg-global-variables>
    `;
  }

  private onSplitDown = (event: PointerEvent): void => {
    event.preventDefault();
    this.dragStartY = event.clientY;
    this.dragStartRatio = this.split;
    (event.target as HTMLElement).setPointerCapture(event.pointerId);
  };

  private onSplitMove = (event: PointerEvent): void => {
    if (!(event.target as HTMLElement).hasPointerCapture(event.pointerId)) {
      return;
    }

    const height = this.renderRoot.querySelector('.panes')?.getBoundingClientRect().height ?? 0;
    if (height > 0) {
      this.split = Math.min(0.78, Math.max(0.3, this.dragStartRatio
        + (event.clientY - this.dragStartY) / height));
    }
  };

  private onSplitUp = (event: PointerEvent): void => {
    const target = event.target as HTMLElement;
    if (target.hasPointerCapture(event.pointerId)) {
      target.releasePointerCapture(event.pointerId);
    }
  };
}

declare global {
  interface HTMLElementTagNameMap {
    'wg-editor-inspector': WgEditorInspector;
  }
}
