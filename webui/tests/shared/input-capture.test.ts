import { describe, expect, it } from 'vitest';
import { keyboardChord, keyName } from '../../src/shared/input/capture.js';

const key = (init: KeyboardEventInit) => new KeyboardEvent('keydown', init);

describe('keyboard capture', () => {
  it('keeps number-pad keys apart from the main row', () => {
    expect(keyboardChord(key({ key: '-', code: 'Minus' }))).toBe('-');
    expect(keyboardChord(key({ key: '-', code: 'NumpadSubtract' }))).toBe('NumPadSubtract');
    expect(keyboardChord(key({ key: 'Enter', code: 'NumpadEnter' }))).toBe('NumPadEnter');
    expect(keyboardChord(key({ key: '7', code: 'Numpad7' }))).toBe('NumPad7');
    expect(keyboardChord(key({
      key: '-', code: 'NumpadSubtract', ctrlKey: true,
    }))).toBe('Ctrl+NumPadSubtract');
    expect(keyName(key({ key: 'Home', code: 'Numpad7' }))).toBe('NumPad7');
  });

  it('reads named keys, spaces and modifiers the way the C# vocabulary does', () => {
    expect(keyboardChord(key({ key: ' ', code: 'Space' }))).toBe('Space');
    expect(keyboardChord(key({ key: 'Escape', code: 'Escape' }))).toBe('Esc');
    expect(keyboardChord(key({ key: 'a', code: 'KeyA' }))).toBe('A');
    expect(keyboardChord(key({ key: 'Control', code: 'ControlLeft' }))).toBeNull();
  });
});
