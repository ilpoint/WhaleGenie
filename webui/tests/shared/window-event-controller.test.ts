import { describe, expect, it } from 'vitest';
import { WindowEventController } from '../../src/shared/lit/window-event-controller.js';
import { FakeHost } from '../fakes/host.js';

describe('WindowEventController', () => {
  it('listens only while the host is connected', () => {
    const host = new FakeHost();
    const seen: string[] = [];
    new WindowEventController(host, 'keydown', (event) => seen.push(event.key));

    host.connect();
    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'F8' }));
    expect(seen).toEqual(['F8']);

    host.disconnect();
    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'F9' }));
    expect(seen).toEqual(['F8']);
  });

  it('capture options are passed to both add and remove', () => {
    const host = new FakeHost();
    const seen: string[] = [];
    new WindowEventController(host, 'keydown', (event) => seen.push(event.key), { capture: true });

    host.connect();
    host.disconnect();
    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'F8' }));
    expect(seen).toEqual([]);
  });
});
