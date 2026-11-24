import { describe, expect, it } from 'vitest';
import {
  resolveListEndDrop,
  resolveRowDrop,
  resolveSectionDrop,
} from '../../../src/features/editor/step-tree-drop.js';

const box = (id: string, top: number, height = 30) => ({ id, top, height });

describe('step tree drop placement', () => {
  it('空分节落成该分节子列表的第一个位置', () => {
    expect(resolveSectionDrop('repeat-1', 'body')).toEqual({
      area: 'section',
      parentId: 'repeat-1',
      list: 'body',
      index: 0,
    });
  });

  it('空分节缺步骤或列表名时不认落点', () => {
    expect(resolveSectionDrop('', 'body')).toBeNull();
    expect(resolveSectionDrop(undefined, 'body')).toBeNull();
    expect(resolveSectionDrop('repeat-1', '')).toBeNull();
    expect(resolveSectionDrop('repeat-1', null)).toBeNull();
  });

  it('分节内最后一行的下半沿仍是该行的接缝', () => {
    const lastChild = box('child-2', 100);
    expect(resolveRowDrop(lastChild, 125)).toEqual({
      area: 'row',
      stepId: 'child-2',
      before: false,
    });
    expect(resolveRowDrop(lastChild, 110)).toEqual({
      area: 'row',
      stepId: 'child-2',
      before: true,
    });
  });

  it('根列表末位挂在最后一行之后', () => {
    expect(resolveListEndDrop([box('root-1', 0), box('root-2', 30)])).toEqual({
      area: 'row',
      stepId: 'root-2',
      before: false,
    });
    expect(resolveListEndDrop([])).toBeNull();
  });
});
