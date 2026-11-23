import { bridgeApi } from '../../shared/bridge/bridge.js';
import type {
  SurfaceMenuMacro,
  SurfaceMenuSnapshot,
} from '../../shared/bridge/protocol.js';
import type { MacroMenuState } from './menu-overlay.macros.js';
import { clampMenuPoint } from './menu-overlay.position.js';

export interface PickerHost {
  snapshot(): SurfaceMenuSnapshot | null;
  setSnapshot(snapshot: SurfaceMenuSnapshot): void;
  setError(message: string): void;
  searchOpen(): boolean;
  focusKeyboard(active: boolean): void;
  focusMacroInput(): void;
  refresh(): void;
}

/** The macro chooser and icon picker layered over the menu; the page only routes events here. */
export class MenuPickerState {
  macroMenu: MacroMenuState | null = null;
  macros: SurfaceMenuMacro[] = [];
  macroQuery = '';
  macroLoading = false;
  iconTarget: string | null = null;

  constructor(private readonly host: PickerHost) {
  }

  async openMacro(pageId: string, row: number, column: number, x: number, y: number,
    withIcon: boolean): Promise<void> {
    const point = clampMenuPoint(x, y, 318, 390, window.innerWidth, window.innerHeight);
    this.macroMenu = { pageId, row, column, ...point, withIcon };
    this.macroQuery = '';
    this.macroLoading = this.macros.length === 0;
    this.host.focusKeyboard(true);
    this.host.refresh();
    if (this.macros.length === 0) {
      try {
        this.macros = await bridgeApi.call<SurfaceMenuMacro[]>('surface.menu.macros');
      } catch (failure) {
        this.host.setError(failure instanceof Error ? failure.message : String(failure));
      } finally {
        this.macroLoading = false;
      }
    }

    this.host.refresh();
    setTimeout(() => this.host.focusMacroInput(), 0);
  }

  closeMacro(): void {
    if (!this.macroMenu) {
      return;
    }

    this.macroMenu = null;
    this.macroQuery = '';
    this.endKeyboard();
  }

  async chooseMacro(macro: SurfaceMenuMacro): Promise<void> {
    const state = this.macroMenu;
    if (!state) {
      return;
    }

    this.macroMenu = null;
    try {
      const snapshot = await bridgeApi.call<SurfaceMenuSnapshot>('surface.menu.cell.assign',
        { pageId: state.pageId, row: state.row, column: state.column, name: macro.name });
      this.host.setSnapshot(snapshot);
      if (state.withIcon) {
        const cell = snapshot.common.concat(snapshot.context)
          .flatMap((page) => page.cells)
          .find((candidate) => candidate.row === state.row && candidate.column === state.column);
        if (cell) {
          this.openIcon(cell.entryId);
        }
      }
    } catch (failure) {
      this.host.setError(failure instanceof Error ? failure.message : String(failure));
    }
  }

  openIcon(entryId: string): void {
    this.macroMenu = null;
    this.iconTarget = entryId;
    this.host.focusKeyboard(true);
    this.host.refresh();
  }

  closeIcon(): void {
    this.iconTarget = null;
    this.endKeyboard();
  }

  async pickIcon(detail: { kind: string; value: string }): Promise<void> {
    const entryId = this.iconTarget;
    this.iconTarget = null;
    this.host.refresh();
    if (!entryId) {
      return;
    }

    try {
      const snapshot = await bridgeApi.call<SurfaceMenuSnapshot>('surface.menu.cell.icon',
        { entryId, kind: detail.kind, value: detail.value });
      this.host.setSnapshot(snapshot);
    } catch (failure) {
      this.host.setError(failure instanceof Error ? failure.message : String(failure));
    } finally {
      this.endKeyboard();
    }
  }

  async pickImage(): Promise<string | null> {
    const answer = await bridgeApi.call<{ path: string | null }>('surface.menu.image');
    return answer.path;
  }

  reset(): void {
    this.macroMenu = null;
    this.macroQuery = '';
    this.macroLoading = false;
    this.iconTarget = null;
  }

  private endKeyboard(): void {
    this.host.refresh();
    if (!this.host.searchOpen() && !this.macroMenu && !this.iconTarget) {
      this.host.focusKeyboard(false);
    }
  }
}