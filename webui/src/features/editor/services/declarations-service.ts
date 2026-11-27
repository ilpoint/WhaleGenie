import type { EditorDeclarationCommand, EditorValuePartSnapshot } from '../../../shared/bridge/protocol.js';
import type { EditorCore } from './editor-core.js';

/** The declarations command family. All state lives in EditorCore; C# remains the source of truth. */
export class EditorDeclarationsService {
  constructor(private readonly core: EditorCore) {}

  private declaration(command: EditorDeclarationCommand): Promise<boolean> {
    return this.core.snapshotCommand('editor.declaration', command);
  }

  addCallableParameter(): Promise<boolean> {
    return this.declaration({ family: 'parameter', kind: 'add' });
  }

  patchCallableParameter(index: number, field: string, value: string,
    parts?: EditorValuePartSnapshot[]): Promise<boolean> {
    return this.declaration({ family: 'parameter', kind: 'patch', index, field, value, parts: parts ?? null });
  }

  removeCallableParameter(index: number): Promise<boolean> {
    return this.declaration({ family: 'parameter', kind: 'remove', index });
  }

  moveCallableParameter(index: number, offset: number): Promise<boolean> {
    return this.declaration({ family: 'parameter', kind: 'move', index, offset });
  }

  addCallableResult(): Promise<boolean> {
    return this.declaration({ family: 'result', kind: 'add' });
  }

  patchCallableResult(index: number, field: string, value: string): Promise<boolean> {
    return this.declaration({ family: 'result', kind: 'patch', index, field, value });
  }

  removeCallableResult(index: number): Promise<boolean> {
    return this.declaration({ family: 'result', kind: 'remove', index });
  }

  moveCallableResult(index: number, offset: number): Promise<boolean> {
    return this.declaration({ family: 'result', kind: 'move', index, offset });
  }

  addCallableParameterField(index: number): Promise<boolean> {
    return this.declaration({ family: 'parameter', kind: 'fieldAdd', index });
  }

  patchCallableParameterField(index: number, fieldIndex: number, field: string,
    value: string): Promise<boolean> {
    return this.declaration({
      family: 'parameter', kind: 'fieldPatch', index, fieldIndex, field, value,
    });
  }

  removeCallableParameterField(index: number, fieldIndex: number): Promise<boolean> {
    return this.declaration({ family: 'parameter', kind: 'fieldRemove', index, fieldIndex });
  }

  addCallableResultField(index: number): Promise<boolean> {
    return this.declaration({ family: 'result', kind: 'fieldAdd', index });
  }

  patchCallableResultField(index: number, fieldIndex: number, field: string,
    value: string): Promise<boolean> {
    return this.declaration({
      family: 'result', kind: 'fieldPatch', index, fieldIndex, field, value,
    });
  }

  removeCallableResultField(index: number, fieldIndex: number): Promise<boolean> {
    return this.declaration({ family: 'result', kind: 'fieldRemove', index, fieldIndex });
  }
}
