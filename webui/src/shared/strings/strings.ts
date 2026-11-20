import { Store } from '../store.js';
import type { StringsBundle } from '../bridge/protocol.js';

/**
 * 词表：页面里所有用户可见文字都从这儿取，键就是程序文案表的键。宿主在启动时下发整包，
 * 切换语言时推送新包；`t()` 找不到键时显示键名——那说明词表该补了，比悄悄显示空白好。
 */
class StringsStore {
  private readonly state = new Store<StringsBundle>({ language: 'zh-Hans', entries: {} });

  /** 切给页面语言：跟宿主下发的 language 一致。 */
  get language(): string {
    return this.state.get().language;
  }

  t(key: string): string {
    return this.state.get().entries[key] ?? key;
  }

  /** 订阅词表；订阅时立即收到当前值，组件因此可以"先订阅再画"。 */
  subscribe(listener: (bundle: StringsBundle) => void): () => void {
    return this.state.subscribe(listener);
  }

  /** 仅供启动装配与测试：灌入一整包。 */
  load(bundle: StringsBundle): void {
    this.state.set(bundle);
    document.documentElement.lang = bundle.language;
  }
}

export const strings = new StringsStore();

/** 组件里的取词入口，读起来像语言自带的字。 */
export function t(key: string): string {
  return strings.t(key);
}
