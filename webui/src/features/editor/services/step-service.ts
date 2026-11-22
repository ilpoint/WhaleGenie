import type { EditorCore } from './editor-core.js';
import type {
  EditorConditionCommand,
  EditorSnapshot,
} from '../../../shared/bridge/protocol.js';
import { flattenSteps } from '../editor-model.js';
import { t } from '../../../shared/strings/strings.js';

/** The steps command family. All state lives in EditorCore; C# remains the source of truth. */
export class EditorStepService {
  constructor(private readonly core: EditorCore) {}

  async addPublicMacro(id: string): Promise<boolean> {
    const snapshot = this.core.state.snapshot;
    if (!snapshot) {
      return false;
    }

    const before = new Set(flattenSteps(snapshot.steps).map((step) => step.id));
    const ok = await this.core.mutate<EditorSnapshot>('editor.session',
      { kind: 'publicMacroAdd', id }, (next) => {
      this.core.applySnapshot(next);
      const added = flattenSteps(next.steps).find((step) => !before.has(step.id));
      if (added) {
        this.core.state.selectedStepId = added.id;
        this.core.state.selectedStepIds = [added.id];
      }
    });

    if (ok) {
      await this.core.refreshTabs();
    }

    return ok;
  }

  async addStep(type: string, parentId?: string, list?: string): Promise<boolean> {
    const snapshot = this.core.state.snapshot;
    if (!snapshot) {
      return false;
    }

    const before = new Set(flattenSteps(snapshot.steps).map((step) => step.id));
    const params: Record<string, unknown> = { type };
    if (parentId) {
      params['parentId'] = parentId;
    }

    if (list) {
      params['list'] = list;
    }

    const ok = await this.core.mutate<EditorSnapshot>('editor.sessionStep', params, (next) => {
      this.core.applySnapshot(next);
      const added = flattenSteps(next.steps).find((step) => !before.has(step.id));
      if (added) {
        this.core.state.selectedStepId = added.id;
        this.core.state.selectedStepIds = [added.id];
        this.core.state.collapsedSteps.delete(parentId ?? '');
        this.core.state.inspectorTab = 'step';
      }
    });

    if (ok) {
      await this.core.refreshTabs();
    }

    return ok;
  }

  async addBranch(parentId: string): Promise<boolean> {
    return this.addStep('control.case', parentId, 'cases');
  }

  async duplicateStep(stepId?: string): Promise<boolean> {
    const steps = stepId ? [stepId] : [...this.core.state.selectedStepIds];
    if (steps.length === 0) {
      return false;
    }

    return this.core.snapshotCommand('editor.steps', { kind: 'duplicate', steps });
  }

  async removeStep(stepId?: string): Promise<boolean> {
    const steps = stepId ? [stepId] : [...this.core.state.selectedStepIds];
    if (steps.length === 0) {
      return false;
    }

    const ok = await this.core.snapshotCommand('editor.steps', { kind: 'remove', steps });
    if (ok) {
      this.core.state.selectedStepId = '';
      this.core.state.selectedStepIds = [];
      this.core.notify();
    }

    return ok;
  }

  async moveStep(stepId: string | undefined, delta: number): Promise<boolean> {
    const steps = stepId ? [stepId] : [...this.core.state.selectedStepIds];
    if (steps.length === 0) {
      return false;
    }

    return this.core.snapshotCommand('editor.steps', { kind: 'moveBy', steps, delta });
  }

  async moveStepsTo(target: string, before: boolean, ids?: string[]): Promise<boolean> {
    const steps = ids && ids.length > 0 ? ids : [...this.core.state.selectedStepIds];
    if (steps.length === 0) {
      return false;
    }

    return this.core.snapshotCommand('editor.steps', { kind: 'move', steps, target, before });
  }

  async groupSteps(stepId?: string): Promise<boolean> {
    const steps = stepId ? [stepId] : [...this.core.state.selectedStepIds];
    if (steps.length === 0) {
      return false;
    }

    const ok = await this.core.snapshotCommand('editor.steps', { kind: 'group', steps });
    if (ok) {
      this.core.state.selectedStepId = '';
      this.core.state.selectedStepIds = [];
      this.core.notify();
    }

    return ok;
  }

  async liftSteps(stepId?: string): Promise<boolean> {
    const steps = stepId ? [stepId] : [...this.core.state.selectedStepIds];
    if (steps.length === 0) {
      return false;
    }

    const ok = await this.core.snapshotCommand('editor.steps', { kind: 'lift', steps });
    if (ok) {
      this.core.state.selectedStepId = '';
      this.core.state.selectedStepIds = [];
      this.core.notify();
    }

    return ok;
  }

  async setStepEnabled(stepId: string | undefined, enabled: boolean): Promise<boolean> {
    const steps = stepId ? [stepId] : [...this.core.state.selectedStepIds];
    if (steps.length === 0) {
      return false;
    }

    return this.core.snapshotCommand('editor.steps', { kind: 'enabled', steps, enabled });
  }

  async copySteps(stepId?: string): Promise<boolean> {
    const steps = stepId ? [stepId] : [...this.core.state.selectedStepIds];
    if (steps.length === 0) {
      return false;
    }

    try {
      const text = await this.core.client.call<string>('editor.steps', { kind: 'copy', steps });
      if (text.length > 0 && navigator.clipboard) {
        void navigator.clipboard.writeText(text).catch(() => undefined);
      }
      this.core.state.notice = t('Palette.Copy');
      this.core.notify();
      return true;
    } catch (error) {
      this.core.fail(error);
      return false;
    }
  }

  async cutSteps(stepId?: string): Promise<boolean> {
    const steps = stepId ? [stepId] : [...this.core.state.selectedStepIds];
    if (steps.length === 0) {
      return false;
    }

    try {
      const text = await this.core.client.call<string>('editor.steps', { kind: 'cut', steps });
      if (text.length > 0 && navigator.clipboard) {
        void navigator.clipboard.writeText(text).catch(() => undefined);
      }
      this.core.state.selectedStepId = '';
      this.core.state.selectedStepIds = [];
      await this.core.reload();
      return true;
    } catch (error) {
      this.core.fail(error);
      return false;
    }
  }

  async pasteSteps(after?: string): Promise<boolean> {
    try {
      await this.core.client.call<EditorSnapshot>('editor.steps', {
        kind: 'paste',
        after: after ?? this.core.state.selectedStepId,
      });
      this.core.state.selectedStepId = '';
      this.core.state.selectedStepIds = [];
      await this.core.reload();
      return true;
    } catch (error) {
      this.core.fail(error);
      return false;
    }
  }

  async setStepMeta(stepId: string, field: string, value: string | number): Promise<boolean> {
    return this.core.snapshotCommand('editor.sessionStepMeta', {
      stepId, field, value: String(value),
    });
  }

  async patchParameter(stepId: string, name: string, value: string): Promise<boolean> {
    return this.core.snapshotCommand('editor.sessionParameter', { stepId, name, value });
  }

  async undo(): Promise<boolean> {
    return this.core.snapshotCommand('editor.session', { kind: 'undo' });
  }

  async redo(): Promise<boolean> {
    return this.core.snapshotCommand('editor.session', { kind: 'redo' });
  }

  async addParameterRow(stepId: string, name: string): Promise<boolean> {
    return this.core.snapshotCommand('editor.sessionParameterRowAdd', { stepId, name });
  }

  async removeParameterRow(stepId: string, name: string, index: number): Promise<boolean> {
    return this.core.snapshotCommand('editor.sessionParameterRowRemove', { stepId, name, index });
  }

  async patchRowCell(stepId: string, name: string, index: number, column: string,
    value: string): Promise<boolean> {
    return this.core.snapshotCommand('editor.sessionParameterRowCell', {
      stepId, name, index, column, value,
    });
  }

  private condition(command: EditorConditionCommand): Promise<boolean> {
    return this.core.snapshotCommand('editor.condition', command);
  }

  async addConditionRow(stepId: string, name: string, path: number[] = []): Promise<boolean> {
    return this.condition({ kind: 'rowAdd', stepId, name, path });
  }

  async addConditionGroup(stepId: string, name: string, path: number[] = []): Promise<boolean> {
    return this.condition({ kind: 'groupAdd', stepId, name, path });
  }

  async removeConditionNode(stepId: string, name: string, path: number[]): Promise<boolean> {
    return this.condition({ kind: 'rowRemove', stepId, name, path });
  }

  async setConditionRow(stepId: string, name: string, path: number[], field: string,
    value: string): Promise<boolean> {
    return this.condition({ kind: 'row', stepId, name, path, field, value });
  }

  async setConditionJoin(stepId: string, name: string, path: number[], join: string): Promise<boolean> {
    return this.condition({ kind: 'join', stepId, name, path, join });
  }

  async pickImage(stepId: string, name: string): Promise<void> {
    try {
      const picked = await this.core.client.call<{ path: string | null }>('dialog.pickImage');
      if (picked.path) {
        await this.patchParameter(stepId, name, picked.path);
      }
    } catch (error) {
      this.core.fail(error);
    }
  }
}
