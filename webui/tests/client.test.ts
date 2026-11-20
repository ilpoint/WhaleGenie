import { describe, expect, it } from 'vitest';
import { BridgeClient } from '../src/shared/bridge/client.js';

/** 与真实宿主同形的内存传输：收到什么、回什么，由测试逐条排队。 */
class Pipe {
  readonly sent: unknown[] = [];
  private receive?: (message: unknown) => void;

  onMessage(handler: (message: unknown) => void): void {
    this.receive = handler;
  }

  send(envelope: unknown): void {
    this.sent.push(envelope);
  }

  /** 模拟宿主的回信与推送。 */
  deliver(message: unknown): void {
    this.receive?.(message);
  }
}

describe('BridgeClient', () => {
  it('给请求编号并按应答的 id 配对', async () => {
    const pipe = new Pipe();
    const client = new BridgeClient(pipe);

    const first = client.call('app.info');
    const second = client.call('theme.get');

    expect(pipe.sent).toEqual([
      { id: 1, method: 'app.info' },
      { id: 2, method: 'theme.get' },
    ]);

    pipe.deliver({ id: 2, ok: true, result: { mode: 'dark' } });
    await expect(second).resolves.toEqual({ mode: 'dark' });

    pipe.deliver({ id: 1, ok: true, result: { version: '0.1.0' } });
    await expect(first).resolves.toEqual({ version: '0.1.0' });
  });

  it('失败应答走到拒绝，并带回错误词与消息', async () => {
    const pipe = new Pipe();
    const client = new BridgeClient(pipe);

    const failing = client.call('theme.set', { mode: 'meh' });
    pipe.deliver({ id: 1, ok: false, error: { code: 'BadArgument', message: 'bad mode' } });

    await expect(failing).rejects.toEqual({ code: 'BadArgument', message: 'bad mode' });
  });

  it('事件按名字分发给所有监听者', () => {
    const pipe = new Pipe();
    const client = new BridgeClient(pipe);

    const seen: string[] = [];
    const stop = client.on('window.changed', (payload) => {
      seen.push(payload.state);
    });

    pipe.deliver({ event: 'window.changed', payload: { state: 'maximized', topmost: false } });
    expect(seen).toEqual(['maximized']);

    stop();
    pipe.deliver({ event: 'window.changed', payload: { state: 'normal', topmost: false } });
    expect(seen).toEqual(['maximized']);
  });

  it('一个监听者抛错不影响同一事件的其余监听者', () => {
    const pipe = new Pipe();
    const client = new BridgeClient(pipe);

    const seen: string[] = [];
    client.on('theme.changed', () => {
      throw new Error('listener bug');
    });
    client.on('theme.changed', (payload) => seen.push(payload.mode));

    pipe.deliver({ event: 'theme.changed', payload: { mode: 'light', baseLight: true, accentHex: null } });

    expect(seen).toEqual(['light']);
  });
});
