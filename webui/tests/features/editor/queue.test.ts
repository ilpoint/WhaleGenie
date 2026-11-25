import { describe, expect, it } from 'vitest';
import { EditorCore } from '../../../src/features/editor/services/editor-core.js';
import type { EditorActionForm, EditorSnapshot } from '../../../src/shared/bridge/protocol.js';
import { FakeBridge } from '../../fakes/bridge.js';

const snapshot = (name: string): EditorSnapshot => ({
  documentId: 'doc-1',
  name,
  revision: 1,
  activeDocumentId: 'doc-1',
  tabs: [],
} as unknown as EditorSnapshot);

describe('the editor command queue', () => {
  it('answers and applies in the order the commands were made', async () => {
    const bridge = new FakeBridge();
    const core = new EditorCore(bridge);
    const seen: string[] = [];
    bridge.reply('editor.step', () => {
      seen.push('step');
      return snapshot('step');
    });
    bridge.reply('editor.session', () => {
      seen.push('session');
      return snapshot('session');
    });

    const first = core.mutate<EditorSnapshot>('editor.step', {}, (value) => {
      seen.push('apply-step:' + value.name);
    });
    const second = core.mutate<EditorSnapshot>('editor.session', {}, (value) => {
      seen.push('apply-session:' + value.name);
    });

    await Promise.all([first, second]);

    expect(seen).toEqual(['step', 'apply-step:step', 'session', 'apply-session:session']);
  });

  it('does not let a failed command break the ones behind it', async () => {
    const bridge = new FakeBridge();
    const core = new EditorCore(bridge);
    const seen: string[] = [];
    bridge.reply('editor.step', () => {
      seen.push('first');
      return snapshot('first');
    });
    bridge.fail('editor.steps', new Error('不认识的命令'));
    bridge.reply('editor.session', () => {
      seen.push('third');
      return snapshot('third');
    });

    const first = core.mutate<EditorSnapshot>('editor.step', {}, () => seen.push('apply-first'));
    const second = core.mutate<EditorSnapshot>('editor.steps', {}, () => seen.push('apply-failed'));
    const third = core.mutate<EditorSnapshot>('editor.session', {}, () => seen.push('apply-third'));

    await expect(first).resolves.toBe(true);
    await expect(second).resolves.toBe(false);
    await expect(third).resolves.toBe(true);
    expect(seen).toEqual(['first', 'apply-first', 'third', 'apply-third']);
  });

  it('keeps action edits in the same order as snapshots', async () => {
    const bridge = new FakeBridge();
    const core = new EditorCore(bridge);
    const seen: string[] = [];
    bridge.reply('editor.step', () => {
      seen.push('step');
      return snapshot('step');
    });
    bridge.reply('editor.action', () => {
      seen.push('action');
      return {
        open: true,
        page: 'base',
        canConfirm: true,
      } as unknown as EditorActionForm;
    });

    const step = core.mutate<EditorSnapshot>('editor.step', {}, () => seen.push('apply-step'));
    const action = core.actionEdit({ kind: 'field', name: 'ms', field: 'ms', value: '1' });

    await Promise.all([step, action]);

    expect(seen).toEqual(['step', 'apply-step', 'action']);
    expect(core.get().actionForm?.open).toBe(true);
  });
});