import type { BridgeApi } from '../../src/shared/bridge/client.js';
import type { BridgeEventName, BridgeEvents } from '../../src/shared/bridge/protocol.js';

type Reply<T> = T | ((params: unknown) => T | Promise<T>);

/**
 * An in-memory BridgeApi. It records the calls a service makes and answers from a small table,
 * so command tests do not need a browser, a WebView, or the real host.
 */
export class FakeBridge implements BridgeApi {
  readonly calls: { method: string; params?: unknown }[] = [];

  private readonly replies = new Map<string, Reply<unknown>>();
  private readonly failures = new Map<string, unknown>();
  private readonly listeners = new Map<string, Set<(payload: unknown) => void>>();

  reply<T>(method: string, value: Reply<T>): void {
    this.replies.set(method, value as Reply<unknown>);
  }

  fail(method: string, error: unknown): void {
    this.failures.set(method, error);
  }

  async call<T = void>(method: string, params?: unknown): Promise<T> {
    this.calls.push(params === undefined ? { method } : { method, params });

    if (this.failures.has(method)) {
      const error = this.failures.get(method);
      this.failures.delete(method);
      throw error;
    }

    const reply = this.replies.get(method);
    if (reply === undefined) {
      throw new Error('FakeBridge has no reply for ' + method);
    }

    return (typeof reply === 'function'
      ? await (reply as (value: unknown) => T | Promise<T>)(params)
      : reply) as T;
  }

  on<K extends BridgeEventName>(event: K, handler: (payload: BridgeEvents[K]) => void): () => void {
    const set = this.listeners.get(event) ?? new Set();
    set.add(handler as (payload: unknown) => void);
    this.listeners.set(event, set);
    return () => set.delete(handler as (payload: unknown) => void);
  }

  emit<K extends BridgeEventName>(event: K, payload: BridgeEvents[K]): void {
    for (const handler of this.listeners.get(event) ?? []) {
      handler(payload);
    }
  }
}
