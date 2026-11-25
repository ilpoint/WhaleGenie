import { describe, expect, it } from 'vitest';
import { EditorStore } from '../../../src/features/editor/editor-store.js';
import type {
  EditorSnapshot,
  EditorTab,
  ProjectState,
} from '../../../src/shared/bridge/protocol.js';
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

const tab = (dirty: boolean, documentId = 'doc-1', name = 'Macro 1'): EditorTab => ({
  documentId,
  name,
  dirty,
  problem: false,
  isSubMacro: false,
  ownerDocumentId: null,
});

const snapshot = (tabs: EditorTab[] = [tab(false)], documentId = 'doc-1',
  revision = 1): EditorSnapshot => ({
  documentId,
  name: tabs.find((candidate) => candidate.documentId === documentId)?.name ?? 'Macro 1',
  description: '',
  group: '',
  activeDocumentId: documentId,
  tabs,
  revision,
} as unknown as EditorSnapshot);

function store(): { store: EditorStore; bridge: FakeBridge } {
  const bridge = new FakeBridge();
  return { store: new EditorStore({ client: bridge, strings, project }), bridge };
}

function open(store: EditorStore, tabs: EditorTab[], documentId = 'doc-1'): void {
  const state = store.get();
  state.tabs = tabs;
  state.activeDocumentId = documentId;
  state.snapshot = snapshot(tabs, documentId);
  state.draftName = state.snapshot.name;
  state.draftDescription = state.snapshot.description;
  state.draftGroup = state.snapshot.group;
}

describe('editor document close', () => {
  it('opens the save/discard/cancel question for a dirty document without calling the bridge', async () => {
    const { store: editor, bridge } = store();
    open(editor, [tab(true)]);

    await editor.dialog.closeDocument('doc-1');

    expect(editor.dialog.get().open).toBe(true);
    expect(editor.dialog.get().kind).toBe('document');
    expect(editor.dialog.get().documents).toEqual(['Macro 1']);
    expect(bridge.calls).toEqual([]);
  });

  it('closes a clean document by its stable id', async () => {
    const { store: editor, bridge } = store();
    open(editor, [tab(false)]);
    bridge.reply('editor.session', null);

    await editor.dialog.closeDocument('doc-1');

    expect(bridge.calls[0]).toEqual({
      method: 'editor.session',
      params: { kind: 'close', documentId: 'doc-1' },
    });
    expect(editor.dialog.get().open).toBe(false);
    expect(editor.get().tabs).toEqual([]);
  });

  it('turns a refused close into the same question instead of a page error', async () => {
    const { store: editor, bridge } = store();
    open(editor, [tab(false)]);
    bridge.fail('editor.session', { code: 'Editor.Unsaved', message: '还有未保存修改' });

    await editor.dialog.closeDocument('doc-1');

    expect(editor.dialog.get().open).toBe(true);
    expect(editor.dialog.get().documents).toEqual(['Macro 1']);
    expect(editor.get().error).toBe('');
  });

  it('keeps the dialog and the per-document reason when a save is blocked', async () => {
    const { store: editor, bridge } = store();
    const dirty = [tab(true)];
    open(editor, dirty);
    bridge.reply('editor.save', {
      snapshot: snapshot(dirty, 'doc-1', 2),
      results: [{
        documentId: 'doc-1',
        name: 'Macro 1',
        outcome: 'blocked',
        problem: '名字是空的',
      }],
    });
    await editor.dialog.closeDocument('doc-1');

    await editor.dialog.answer('save');

    expect(editor.dialog.get().open).toBe(true);
    expect(editor.dialog.get().details.join(' ')).toContain('名字是空的');
    expect(editor.dialog.get().error).not.toBe('');
    expect(editor.get().tabs.some((candidate) => candidate.dirty)).toBe(true);
    expect(editor.get().error).toBe('');
  });

  it('saves and closes the document when the result is saved', async () => {
    const { store: editor, bridge } = store();
    const dirty = [tab(true)];
    open(editor, dirty);
    bridge.reply('editor.save', {
      snapshot: snapshot([tab(false)], 'doc-1', 2),
      results: [{
        documentId: 'doc-1',
        name: 'Macro 1',
        outcome: 'saved',
        problem: null,
      }],
    });
    bridge.reply('editor.session', null);
    await editor.dialog.closeDocument('doc-1');

    await editor.dialog.answer('save');

    expect(editor.dialog.get().open).toBe(false);
    expect(bridge.calls.map((call) => call.method)).toEqual(['editor.save', 'editor.session']);
  });

  it('discards and closes the document without saving', async () => {
    const { store: editor, bridge } = store();
    open(editor, [tab(true)]);
    bridge.reply('editor.session', null);
    await editor.dialog.closeDocument('doc-1');

    await editor.dialog.answer('discard');

    expect(bridge.calls[0]).toEqual({
      method: 'editor.session',
      params: { kind: 'close', documentId: 'doc-1', discard: true },
    });
    expect(editor.dialog.get().open).toBe(false);
  });
});