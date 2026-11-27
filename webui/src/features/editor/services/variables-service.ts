import type { EditorValuePartSnapshot, EditorVariableCommand } from '../../../shared/bridge/protocol.js';
import type { EditorCore } from './editor-core.js';

/** The variables command family. All state lives in EditorCore; C# remains the source of truth. */
export class EditorVariablesService {
  constructor(private readonly core: EditorCore) {}

  private variable(command: EditorVariableCommand): Promise<boolean> {
    return this.core.snapshotCommand('editor.variable', command);
  }

  addVariable(): Promise<boolean> {
    return this.variable({ family: 'local', kind: 'add' });
  }

  patchVariable(originalName: string, field: string, value: string,
    parts?: EditorValuePartSnapshot[]): Promise<boolean> {
    return this.variable({
      family: 'local', kind: 'patch', originalName, field, value, parts: parts ?? null,
    });
  }

  removeVariable(name: string): Promise<boolean> {
    return this.variable({ family: 'local', kind: 'remove', name });
  }

  addGlobal(): Promise<boolean> {
    return this.variable({ family: 'global', kind: 'add' });
  }

  patchGlobal(originalName: string, field: string, value: string,
    parts?: EditorValuePartSnapshot[]): Promise<boolean> {
    return this.variable({
      family: 'global', kind: 'patch', originalName, field, value, parts: parts ?? null,
    });
  }

  removeGlobal(name: string): Promise<boolean> {
    return this.variable({ family: 'global', kind: 'remove', name });
  }
}
