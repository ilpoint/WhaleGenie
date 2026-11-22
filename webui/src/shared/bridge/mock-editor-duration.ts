import type { EditorDuration } from './protocol.js';

export function duration(milliseconds = 0): EditorDuration {
  return { milliseconds, written: milliseconds > 0 ? String(milliseconds) : '', unit: '', units: [] };
}
