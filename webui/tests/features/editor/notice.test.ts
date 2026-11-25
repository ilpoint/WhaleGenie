import { afterEach, describe, expect, it, vi } from 'vitest';
import { EditorCore } from '../../../src/features/editor/services/editor-core.js';
import { FakeBridge } from '../../fakes/bridge.js';

afterEach(() => {
  vi.useRealTimers();
});

describe('editor success notices', () => {
  it('goes away on its own instead of sticking to the page', () => {
    vi.useFakeTimers();
    const core = new EditorCore(new FakeBridge());

    core.showNotice('复制选中的步骤');
    expect(core.get().notice).toBe('复制选中的步骤');

    vi.advanceTimersByTime(2500);
    expect(core.get().notice).toBe('');
  });

  it('a later notice replaces the timer of the earlier one', () => {
    vi.useFakeTimers();
    const core = new EditorCore(new FakeBridge());

    core.showNotice('第一条');
    vi.advanceTimersByTime(1000);
    core.showNotice('第二条');
    vi.advanceTimersByTime(1600);
    expect(core.get().notice).toBe('第二条');
    vi.advanceTimersByTime(900);
    expect(core.get().notice).toBe('');
  });
});
