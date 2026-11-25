import { describe, expect, it } from 'vitest';
import { hotkeyKeys } from '../../src/app/nav-model.js';

describe('system hotkey lines', () => {
  it('keeps a single key on one line', () => {
    expect(hotkeyKeys('Home')).toEqual(['Home']);
  });

  it('splits a chord into the keys the side bar stacks', () => {
    expect(hotkeyKeys('Ctrl+K+L')).toEqual(['Ctrl', 'K', 'L']);
  });

  it('trims the spaces around the separators and ignores empty pieces', () => {
    expect(hotkeyKeys(' Ctrl + Alt + F9 ')).toEqual(['Ctrl', 'Alt', 'F9']);
    expect(hotkeyKeys('')).toEqual([]);
  });
});
