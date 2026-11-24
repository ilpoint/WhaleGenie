import type {
  EditorActionForm,
  EditorSnapshot,
  EditorStep,
  EditorTrigger,
} from './protocol.js';

import type { MockValueEditor } from './mock-value-editor.js';
import {
  activeSnapshot as activeSnapshotCommand,
  change as changeCommand,
} from './mock-editor-documents.js';
import { patchTrigger as patchTriggerCommand } from './mock-editor-step-commands.js';
import { refreshActionForm } from './mock-editor-action-form.js';
import { handleSessionRun } from './mock-editor-run-session.js';
import { handleValueRun } from './mock-editor-run-value.js';
import { handleActionRun } from './mock-editor-run-action.js';

/** One project macro the mock editor may open; the mock passes the project list through. */
export interface MockMacroEntry {
  name: string;
  description: string;
  group: string;
}

/**
 * The editor half of the mock host. Each open macro is a document of its own with its own dirty
 * state, exactly like the host's editor workspace; the main mock only carries commands in and
 * snapshots out, so the non-editor domains stay untouched. The commands live in the domain files
 * and take this object as their host; only the helpers every command family shares stay here.
 */
export class MockEditor {
  readonly documents = new Map<string, EditorSnapshot>();

  activeKey: string | null = null;

  recording = false;

  captured = 0;

  snapshotRevision = 0;

  stepSequence = 0;

  triggerSequence = 0;

  valueEditor: MockValueEditor | null = null;

  stepClipboard = '';

  actionForm: EditorActionForm | null = null;

  actionStep: EditorStep | null = null;

  actionIsEditing = false;

  actionPage = 'base';

  actionTarget: { targetStepId: string; before: boolean; parentId: string; list: string } = {
    targetStepId: '', before: false, parentId: '', list: '',
  };

  readonly macros: () => readonly MockMacroEntry[];

  readonly language: () => string;

  constructor(
    macros: readonly MockMacroEntry[] | (() => readonly MockMacroEntry[]) = () => [],
    language: string | (() => string) = 'zh-Hans',
  ) {
    this.macros = typeof macros === 'function' ? macros : () => macros;
    this.language = typeof language === 'function' ? language : () => language;
  }

  /** Every editor command the host serves; the main mock only forwards the method and params. */
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

  /** Applies one edit to the active document and stores the refreshed snapshot in its place. */
  change(edit: (document: EditorSnapshot) => EditorSnapshot): EditorSnapshot | null {
    return changeCommand(this, edit);
  }

  activeSnapshot(): EditorSnapshot | null {
    return activeSnapshotCommand(this);
  }

  refreshActionForm(): void {
    refreshActionForm(this);
  }

  nextStepId(): string {
    this.stepSequence += 1;
    return 'mock-' + String(this.stepSequence);
  }

  nextTriggerId(): string {
    this.triggerSequence += 1;
    return 'trigger-' + String(this.triggerSequence);
  }

  patchTrigger(
    params: Record<string, unknown>,
    update: (trigger: EditorTrigger) => EditorTrigger,
  ): EditorSnapshot | null {
    return patchTriggerCommand(this, params, update);
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
