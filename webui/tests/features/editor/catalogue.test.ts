import { describe, expect, it } from 'vitest';
import { EditorStore } from '../../../src/features/editor/editor-store.js';
import type {
  EditorCatalogue,
  EditorSnapshot,
  ProjectState,
} from '../../../src/shared/bridge/protocol.js';
import { FakeBridge } from '../../fakes/bridge.js';

const project = {
  subscribe(listener: (state: ProjectState | null) => void): () => void {
    listener(null);
    return () => {};
  },
};

function stringStore() {
  const listeners = new Set<() => void>();
  return {
    subscribe(listener: () => void): () => void {
      listeners.add(listener);
      listener();
      return () => listeners.delete(listener);
    },
    change(): void {
      for (const listener of listeners) {
        listener();
      }
    },
  };
}

const snapshot = (): EditorSnapshot => ({
  documentId: 'doc-1',
  name: 'demo',
  revision: 1,
  activeDocumentId: 'doc-1',
  tabs: [],
} as unknown as EditorSnapshot);

const catalogue = (): EditorCatalogue => ({
  count: 1,
  actions: [{
    key: 'control.delay',
    name: '延迟',
    category: 'Control',
    categoryLabel: '流程控制',
    categoryShort: '流程',
    group: 'timing',
    groupLabel: '等待',
    groupRank: 0,
    description: '',
    parameters: [],
  }],
  categories: [{ value: null, label: '全部', short: '全部' }],
  publicMacros: [],
  valueFunctions: [{ name: 'upper', signature: 'upper(text)', description: '大写', group: 'Text' }],
  variableTypes: [{ value: 'text', label: '文本' }],
  conditionCompares: [{ value: 'Equal', label: '=' }],
  triggerKinds: [{ value: 'Key', label: '按键' }],
  systemValueChoices: [{
    name: 'date',
    type: 'text',
    origin: 'System',
    source: 'sys.date',
    description: '',
    group: 'System',
    display: '日期',
    persistent: false,
  }],
} as unknown as EditorCatalogue);

describe('the shared editor catalogue', () => {
  it('loads once, then again when the language changes', async () => {
    const bridge = new FakeBridge();
    const strings = stringStore();
    let loads = 0;
    bridge.reply('editor.sessionActions', () => {
      loads++;
      return catalogue();
    });
    bridge.reply('editor.sessionState', snapshot);
    const editor = new EditorStore({ client: bridge, strings, project });

    await editor.ensureLoaded();
    await editor.ensureLoaded();

    expect(loads).toBe(1);
    expect(editor.get().catalogue?.variableTypes).toHaveLength(1);
    expect(editor.get().catalogue?.conditionCompares).toHaveLength(1);
    expect(editor.get().catalogue?.triggerKinds).toHaveLength(1);
    expect(editor.get().catalogue?.systemValueChoices).toHaveLength(1);
    expect(editor.get().actions).toHaveLength(1);

    strings.change();
    for (let tick = 0; tick < 20 && loads < 2; tick++) {
      await Promise.resolve();
    }

    expect(loads).toBe(2);
  });
});