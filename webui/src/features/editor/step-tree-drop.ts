import { editor } from './editor-store.js';

/**
 * The step list's drag and drop state, kept out of the renderer. A drop is a seam beside a row
 * (the existing target/before words) or a slot inside a block: an empty section paints itself as
 * a drop area naming the section list the snapshot already carries, so nothing is inferred from
 * the step type.
 */
export type StepDrop =
  | { area: 'row'; stepId: string; before: boolean }
  | { area: 'section'; parentId: string; list: string; index: number };

export interface StepRowBox {
  id: string;
  top: number;
  height: number;
}

/** Upper half of a row → a seam before it; lower half → a seam after it. */
export function resolveRowDrop(box: StepRowBox, clientY: number): StepDrop {
  return { area: 'row', stepId: box.id, before: clientY < box.top + box.height / 2 };
}

/**
 * An empty section is its own child list with nothing in it, so the drop is the first slot of the
 * list the snapshot names (EditorStepSectionSnapshot.name). The page never assumes "the first list
 * that accepts steps": the section name travels on the empty area as data-list.
 */
export function resolveSectionDrop(parentId: string | null | undefined,
  list: string | null | undefined): StepDrop | null {
  const parent = parentId?.trim() ?? '';
  const name = list?.trim() ?? '';
  return parent.length > 0 && name.length > 0
    ? { area: 'section', parentId: parent, list: name, index: 0 }
    : null;
}

/** Below the last row of a list the seam belongs to that row; a list with no rows has no seam. */
export function resolveListEndDrop(rows: readonly StepRowBox[]): StepDrop | null {
  const last = rows.at(-1);
  return last ? { area: 'row', stepId: last.id, before: false } : null;
}

/** One drag at a time: the rows it carries, the mark it paints, and the command the drop sends. */
export class StepTreeDrop {
  private ids: string[] = [];

  private target: StepDrop | null = null;

  constructor(private readonly host: { requestUpdate(): void }) {}

  /** The marker a row draws for itself; the template reads it instead of writing the DOM. */
  rowMark(stepId: string): 'before' | 'after' | 'none' {
    const target = this.target;
    return target?.area === 'row' && target.stepId === stepId
      ? (target.before ? 'before' : 'after') : 'none';
  }

  sectionMark(parentId: string, list: string): 'into' | 'none' {
    const target = this.target;
    return target?.area === 'section' && target.parentId === parentId && target.list === list
      ? 'into' : 'none';
  }

  start(event: DragEvent, stepId: string): void {
    this.ids = editor.get().selectedStepIds.includes(stepId)
      ? editor.ui.selectedSteps()
      : [stepId];
    if (event.dataTransfer) {
      event.dataTransfer.effectAllowed = 'move';
      event.dataTransfer.setData('text/plain', this.ids.join(','));
    }
  }

  overRow(event: DragEvent, row: HTMLElement, stepId: string): void {
    // A row carrying the drag cannot land on itself; an action card from the library has no rows.
    if (!this.accepts(event) || this.ids.includes(stepId)) {
      return;
    }

    const rect = row.getBoundingClientRect();
    this.accept(event);
    this.setTarget(resolveRowDrop({ id: stepId, top: rect.top, height: rect.height },
      event.clientY));
  }

  overSection(event: DragEvent, parentId: string, list: string): void {
    if (this.accepts(event)) {
      this.accept(event);
      this.setTarget(resolveSectionDrop(parentId, list));
    }
  }

  overListEnd(event: DragEvent, rows: readonly StepRowBox[]): void {
    if (this.accepts(event)) {
      this.accept(event);
      this.setTarget(resolveListEndDrop(rows));
    }
  }

  leaveRow(stepId: string): void {
    if (this.target?.area === 'row' && this.target.stepId === stepId) {
      this.setTarget(null);
    }
  }

  leaveSection(parentId: string, list: string): void {
    const target = this.target;
    if (target?.area === 'section' && target.parentId === parentId && target.list === list) {
      this.setTarget(null);
    }
  }

  dropRow(event: DragEvent, stepId: string): void {
    const rect = (event.currentTarget as HTMLElement).getBoundingClientRect();
    this.dropAt(event, resolveRowDrop({ id: stepId, top: rect.top, height: rect.height },
      event.clientY));
  }

  dropSection(event: DragEvent, parentId: string, list: string): void {
    this.dropAt(event, resolveSectionDrop(parentId, list));
  }

  dropListEnd(event: DragEvent, rows: readonly StepRowBox[]): void {
    this.dropAt(event, resolveListEndDrop(rows));
  }

  end(): void { this.ids = []; this.setTarget(null); }

  private accepts(event: DragEvent): boolean {
    return this.external(event) || this.ids.length > 0;
  }

  private external(event: DragEvent): boolean {
    return event.dataTransfer
      ? Array.from(event.dataTransfer.types).includes('application/x-whalegenie-action')
      : false;
  }

  /** Allow the pointer and paint the drop effect; only a drop that would land calls this. */
  private accept(event: DragEvent): void {
    event.preventDefault();
    if (event.dataTransfer) {
      event.dataTransfer.dropEffect = 'move';
    }
  }

  private setTarget(target: StepDrop | null): void {
    if (!sameDrop(this.target, target)) {
      this.target = target;
      this.host.requestUpdate();
    }
  }

  private dropAt(event: DragEvent, target: StepDrop | null): void {
    event.preventDefault();
    const actionKey = event.dataTransfer?.getData('application/x-whalegenie-action') ?? '';
    const ids = this.ids;
    this.end();
    if (!target) {
      return;
    }

    if (target.area === 'section') {
      if (actionKey.length > 0) {
        void editor.action.openAction(actionKey, { parentId: target.parentId, list: target.list });
      } else {
        void editor.steps.moveStepsInto(target.parentId, target.index, target.list, ids);
      }

      return;
    }

    if (actionKey.length > 0) {
      void editor.action.openAction(actionKey, {
        targetStepId: target.stepId,
        before: target.before,
      });
      return;
    }

    if (!ids.includes(target.stepId)) {
      void editor.steps.moveStepsTo(target.stepId, target.before, ids);
    }
  }
}

function sameDrop(left: StepDrop | null, right: StepDrop | null): boolean {
  if (!left || !right) {
    return left === right;
  }

  if (left.area === 'row' && right.area === 'row') {
    return left.stepId === right.stepId && left.before === right.before;
  }

  return left.area === 'section' && right.area === 'section'
    && left.parentId === right.parentId && left.list === right.list && left.index === right.index;
}
