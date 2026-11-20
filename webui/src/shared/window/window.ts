import { Store } from '../store.js';
import type { WindowState } from '../bridge/protocol.js';

/**
 * 窗口的镜像：网页标题栏按它画按钮，而真相在原生一侧——宿主推 `window.changed`，这里跟着
 * 更新。页面从不假设"我刚点了最大化所以现在最大化了"：任务栏、贴边分屏、Win+方向键都
 * 会改变它，镜像只认推送。
 */
class WindowStateStore {
  private readonly state = new Store<WindowState | null>(null);

  subscribe(listener: (value: WindowState | null) => void): () => void {
    return this.state.subscribe(listener);
  }

  /** 仅供事件接线：灌入一份。 */
  load(value: WindowState): void {
    this.state.set(value);
  }
}

export const windowMirror = new WindowStateStore();
