import { LitElement, html, nothing } from 'lit';
import { editorPageCss } from './editor-page-css.js';
import { customElement } from 'lit/decorators.js';
import { icon } from '../../shared/icons.js';
import { t } from '../../shared/strings/strings.js';
import { editor } from './editor-store.js';
import { findStep, flattenSteps } from './editor-model.js';
import { editorBase } from './editor-css.js';
import './editor-tabs.js';
import './editor-palette.js';
import './step-tree.js';
import './inspector-panel.js';
import './action-editor.js';
import './value-editor.js';

/**
 * The editor workbench: document tabs and the step list in the middle, the inspector on the right.
 * The action library is the shell's side bar (wg-editor-palette), so the palette is not duplicated
 * inside the page.
 */
@customElement('wg-editor')
export class WgEditor extends LitElement {
  static styles = [editorBase, editorPageCss];

  private unsubscribe?: () => void;

  private splitStartX = 0;

  private splitStartWidth = 0;

  connectedCallback(): void {
    super.connectedCallback();
    editor.ensureLoaded();
    editor.refresh();
    this.unsubscribe = editor.subscribe(() => this.requestUpdate());
    window.addEventListener('keydown', this.onKeydown);
  }

  disconnectedCallback(): void {
    this.unsubscribe?.();
    window.removeEventListener('keydown', this.onKeydown);
    editor.leaveEditor();
    super.disconnectedCallback();
  }

  render() {
    const state = editor.get();
    const snapshot = state.snapshot;
    const hasStep = state.selectedStepIds.length > 0;
    const singleStep = state.selectedStepIds.length === 1;
    return html`
      <h1 class="sr-only">${t('Shell.Editor')}</h1>
      ${state.notice
        ? html`<div class="notice">${state.notice}</div>`
        : nothing}
      ${state.error
        ? html`
          <div class="error">
            <fluent-message-bar intent="error" layout="multiline"
              @dismiss=${() => editor.clearError()}> ${state.error} </fluent-message-bar>
          </div>
        `
        : nothing}
      ${snapshot === null
        ? html`
          <div class="empty">
            <span class="title">${t('Editor.NoDocument')}</span>
            <span>${t('Editor.NoDocumentHint')}</span>
            <fluent-button appearance="accent" @click=${() => void editor.newDocument()}>
              ${t('Main.AddMacro')}
            </fluent-button>
          </div>
        `
        : html`
          <div class="workspace">
            <div class="palette-column"><wg-editor-palette></wg-editor-palette></div>
            <div class="palette-splitter" aria-hidden="true"></div>
            <div class="center">
              <wg-editor-tabs></wg-editor-tabs>
              <div class="toolbar" role="toolbar" aria-label=${t('Editor.Steps')}>
                ${this.iconButton('undo', t('Palette.Undo'), !snapshot.canUndo,
                  () => void editor.undo())}
                ${this.iconButton('redo', t('Palette.Redo'), !snapshot.canRedo,
                  () => void editor.redo())}
                ${this.iconButton('edit', t('Palette.Edit'), !singleStep,
                  () => editor.openStepEditor())}
                ${this.iconButton('check', t('Palette.ToggleEnabled'), !hasStep,
                  () => {
                    const first = state.snapshot && state.selectedStepIds[0]
                      ? findStep(state.snapshot.steps, state.selectedStepIds[0])
                      : null;
                    if (first) {
                      void editor.setStepEnabled(undefined, !first.enabled);
                    }
                  })}
                ${this.iconButton('copy', t('Palette.Copy'), !hasStep,
                  () => void editor.copySteps())}
                ${this.iconButton('cut', t('Palette.Cut'), !hasStep,
                  () => void editor.cutSteps())}
                ${this.iconButton('paste', t('Palette.Paste'), false,
                  () => void editor.pasteSteps(state.selectedStepId))}
                ${this.iconButton('group', t('Palette.Group'), !hasStep,
                  () => void editor.groupSteps())}
                ${this.iconButton('export', t('Palette.LiftToMacro'), !hasStep,
                  () => void editor.liftSteps())}
                <span class="sep"></span>
                <fluent-button appearance="subtle"
                  title=${state.selectedActionKey.length === 0
                    ? t('Editor.PickActionHint') : t('Editor.AddStep')}
                  ?disabled=${state.selectedActionKey.length === 0}
                  @click=${() => void editor.addSelectedStep()}>
                  ${t('Editor.AddStep')}
                </fluent-button>
                ${this.iconButton('copy', t('Editor.DuplicateStep'), !hasStep,
                  () => void editor.duplicateStep())}
                ${this.iconButton('delete', t('Editor.Delete'), !hasStep,
                  () => void editor.removeStep())}
                ${this.iconButton('arrow-up', t('Palette.MoveUp'), !hasStep,
                  () => void editor.moveStep(undefined, -1))}
                ${this.iconButton('arrow-down', t('Palette.MoveDown'), !hasStep,
                  () => void editor.moveStep(undefined, 1))}
                <span class="grow"></span>
                <button class="ghost" type="button"
                  title=${state.inspectorVisible ? t('Editor.HideInspector') : t('Editor.ShowInspector')}
                  aria-label=${state.inspectorVisible ? t('Editor.HideInspector') : t('Editor.ShowInspector')}
                  @click=${() => editor.setInspectorVisible(!state.inspectorVisible)}>
                  ${icon(state.inspectorVisible ? 'panel-right-close' : 'panel-right-open')}
                </button>
              </div>
              <div class="tree">
                <wg-step-tree></wg-step-tree>
              </div>
            </div>
            ${state.inspectorVisible
              ? html`
                <div class="splitter" role="separator" tabindex="0"
                  aria-orientation="vertical" aria-label=${t('Editor.Inspector')}
                  @pointerdown=${this.onSplitDown}
                  @pointermove=${this.onSplitMove}
                  @pointerup=${this.onSplitUp}
                  @dblclick=${() => editor.setInspectorWidth(360)}
                  @keydown=${(event: KeyboardEvent) => {
                    if (event.key === 'ArrowLeft') {
                      editor.setInspectorWidth(state.inspectorWidth + 16);
                    } else if (event.key === 'ArrowRight') {
                      editor.setInspectorWidth(state.inspectorWidth - 16);
                    }
                  }}></div>
                <div class="inspector" style=${'--inspector-width: ' + String(state.inspectorWidth) + 'px'}>
                  <wg-editor-inspector></wg-editor-inspector>
                </div>
              `
              : nothing}
          </div>
        `}
      <wg-action-editor></wg-action-editor>
      <wg-value-editor></wg-value-editor>
    `;
  }

  private iconButton(name: 'undo' | 'redo' | 'edit' | 'copy' | 'cut' | 'paste' | 'group'
    | 'check' | 'export' | 'delete' | 'arrow-up' | 'arrow-down',
    label: string, disabled: boolean, run: () => void) {
    return html`
      <button class="ghost" type="button" title=${label} aria-label=${label}
        ?disabled=${disabled} @click=${run}>${icon(name)}</button>
    `;
  }

  private onSplitDown = (event: PointerEvent): void => {
    event.preventDefault();
    this.splitStartX = event.clientX;
    this.splitStartWidth = editor.get().inspectorWidth;
    (event.target as HTMLElement).setPointerCapture(event.pointerId);
  };

  private onSplitMove = (event: PointerEvent): void => {
    if (!(event.target as HTMLElement).hasPointerCapture(event.pointerId)) {
      return;
    }

    editor.setInspectorWidth(this.splitStartWidth - (event.clientX - this.splitStartX));
  };

  private onSplitUp = (event: PointerEvent): void => {
    const target = event.target as HTMLElement;
    if (target.hasPointerCapture(event.pointerId)) {
      target.releasePointerCapture(event.pointerId);
    }
  };

  private onKeydown = (event: KeyboardEvent): void => {
    const target = event.target;
    const typing = target instanceof HTMLInputElement
      || target instanceof HTMLTextAreaElement
      || target instanceof HTMLSelectElement;
    if (!event.ctrlKey && !event.altKey) {
      if (typing) {
        return;
      }

      if (event.key === 'Delete' || event.key === 'Backspace') {
        event.preventDefault();
        void editor.removeStep();
      } else if (event.key === 'F2') {
        event.preventDefault();
        editor.openStepEditor();
      }
      return;
    }

    if (event.altKey) {
      return;
    }

    const state = editor.get();
    switch (event.key.toLowerCase()) {
      case 's':
        event.preventDefault();
        void editor.save();
        break;
      case 'w': {
        event.preventDefault();
        if (state.activeKey.length > 0) {
          editor.requestCloseTab(state.activeKey);
        }
        break;
      }
      case 'z':
        if (!typing) {
          event.preventDefault();
          void (event.shiftKey ? editor.redo() : editor.undo());
        }
        break;
      case 'y':
        if (!typing) {
          event.preventDefault();
          void editor.redo();
        }
        break;
      case 'a':
        if (!typing && state.snapshot) {
          event.preventDefault();
          const ids = flattenSteps(state.snapshot.steps).map((step) => step.id);
          if (ids.length > 0) {
            editor.selectStep(ids.at(-1) ?? '', false, ids);
          }
        }
        break;
      case 'c':
        if (!typing) {
          event.preventDefault();
          void editor.copySteps();
        }
        break;
      case 'x':
        if (!typing) {
          event.preventDefault();
          void editor.cutSteps();
        }
        break;
      case 'v':
        if (!typing) {
          event.preventDefault();
          void editor.pasteSteps(state.selectedStepId);
        }
        break;
      case 'g':
        if (!typing) {
          event.preventDefault();
          void editor.groupSteps();
        }
        break;
      case 'e':
        if (!typing) {
          event.preventDefault();
          const first = state.snapshot && state.selectedStepIds[0]
            ? findStep(state.snapshot.steps, state.selectedStepIds[0])
            : null;
          if (first) {
            void editor.setStepEnabled(undefined, !first.enabled);
          }
        }
        break;
      case 'arrowup':
        if (!typing) {
          event.preventDefault();
          void editor.moveStep(undefined, -1);
        }
        break;
      case 'arrowdown':
        if (!typing) {
          event.preventDefault();
          void editor.moveStep(undefined, 1);
        }
        break;
      default:
        break;
    }
  };

}

declare global {
  interface HTMLElementTagNameMap {
    'wg-editor': WgEditor;
  }
}
