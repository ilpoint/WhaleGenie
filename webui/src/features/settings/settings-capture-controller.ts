import type { ReactiveController, ReactiveControllerHost } from 'lit';
import { keyboardChord, pointerBinding } from '../../shared/input/capture.js';
import type { CaptureKind } from './settings-model.js';

export interface SettingsCaptureHost {
  applyCapture(kind: CaptureKind, binding: string): Promise<void>;
  focusCapture(kind: CaptureKind): Promise<void>;
}

/**
 * The settings page's capture mode: which field is waiting for a chord or pointer, and the
 * key/pointer translation. The bridge write stays in the page's applyCapture.
 */
export class SettingsCaptureController implements ReactiveController {
  active: CaptureKind = '';

  constructor(private readonly host: ReactiveControllerHost & SettingsCaptureHost) {
    host.addController(this);
  }

  hostConnected(): void {
    // Capture starts only when a field calls begin(); the page binds its own handlers.
  }

  async begin(kind: CaptureKind): Promise<void> {
    this.active = kind;
    this.host.requestUpdate();
    await this.host.updateComplete;
    await this.host.focusCapture(kind);
  }

  key(kind: CaptureKind, event: KeyboardEvent): void {
    if (this.active !== kind) {
      return;
    }
    if (event.key === 'Escape') {
      event.preventDefault();
      this.cancel();
      return;
    }
    const binding = keyboardChord(event);
    if (!binding) {
      return;
    }
    event.preventDefault();
    event.stopPropagation();
    this.cancel();
    void this.host.applyCapture(kind, binding);
  }

  pointer(kind: CaptureKind, event: PointerEvent): void {
    if (this.active !== kind) {
      return;
    }
    const binding = pointerBinding(event);
    if (!binding) {
      return;
    }
    event.preventDefault();
    event.stopPropagation();
    this.cancel();
    void this.host.applyCapture(kind, binding);
  }

  cancel(): void {
    this.active = '';
    this.host.requestUpdate();
  }
}
