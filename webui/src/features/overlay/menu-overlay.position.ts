/** Keeps a flyout fully inside the overlay window, growing up/left when it would leave. */
export const ContextWidth = 188;
export const CellMenuHeight = 226;
export const EmptyMenuHeight = 78;
export const PageMenuHeight = 38;

export function clampMenuPoint(x: number, y: number, width: number, height: number,
  viewportWidth: number, viewportHeight: number): { x: number; y: number } {
  return {
    x: Math.max(4, Math.min(x, Math.max(4, viewportWidth - width - 4))),
    y: Math.max(4, Math.min(y, Math.max(4, viewportHeight - height - 4))),
  };
}
