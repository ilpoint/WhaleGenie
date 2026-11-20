import { Store } from '../store.js';
import type { AppInfo } from '../bridge/protocol.js';

/**
 * 程序自己的画像：版本、界面语言、当前项目。标题栏与首页读这里；项目变化由宿主推送，
 * 阶段 B 的 `project.changed` 到来时同一间屋子就能装下。
 */
class AppInfoStore {
  private readonly state = new Store<AppInfo | null>(null);

  subscribe(listener: (info: AppInfo | null) => void): () => void {
    return this.state.subscribe(listener);
  }

  /** 仅供启动装配：灌入一份。 */
  load(info: AppInfo): void {
    this.state.set(info);
  }
}

export const appInfo = new AppInfoStore();
