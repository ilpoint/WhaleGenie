import type { EditorCore } from './editor-core.js';
import type {
  EditorValueCommand,
  EditorValueKeyCommand,
  EditorValueOpenCommand,
  EditorValueOutcome,
  EditorValueSnapshot,
} from '../../../shared/bridge/protocol.js';
import { describeError } from '../../../shared/errors.js';

/** The value command family. All state lives in EditorCore; C# remains the source of truth. */
export class EditorValueService {
  constructor(private readonly core: EditorCore) {}

  async openValue(stepId: string, parameter: string): Promise<void> {
    const command: EditorValueCommand = { kind: 'open', step: stepId, parameter };
    await this.open(command);
  }

  async openConditionValue(stepId: string, parameter: string, path: number[],
    field: 'left' | 'right'): Promise<void> {
    await this.openValueAt({
      step: stepId,
      parameter,
      conditionField: field,
      conditionPath: path,
    });
  }

  async openRowValue(stepId: string, parameter: string, rowIndex: number,
    column: string): Promise<void> {
    await this.openValueAt({
      step: stepId,
      parameter,
      rowIndex,
      column,
    });
  }

  private async openValueAt(params: Omit<EditorValueOpenCommand, 'kind'>): Promise<void> {
    await this.open({ kind: 'open', ...params });
  }

  private async open(command: EditorValueCommand): Promise<void> {
    try {
      const snapshot = await this.core.request<EditorValueSnapshot>('editor.value', command);
      this.core.state.valueEditor = snapshot;
      this.core.state.valueError = '';
    } catch (error) {
      this.core.state.valueError = describeError(error);
      this.core.state.error = this.core.state.valueError;
    }

    this.core.notify();
  }

  async valueCommand(action: string, params: Record<string, unknown> = {}): Promise<void> {
    try {
      const command = { kind: 'command', action, ...params } as EditorValueKeyCommand;
      const snapshot = await this.core.request<EditorValueSnapshot>('editor.value', command);
      this.core.state.valueEditor = snapshot;
      this.core.state.valueError = '';
    } catch (error) {
      this.core.state.valueError = describeError(error);
      this.core.state.error = this.core.state.valueError;
    }

    this.core.notify();
  }

  /** Confirms the value window; true only once it closed, so a refused draft stops a save. */
  async confirmValue(): Promise<boolean> {
    try {
      const command: EditorValueCommand = { kind: 'confirm' };
      const outcome = await this.core.request<EditorValueOutcome>('editor.value', command);
      if (outcome.closed) {
        this.core.state.valueEditor = null;
        this.core.state.valueError = '';
        if (outcome.applied && outcome.state !== null) {
          this.core.applySnapshot(outcome.state);
        }
      } else {
        this.core.state.valueEditor = outcome.snapshot;
        this.core.state.valueError = outcome.snapshot.noteIsProblem ? outcome.snapshot.note : '';
      }

      this.core.notify();
      return outcome.closed;
    } catch (error) {
      this.core.state.valueError = describeError(error);
      this.core.state.error = this.core.state.valueError;
      this.core.notify();
      return false;
    }
  }

  async cancelValue(): Promise<void> {
    try {
      const command: EditorValueCommand = { kind: 'cancel' };
      await this.core.request<EditorValueOutcome>('editor.value', command);
    } catch (error) {
      this.core.state.error = describeError(error);
    }

    this.core.state.valueEditor = null;
    this.core.state.valueError = '';
    this.core.notify();
  }

  closeValue(): void {
    if (this.core.state.valueEditor) {
      this.core.state.valueEditor = null;
      this.core.state.valueError = '';
      this.core.notify();
    }
  }
}