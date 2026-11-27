import { describe, expect, it } from 'vitest';
import { isTextEntryEvent, isTextEntryTarget } from '../../src/shared/input/typing.js';

function input(tag: 'input' | 'textarea' | 'select' | 'div'): HTMLElement {
  return document.createElement(tag);
}

describe('text entry detection', () => {
  it('recognizes the controls a key is typed into', () => {
    expect(isTextEntryTarget(input('input'))).toBe(true);
    expect(isTextEntryTarget(input('textarea'))).toBe(true);
    expect(isTextEntryTarget(input('select'))).toBe(true);
    expect(isTextEntryTarget(input('div'))).toBe(false);
  });

  it('recognizes a contenteditable host', () => {
    const host = input('div');
    Object.defineProperty(host, 'isContentEditable', { value: true });
    expect(isTextEntryTarget(host)).toBe(true);
  });

  it('reads the composed path so a shadow-root input still counts', () => {
    const host = input('div');
    const box = input('input');
    host.append(box);

    const event = {
      composedPath: () => [box, host, document.body],
    } as unknown as Event;

    expect(isTextEntryEvent(event)).toBe(true);
    expect(isTextEntryEvent({
      composedPath: () => [host, document.body],
    } as unknown as Event)).toBe(false);
  });
});
