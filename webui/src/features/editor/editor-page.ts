import { LitElement, html, nothing } from 'lit';
import { editorPageCss } from './editor-page-css.js';
import { customElement } from 'lit/decorators.js';
import { icon } from '../../shared/icons.js';
import { t } from '../../shared/strings/strings.js';
import { editor } from './editor-store.js';
import { StoreController } from '../../shared/lit/store-controller.js';
import { isTextEntryEvent } from '../../shared/input/typing.js';
import { WindowEventController } from '../../shared/lit/window-event-controller.js';
import { bridge } from '../../shared/bridge/bridge.js';
import { go } from '../../shared/navigation/router.js';
import { isWorkbench } from '../../shared/window/window-kind.js';
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

  protected readonly editorController = new StoreController<void>(this, editor);

  protected readonly keydownController = new WindowEventController(this, 'keydown', (event) => {
    this.onKeydown(event);
  });

  private splitStartX = 0;

  private openMacroList(): void {
    if (isWorkbench()) {
      void bridge.value.call('workbench.focusMain');
      return;
    }

    void go('macros');
  }

  private splitStartWidth = 0;

  /** True once this page has shown a document; closing the last one returns to the list. */
  private hadDocument = false;

  connectedCallback(): void {
    super.connectedCallback();
    // 启动只拉一次：会话服务先把共享目录取来，再取当前文档。页面本身不再补一次。
    void editor.session.ensureLoaded();
  }

  disconnectedCallback(): void {
    editor.leaveEditor();
    super.disconnectedCallback();
  }

  protected updated(): void {
    if (editor.get().snapshot !== null) {
      this.hadDocument = true;
      return;
    }

    if (this.hadDocument) {
      this.hadDocument = false;
      if (!isWorkbench()) {
        void go('macros');
      }
    }
  }

  render() {
    const state = editor.get();
    const snapshot = state.snapshot;
    const hasStep = state.selectedStepIds.length > 0;
    const singleStep = state.selectedStepIds.length === 1;
    return html`
      <h1 class="sr-only">${t('Shell.Editor')}</h1>
      ${snapshot !== null && state.notice
        ? html`<div class="notice">${state.notice}</div>`
        : nothing}
      ${snapshot !== null && state.error
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
            <div class="empty-actions">
              <fluent-button appearance="subtle" @click=${() => void this.openMacroList()}>
                ${t('Editor.OpenMacroList')}
              </fluent-button>
              <fluent-button appearance="accent" @click=${() => void editor.session.newDocument()}>
                ${t('Main.AddMacro')}
              </fluent-button>
            </div>
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
                  () => void editor.steps.undo())}
                ${this.iconButton('redo', t('Palette.Redo'), !snapshot.canRedo,
                  () => void editor.steps.redo())}
                ${this.iconButton('edit', t('Palette.Edit'), !singleStep,
                  () => editor.action.openStepEditor())}
                ${this.iconButton('check', t('Palette.ToggleEnabled'), !hasStep,
                  () => {
                    const first = state.snapshot && state.selectedStepIds[0]
                      ? findStep(state.snapshot.steps, state.selectedStepIds[0])
                      : null;
                    if (first) {
                      void editor.steps.setStepEnabled(undefined, !first.enabled);
                    }
                  })}
                ${this.iconButton('copy', t('Palette.Copy'), !hasStep,
                  () => void editor.steps.copySteps())}
                ${this.iconButton('cut', t('Palette.Cut'), !hasStep,
                  () => void editor.steps.cutSteps())}
                ${this.iconButton('paste', t('Palette.Paste'), false,
                  () => void editor.steps.pasteSteps(state.selectedStepId))}
                ${this.iconButton('group', t('Palette.Group'), !hasStep,
                  () => void editor.steps.groupSteps())}
                ${this.iconButton('export', t('Palette.LiftToMacro'), !hasStep,
                  () => void editor.steps.liftSteps())}
                <span class="sep"></span>
                <fluent-button appearance="subtle"
                  title=${state.selectedActionKey.length === 0
                    ? t('Editor.PickActionHint') : t('Editor.AddStep')}
                  ?disabled=${state.selectedActionKey.length === 0}
                  @click=${() => void editor.action.addSelectedStep()}>
                  ${t('Editor.AddStep')}
                </fluent-button>
                ${this.iconButton('copy', t('Editor.DuplicateStep'), !hasStep,
                  () => void editor.steps.duplicateStep())}
                ${this.iconButton('delete', t('Editor.Delete'), !hasStep,
                  () => void editor.steps.removeStep())}
                ${this.iconButton('arrow-up', t('Palette.MoveUp'), !hasStep,
                  () => void editor.steps.moveStep(undefined, -1))}
                ${this.iconButton('arrow-down', t('Palette.MoveDown'), !hasStep,
                  () => void editor.steps.moveStep(undefined, 1))}
                <span class="grow"></span>
                <button class="ghost" type="button"
                  title=${state.inspectorVisible ? t('Editor.HideInspector') : t('Editor.ShowInspector')}
                  aria-label=${state.inspectorVisible ? t('Editor.HideInspector') : t('Editor.ShowInspector')}
                  @click=${() => editor.ui.setInspectorVisible(!state.inspectorVisible)}>
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
                  @dblclick=${() => editor.ui.setInspectorWidth(360)}
                  @keydown=${(event: KeyboardEvent) => {
                    if (event.key === 'ArrowLeft') {
                      editor.ui.setInspectorWidth(state.inspectorWidth + 16);
                    } else if (event.key === 'ArrowRight') {
                      editor.ui.setInspectorWidth(state.inspectorWidth - 16);
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

    editor.ui.setInspectorWidth(this.splitStartWidth - (event.clientX - this.splitStartX));
  };

  private onSplitUp = (event: PointerEvent): void => {
    const target = event.target as HTMLElement;
    if (target.hasPointerCapture(event.pointerId)) {
      target.releasePointerCapture(event.pointerId);
    }
  };

  /**
   * Ctrl+S first finishes the draft the page is showing: the action form's confirmation and the
   * value window's answer are part of saving the document the person is looking at, and a refusal
   * stops the save with the reason already on screen.
   */
  private async saveCurrent(): Promise<void> {
    const form = editor.get().actionForm;
    if (form?.open === true) {
      if (!form.canConfirm) {
        if (form.validationMessage.length > 0) {
          editor.setError(form.validationMessage);
        }
        return;
      }

      if (!(await editor.action.confirmAction())) {
        return;
      }
    }

    if (editor.get().valueEditor !== null && !(await editor.value.confirmValue())) {
      return;
    }

    await editor.session.save();
  }

  private onKeydown = (event: KeyboardEvent): void => {
    // A window listener sees the custom element, not the box inside its shadow root, so the
    // composed path decides whether this key belongs to a text field.
    const typing = isTextEntryEvent(event);
    if (!event.ctrlKey && !event.altKey) {
      if (typing) {
        return;
      }

      if (event.key === 'Delete' || event.key === 'Backspace') {
        event.preventDefault();
        void editor.steps.removeStep();
      } else if (event.key === 'F2') {
        event.preventDefault();
        editor.action.openStepEditor();
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
        if (event.shiftKey) {
          void editor.session.saveAll();
        } else {
          void this.saveCurrent();
        }
        break;
      case 'w': {
        event.preventDefault();
        if (state.actionForm !== null || state.valueEditor !== null) {
          // The draft belongs to this page; Ctrl+W throws it away before it throws the tab away.
          if (state.actionForm !== null) {
            void editor.action.cancelAction();
          }

          if (state.valueEditor !== null) {
            void editor.value.cancelValue();
          }
        } else if (state.activeDocumentId.length > 0) {
          editor.dialog.closeDocument(state.activeDocumentId);
        }
        break;
      }
      case 'z':
        if (!typing) {
          event.preventDefault();
          void (event.shiftKey ? editor.steps.redo() : editor.steps.undo());
        }
        break;
      case 'y':
        if (!typing) {
          event.preventDefault();
          void editor.steps.redo();
        }
        break;
      case 'a':
        if (!typing && state.snapshot) {
          event.preventDefault();
          const ids = flattenSteps(state.snapshot.steps).map((step) => step.id);
          if (ids.length > 0) {
            editor.ui.selectStep(ids.at(-1) ?? '', false, ids);
          }
        }
        break;
      case 'c':
        if (!typing) {
          event.preventDefault();
          void editor.steps.copySteps();
        }
        break;
      case 'x':
        if (!typing) {
          event.preventDefault();
          void editor.steps.cutSteps();
        }
        break;
      case 'v':
        if (!typing) {
          event.preventDefault();
          void editor.steps.pasteSteps(state.selectedStepId);
        }
        break;
      case 'g':
        if (!typing) {
          event.preventDefault();
          void editor.steps.groupSteps();
        }
        break;
      case 'e':
        if (!typing) {
          event.preventDefault();
          const first = state.snapshot && state.selectedStepIds[0]
            ? findStep(state.snapshot.steps, state.selectedStepIds[0])
            : null;
          if (first) {
            void editor.steps.setStepEnabled(undefined, !first.enabled);
          }
        }
        break;
      case 'arrowup':
        if (!typing) {
          event.preventDefault();
          void editor.steps.moveStep(undefined, -1);
        }
        break;
      case 'arrowdown':
        if (!typing) {
          event.preventDefault();
          void editor.steps.moveStep(undefined, 1);
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