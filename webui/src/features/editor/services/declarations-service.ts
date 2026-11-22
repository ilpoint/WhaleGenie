import type { BridgeMethod } from '../../../shared/bridge/protocol.js';
import type { EditorCore } from './editor-core.js';

/** The declarations command family. All state lives in EditorCore; C# remains the source of truth. */
export class EditorDeclarationsService {
  constructor(private readonly core: EditorCore) {}

  private declaration(method: BridgeMethod, params: Record<string, unknown>): Promise<boolean> {
    return this.core.snapshotCommand(method, params);
  }

  addCallableParameter(): Promise<boolean> {
    return this.declaration('editor.sessionParameterDeclAdd', {});
  }

  patchCallableParameter(index: number, field: string, value: string): Promise<boolean> {
    return this.declaration('editor.sessionParameterDeclPatch', { index, field, value });
  }

  removeCallableParameter(index: number): Promise<boolean> {
    return this.declaration('editor.sessionParameterDeclRemove', { index });
  }

  moveCallableParameter(index: number, offset: number): Promise<boolean> {
    return this.declaration('editor.sessionParameterDeclMove', { index, offset });
  }

  addCallableResult(): Promise<boolean> {
    return this.declaration('editor.sessionResultDeclAdd', {});
  }

  patchCallableResult(index: number, field: string, value: string): Promise<boolean> {
    return this.declaration('editor.sessionResultDeclPatch', { index, field, value });
  }

  removeCallableResult(index: number): Promise<boolean> {
    return this.declaration('editor.sessionResultDeclRemove', { index });
  }

  moveCallableResult(index: number, offset: number): Promise<boolean> {
    return this.declaration('editor.sessionResultDeclMove', { index, offset });
  }

  addCallableParameterField(index: number): Promise<boolean> {
    return this.declaration('editor.sessionParameterFieldAdd', { index });
  }

  patchCallableParameterField(index: number, fieldIndex: number, field: string,
    value: string): Promise<boolean> {
    return this.declaration('editor.sessionParameterFieldPatch', {
      index, fieldIndex, field, value,
    });
  }

  removeCallableParameterField(index: number, fieldIndex: number): Promise<boolean> {
    return this.declaration('editor.sessionParameterFieldRemove', { index, fieldIndex });
  }

  addCallableResultField(index: number): Promise<boolean> {
    return this.declaration('editor.sessionResultFieldAdd', { index });
  }

  patchCallableResultField(index: number, fieldIndex: number, field: string,
    value: string): Promise<boolean> {
    return this.declaration('editor.sessionResultFieldPatch', {
      index, fieldIndex, field, value,
    });
  }

  removeCallableResultField(index: number, fieldIndex: number): Promise<boolean> {
    return this.declaration('editor.sessionResultFieldRemove', { index, fieldIndex });
  }
}
