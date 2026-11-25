import { describe, expect, it } from 'vitest';
import { applySnapshotToState, createEditorState } from '../../../src/features/editor/state/editor-state.js';
import type { EditorSnapshot } from '../../../src/shared/bridge/protocol.js';

const snapshot = (documentId: string): EditorSnapshot => ({
  documentId,
  name: documentId,
  description: '',
  group: '',
  revision: 2,
  activeDocumentId: documentId,
  tabs: [{
    documentId,
    name: documentId,
    dirty: false,
    problem: false,
    isSubMacro: false,
    ownerDocumentId: null,
  }],
} as unknown as EditorSnapshot);

describe('document-scoped editor state', () => {
  it('clears notices, errors and the saved mark when the last document goes away', () => {
    const state = createEditorState();
    state.notice = '复制选中的步骤';
    state.error = '这份文档还有未保存修改';
    state.saved = true;
    state.tabs = snapshot('a').tabs;
    state.activeDocumentId = 'a';
    state.valueEditor = { note: '' } as never;

    applySnapshotToState(state, null);

    expect(state.notice).toBe('');
    expect(state.error).toBe('');
    expect(state.saved).toBe(false);
    expect(state.valueEditor).toBeNull();
    expect(state.tabs).toEqual([]);
    expect(state.activeDocumentId).toBe('');
  });

  it('does not carry the previous document feedback into another tab', () => {
    const state = createEditorState();
    state.activeDocumentId = 'a';
    state.notice = '已导出';
    state.error = '保存失败';
    state.saved = true;

    applySnapshotToState(state, snapshot('b'));

    expect(state.activeDocumentId).toBe('b');
    expect(state.notice).toBe('');
    expect(state.error).toBe('');
    expect(state.saved).toBe(false);
  });

  it('keeps the feedback when the snapshot is still the same document', () => {
    const state = createEditorState();
    state.activeDocumentId = 'a';
    state.notice = '已导出';
    state.error = '保存失败';
    state.saved = true;

    applySnapshotToState(state, snapshot('a'));

    expect(state.notice).toBe('已导出');
    expect(state.error).toBe('保存失败');
    expect(state.saved).toBe(true);
  });
});