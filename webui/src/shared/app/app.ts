import { Store } from '../store.js';
import type { AppInfo } from '../bridge/protocol.js';

/**
 * 程序自己的画像：版本与界面语言。标题栏、首页与关于页读这里；当前项目不在这份快照里，
 * 它由 `project.state` 与 `project.changed` 单独承载，避免同一事实写两份。
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
