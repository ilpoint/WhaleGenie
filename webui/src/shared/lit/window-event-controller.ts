import type { ReactiveController, ReactiveControllerHost } from 'lit';

/**
 * Owns one window event listener for the element's connected lifetime. Components that add a
 * global keydown or pointerdown no longer need a matching removeEventListener in their own
 * disconnectedCallback.
 */
export class WindowEventController<K extends keyof WindowEventMap> implements ReactiveController {
  constructor(
    host: ReactiveControllerHost,
    private readonly type: K,
    private readonly listener: (event: WindowEventMap[K]) => void,
    private readonly options?: AddEventListenerOptions,
  ) {
    host.addController(this);
  }

  hostConnected(): void {
    window.addEventListener(this.type, this.listener as EventListener, this.options);
  }

  hostDisconnected(): void {
    window.removeEventListener(this.type, this.listener as EventListener, this.options);
  }
}
