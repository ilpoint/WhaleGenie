import { bridgeApi } from '../../shared/bridge/bridge.js';
import type { SurfaceMenuSnapshot } from '../../shared/bridge/protocol.js';

/** Moves the cell a drag carries to the slot it was dropped on. */
export function dropCell(event: DragEvent, dragging: string | null, pageId: string, row: number,
  column: number, apply: (snapshot: SurfaceMenuSnapshot) => void): void {
  event.preventDefault();
  const entryId = event.dataTransfer?.getData('text/plain') || dragging;
  if (!entryId) {
    return;
  }

  void bridgeApi.call<SurfaceMenuSnapshot>('surface.menu.move',
    { entryId, pageId, row, column }).then(apply);
}
