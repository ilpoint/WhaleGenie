import type {
  EditorActionForm,
  EditorSnapshot,
  EditorStep,
  EditorValueSnapshot,
} from './protocol.js';

import type { MockValueEditor } from './mock-value-editor.js';
import { openValue, valueCommand } from './mock-editor-value.js';
import {
  open as openCommand,
  newDocument as newDocumentCommand,
  newSession as newCommand,
  activate as activateCommand,
  close as closeCommand,
  tabs as tabsCommand,
  subOpen as subOpenCommand,
  subMacroCreate as subMacroCreateCommand,
  subMacroCopy as subMacroCopyCommand,
  subMacroDelete as subMacroDeleteCommand,
  change as changeCommand,
  patch as patchCommand,
  macroSetting as macroSettingCommand,
  activeSnapshot as activeSnapshotCommand,
  nextNewKey as nextNewKeyCommand,
  save as saveCommand,
  stopRecording as stopRecordingCommand,
} from './mock-editor-documents.js';
import {
  addStep as addStepCommand,
  requestedSteps as requestedStepsCommand,
  stepsEnabled as stepsEnabledCommand,
  stepsRemove as stepsRemoveCommand,
  stepsDuplicate as stepsDuplicateCommand,
  stepsMoveBy as stepsMoveByCommand,
  stepsCopy as stepsCopyCommand,
  stepsCut as stepsCutCommand,
  stepsPaste as stepsPasteCommand,
  stepsGroup as stepsGroupCommand,
  stepsLift as stepsLiftCommand,
  stepsMoveTo as stepsMoveToCommand,
  cloneStep as cloneStepCommand,
  setStepEnabledCommand as setStepEnabledCommandCommand,
  setStepMeta as setStepMetaCommand,
  duplicateStep as duplicateStepCommand,
  moveStep as moveStepCommand,
  removeStep as removeStepCommand,
  conditionCommand as conditionCommandCommand,
  setParameterCommand as setParameterCommandCommand,
  rowCommand as rowCommandCommand,
  addTrigger as addTriggerCommand,
  removeTrigger as removeTriggerCommand,
  patchTrigger as patchTriggerCommand,
} from './mock-editor-step-commands.js';
import {
  actionConfirm,
  actionOpen,
  emptyActionForm,
  refreshActionForm,
} from './mock-editor-action-form.js';
import { handleSessionRun } from './mock-editor-run-session.js';
import { handleValueRun } from './mock-editor-run-value.js';
import { handleActionRun } from './mock-editor-run-action.js';

type Tail<T extends readonly unknown[]> = T extends readonly [unknown, ...infer Rest] ? Rest : never;

/** One project macro the mock editor may open; the mock passes the project list through. */
export interface MockMacroEntry {
  name: string;
  description: string;
  group: string;
}

/**
 * The editor half of the mock host. Each open macro is a document of its own with its own dirty
 * state, exactly like the host's editor workspace; the main mock only carries commands in and
 * snapshots out, so the non-editor domains stay untouched.
 */
export class MockEditor {
  readonly documents = new Map<string, EditorSnapshot>();

  activeKey: string | null = null;

  recording = false;

  captured = 0;

  snapshotRevision = 0;

  private stepSequence = 0;

  private triggerSequence = 0;

  valueEditor: MockValueEditor | null = null;

  stepClipboard = '';

  actionForm: EditorActionForm | null = null;

  actionStep: EditorStep | null = null;

  actionIsEditing = false;

  actionPage = 'base';

  readonly macros: () => readonly MockMacroEntry[];

  readonly language: () => string;

  constructor(
    macros: readonly MockMacroEntry[] | (() => readonly MockMacroEntry[]) = () => [],
    language: string | (() => string) = 'zh-Hans',
  ) {
    this.macros = typeof macros === 'function' ? macros : () => macros;
    this.language = typeof language === 'function' ? language : () => language;
  }

  /** Opens a named macro, or the single unnamed scratch document when no name is given. */
  open(...args: Tail<Parameters<typeof openCommand>>): ReturnType<typeof openCommand> {
    return openCommand(this, ...args);
  }

  /** Adds another unnamed document; the key never collides with one already open. */
  newDocument(...args: Tail<Parameters<typeof newDocumentCommand>>): ReturnType<typeof newDocumentCommand> {
    return newDocumentCommand(this, ...args);
  }

  /** Alias for newDocument, matching the host's New command. */
  new(...args: Tail<Parameters<typeof newCommand>>): ReturnType<typeof newCommand> {
    return newCommand(this, ...args);
  }

  activate(...args: Tail<Parameters<typeof activateCommand>>): ReturnType<typeof activateCommand> {
    return activateCommand(this, ...args);
  }

  /** Closes the named document (or the active one) and answers with what is active afterwards. */
  close(...args: Tail<Parameters<typeof closeCommand>>): ReturnType<typeof closeCommand> {
    return closeCommand(this, ...args);
  }

  /** The tab strip lists only documents that are still open, in the order they were opened. */
  tabs(...args: Tail<Parameters<typeof tabsCommand>>): ReturnType<typeof tabsCommand> {
    return tabsCommand(this, ...args);
  }

  /** Opens a submacro of the active document as a document of its own. */
  subOpen(...args: Tail<Parameters<typeof subOpenCommand>>): ReturnType<typeof subOpenCommand> {
    return subOpenCommand(this, ...args);
  }

  /** Adds an empty submacro; a name given here still has to be unique in the document. */
  subMacroCreate(...args: Tail<Parameters<typeof subMacroCreateCommand>>): ReturnType<typeof subMacroCreateCommand> {
    return subMacroCreateCommand(this, ...args);
  }

  /** Copies an existing submacro or a project macro under a fresh name. */
  subMacroCopy(...args: Tail<Parameters<typeof subMacroCopyCommand>>): ReturnType<typeof subMacroCopyCommand> {
    return subMacroCopyCommand(this, ...args);
  }

  /** Removes a submacro; a call to it anywhere in the document refuses the delete. */
  subMacroDelete(...args: Tail<Parameters<typeof subMacroDeleteCommand>>): ReturnType<typeof subMacroDeleteCommand> {
    return subMacroDeleteCommand(this, ...args);
  }

  /** Every editor command the host serves; the main mock only forwards the method and params. */
  emptyActionForm(): EditorActionForm {
    return emptyActionForm();
  }

  actionOpen(params: Record<string, unknown>): EditorActionForm {
    return actionOpen(this, params);
  }

  refreshActionForm(): void {
    refreshActionForm(this);
  }

  actionConfirm(): EditorSnapshot | null {
    return actionConfirm(this);
  }

  addStep(...args: Tail<Parameters<typeof addStepCommand>>): ReturnType<typeof addStepCommand> {
    return addStepCommand(this, ...args);
  }

  requestedSteps(...args: Tail<Parameters<typeof requestedStepsCommand>>): ReturnType<typeof requestedStepsCommand> {
    return requestedStepsCommand(this, ...args);
  }

  stepsEnabled(...args: Tail<Parameters<typeof stepsEnabledCommand>>): ReturnType<typeof stepsEnabledCommand> {
    return stepsEnabledCommand(this, ...args);
  }

  stepsRemove(...args: Tail<Parameters<typeof stepsRemoveCommand>>): ReturnType<typeof stepsRemoveCommand> {
    return stepsRemoveCommand(this, ...args);
  }

  stepsDuplicate(...args: Tail<Parameters<typeof stepsDuplicateCommand>>): ReturnType<typeof stepsDuplicateCommand> {
    return stepsDuplicateCommand(this, ...args);
  }

  stepsMoveBy(...args: Tail<Parameters<typeof stepsMoveByCommand>>): ReturnType<typeof stepsMoveByCommand> {
    return stepsMoveByCommand(this, ...args);
  }

  stepsCopy(...args: Tail<Parameters<typeof stepsCopyCommand>>): ReturnType<typeof stepsCopyCommand> {
    return stepsCopyCommand(this, ...args);
  }

  stepsCut(...args: Tail<Parameters<typeof stepsCutCommand>>): ReturnType<typeof stepsCutCommand> {
    return stepsCutCommand(this, ...args);
  }

  stepsPaste(...args: Tail<Parameters<typeof stepsPasteCommand>>): ReturnType<typeof stepsPasteCommand> {
    return stepsPasteCommand(this, ...args);
  }

  stepsGroup(...args: Tail<Parameters<typeof stepsGroupCommand>>): ReturnType<typeof stepsGroupCommand> {
    return stepsGroupCommand(this, ...args);
  }

  stepsLift(...args: Tail<Parameters<typeof stepsLiftCommand>>): ReturnType<typeof stepsLiftCommand> {
    return stepsLiftCommand(this, ...args);
  }

  stepsMoveTo(...args: Tail<Parameters<typeof stepsMoveToCommand>>): ReturnType<typeof stepsMoveToCommand> {
    return stepsMoveToCommand(this, ...args);
  }

  cloneStep(...args: Tail<Parameters<typeof cloneStepCommand>>): ReturnType<typeof cloneStepCommand> {
    return cloneStepCommand(this, ...args);
  }

  setStepEnabledCommand(...args: Tail<Parameters<typeof setStepEnabledCommandCommand>>): ReturnType<typeof setStepEnabledCommandCommand> {
    return setStepEnabledCommandCommand(this, ...args);
  }

  setStepMeta(...args: Tail<Parameters<typeof setStepMetaCommand>>): ReturnType<typeof setStepMetaCommand> {
    return setStepMetaCommand(this, ...args);
  }

  duplicateStep(...args: Tail<Parameters<typeof duplicateStepCommand>>): ReturnType<typeof duplicateStepCommand> {
    return duplicateStepCommand(this, ...args);
  }

  moveStep(...args: Tail<Parameters<typeof moveStepCommand>>): ReturnType<typeof moveStepCommand> {
    return moveStepCommand(this, ...args);
  }

  removeStep(...args: Tail<Parameters<typeof removeStepCommand>>): ReturnType<typeof removeStepCommand> {
    return removeStepCommand(this, ...args);
  }

  conditionCommand(...args: Tail<Parameters<typeof conditionCommandCommand>>): ReturnType<typeof conditionCommandCommand> {
    return conditionCommandCommand(this, ...args);
  }

  setParameterCommand(...args: Tail<Parameters<typeof setParameterCommandCommand>>): ReturnType<typeof setParameterCommandCommand> {
    return setParameterCommandCommand(this, ...args);
  }

  rowCommand(...args: Tail<Parameters<typeof rowCommandCommand>>): ReturnType<typeof rowCommandCommand> {
    return rowCommandCommand(this, ...args);
  }

  addTrigger(...args: Tail<Parameters<typeof addTriggerCommand>>): ReturnType<typeof addTriggerCommand> {
    return addTriggerCommand(this, ...args);
  }

  removeTrigger(...args: Tail<Parameters<typeof removeTriggerCommand>>): ReturnType<typeof removeTriggerCommand> {
    return removeTriggerCommand(this, ...args);
  }

  patchTrigger(...args: Tail<Parameters<typeof patchTriggerCommand>>): ReturnType<typeof patchTriggerCommand> {
    return patchTriggerCommand(this, ...args);
  }

  run(method: string, params: Record<string, unknown>): unknown {
    if (method.startsWith('editor.action')) {
      return handleActionRun(this, method, params);
    }

    if (method.startsWith('editor.value')) {
      return handleValueRun(this, method, params);
    }

    // handler 的 switch 覆盖 editor.session* 之外的 step/steps/condition/trigger/variable/
    // declaration/record 与字符键盘；只放行 session 前缀会让这些命令族一律报"不认识"。
    if (method.startsWith('editor.')) {
      return handleSessionRun(this, method, params);
    }

    throw new Error('不认识编辑器命令 ' + method + '。');
  }






  actionTarget: { targetStepId: string; before: boolean; parentId: string; list: string } = {
    targetStepId: '', before: false, parentId: '', list: '',
  };



  openValue(params: Record<string, unknown>): EditorValueSnapshot {
    return openValue(this, params);
  }

  valueCommand(params: Record<string, unknown>): EditorValueSnapshot {
    return valueCommand(this, params);
  }

  activeSnapshot(...args: Tail<Parameters<typeof activeSnapshotCommand>>): ReturnType<typeof activeSnapshotCommand> {
    return activeSnapshotCommand(this, ...args);
  }

  nextNewKey(...args: Tail<Parameters<typeof nextNewKeyCommand>>): ReturnType<typeof nextNewKeyCommand> {
    return nextNewKeyCommand(this, ...args);
  }

  /** Applies one edit to the active document and stores the refreshed snapshot in its place. */
  change(...args: Tail<Parameters<typeof changeCommand>>): ReturnType<typeof changeCommand> {
    return changeCommand(this, ...args);
  }

  patch(...args: Tail<Parameters<typeof patchCommand>>): ReturnType<typeof patchCommand> {
    return patchCommand(this, ...args);
  }

  macroSetting(...args: Tail<Parameters<typeof macroSettingCommand>>): ReturnType<typeof macroSettingCommand> {
    return macroSettingCommand(this, ...args);
  }

























  stopRecording(...args: Tail<Parameters<typeof stopRecordingCommand>>): ReturnType<typeof stopRecordingCommand> {
    return stopRecordingCommand(this, ...args);
  }

  save(...args: Tail<Parameters<typeof saveCommand>>): ReturnType<typeof saveCommand> {
    return saveCommand(this, ...args);
  }

  nextStepId(): string {
    this.stepSequence += 1;
    return 'mock-' + String(this.stepSequence);
  }

  nextTriggerId(): string {
    this.triggerSequence += 1;
    return 'trigger-' + String(this.triggerSequence);
  }

  text(params: Record<string, unknown>, name: string): string {
    const value = params[name];
    return value === undefined || value === null ? '' : String(value);
  }

  optionalText(params: Record<string, unknown>, name: string): string | undefined {
    const value = params[name];
    if (value === undefined) {
      return undefined;
    }

    return value === null ? '' : String(value);
  }

  number(value: string, fallback: number): number {
    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : fallback;
  }

  presets(value: unknown): Record<string, string> {
    if (value === null || typeof value !== 'object' || Array.isArray(value)) {
      return {};
    }

    const presets: Record<string, string> = {};
    for (const [name, entry] of Object.entries(value as Record<string, unknown>)) {
      presets[name] = typeof entry === 'string' ? entry : JSON.stringify(entry) ?? '';
    }

    return presets;
  }
}



