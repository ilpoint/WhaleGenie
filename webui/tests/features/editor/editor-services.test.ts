import { describe, expect, it } from 'vitest';
import type {
  EditorActionForm,
  EditorSnapshot,
  EditorStep,
  EditorValueSnapshot,
  ProjectState,
} from '../../../src/shared/bridge/protocol.js';
import { EditorStore } from '../../../src/features/editor/editor-store.js';
import { FakeBridge } from '../../fakes/bridge.js';

const step = (id: string): EditorStep => ({ id, sections: [] } as unknown as EditorStep);

const snapshot = (name = 'demo', steps: EditorStep[] = []): EditorSnapshot =>
  ({
    name,
    description: '',
    group: '',
    steps,
    isSubMacro: false,
  } as unknown as EditorSnapshot);

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

function storeWith(bridge: FakeBridge): EditorStore {
  return new EditorStore({ client: bridge, strings, project });
}

describe('editor services', () => {
  it('addStep sends the type and selects the step C# added', async () => {
    const bridge = new FakeBridge();
    bridge.reply('editor.sessionStep', snapshot('demo', [step('s1')]));
    const store = storeWith(bridge);
    store.get().snapshot = snapshot();

    await expect(store.steps.addStep('control.delay')).resolves.toBe(true);

    expect(bridge.calls[0]).toEqual({
      method: 'editor.sessionStep',
      params: { type: 'control.delay' },
    });
    expect(store.get().selectedStepId).toBe('s1');
    expect(store.get().selectedStepIds).toEqual(['s1']);
  });

  it('addStep refuses without an open document', async () => {
    const bridge = new FakeBridge();
    const store = storeWith(bridge);

    await expect(store.steps.addStep('control.delay')).resolves.toBe(false);
    expect(bridge.calls).toEqual([]);
  });

  it('a failed save leaves the error text and returns false', async () => {
    const bridge = new FakeBridge();
    bridge.fail('editor.session', new Error('disk full'));
    const store = storeWith(bridge);
    const open = snapshot('demo');
    store.get().snapshot = open;
    store.get().draftName = open.name;

    await expect(store.session.save()).resolves.toBe(false);
    expect(store.get().error).toContain('disk full');
  });

  it('openAction applies the form and its page', async () => {
    const bridge = new FakeBridge();
    bridge.reply('editor.actionOpen', {
      open: true,
      page: 'base',
      canConfirm: true,
      isEditing: false,
      stepId: '',
    } as unknown as EditorActionForm);
    const store = storeWith(bridge);

    await expect(store.action.openAction('control.delay')).resolves.toBe(true);
    expect(store.get().actionForm?.open).toBe(true);
    expect(store.get().actionPage).toBe('base');
  });

  it('openAction records a bridge failure without throwing', async () => {
    const bridge = new FakeBridge();
    bridge.fail('editor.actionOpen', new Error('bad field'));
    const store = storeWith(bridge);

    await expect(store.action.openAction('control.delay')).resolves.toBe(false);
    expect(store.get().actionError).toContain('bad field');
    expect(store.get().error).toContain('bad field');
  });

  it('openValue applies the value snapshot', async () => {
    const bridge = new FakeBridge();
    bridge.reply('editor.value', { title: 'Name', parts: [] } as unknown as EditorValueSnapshot);
    const store = storeWith(bridge);

    await store.value.openValue('s1', 'name');
    expect(bridge.calls[0]).toEqual({
      method: 'editor.value',
      params: { kind: 'open', step: 's1', parameter: 'name' },
    });
    expect(store.get().valueEditor).not.toBeNull();
    expect(store.get().valueError).toBe('');
  });

  it('selectStep is pure page state and notifies subscribers', () => {
    const bridge = new FakeBridge();
    const store = storeWith(bridge);
    let updates = 0;
    const stop = store.subscribe(() => updates++);

    store.ui.selectStep('a', false, ['a', 'b']);
    expect(store.get().selectedStepId).toBe('a');
    expect(store.ui.selectedSteps()).toEqual(['a', 'b']);
    expect(store.ui.hasStepSelection()).toBe(true);
    expect(updates).toBeGreaterThanOrEqual(2);

    stop();
  });
});
