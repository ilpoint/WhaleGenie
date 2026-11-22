import type { EditorCore } from './editor-core.js';
import type { BridgeMethod, EditorCharacterPad } from '../../../shared/bridge/protocol.js';

/** The triggers command family. All state lives in EditorCore; C# remains the source of truth. */
export class EditorTriggersService {
  constructor(private readonly core: EditorCore) {}

  private trigger(method: BridgeMethod, params: Record<string, unknown>): Promise<boolean> {
    return this.core.snapshotCommand(method, params);
  }

  addTrigger(kind: string): Promise<boolean> {
    return this.trigger('editor.sessionTriggerAdd', { kind });
  }

  removeTrigger(triggerId: string): Promise<boolean> {
    return this.trigger('editor.sessionTriggerRemove', { triggerId });
  }

  setTriggerEnabled(triggerId: string, enabled: boolean): Promise<boolean> {
    return this.trigger('editor.sessionTriggerEnabled', { triggerId, enabled });
  }

  setTriggerBinding(triggerId: string, binding: string): Promise<boolean> {
    return this.trigger('editor.sessionTriggerBinding', { triggerId, binding });
  }

  setTriggerLoop(triggerId: string, loop: string): Promise<boolean> {
    return this.trigger('editor.sessionTriggerLoop', { triggerId, loop });
  }

  setTriggerOnce(triggerId: string, once: boolean): Promise<boolean> {
    return this.trigger('editor.sessionTriggerOnce', { triggerId, once });
  }

  setTriggerTimer(triggerId: string, field: string, value: string): Promise<boolean> {
    return this.trigger('editor.sessionTriggerTimer', { triggerId, field, value });
  }

  setTriggerFile(triggerId: string, field: string, value: string): Promise<boolean> {
    return this.trigger('editor.sessionTriggerFile', { triggerId, field, value });
  }

  setTriggerProcess(triggerId: string, field: string, value: string): Promise<boolean> {
    return this.trigger('editor.sessionTriggerProcess', { triggerId, field, value });
  }

  setTriggerHotstring(triggerId: string, value: string): Promise<boolean> {
    return this.trigger('editor.sessionTriggerHotstring', { triggerId, value });
  }

  setTriggerHotstringOption(triggerId: string, field: string, value: string): Promise<boolean> {
    return this.trigger('editor.sessionTriggerHotstringOption', { triggerId, field, value });
  }

  setTriggerHotstringEndKey(triggerId: string, value: string, checked: boolean): Promise<boolean> {
    return this.trigger('editor.sessionTriggerHotstringEndKey', { triggerId, value, checked });
  }

  resetTriggerHotstringEndKeys(triggerId: string): Promise<boolean> {
    return this.trigger('editor.sessionTriggerHotstringEndReset', { triggerId });
  }

  captureTriggerHotstringEnd(triggerId: string, character: string): Promise<boolean> {
    return this.trigger('editor.sessionTriggerHotstringEndCapture', { triggerId, character });
  }

  setTriggerInterfaceHotkey(triggerId: string, binding: string): Promise<boolean> {
    return this.trigger('editor.sessionTriggerInterfaceHotkey', { triggerId, binding });
  }

  clearTriggerInterfaceHotkey(triggerId: string): Promise<boolean> {
    return this.trigger('editor.sessionTriggerInterfaceHotkeyClear', { triggerId });
  }

  async hotstringKeyPad(): Promise<EditorCharacterPad | null> {
    try {
      return await this.core.client.call<EditorCharacterPad>('editor.hotstringKeyPad');
    } catch (error) {
      this.core.fail(error);
      return null;
    }
  }

  setTriggerColor(triggerId: string, field: string, value: string): Promise<boolean> {
    return this.trigger('editor.sessionTriggerColor', { triggerId, field, value });
  }

  setTriggerWindow(triggerId: string, field: string, value: string): Promise<boolean> {
    return this.trigger('editor.sessionTriggerWindow', { triggerId, field, value });
  }

  setTriggerInterface(triggerId: string, field: string, value: string): Promise<boolean> {
    return this.trigger('editor.sessionTriggerInterface', { triggerId, field, value });
  }

  setTriggerIdle(triggerId: string, value: string): Promise<boolean> {
    return this.trigger('editor.sessionTriggerIdle', { triggerId, value });
  }
}
