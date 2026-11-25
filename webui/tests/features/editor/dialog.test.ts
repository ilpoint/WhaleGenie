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

const tab = (dirty: boolean): EditorTab => ({
  documentId: 'doc-1',
  name: '第一页',
  dirty,
  problem: false,
  isSubMacro: false,
  ownerDocumentId: null,
});

const snapshot = (dirty = true): EditorSnapshot => {
  const tabs = [tab(dirty)];
  return {
    documentId: 'doc-1',
    name: '第一页',
    description: '',
    group: '',
    activeDocumentId: 'doc-1',
    tabs,
    revision: 1,
  } as unknown as EditorSnapshot;
};

function store(answerHost?: (requestId: number, leave: boolean) => void):
  { store: EditorStore; bridge: FakeBridge } {
  const bridge = new FakeBridge();
  bridge.reply('editor.sessionState', snapshot());
  return {
    store: new EditorStore({
      client: bridge,
      strings,
      project,
      ...(answerHost === undefined ? {} : { answerHost }),
    }),
    bridge,
  };
}

function activate(editor: EditorStore, dirty = true): void {
  const state = editor.get();
  state.snapshot = snapshot(dirty);
  state.tabs = state.snapshot.tabs;
  state.activeDocumentId = 'doc-1';
  state.draftName = state.snapshot.name;
  state.draftDescription = state.snapshot.description;
  state.draftGroup = state.snapshot.group;
}

async function waitOpen(editor: EditorStore): Promise<void> {
  for (let tick = 0; tick < 20 && !editor.dialog.get().open; tick++) {
    await Promise.resolve();
  }
}

describe('the one editor dialog', () => {
  it('lets a clean editor go without a dialog', async () => {
    const { store: editor, bridge } = store();
    bridge.reply('editor.unsavedDocuments', { names: [] });

    await expect(editor.dialog.confirmLeave()).resolves.toBe(true);
    expect(editor.dialog.get().open).toBe(false);
  });

  it('uses host document names even when the local strip has not caught up', async () => {
    const { store: editor, bridge } = store();
    activate(editor);

    const answer = editor.dialog.confirmLeave(['Macro 1', 'Macro 2']);
    expect(editor.dialog.get().open).toBe(true);
    expect(editor.dialog.get().kind).toBe('host');
    expect(editor.dialog.get().documents).toEqual(['Macro 1', 'Macro 2']);

    bridge.reply('editor.saveAll', { snapshot: snapshot(false), results: [] });
    await editor.dialog.answer('save');
    await expect(answer).resolves.toBe(true);
  });

  it('asks C# for the unsaved names when the local strip looks clean', async () => {
    const { store: editor, bridge } = store();
    bridge.reply('editor.unsavedDocuments', { names: ['第一页'] });

    const answer = editor.dialog.confirmLeave();
    await waitOpen(editor);

    expect(editor.dialog.get().open).toBe(true);
    expect(editor.dialog.get().kind).toBe('leave');
    expect(editor.dialog.get().documents).toEqual(['第一页']);
    await editor.dialog.answer('cancel');
    await expect(answer).resolves.toBe(false);
  });

  it('keeps one question and one answer for two callers', async () => {
    const { store: editor, bridge } = store();
    bridge.reply('editor.unsavedDocuments', { names: ['第一页'] });

    const first = editor.dialog.confirmLeave();
    const second = editor.dialog.confirmLeave();
    await waitOpen(editor);

    expect(second).toBe(first);
    expect(editor.dialog.get().open).toBe(true);
    await editor.dialog.answer('cancel');
    await expect(first).resolves.toBe(false);
    await expect(second).resolves.toBe(false);
    expect(bridge.calls.filter((call) => call.method === 'editor.unsavedDocuments')).toHaveLength(1);
  });

  it('keeps the dialog and the details when save all is blocked', async () => {
    const { store: editor, bridge } = store();
    activate(editor);
    bridge.reply('editor.unsavedDocuments', { names: ['第一页'] });
    bridge.reply('editor.saveAll', {
      snapshot: snapshot(true),
      results: [{
        documentId: 'doc-1',
        name: '第一页',
        outcome: 'blocked',
        problem: '名字是空的',
      }],
    });

    const answer = editor.dialog.confirmLeave();
    await waitOpen(editor);
    await editor.dialog.answer('save');

    expect(editor.dialog.get().open).toBe(true);
    expect(editor.dialog.get().details.join(' ')).toContain('名字是空的');
    expect(editor.dialog.get().error).not.toBe('');
    editor.dialog.cancel();
    await expect(answer).resolves.toBe(false);
  });

  it('discards every document and leaves', async () => {
    const { store: editor, bridge } = store();
    activate(editor);
    bridge.reply('editor.unsavedDocuments', { names: ['第一页'] });
    bridge.reply('editor.discardAll', null);

    const answer = editor.dialog.confirmLeave();
    await waitOpen(editor);
    await editor.dialog.answer('discard');

    await expect(answer).resolves.toBe(true);
    expect(editor.dialog.get().open).toBe(false);
  });

  it('answers a host leave question once with the id the host gave', async () => {
    const answered: [number, boolean][] = [];
    const { store: editor, bridge } = store((requestId, leave) => {
      answered.push([requestId, leave]);
    });
    activate(editor);
    bridge.reply('editor.saveAll', { snapshot: snapshot(false), results: [] });

    editor.answerLeaveAsked(7, ['第一页']);
    expect(editor.dialog.get().open).toBe(true);
    await editor.dialog.answer('save');
    for (let tick = 0; tick < 20 && answered.length === 0; tick++) {
      await Promise.resolve();
    }

    expect(answered).toEqual([[7, true]]);
  });

  it('saves host documents even before the editor page has a snapshot', async () => {
    const { store: editor, bridge } = store();
    bridge.reply('editor.saveAll', {
      snapshot: null,
      results: [{
        documentId: 'doc-1',
        name: '第一页',
        outcome: 'saved',
        problem: null,
      }],
    });

    const answer = editor.dialog.confirmLeave(['第一页']);
    await editor.dialog.answer('save');

    await expect(answer).resolves.toBe(true);
  });
});