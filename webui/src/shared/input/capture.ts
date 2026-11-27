/**
 * Turning the browser's key and pointer names into the vocabulary the program stores.
 *
 * The keyboard's own state decides some names: the main-row minus and the number-pad minus both
 * arrive with `key === '-'`, while the program keeps them apart as `-` and `NumPadSubtract`.
 * The number pad is therefore read from `code`, which names the physical key, not from `key`,
 * which names the character it produced. Every capture path goes through here.
 */
export function keyboardChord(event: KeyboardEvent): string | null {
  if (isModifier(event.key)) {
    return null;
  }

  const key = keyName(event);
  if (!key) {
    return null;
  }

  const parts: string[] = [];
  if (event.ctrlKey) {
    parts.push('Ctrl');
  }
  if (event.altKey) {
    parts.push('Alt');
  }
  if (event.shiftKey) {
    parts.push('Shift');
  }
  if (event.metaKey) {
    parts.push('Win');
  }
  parts.push(key);
  return parts.join('+');
}

/** Mouse 1 is the middle button; 3 and 4 are the side buttons Windows calls Mouse 4 and 5. */
export function pointerBinding(event: PointerEvent): string | null {
  switch (event.button) {
    case 1:
      return 'Mouse Middle';
    case 3:
      return 'Mouse 4';
    case 4:
      return 'Mouse 5';
    default:
      return null;
  }
}

const numberPadNames: Record<string, string> = {
  Numpad0: 'NumPad0',
  Numpad1: 'NumPad1',
  Numpad2: 'NumPad2',
  Numpad3: 'NumPad3',
  Numpad4: 'NumPad4',
  Numpad5: 'NumPad5',
  Numpad6: 'NumPad6',
  Numpad7: 'NumPad7',
  Numpad8: 'NumPad8',
  Numpad9: 'NumPad9',
  NumpadAdd: 'NumPadAdd',
  NumpadSubtract: 'NumPadSubtract',
  NumpadMultiply: 'NumPadMultiply',
  NumpadDivide: 'NumPadDivide',
  NumpadDecimal: 'NumPadDecimal',
  NumpadEnter: 'NumPadEnter',
  NumpadEqual: 'NumPadEquals',
  NumpadComma: 'NumPadSeparator',
};

export function keyName(event: KeyboardEvent): string | null {
  const numberPad = numberPadNames[event.code];
  if (numberPad) {
    return numberPad;
  }

  const named: Record<string, string> = {
    ArrowLeft: 'Left',
    ArrowRight: 'Right',
    ArrowUp: 'Up',
    ArrowDown: 'Down',
    Escape: 'Esc',
    ' ': 'Space',
    Spacebar: 'Space',
    Enter: 'Enter',
    Backspace: 'Backspace',
    Delete: 'Delete',
    Tab: 'Tab',
    PageUp: 'PageUp',
    PageDown: 'PageDown',
    Home: 'Home',
    End: 'End',
    Insert: 'Insert',
    CapsLock: 'CapsLock',
    NumLock: 'NumLock',
    ScrollLock: 'ScrollLock',
    Pause: 'Pause',
    PrintScreen: 'PrintScreen',
    ContextMenu: 'Apps',
    '+': 'plus',
  };
  const key = event.key;
  if (key in named) {
    return named[key] ?? null;
  }
  if (/^F\d{1,2}$/.test(key) || key.length === 1) {
    return key.length === 1 ? key.toUpperCase() : key;
  }
  return null;
}

function isModifier(key: string): boolean {
  return ['Control', 'Shift', 'Alt', 'Meta', 'AltGraph'].includes(key);
}