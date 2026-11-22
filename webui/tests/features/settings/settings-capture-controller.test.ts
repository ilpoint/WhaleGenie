import { describe, expect, it } from 'vitest';
import { SettingsCaptureController } from '../../../src/features/settings/settings-capture-controller.js';
import type { CaptureKind } from '../../../src/features/settings/settings-model.js';
import { FakeHost } from '../../fakes/host.js';

class CaptureHost extends FakeHost {
  applied: { kind: CaptureKind; binding: string }[] = [];
  focused: CaptureKind[] = [];

  async applyCapture(kind: CaptureKind, binding: string): Promise<void> {
    this.applied.push({ kind, binding });
  }

  async focusCapture(kind: CaptureKind): Promise<void> {
    this.focused.push(kind);
  }
}

describe('SettingsCaptureController', () => {
  it('begin focuses the field and key translates a chord', async () => {
    const host = new CaptureHost();
    const capture = new SettingsCaptureController(host);

    await capture.begin('menu');
    expect(capture.active).toBe('menu');
    expect(host.focused).toEqual(['menu']);

    capture.key('menu', new KeyboardEvent('keydown', { key: 'F8', ctrlKey: true }));
    expect(capture.active).toBe('');
    expect(host.applied).toEqual([{ kind: 'menu', binding: 'Ctrl+F8' }]);
  });

  it('Escape cancels without writing a binding', async () => {
    const host = new CaptureHost();
    const capture = new SettingsCaptureController(host);
    await capture.begin('systemHotkey');

    capture.key('systemHotkey', new KeyboardEvent('keydown', { key: 'Escape' }));
    expect(capture.active).toBe('');
    expect(host.applied).toEqual([]);
  });

  it('only the active kind can capture', async () => {
    const host = new CaptureHost();
    const capture = new SettingsCaptureController(host);
    await capture.begin('menu');

    capture.key('wheelRepeat', new KeyboardEvent('keydown', { key: 'F8' }));
    expect(capture.active).toBe('menu');
    expect(host.applied).toEqual([]);
  });

  it('pointer translates middle and side buttons', async () => {
    const host = new CaptureHost();
    const capture = new SettingsCaptureController(host);
    await capture.begin('menu');

    capture.pointer('menu', new PointerEvent('pointerdown', { button: 1 }));
    expect(host.applied).toEqual([{ kind: 'menu', binding: 'Mouse Middle' }]);
  });
});
