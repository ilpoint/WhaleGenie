import { describe, expect, it } from 'vitest';
import { filterMacros, moveMacroNames } from '../../../src/features/macros/macros-model.js';
import type { MacroEntry } from '../../../src/shared/bridge/protocol.js';

function macro(name: string, overrides: Partial<MacroEntry> = {}): MacroEntry {
  return {
    name,
    group: '',
    description: '',
    enabled: false,
    running: false,
    triggerSummary: '',
    triggerActions: '',
    triggerPreview: '',
    views: ['All'],
    ...overrides,
  };
}

const state: MacroEntry[] = [
  macro('停用一', { group: '办公' }),
  macro('启用一', { enabled: true, group: '办公' }),
  macro('停用二'),
  macro('启用二', { enabled: true }),
];

describe('filterMacros', () => {
  it('keeps the project order when enabled-first is off', () => {
    expect(filterMacros(state, {
      view: 'All', group: '', search: '', enabledFirst: false,
    }).map((entry) => entry.name)).toEqual(['停用一', '启用一', '停用二', '启用二']);
  });

  it('puts enabled macros first without reordering inside either group', () => {
    expect(filterMacros(state, {
      view: 'All', group: '', search: '', enabledFirst: true,
    }).map((entry) => entry.name)).toEqual(['启用一', '启用二', '停用一', '停用二']);
  });

  it('narrows by view before by group and search', () => {
    const keys = [macro('按键', { views: ['All', 'Keys'] }), macro('菜单', { views: ['All', 'Menu'] })];
    expect(filterMacros(keys, {
      view: 'Keys', group: '', search: '', enabledFirst: false,
    }).map((entry) => entry.name)).toEqual(['按键']);
  });

  it('treats null as the ungrouped macros and the empty string as every group', () => {
    expect(filterMacros(state, {
      view: 'All', group: null, search: '', enabledFirst: false,
    }).map((entry) => entry.name)).toEqual(['停用二', '启用二']);
    expect(filterMacros(state, {
      view: 'All', group: '办公', search: '', enabledFirst: false,
    }).map((entry) => entry.name)).toEqual(['停用一', '启用一']);
  });

  it('moves one macro before or after another without losing the rest', () => {
    const names = state.map((entry) => entry.name);
    expect(moveMacroNames(state, '停用二', '启用一', false))
      .toEqual(['停用一', '停用二', '启用一', '启用二']);
    expect(moveMacroNames(state, '停用一', '停用二', true))
      .toEqual(['启用一', '停用二', '停用一', '启用二']);
    expect(moveMacroNames(state, '停用一', '停用一', false)).toEqual(names);
  });

  it('searches names case-insensitively and trims the needle', () => {
    const named = [macro('Save All'), macro('Open')];
    expect(filterMacros(named, {
      view: 'All', group: '', search: '  save  ', enabledFirst: false,
    }).map((entry) => entry.name)).toEqual(['Save All']);
    expect(filterMacros(named, {
      view: 'All', group: '', search: 'missing', enabledFirst: false,
    })).toEqual([]);
  });
});
