import { describe, expect, it } from 'vitest';
import { clampPage, filterIcons, pageCount, pageIcons } from '../../src/shared/icons/icon-picker-model.js';
import { clampMenuPoint } from '../../src/features/overlay/menu-overlay.position.js';
import { matchesCell } from '../../src/features/overlay/menu-overlay.grid.js';
import type { SurfaceMenuCell } from '../../src/shared/bridge/protocol.js';

const cell: SurfaceMenuCell = {
  row: 0, column: 0, entryId: 'one', macroName: 'Copy Text',
  description: 'clipboard', iconKind: null, iconValue: null,
};

describe('icon picker list', () => {
  it('filters by name and ignores case', () => {
    expect(filterIcons(['Play', 'Pause', 'Settings'], 'PL')).toEqual(['Play']);
    expect(filterIcons(['Play', 'Pause', 'Settings'], 'PA')).toEqual(['Pause']);
  });

  it('returns every icon for an empty query', () => {
    expect(filterIcons(['Play', 'Pause'], '  ')).toEqual(['Play', 'Pause']);
  });

  it('keeps paging inside the match list', () => {
    expect(pageCount(0)).toBe(1);
    expect(pageCount(43)).toBe(2);
    expect(clampPage(9, 2)).toBe(1);
    expect(clampPage(-1, 2)).toBe(0);
    expect(pageIcons(Array.from({ length: 90 }, (_, index) => String(index)), 1)).toHaveLength(42);
    expect(pageIcons(Array.from({ length: 90 }, (_, index) => String(index)), 1)[0]).toBe('42');
  });
});

describe('menu context position', () => {
  it('moves a flyout away from the right and bottom edges', () => {
    expect(clampMenuPoint(340, 660, 188, 226, 350, 667)).toEqual({ x: 158, y: 437 });
  });

  it('keeps a top-left flyout where it was', () => {
    expect(clampMenuPoint(20, 30, 188, 226, 350, 667)).toEqual({ x: 20, y: 30 });
  });
});

describe('menu cell search', () => {
  it('matches the macro name and the description', () => {
    expect(matchesCell(cell, 'copy')).toBe(true);
    expect(matchesCell(cell, 'clip')).toBe(true);
    expect(matchesCell(cell, 'nothing')).toBe(false);
    expect(matchesCell(cell, '  ')).toBe(true);
  });
});
