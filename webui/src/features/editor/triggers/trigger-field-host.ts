import type { EditorTrigger } from '../../../shared/bridge/protocol.js';

/** The component parts the trigger fields need for capture mode and character pads. */
export interface TriggerFieldHost {
  capturingTrigger: string;
  keyName(event: KeyboardEvent): string;
  startTriggerCapture(triggerId: string): void;
  openCharacterPad(trigger: EditorTrigger): Promise<void>;
  beginEndCapture(trigger: EditorTrigger): void;
  beginInterfaceCapture(trigger: EditorTrigger): void;
}

