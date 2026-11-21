/** Turning the browser's key and pointer names into the vocabulary the program stores. */
export function keyboardChord(event: KeyboardEvent): string | null {
  if (isModifier(event.key)) {
    return null;
  }

  const key = keyName(event.key);
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

function isModifier(key: string): boolean {
  return ['Control', 'Shift', 'Alt', 'Meta', 'AltGraph'].includes(key);
}

function keyName(key: string): string | null {
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
  if (key in named) {
    return named[key] ?? null;
  }
  if (/^F\d{1,2}$/.test(key) || key.length === 1) {
    return key.length === 1 ? key.toUpperCase() : key;
  }
  return null;
}