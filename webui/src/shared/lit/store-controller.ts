import type { ReactiveController, ReactiveControllerHost } from 'lit';

/** The subset of a store a controller needs: value subscription with an unsubscribe handle. */
export interface StoreSource<T> {
  subscribe(listener: (value: T) => void): () => void;
}

/**
 * Subscribes a Lit element to a store for exactly its connected lifetime. The callback runs
 * before requestUpdate, so a component that mirrors the value into @state renders once with the
 * new value instead of once with the store's value and once after the assignment.
 */
export class StoreController<T> implements ReactiveController {
  private unsubscribe: (() => void) | undefined;

  constructor(
    private readonly host: ReactiveControllerHost,
    private readonly store: StoreSource<T>,
    private readonly onChange?: (value: T) => void,
  ) {
    host.addController(this);
  }

  hostConnected(): void {
    this.unsubscribe = this.store.subscribe((value) => {
      this.onChange?.(value);
      this.host.requestUpdate();
    });
  }

  hostDisconnected(): void {
    this.unsubscribe?.();
    this.unsubscribe = undefined;
  }
}
