import type { BridgeEventName, BridgeEvents, BridgeMessage, BridgeFailurePayload } from './protocol.js';

/**
 * 桥的客户端：请求编号、应答配对与事件分发都在这里。页面里的每一处业务只写
 * `client.call('app.info')` 和 `client.on('theme.changed', …)`，不知道信封长什么样。
 */
export class BridgeClient {
  private nextId = 1;

  private readonly waiting = new Map<number, {
    resolve: (value: unknown) => void;
    reject: (error: BridgeFailurePayload) => void;
  }>();

  private readonly listeners = new Map<string, Set<(payload: unknown) => void>>();

  constructor(private readonly transport: {
    onMessage(handler: (message: unknown) => void): void;
    send(envelope: unknown): void;
  }) {
    transport.onMessage((message) => this.receive(message));
  }

  call<T = void>(method: string, params?: unknown): Promise<T> {
    const id = this.nextId++;
    return new Promise<T>((resolve, reject) => {
      this.waiting.set(id, { resolve: resolve as (value: unknown) => void, reject });
      this.transport.send({ id, method, params });
    });
  }

  on<K extends BridgeEventName>(event: K, handler: (payload: BridgeEvents[K]) => void): () => void {
    const set = this.listeners.get(event) ?? new Set();
    set.add(handler as (payload: unknown) => void);
    this.listeners.set(event, set);
    return () => set.delete(handler as (payload: unknown) => void);
  }

  private receive(message: unknown): void {
    const envelope = message as BridgeMessage;
    if ('ok' in envelope && typeof envelope.id === 'number') {
      const pending = this.waiting.get(envelope.id);
      if (!pending) {
        return;
      }
      this.waiting.delete(envelope.id);
      if (envelope.ok) {
        pending.resolve(envelope.result);
      } else {
        pending.reject(envelope.error);
      }
      return;
    }

    if ('event' in envelope && typeof envelope.event === 'string') {
      for (const handler of this.listeners.get(envelope.event) ?? []) {
        try {
          handler(envelope.payload);
        } catch (error) {
          // 一个监听者的错不该打断别人的同一份推送；控制台留痕即可。
          console.error(`bridge listener for ${envelope.event} threw`, error);
        }
      }
    }
  }
}
