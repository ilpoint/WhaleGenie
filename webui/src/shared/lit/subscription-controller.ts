import type { ReactiveController, ReactiveControllerHost } from 'lit';

/**
 * A generic connected-lifetime subscription for things that are not stores: bridge events,
 * observers, or any API that hands back its own unsubscribe handle.
 */
export class SubscriptionController implements ReactiveController {
  private unsubscribe: (() => void) | undefined;

  constructor(
    host: ReactiveControllerHost,
    private readonly subscribe: () => () => void,
  ) {
    host.addController(this);
  }

  hostConnected(): void {
    this.unsubscribe = this.subscribe();
  }

  hostDisconnected(): void {
    this.unsubscribe?.();
    this.unsubscribe = undefined;
  }
}
