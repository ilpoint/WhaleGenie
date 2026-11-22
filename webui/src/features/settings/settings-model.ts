import type { SettingsWheelSlot, WindowRuleState } from '../../shared/bridge/protocol.js';
import type { WheelSlotDraft } from './settings-wheel.js';

export type CaptureKind = 'systemHotkey' | 'recordStopKey' | 'menu' | 'wheelRepeat' | '';

export interface RuleEditor {
  kind: 'menu-add' | 'menu-edit' | 'wheel-add' | 'wheel-edit';
  key: string;
  title: string;
  rule: WindowRuleState;
}

export function draftOf(slot: SettingsWheelSlot): WheelSlotDraft {
  return {
    ring: slot.ring,
    index: slot.index,
    position: slot.position,
    macro: slot.macro,
    note: slot.note,
    icon: slot.icon,
    inherited: slot.inherited,
    overridden: slot.overridden,
  };
}
