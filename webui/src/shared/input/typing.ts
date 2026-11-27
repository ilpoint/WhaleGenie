/**
 * Whether a keyboard event is going into a text entry control.
 *
 * The page is Lit components all the way down, so an event that reaches a window listener has
 * already been retargeted: `event.target` is the custom element, not the input inside its shadow
 * root. Reading the composed path is the only way a global shortcut can tell "Backspace in a box"
 * from "Backspace on the tree".
 */
export function isTextEntryTarget(target: EventTarget | null): boolean {
  const element = target as HTMLElement | null;
  if (!element || typeof element.tagName !== 'string') {
    return false;
  }

  const tag = element.tagName;
  return tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT'
    || element.isContentEditable === true;
}

export function isTextEntryEvent(event: Event): boolean {
  return event.composedPath().some((target) => isTextEntryTarget(target));
}
