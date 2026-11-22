import type { EditorCore } from './editor-core.js';
import type { EditorActionForm, EditorKeyPad, EditorSnapshot, EditorTrialReply, EditorWindowPreview } from '../../../shared/bridge/protocol.js';
import { describeError } from '../../../shared/errors.js';
import { selectedAction } from '../editor-model.js';
import { flattenSteps } from '../editor-model.js';

/** The action command family. All state lives in EditorCore; C# remains the source of truth. */
export class EditorActionService {
  constructor(private readonly core: EditorCore) {}

  openStepEditor(): void {
    const id = this.core.state.selectedStepIds.length === 1 ? this.core.state.selectedStepId : '';
    if (id.length > 0) {
      void this.openStepAction(id);
    }
  }

  closeStepEditor(): void {
    void this.cancelAction();
  }

  async openAction(type: string,
    target: { targetStepId?: string; before?: boolean; parentId?: string; list?: string } = {}): Promise<boolean> {
    try {
      const form = await this.core.client.call<EditorActionForm>('editor.actionOpen', {
        type,
        ...target,
      });
      this.core.state.actionForm = form;
      this.core.state.actionPage = form.page;
      this.core.state.actionError = '';
      this.core.state.stepEditorOpen = false;
      this.core.notify();
      return true;
    } catch (error) {
      this.core.state.actionError = describeError(error);
      this.core.state.error = this.core.state.actionError;
      this.core.notify();
      return false;
    }
  }

  async openStepAction(stepId: string): Promise<boolean> {
    try {
      const form = await this.core.client.call<EditorActionForm>('editor.actionOpen', { stepId });
      this.core.state.actionForm = form;
      this.core.state.actionPage = form.page;
      this.core.state.actionError = '';
      this.core.state.stepEditorOpen = false;
      this.core.notify();
      return true;
    } catch (error) {
      this.core.state.actionError = describeError(error);
      this.core.state.error = this.core.state.actionError;
      this.core.notify();
      return false;
    }
  }

  async setActionPage(page: string): Promise<void> {
    await this.core.actionEdit({ kind: 'page', page });
  }

  async confirmAction(): Promise<boolean> {
    const form = this.core.state.actionForm;
    if (!form?.canConfirm) {
      return false;
    }

    const before = this.core.state.snapshot
      ? new Set(flattenSteps(this.core.state.snapshot.steps).map((step) => step.id))
      : new Set<string>();
    try {
      const next = await this.core.client.call<EditorSnapshot>('editor.actionConfirm');
      this.core.applySnapshot(next);
      this.core.state.actionForm = null;
      this.core.state.actionError = '';
      const selected = form.isEditing
        ? form.stepId
        : flattenSteps(next.steps).find((step) => !before.has(step.id))?.id ?? '';
      this.core.state.selectedStepId = selected;
      this.core.state.selectedStepIds = selected.length > 0 ? [selected] : [];
      this.core.notify();
      return true;
    } catch (error) {
      this.core.state.actionError = describeError(error);
      this.core.state.error = this.core.state.actionError;
      this.core.notify();
      return false;
    }
  }

  async cancelAction(): Promise<void> {
    if (!this.core.state.actionForm) {
      return;
    }

    try {
      await this.core.client.call<EditorSnapshot>('editor.actionCancel');
    } catch {
      // The form is page state; closing it must not be blocked by a failed discard.
    }

    this.core.state.actionForm = null;
    this.core.state.actionError = '';
    this.core.state.stepEditorOpen = false;
    this.core.notify();
  }

  async patchActionField(name: string, field: string, value: string): Promise<boolean> {
    return this.core.actionEdit({ kind: 'field', name, field, value });
  }

  async addActionRow(name: string): Promise<boolean> {
    return this.core.actionEdit({ kind: 'rowAdd', name });
  }

  async removeActionRow(name: string, index: number): Promise<boolean> {
    return this.core.actionEdit({ kind: 'rowRemove', name, index });
  }

  async moveActionRow(name: string, index: number, by: number): Promise<boolean> {
    return this.core.actionEdit({ kind: 'rowMove', name, index, by });
  }

  async patchActionRow(name: string, index: number, column: string, value: string): Promise<boolean> {
    return this.core.actionEdit({ kind: 'rowCell', name, index, column, value });
  }

  async patchActionReturn(key: string, change: { ticked?: boolean; name?: string }): Promise<boolean> {
    return this.core.actionEdit({ kind: 'returns', key, ...change });
  }

  async fixActionReturnName(key: string): Promise<boolean> {
    return this.core.actionEdit({ kind: 'returnsFixName', key });
  }

  async patchActionCallArgument(name: string,
    change: { given?: boolean; value?: string }): Promise<boolean> {
    return this.core.actionEdit({ kind: 'callArgument', name, ...change });
  }

  async patchActionMeta(field: string, value: string, unit = ''): Promise<boolean> {
    return this.core.actionEdit({ kind: 'meta', field, value, unit });
  }

  async patchActionGlobal(field: string, value: string): Promise<boolean> {
    return this.core.actionEdit({ kind: 'global', field, value });
  }

  async addActionBranch(): Promise<boolean> {
    return this.core.actionEdit({ kind: 'branch' });
  }

  async applyActionWindow(name: string, choice: string, value = ''): Promise<boolean> {
    return this.core.actionEdit({ kind: 'window', name, choice, value });
  }

  async applyActionWindowRule(name: string, rule: string): Promise<boolean> {
    return this.core.actionEdit({ kind: 'windowRule', name, rule });
  }

  async testActionBrowser(name: string): Promise<boolean> {
    return this.core.actionCommand('editor.actionTestBrowser', { name });
  }

  async testActionElement(name: string): Promise<boolean> {
    return this.core.actionCommand('editor.actionTestElement', { name });
  }

  async testNarrow(name: string, index: number): Promise<boolean> {
    return this.core.actionCommand('editor.actionTestNarrow', { name, index });
  }

  async previewActionWindow(name: string, rule: string): Promise<EditorWindowPreview | null> {
    try {
      return await this.core.client.call<EditorWindowPreview | null>('editor.actionWindowPreview',
        { name, rule });
    } catch (error) {
      this.core.fail(error);
      return null;
    }
  }

  async runAction(confirmed = false): Promise<EditorTrialReply | null> {
    try {
      const reply = await this.core.client.call<EditorTrialReply>('editor.actionRun', { confirmed });
      if (reply.result) {
        this.core.state.trial = reply.result;
        this.core.notify();
      }
      return reply;
    } catch (error) {
      this.core.state.actionError = describeError(error);
      this.core.notify();
      return null;
    }
  }

  closeTrial(): void {
    this.core.state.trial = null;
    this.core.notify();
  }

  async keyPad(mode = 'key'): Promise<EditorKeyPad | null> {
    try {
      return await this.core.client.call<EditorKeyPad>('editor.actionKeyPad', { mode });
    } catch (error) {
      this.core.fail(error);
      return null;
    }
  }

  async padKey(name: string, key: string, index = -1, mode = 'key'): Promise<boolean> {
    return this.core.actionEdit({ kind: 'padKey', name, key, index, mode });
  }

  async pickAction(name: string, mode: string, index = -1): Promise<boolean> {
    if (mode === 'lookOffset' || mode === 'lookColours') {
      return this.core.actionCommand('editor.actionLook', {
        name, mode: mode === 'lookColours' ? 'colours' : 'offset', index,
      });
    }

    return this.core.actionCommand('editor.actionPick', { name, mode, index });
  }

  async pickWindowForAction(): Promise<{ chosen: boolean; rule: string; title: string } | null> {
    try {
      return await this.core.client.call<{ chosen: boolean; rule: string; title: string }>(
        'editor.actionWindowPick');
    } catch (error) {
      this.core.fail(error);
      return null;
    }
  }

  async addSelectedStep(parentId?: string, list?: string): Promise<boolean> {
    const action = selectedAction(this.core.state.actions, this.core.state.selectedActionKey);
    if (!action) {
      return false;
    }

    const target: { parentId?: string; list?: string } = {};
    if (parentId !== undefined) {
      target.parentId = parentId;
    }
    if (list !== undefined) {
      target.list = list;
    }

    return this.openAction(action.key, target);
  }
}
