import { describe, expect, it } from 'vitest';
import { Store } from '../../src/shared/store.js';
import { StoreController } from '../../src/shared/lit/store-controller.js';
import { FakeHost } from '../fakes/host.js';

describe('StoreController', () => {
  it('subscribes on connect, mirrors values, and unsubscribes on disconnect', () => {
    const store = new Store(0);
    const host = new FakeHost();
    const seen: number[] = [];
    new StoreController(host, store, (value) => seen.push(value));

    host.connect();
    expect(seen).toEqual([0]);
    expect(host.updates).toBe(1);

    store.set(1);
    expect(seen).toEqual([0, 1]);
    expect(host.updates).toBe(2);

    host.disconnect();
    store.set(2);
    expect(seen).toEqual([0, 1]);
  });

  it('reconnects without keeping a stale subscription', () => {
    const store = new Store(0);
    const host = new FakeHost();
    const seen: number[] = [];
    new StoreController(host, store, (value) => seen.push(value));

    host.connect();
    host.disconnect();
    host.connect();
    store.set(3);

    expect(seen).toEqual([0, 0, 3]);
    expect(host.updates).toBe(3);
  });
});
