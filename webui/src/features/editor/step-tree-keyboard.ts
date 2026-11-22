import { editor } from './editor-store.js';
import { findStep } from './editor-model.js';

export interface StepTreeKeyboardHost {
  visibleIds(): string[];
  clearMenu(): void;
}

export function handleStepTreeKeydown(host: StepTreeKeyboardHost, event: KeyboardEvent): void {
    if (event.target instanceof HTMLInputElement || event.target instanceof HTMLTextAreaElement
        || event.target instanceof HTMLSelectElement) {
      return;
    }

    event.stopPropagation();
    const state = editor.get();
    const selected = state.selectedStepIds;
    const ctrl = event.ctrlKey || event.metaKey;
    if (event.key === 'Escape') {
      event.preventDefault();
      editor.ui.selectStep('');
      host.clearMenu();
      return;
    }

    if (ctrl && event.key.toLowerCase() === 'a') {
      event.preventDefault();
      const ids = host.visibleIds();
      if (ids.length > 0) {
        editor.ui.selectStep(ids.at(-1) ?? '', false, ids);
      }
      return;
    }

    if (ctrl && event.key.toLowerCase() === 'c') {
      event.preventDefault();
      void editor.steps.copySteps();
      return;
    }

    if (ctrl && event.key.toLowerCase() === 'x') {
      event.preventDefault();
      void editor.steps.cutSteps();
      return;
    }

    if (ctrl && event.key.toLowerCase() === 'v') {
      event.preventDefault();
      void editor.steps.pasteSteps(state.selectedStepId);
      return;
    }

    if (ctrl && event.key.toLowerCase() === 'g') {
      event.preventDefault();
      void editor.steps.groupSteps();
      return;
    }

    if (ctrl && event.key.toLowerCase() === 'e') {
      event.preventDefault();
      const first = selected.length > 0 && state.snapshot
        ? findStep(state.snapshot.steps, selected[0] ?? '')
        : null;
      if (first) {
        void editor.steps.setStepEnabled(undefined, !first.enabled);
      }
      return;
    }

    if (ctrl && (event.key === 'ArrowUp' || event.key === 'ArrowDown')) {
      event.preventDefault();
      void editor.steps.moveStep(undefined, event.key === 'ArrowUp' ? -1 : 1);
      return;
    }

    if (event.key === 'Delete' || event.key === 'Backspace') {
      event.preventDefault();
      void editor.steps.removeStep();
      return;
    }

    if (event.key === 'F2') {
      event.preventDefault();
      editor.action.openStepEditor();
    }
  }
