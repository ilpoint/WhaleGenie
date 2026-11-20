import type { BridgeClient } from './client.js';

/**
 * 桥的单例持有者。main.ts 在启动装配时 connect，其余代码只管用；在 connect 之前调用是
 * 装配顺序的 bug，直接抛出来比给个空实现更诚实。
 */
class BridgeHolder {
  private inner?: BridgeClient;

  connect(client: BridgeClient): void {
    this.inner = client;
  }

  get value(): BridgeClient {
    if (!this.inner) {
      throw new Error('the bridge is not connected yet; connect it in main.ts before anything renders');
    }
    return this.inner;
  }
}

export const bridge = new BridgeHolder();
