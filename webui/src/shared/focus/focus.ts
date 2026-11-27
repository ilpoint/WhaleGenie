import { bridge } from '../bridge/bridge.js';
import { windowCommand } from '../window/window-kind.js';

/**
 * Reports whether a text field in the page has the keyboard. The trigger host asks the active
 * window before a global hotkey is allowed to turn into the system or macro switch; a page that
 * keeps typing into a search box must be able to say "this key belongs to me".
 *
 * Controls live inside Lit shadow roots, so the listeners read the composed path rather than
 * event.target, which would stop at the custom element.
 */

let reported = false;

function isTextField(target: EventTarget | null): boolean {
  const element = target as HTMLElement | null;
  if (!element || typeof element.tagName !== 'string') {
    return false;
  }

  const tag = element.tagName;
  return tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || element.isContentEditable;
}

function onTextField(event: Event): boolean {
  return event.composedPath().some((target) => isTextField(target));
}

function report(next: boolean): void {
  if (next === reported) {
    return;
  }

  reported = next;
  void bridge.value.call(windowCommand('focus'), { textField: next }).catch(() => {
    // The host may be going away; the next page reports its focus from scratch.
  });
}

export function installFocusReporting(): void {
  document.addEventListener('pointerdown', (event) => report(onTextField(event)), true);
  document.addEventListener('focusin', (event) => report(onTextField(event)), true);
  document.addEventListener('keydown', (event) => {
    if (!reported && onTextField(event)) {
      report(true);
    }
  }, true);
  document.addEventListener('focusout', () => {
    queueMicrotask(() => report(false));
  }, true);
  window.addEventListener('blur', () => report(false));
  document.addEventListener('visibilitychange', () => {
    if (document.hidden) {
      report(false);
    }
  });
}
