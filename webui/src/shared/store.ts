/**
 * 最小的一块状态盒：值、订阅、通知。页面里的共享状态都放它，组件订阅后在回调里刷新自己；
 * 不引入更大的状态库，等哪一块真的复杂起来再说。
 */
export class Store<T> {
  private readonly listeners = new Set<(value: T) => void>();

  constructor(private value: T) {}

  get(): T {
    return this.value;
  }

  set(value: T): void {
    this.value = value;
    for (const listener of this.listeners) {
      listener(value);
    }
  }

  subscribe(listener: (value: T) => void): () => void {
    this.listeners.add(listener);
    listener(this.value);
    return () => this.listeners.delete(listener);
  }
}
