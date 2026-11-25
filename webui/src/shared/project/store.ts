import { Store } from '../store.js';
import type { ProjectState } from '../bridge/protocol.js';

/**
 * 项目文档的镜像：宏列表、分组、系统开关。真相在程序里——页面画什么、命令什么，都以这里
 * 的值为准，宿主的 `project.changed` 推送随时会把它整个换掉。过滤与搜索是页面的本职，
 * 在组件里做，不进这间屋子。
 */
class ProjectStore {
  private readonly state = new Store<ProjectState | null>(null);

  subscribe(listener: (value: ProjectState | null) => void): () => void {
    return this.state.subscribe(listener);
  }

  /** 当前镜像；离开页面前判断自动保存开没开、项目脏没脏时读它。 */
  current(): ProjectState | null {
    return this.state.get();
  }

  /** 仅供启动装配与事件接线：灌入一份。 */
  load(value: ProjectState): void {
    this.state.set(value);
  }
}

export const project = new ProjectStore();
