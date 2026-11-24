import { afterEach, describe, expect, it, vi } from 'vitest';
import { bridge } from '../../src/shared/bridge/bridge.js';
import type { WindowEntryState } from '../../src/shared/bridge/protocol.js';
import { filterWindows } from '../../src/shared/window/window-list.js';
import type { WgWindowList } from '../../src/shared/window/window-list.js';
import { FakeBridge } from '../fakes/bridge.js';

const windows: WindowEntryState[] = [
  {
    handle: 1, title: 'Untitled - Notepad', process: 'notepad', className: 'Notepad',
    detail: '800 × 600 · normal', summary: 'notepad · Notepad · 800 × 600 · normal',
  },
  {
    handle: 2, title: 'Calculator', process: 'calc', className: 'CalcFrame',
    detail: '320 × 240 · normal', summary: 'calc · CalcFrame · 320 × 240 · normal',
  },
];

afterEach(() => {
  document.body.replaceChildren();
});

async function open(fake: FakeBridge): Promise<WgWindowList> {
  bridge.connect(fake);
  fake.reply('window.list', { windows, hows: [] });
  const element = document.createElement('wg-window-list') as WgWindowList;
  document.body.append(element);
  await vi.waitFor(() => {
    expect(element.shadowRoot?.querySelectorAll('.window')).toHaveLength(windows.length);
  });
  return element;
}

describe('window list', () => {
  it('searches title, program and class', () => {
    expect(filterWindows(windows, '').map((window) => window.handle)).toEqual([1, 2]);
    expect(filterWindows(windows, 'calc').map((window) => window.handle)).toEqual([2]);
    expect(filterWindows(windows, 'frame').map((window) => window.handle)).toEqual([2]);
    expect(filterWindows(windows, 'notepad').map((window) => window.handle)).toEqual([1]);
    expect(filterWindows(windows, 'nothing')).toEqual([]);
  });

  it('lists what window.list answers and confirms the chosen row', async () => {
    const fake = new FakeBridge();
    const element = await open(fake);
    const chosen: string[] = [];
    element.addEventListener('wg-window-list-choose',
      (event) => chosen.push((event as CustomEvent<WindowEntryState>).detail.title));

    const rows = element.shadowRoot!.querySelectorAll<HTMLElement>('.window');
    expect(rows[0]?.textContent).toContain('Notepad');
    expect(rows[1]?.dataset['selected']).toBe('false');
    rows[1]!.click();
    await element.updateComplete;
    expect(rows[1]?.dataset['selected']).toBe('true');
    element.shadowRoot!.querySelectorAll<HTMLElement>('fluent-button')[1]!.click();

    expect(chosen).toEqual(['Calculator']);
    expect(fake.calls.map((call) => call.method)).toEqual(['window.list']);
  });

  it('narrows the list as the search box is used', async () => {
    const fake = new FakeBridge();
    const element = await open(fake);
    const search = element.shadowRoot!.querySelector('input') as HTMLInputElement;
    search.value = 'calc';
    search.dispatchEvent(new Event('input', { bubbles: true }));
    await element.updateComplete;

    const rows = element.shadowRoot!.querySelectorAll('.window');
    expect(rows).toHaveLength(1);
    expect(rows[0]?.textContent).toContain('Calculator');
  });

  it('asks to be dismissed without a choice', async () => {
    const fake = new FakeBridge();
    const element = await open(fake);
    let cancelled = 0;
    element.addEventListener('wg-window-list-cancel', () => { cancelled++; });

    element.shadowRoot!.querySelectorAll<HTMLElement>('fluent-button')[0]!.click();

    expect(cancelled).toBe(1);
  });

  it('shows an empty list when the host has nothing to offer', async () => {
    const fake = new FakeBridge();
    bridge.connect(fake);
    fake.reply('window.list', { windows: [], hows: [] });
    const element = document.createElement('wg-window-list') as WgWindowList;
    document.body.append(element);
    await vi.waitFor(() => {
      expect(element.shadowRoot?.querySelector('.windows .hint')).not.toBeNull();
    });
    expect(element.shadowRoot!.querySelectorAll('.window')).toHaveLength(0);
  });
});
