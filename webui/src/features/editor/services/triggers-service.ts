import type { EditorCore } from './editor-core.js';
import type {
  EditorCharacterPad,
  EditorTriggerCommand,
} from '../../../shared/bridge/protocol.js';

/** The triggers command family. All state lives in EditorCore; C# remains the source of truth. */
export class EditorTriggersService {
  constructor(private readonly core: EditorCore) {}

  private trigger(command: EditorTriggerCommand): Promise<boolean> {
    return this.core.snapshotCommand('editor.trigger', command);
  }

  private field(triggerId: string,
    family: 'timer' | 'file' | 'process' | 'hotstringOption' | 'color' | 'window' | 'interface',
    field: string, value: string): Promise<boolean> {
    return this.trigger({ kind: 'field', triggerId, family, field, value });
  }

  addTrigger(kind: string): Promise<boolean> {
    return this.trigger({ kind: 'add', triggerKind: kind });
  }

  removeTrigger(triggerId: string): Promise<boolean> {
    return this.trigger({ kind: 'remove', triggerId });
  }

  setTriggerEnabled(triggerId: string, enabled: boolean): Promise<boolean> {
    return this.trigger({ kind: 'enabled', triggerId, enabled });
  }

  setTriggerBinding(triggerId: string, binding: string): Promise<boolean> {
    return this.trigger({ kind: 'binding', triggerId, binding });
  }

  setTriggerLoop(triggerId: string, loop: string): Promise<boolean> {
    return this.trigger({ kind: 'loop', triggerId, loop });
  }

  setTriggerOnce(triggerId: string, once: boolean): Promise<boolean> {
    return this.trigger({ kind: 'once', triggerId, once });
  }

  setTriggerTimer(triggerId: string, field: string, value: string): Promise<boolean> {
    return this.field(triggerId, 'timer', field, value);
  }

  setTriggerFile(triggerId: string, field: string, value: string): Promise<boolean> {
    return this.field(triggerId, 'file', field, value);
  }

  setTriggerProcess(triggerId: string, field: string, value: string): Promise<boolean> {
    return this.field(triggerId, 'process', field, value);
  }

  setTriggerHotstring(triggerId: string, value: string): Promise<boolean> {
    return this.trigger({ kind: 'hotstring', triggerId, value });
  }

  setTriggerHotstringOption(triggerId: string, field: string, value: string): Promise<boolean> {
    return this.field(triggerId, 'hotstringOption', field, value);
  }

  setTriggerHotstringEndKey(triggerId: string, value: string, checked: boolean): Promise<boolean> {
    return this.trigger({ kind: 'hotstringEndKey', triggerId, value, checked });
  }

  resetTriggerHotstringEndKeys(triggerId: string): Promise<boolean> {
    return this.trigger({ kind: 'hotstringEndReset', triggerId });
  }

  captureTriggerHotstringEnd(triggerId: string, character: string): Promise<boolean> {
    return this.trigger({ kind: 'hotstringEndCapture', triggerId, character });
  }

  setTriggerInterfaceHotkey(triggerId: string, binding: string): Promise<boolean> {
    return this.trigger({ kind: 'interfaceHotkey', triggerId, binding });
  }

  clearTriggerInterfaceHotkey(triggerId: string): Promise<boolean> {
    return this.trigger({ kind: 'interfaceHotkeyClear', triggerId });
  }

  async hotstringKeyPad(): Promise<EditorCharacterPad | null> {
    try {
      return await this.core.request<EditorCharacterPad>('editor.hotstringKeyPad');
    } catch (error) {
      this.core.fail(error);
      return null;
    }
  }

  setTriggerColor(triggerId: string, field: string, value: string): Promise<boolean> {
    return this.field(triggerId, 'color', field, value);
  }

  setTriggerWindow(triggerId: string, field: string, value: string): Promise<boolean> {
    return this.field(triggerId, 'window', field, value);
  }

  setTriggerInterface(triggerId: string, field: string, value: string): Promise<boolean> {
    return this.field(triggerId, 'interface', field, value);
  }

  setTriggerIdle(triggerId: string, value: string): Promise<boolean> {
    return this.trigger({ kind: 'idle', triggerId, value });
  }
}
