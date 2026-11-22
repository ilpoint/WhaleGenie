import type { MockEditor } from './mock-editor.js';
import type {
  EditorSnapshot,
  EditorTab,
} from './protocol.js';
import { createDocumentSnapshot } from './mock-editor-fixtures.js';
import { presentDocument, stepFromAction } from './mock-editor-steps.js';
import type { EditorStep } from './protocol.js';
import { findAction } from './mock-editor-actions.js';

const NEW_KEY = '\u0000new';

export function open(host: MockEditor, name?: string | null): EditorSnapshot  {
    const trimmed = name?.trim() ?? '';
    const key = trimmed.length > 0 ? trimmed : NEW_KEY;
    let document = host.documents.get(key);
    if (!document) {
      const macro = trimmed.length > 0
        ? host.macros().find((entry) => entry.name.trim().toLocaleLowerCase()
          === trimmed.toLocaleLowerCase())
        : undefined;
      document = createDocumentSnapshot(trimmed.length > 0 ? trimmed : '未命名宏', {
        description: macro?.description ?? '',
        group: macro?.group ?? '',
        subMacros: trimmed.length > 0 ? ['辅助宏'] : [],
      });
      host.documents.set(key, document);
    }

    host.activeKey = key;
    return document;
  }

export function newDocument(host: MockEditor): EditorSnapshot  {
    const key = host.nextNewKey();
    const document = createDocumentSnapshot('未命名宏', {});
    host.documents.set(key, document);
    host.activeKey = key;
    return document;
  }

export function newSession(host: MockEditor): EditorSnapshot {
    return host.newDocument();
  }

export function activate(host: MockEditor, key: string): EditorSnapshot  {
    const document = host.documents.get(key);
    if (!document) {
      throw new Error('编辑器里没有这个文档：' + key + '。');
    }

    host.activeKey = key;
    return document;
  }

export function close(host: MockEditor, key?: string | null): EditorSnapshot | null  {
    const target = key !== undefined && key !== null && key.length > 0 ? key : host.activeKey;
    if (target === null) {
      return null;
    }

    const document = host.documents.get(target);
    if (!document) {
      return host.activeSnapshot();
    }

    if (document.dirty) {
      throw new Error('这份文档还有未保存修改，请先保存再关闭。');
    }

    const wasActive = host.activeKey === target;
    host.documents.delete(target);
    if (wasActive) {
      const keys = [...host.documents.keys()];
      host.activeKey = keys.length > 0 ? keys[keys.length - 1]! : null;
    }

    return host.activeSnapshot();
  }

function withTabs(host: MockEditor, document: EditorSnapshot): EditorSnapshot {
  return { ...document, tabs: tabs(host).tabs, activeKey: host.activeKey };
}

export function tabs(host: MockEditor): { activeKey: string | null; tabs: EditorTab[] } {
  return {
    activeKey: host.activeKey,
    tabs: [...host.documents.entries()].map(([key, document]) => ({
      key,
      name: document.name,
      dirty: document.dirty,
      problem: !document.canSave,
      isSubMacro: document.isSubMacro,
    })),
  };
}

export function subOpen(host: MockEditor, name?: string | null): EditorSnapshot  {
    const parentKey = host.activeKey;
    if (parentKey === null) {
      throw new Error('先打开一个宏，才能打开它的子宏。');
    }

    const trimmed = name?.trim() ?? '';
    if (trimmed.length === 0) {
      throw new Error('子宏需要一个名字。');
    }

    const parent = host.documents.get(parentKey);
    if (!parent) {
      throw new Error('编辑器里还没有打开的文档。');
    }

    if (!parent.subMacros.some((macro) => macro.trim() === trimmed)) {
      throw new Error('这个宏没有名为 ' + trimmed + ' 的子宏。');
    }

    const key = 'sub:' + parentKey + ':' + trimmed;
    let document = host.documents.get(key);
    if (!document) {
      document = createDocumentSnapshot(trimmed, { isSubMacro: true });
      host.documents.set(key, document);
    }

    host.activeKey = key;
    return withTabs(host, document);
  }

export function change(host: MockEditor, change: (document: EditorSnapshot) => EditorSnapshot): EditorSnapshot | null  {
    const key = host.activeKey;
    if (key === null) {
      return null;
    }

    const document = host.documents.get(key);
    if (!document) {
      return null;
    }

    const next = presentDocument(change(document));
    host.documents.set(key, next);
    return withTabs(host, next);
  }

export function patch(host: MockEditor, params: Record<string, unknown>): EditorSnapshot | null  {
    const name = host.optionalText(params, 'name');
    const description = host.optionalText(params, 'description');
    const group = host.optionalText(params, 'group');
    return host.change((document) => ({
      ...document,
      name: name ?? document.name,
      description: description ?? document.description,
      group: group ?? document.group,
      dirty: true,
    }));
  }

export function macroSetting(host: MockEditor, params: Record<string, unknown>): EditorSnapshot | null  {
    const field = host.text(params, 'field');
    const value = host.text(params, 'value');
    return host.change((document) => {
      switch (field) {
        case 'followInputLimit':
          return { ...document, dirty: true, followInputLimit: value === 'true' };
        case 'inputLimit': {
          const limit = Number(value);
          return {
            ...document,
            dirty: true,
            inputLimit: Number.isFinite(limit) ? limit : document.inputLimit,
          };
        }
        case 'toggleKey':
          return { ...document, dirty: true, toggleKey: value };
        default:
          return { ...document, dirty: true };
      }
    });
  }

export function activeSnapshot(host: MockEditor): EditorSnapshot | null  {
    const document = host.activeKey === null ? null : host.documents.get(host.activeKey) ?? null;
    return document ? withTabs(host, document) : null;
  }

export function nextNewKey(host: MockEditor): string  {
    let key = NEW_KEY;
    for (let index = 2; host.documents.has(key); index++) {
      key = NEW_KEY + ':' + String(index);
    }

    return key;
  }

export function save(host: MockEditor): EditorSnapshot | null  {
    return host.change((document) => {
      if (document.name.trim().length === 0) {
        throw new Error('宏需要一个名字。');
      }

      return {
        ...document,
        name: document.name.trim(),
        dirty: false,
      };
    });
  }

export function stopRecording(host: MockEditor): {
  captured: number;
  steps: number;
  snapshot: EditorSnapshot | null;
} {
  const wasRecording = host.recording;
  const captured = host.captured;
  host.recording = false;
  host.captured = 0;
  let steps = 0;
  const snapshot = wasRecording
    ? host.change((document) => {
        const action = findAction('control.delay');
        const delays: EditorStep[] = action
          ? [stepFromAction(action, host.nextStepId()), stepFromAction(action, host.nextStepId())]
          : [];
        steps = delays.length;
        return { ...document, dirty: true, steps: [...document.steps, ...delays] };
      })
    : host.activeSnapshot();
  return { captured, steps, snapshot };
}
