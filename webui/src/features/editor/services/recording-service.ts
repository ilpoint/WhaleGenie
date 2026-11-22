import type { EditorCore } from './editor-core.js';
import type { EditorSnapshot } from '../../../shared/bridge/protocol.js';
import { t } from '../../../shared/strings/strings.js';

/** The recording command family. All state lives in EditorCore; C# remains the source of truth. */
export class EditorRecordingService {
  private recordTimer = 0;

  constructor(private readonly core: EditorCore) {}

  async startRecording(): Promise<void> {
    try {
      const state = await this.core.client.call<{ recording: boolean; captured: number }>(
        'editor.record', { kind: 'start' });
      this.core.state.recording = state.recording;
      this.core.state.captured = state.captured;
      this.core.state.error = '';
      this.core.notify();
      window.clearInterval(this.recordTimer);
      if (state.recording) {
        this.recordTimer = window.setInterval(() => void this.pollRecording(), 500);
      }
    } catch (error) {
      this.core.fail(error);
    }
  }

  private async pollRecording(): Promise<void> {
    try {
      const state = await this.core.client.call<{
        recording?: boolean;
        captured: number;
        snapshot?: EditorSnapshot;
      }>('editor.record', { kind: 'state' });
      this.core.state.captured = state.captured;
      if (state.snapshot) {
        this.core.applySnapshot(state.snapshot);
        await this.core.refreshTabs();
      }

      if (!state.recording) {
        window.clearInterval(this.recordTimer);
        this.recordTimer = 0;
        this.core.state.recording = false;
      }

      this.core.notify();
    } catch (error) {
      this.core.state.recording = false;
      window.clearInterval(this.recordTimer);
      this.recordTimer = 0;
      this.core.fail(error);
    }
  }

  async stopRecording(): Promise<void> {
    window.clearInterval(this.recordTimer);
    this.recordTimer = 0;
    try {
      const result = await this.core.client.call<{
        captured: number;
        steps: number;
        snapshot: EditorSnapshot;
      }>('editor.record', { kind: 'stop' });
      this.core.state.recording = false;
      this.core.state.captured = result.captured;
      this.core.applySnapshot(result.snapshot);
      if (result.steps === 0) {
        this.core.state.error = t('Editor.RecordEmpty');
      }

      await this.core.refreshTabs();
      this.core.notify();
    } catch (error) {
      this.core.state.recording = false;
      this.core.fail(error);
    }
  }

  async toggleRecording(): Promise<void> {
    if (this.core.state.recording) {
      await this.stopRecording();
    } else {
      await this.startRecording();
    }
  }
  clearTimer(): void {
    window.clearInterval(this.recordTimer);
    this.recordTimer = 0;
  }
}
