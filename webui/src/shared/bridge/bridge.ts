import type { BridgeApi } from './client.js';
import type { BridgeEventName, BridgeEvents } from './protocol.js';

/**
 * 桥的单例持有者。main.ts 在启动装配时 connect，其余代码只管用；在 connect 之前调用是
 * 装配顺序的 bug，直接抛出来比给个空实现更诚实。
 */
class BridgeHolder {
  private inner?: BridgeApi;

  connect(client: BridgeApi): void {
    this.inner = client;
  }

  get value(): BridgeApi {
    if (!this.inner) {
      throw new Error('the bridge is not connected yet; connect it in main.ts before anything renders');
    }
    return this.inner;
  }
}

export const bridge = new BridgeHolder();

/**
 * A lazy facade for stores and services. Resolving the holder at call time keeps the module
 * importable before main.ts connects, and lets a test pass its own BridgeApi instead.
 */
export const bridgeApi: BridgeApi = {
  call<T = void>(method: string, params?: unknown): Promise<T> {
    return bridge.value.call<T>(method, params);
  },
  on<K extends BridgeEventName>(event: K, handler: (payload: BridgeEvents[K]) => void): () => void {
    return bridge.value.on(event, handler);
  },
};

export type { BridgeApi } from './client.js';
