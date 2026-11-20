/**
 * 传输层：页面到宿主的一根管子。宿主进程是唯一的对端——它在网页里注入
 * `invokeCSharpAction`，页面把请求封成 JSON 发过去；宿主通过
 * `window.__whale.onMessage(json)` 把应答与事件推回来。
 */

export interface BridgeTransport {
  /** 注册收信口。宿主的推送与应答都会落到这里。 */
  onMessage(handler: (message: unknown) => void): void;

  /** 发一封信出去。宿主丢了它也没有重发机制，调用方只管把词说清。 */
  send(envelope: unknown): void;
}

declare global {
  interface Window {
    /** 宿主推送的入口；页面在启动的最早期注册它，之后才发第一个请求。 */
    __whale?: { onMessage(json: string): void };
    /** 宿主注入的发信函数；网页壳之外（浏览器 mock 模式）不存在。 */
    invokeCSharpAction?: (body: string) => void;
  }
}

export class HostTransport implements BridgeTransport {
  private receive?: (message: unknown) => void;

  constructor() {
    // 必须先注册收信口再发信：宿主对第一个请求的应答就可能先到。
    window.__whale = { onMessage: (json) => this.receive?.(json) };
  }

  onMessage(handler: (message: unknown) => void): void {
    this.receive = handler;
  }

  send(envelope: unknown): void {
    const talk = window.invokeCSharpAction;
    if (!talk) {
      throw new Error('the host bridge is not present; run inside the WhaleGenie shell or use ?bridge=mock');
    }
    talk(JSON.stringify(envelope));
  }
}
