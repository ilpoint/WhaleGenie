import type { ReactiveController, ReactiveControllerHost } from 'lit';

/** A minimal Lit controller host: enough to test hostConnected/hostDisconnected and updates. */
export class FakeHost implements ReactiveControllerHost {
  readonly controllers: ReactiveController[] = [];
  updates = 0;

  addController(controller: ReactiveController): void {
    this.controllers.push(controller);
  }

  removeController(controller: ReactiveController): void {
    const at = this.controllers.indexOf(controller);
    if (at >= 0) {
      this.controllers.splice(at, 1);
    }
  }

  requestUpdate(): void {
    this.updates++;
  }

  get updateComplete(): Promise<boolean> {
    return Promise.resolve(true);
  }

  connect(): void {
    for (const controller of this.controllers) {
      controller.hostConnected?.();
    }
  }

  disconnect(): void {
    for (const controller of this.controllers) {
      controller.hostDisconnected?.();
    }
  }
}
