import type { EditorCore } from './editor-core.js';

/** The variables command family. All state lives in EditorCore; C# remains the source of truth. */
export class EditorVariablesService {
  constructor(private readonly core: EditorCore) {}

  addVariable(): Promise<boolean> {
    return this.core.snapshotCommand('editor.sessionVariableAdd', {});
  }

  patchVariable(originalName: string, field: string, value: string): Promise<boolean> {
    return this.core.snapshotCommand('editor.sessionVariablePatch', { originalName, field, value });
  }

  removeVariable(name: string): Promise<boolean> {
    return this.core.snapshotCommand('editor.sessionVariableRemove', { name });
  }

  addGlobal(): Promise<boolean> {
    return this.core.snapshotCommand('editor.sessionGlobalAdd', {});
  }

  patchGlobal(originalName: string, field: string, value: string): Promise<boolean> {
    return this.core.snapshotCommand('editor.sessionGlobalPatch', { originalName, field, value });
  }

  removeGlobal(name: string): Promise<boolean> {
    return this.core.snapshotCommand('editor.sessionGlobalRemove', { name });
  }
}
