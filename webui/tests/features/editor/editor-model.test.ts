import { describe, expect, it } from 'vitest';
import type { EditorAction, EditorSnapshot, EditorStep } from '../../../src/shared/bridge/protocol.js';
import {
  categoryOfAction,
  normalizeSelection,
  selectedAction,
  visibleStepIds,
} from '../../../src/features/editor/editor-model.js';

const step = (id: string, sections: { name: string; label: string; steps: EditorStep[] }[] = []): EditorStep =>
  ({ id, sections } as unknown as EditorStep);

const snapshot = (...steps: EditorStep[]): EditorSnapshot =>
  ({ steps } as unknown as EditorSnapshot);

describe('editor-model selection', () => {
  it('a missing snapshot clears the selection', () => {
    expect(normalizeSelection(null, 'a', ['a', 'b'])).toEqual({
      selectedStepId: '',
      selectedStepIds: [],
    });
  });

  it('keeps the surviving ids and moves the primary to the last survivor', () => {
    const tree = snapshot(step('a'), step('b'));
    expect(normalizeSelection(tree, 'gone', ['a', 'gone', 'b'])).toEqual({
      selectedStepId: 'b',
      selectedStepIds: ['a', 'b'],
    });
  });

  it('collapses to the primary id when it is not in the selection', () => {
    const tree = snapshot(step('a'), step('b'));
    expect(normalizeSelection(tree, 'a', ['b'])).toEqual({
      selectedStepId: 'a',
      selectedStepIds: ['a'],
    });
  });

  it('keeps a non-empty selection when the primary id is empty', () => {
    const tree = snapshot(step('a'), step('b'));
    expect(normalizeSelection(tree, '', ['a', 'b'])).toEqual({
      selectedStepId: '',
      selectedStepIds: ['a', 'b'],
    });
  });

  it('a missing primary with no survivors clears to empty', () => {
    const tree = snapshot(step('a'));
    expect(normalizeSelection(tree, 'gone', ['gone'])).toEqual({
      selectedStepId: '',
      selectedStepIds: [],
    });
  });
});

describe('editor-model visibility', () => {
  it('walks nested sections in display order', () => {
    const tree = snapshot(step('a', [{ name: 'then', label: 'Then', steps: [step('b')] }]), step('c'));
    expect(visibleStepIds(tree, new Set())).toEqual(['a', 'b', 'c']);
  });

  it('does not descend into a folded block', () => {
    const tree = snapshot(step('a', [{ name: 'then', label: 'Then', steps: [step('b')] }]), step('c'));
    expect(visibleStepIds(tree, new Set(['a']))).toEqual(['a', 'c']);
  });

  it('a missing snapshot has no visible steps', () => {
    expect(visibleStepIds(null, new Set())).toEqual([]);
  });
});

describe('editor-model catalogue', () => {
  const actions = [
    { key: 'control.delay', category: 'Control' },
    { key: 'window.activate', category: 'Window' },
  ] as unknown as EditorAction[];

  it('reads the category from the catalogue', () => {
    expect(categoryOfAction(actions, 'window.activate')).toBe('Window');
  });

  it('falls back to Control when the catalogue does not know the type', () => {
    expect(categoryOfAction(actions, 'gone.action')).toBe('Control');
  });

  it('finds the selected action or returns null', () => {
    expect(selectedAction(actions, 'control.delay')?.category).toBe('Control');
    expect(selectedAction(actions, 'gone.action')).toBeNull();
  });
});
