import { describe, expect, it } from 'vitest';
import { BridgeClient } from '../../../src/shared/bridge/client.js';
import { MockTransport } from '../../../src/shared/bridge/mock.js';
import type { ProjectState } from '../../../src/shared/bridge/protocol.js';
import { EditorStore } from '../../../src/features/editor/editor-store.js';

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

async function loaded(): Promise<{ editor: EditorStore; transport: MockTransport }> {
  const transport = new MockTransport();
  const editor = new EditorStore({
    client: new BridgeClient(transport),
    strings,
    project,
  });
  await editor.ensureLoaded();
  return { editor, transport };
}

describe('a document keeps one identity', () => {
  it('saving and reopening a macro does not add a second tab', async () => {
    const { editor } = await loaded();
    await editor.session.open('自动保存');
    const firstId = editor.get().tabs[0]?.documentId;
    expect(firstId).toBeDefined();

    await editor.session.open('游戏连招');
    expect(editor.get().tabs).toHaveLength(2);

    await editor.session.open('自动保存');
    expect(editor.get().tabs).toHaveLength(2);
    expect(editor.get().activeDocumentId).toBe(firstId);
  });

  it('a rename keeps the tab and the saved name is the one reopened', async () => {
    const { editor, transport } = await loaded();
    await editor.session.open('自动保存');
    const documentId = editor.get().activeDocumentId;

    editor.session.setDraft('name', '我的宏');
    await expect(editor.session.save()).resolves.toBe(true);

    expect(editor.get().activeDocumentId).toBe(documentId);
    expect(editor.get().tabs).toHaveLength(1);
    expect(editor.get().tabs[0]?.name).toBe('我的宏');
    expect(transport.projectState.macros.some((macro) => macro.name === '我的宏')).toBe(true);
    expect(transport.projectState.macros.some((macro) => macro.name === '自动保存')).toBe(false);

    await editor.session.open('我的宏');
    expect(editor.get().tabs).toHaveLength(1);
    expect(editor.get().activeDocumentId).toBe(documentId);
  });

  it('a new document gets a stable document id after its first save', async () => {
    const { editor, transport } = await loaded();
    await editor.session.newDocument();
    const documentId = editor.get().activeDocumentId;
    expect(documentId).not.toBe('');
    await expect(editor.session.setMacroSetting('toggleKey', 'F8')).resolves.toBe(true);

    await expect(editor.session.save()).resolves.toBe(true);
    expect(editor.get().activeDocumentId).toBe(documentId);
    expect(transport.projectState.macros.some((macro) => macro.name === '未命名宏')).toBe(true);

    await editor.session.open('未命名宏');
    expect(editor.get().tabs).toHaveLength(1);
    expect(editor.get().activeDocumentId).toBe(documentId);
  });
});