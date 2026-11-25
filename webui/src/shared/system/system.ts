import { Store } from '../store.js';
import type { SystemSwitchState } from '../bridge/protocol.js';

/**
 * 系统开关与开关键。侧边栏在任何页面都要显示这两件事，所以它有一份自己的快照，而不是
 * 等设置页打开才去问；开关本体仍是程序里的那一个对象，这里只读它的推送。
 */
class SystemSwitchStore {
  private readonly state = new Store<SystemSwitchState | null>(null);

  subscribe(listener: (value: SystemSwitchState | null) => void): () => void {
    return this.state.subscribe(listener);
  }

  current(): SystemSwitchState | null {
    return this.state.get();
  }

  /** 仅供启动装配与推送接线：灌入一份。 */
  load(value: SystemSwitchState): void {
    this.state.set(value);
  }
}

export const systemSwitch = new SystemSwitchStore();
