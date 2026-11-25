import { describe, expect, it } from 'vitest';
import type { EditorSnapshot, ProjectState } from '../../../src/shared/bridge/protocol.js';
import { EditorStore } from '../../../src/features/editor/editor-store.js';
import { FakeBridge } from '../../fakes/bridge.js';

const strings = {
  subscribe(listener: () => void): () => void {
    listener();
    return () => {};
  },
};

const project = {
  subscribe(listener: (state: ProjectState | null) => void): () => void {
    listener(null);
    return () => {};
  },
};

function snapshot(subMacros: string[] = [], revision = 1): EditorSnapshot {
  return {
    name: 'demo',
    description: '',
    group: '',
    subMacros,
    steps: [],
    revision,
  } as unknown as EditorSnapshot;
}

function storeWith(bridge: FakeBridge): EditorStore {
  return new EditorStore({ client: bridge, strings, project });
}

describe('editor submacro commands', () => {
  it('新建不带名字时只发 subMacroCreate，宿主自己起唯一名', async () => {
    const bridge = new FakeBridge();
    bridge.reply('editor.session', snapshot(['子宏'], 2));
    const store = storeWith(bridge);
    store.get().snapshot = snapshot([], 1);

    await expect(store.steps.createSubMacro()).resolves.toBe(true);

    expect(bridge.calls[0]).toEqual({
      method: 'editor.session',
      params: { kind: 'subMacroCreate' },
    });
    expect(store.get().snapshot?.subMacros).toEqual(['子宏']);
  });

  it('新建可带名字，名字两边的空白不发出去', async () => {
    const bridge = new FakeBridge();
    bridge.reply('editor.session', snapshot(['辅助'], 2));
    const store = storeWith(bridge);
    store.get().snapshot = snapshot([], 1);

    await expect(store.steps.createSubMacro('  辅助  ')).resolves.toBe(true);

    expect(bridge.calls[0]).toEqual({
      method: 'editor.session',
      params: { kind: 'subMacroCreate', name: '辅助' },
    });
  });

  it('复制只发来源名；给了新名字才带 name', async () => {
    const bridge = new FakeBridge();
    bridge.reply('editor.session', snapshot(['辅助', '辅助的副本'], 2));
    const store = storeWith(bridge);
    store.get().snapshot = snapshot(['辅助'], 1);

    await expect(store.steps.copySubMacro(' 辅助 ')).resolves.toBe(true);
    expect(bridge.calls[0]).toEqual({
      method: 'editor.session',
      params: { kind: 'subMacroCopy', from: '辅助' },
    });

    bridge.reply('editor.session', snapshot(['辅助', '第二份'], 3));
    await expect(store.steps.copySubMacro('辅助', ' 第二份 ')).resolves.toBe(true);
    expect(bridge.calls[1]).toEqual({
      method: 'editor.session',
      params: { kind: 'subMacroCopy', from: '辅助', name: '第二份' },
    });
  });

  it('复制缺来源名时不动桥', async () => {
    const bridge = new FakeBridge();
    const store = storeWith(bridge);
    store.get().snapshot = snapshot(['辅助'], 1);

    await expect(store.steps.copySubMacro('   ')).resolves.toBe(false);
    expect(bridge.calls).toEqual([]);
  });

  it('删除发 subMacroDelete 和名字', async () => {
    const bridge = new FakeBridge();
    bridge.reply('editor.session', snapshot([], 2));
    const store = storeWith(bridge);
    store.get().snapshot = snapshot(['辅助'], 1);

    await expect(store.steps.deleteSubMacro(' 辅助 ')).resolves.toBe(true);

    expect(bridge.calls[0]).toEqual({
      method: 'editor.session',
      params: { kind: 'subMacroDelete', name: '辅助' },
    });
    expect(store.get().snapshot?.subMacros).toEqual([]);
  });

  it('桥拒绝删除被引用的子宏时，把桥自己的句子留在页面错误条', async () => {
    const bridge = new FakeBridge();
    bridge.fail('editor.session', new Error('子宏“辅助”仍在被调用：请先移除调用。'));
    const store = storeWith(bridge);
    store.get().snapshot = snapshot(['辅助'], 1);

    await expect(store.steps.deleteSubMacro('辅助')).resolves.toBe(false);

    expect(store.get().error).toContain('仍在被调用');
    expect(store.get().notice).toBe('');
    expect(store.get().snapshot?.subMacros).toEqual(['辅助']);
  });

  it('移进分节发 parentId、list 与 index', async () => {
    const bridge = new FakeBridge();
    bridge.reply('editor.steps', snapshot([], 2));
    const store = storeWith(bridge);
    store.get().snapshot = snapshot([], 1);

    await expect(store.steps.moveStepsInto('repeat-1', 0, 'body', ['s1'])).resolves.toBe(true);
    expect(bridge.calls[0]).toEqual({
      method: 'editor.steps',
      params: { kind: 'move', steps: ['s1'], parentId: 'repeat-1', index: 0, list: 'body' },
    });
  });

  it('没有列表名时不发 list，用追加语义', async () => {
    const bridge = new FakeBridge();
    bridge.reply('editor.steps', snapshot([], 2));
    const store = storeWith(bridge);
    store.get().snapshot = snapshot([], 1);

    await expect(store.steps.moveStepsInto('repeat-1', 2, undefined, ['s1'])).resolves.toBe(true);
    expect(bridge.calls[0]).toEqual({
      method: 'editor.steps',
      params: { kind: 'move', steps: ['s1'], parentId: 'repeat-1', index: 2 },
    });
  });
});
