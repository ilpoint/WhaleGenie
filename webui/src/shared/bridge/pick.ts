import { HostTransport, type BridgeTransport } from './transport.js';
import { MockTransport } from './mock.js';

/**
 * 选传输：宿主进程注入了发信函数，就是真壳；`?bridge=mock|host` 强制指定；都没有时落在
 * mock 上，让网页在普通浏览器里也能打开——开发壳、跑 Playwright 都靠它。mock 挂到
 * `window.__mock` 上，测试才能假装宿主往页面里推事件。
 */
export function pickTransport(): BridgeTransport {
  const forced = new URLSearchParams(window.location.search).get('bridge');
  if (forced === 'host') {
    return new HostTransport();
  }
  if (forced === 'mock' || !window.invokeCSharpAction) {
    const mock = new MockTransport();
    window.__mock = mock;
    return mock;
  }
  return new HostTransport();
}

declare global {
  interface Window {
    /** 测试与调试的观察口；mock 模式下才有值。 */
    __mock?: { push(event: string, payload: unknown): void };
    /** 测试与调试的观察口：应用启动后指向桥的客户端。 */
    __bridge?: import('./client.js').BridgeClient;
  }
}
