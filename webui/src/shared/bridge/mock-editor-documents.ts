import type { MockEditor } from './mock-editor.js';
import type {
  EditorSnapshot,
  EditorTab,
} from './protocol.js';
import { createDocumentSnapshot } from './mock-editor-fixtures.js';
import { presentDocument, stepFromAction } from './mock-editor-steps.js';
import type { EditorStep } from './protocol.js';
import { findAction } from './mock-editor-actions.js';

/** Opens a project macro, or activates the document that already holds it. */
export function open(host: MockEditor, name?: string | null): EditorSnapshot {
  const trimmed = name?.trim() ?? '';
  if (trimmed.length === 0) {
    const first = [...host.documents.keys()][0];
    return first === undefined ? newDocument(host) : activate(host, first);
  }

  const existing = findRoot(host, trimmed);
  if (existing !== null) {
    return activate(host, existing);
  }

  const macro = host.macros().find((entry) => entry.name.trim().toLocaleLowerCase()
    === trimmed.toLocaleLowerCase());
  const documentId = host.nextDocumentId();
  const document = createDocumentSnapshot(documentId, trimmed, {
    description: macro?.description ?? '',
    group: macro?.group ?? '',
    subMacros: macro ? ['辅助宏'] : [],
  });
  host.documents.set(documentId, document);
  host.owners.set(documentId, null);
  if (macro) {
    host.savedNames.set(documentId, macro.name);
  }

  host.activeDocumentId = documentId;
  return withTabs(host, document);
}

export function newDocument(host: MockEditor): EditorSnapshot {
  const documentId = host.nextDocumentId();
  const document = createDocumentSnapshot(documentId, '未命名宏', {});
  host.documents.set(documentId, document);
  host.owners.set(documentId, null);
  host.activeDocumentId = documentId;
  return withTabs(host, document);
}

export function newSession(host: MockEditor): EditorSnapshot {
  return newDocument(host);
}

export function activate(host: MockEditor, documentId: string): EditorSnapshot {
  const document = host.documents.get(documentId);
  if (!document) {
    throw new Error('编辑器里没有这个文档：' + documentId + '。');
  }

  host.activeDocumentId = documentId;
  return withTabs(host, document);
}

export function close(host: MockEditor, documentId?: string | null,
  discard = false): EditorSnapshot | null {
  const target = documentId !== undefined && documentId !== null && documentId.length > 0
    ? documentId
    : host.activeDocumentId;
  if (target === null) {
    return null;
  }

  if (!host.documents.has(target)) {
    return host.activeSnapshot();
  }

  const ids = [...host.documents.keys()];
  const subtree = ids.filter((id) => id === target || host.owners.get(id) === target);
  if (!discard && subtree.some((id) => host.documents.get(id)?.dirty === true)) {
    throw Object.assign(new Error('这份文档还有未保存修改，请先保存再关闭。'), {
      code: 'Editor.Unsaved',
    });
  }

  const index = ids.indexOf(target);
  for (const id of subtree) {
    host.documents.delete(id);
    host.owners.delete(id);
    host.savedNames.delete(id);
  }

  if (host.activeDocumentId !== null && subtree.includes(host.activeDocumentId)) {
    const remaining = [...host.documents.keys()];
    const neighbour = remaining[Math.max(0, Math.min(index - 1, remaining.length - 1))];
    host.activeDocumentId = neighbour ?? remaining[0] ?? null;
  }

  return activeSnapshot(host);
}

/** The undo/redo pair shares one command shape; only the direction changes. */
export function undoRedo(host: MockEditor, kind: string): EditorSnapshot | null {
  return host.change((document) => kind === 'undo'
    ? { ...document, canUndo: false, canRedo: true }
    : { ...document, canUndo: true, canRedo: false });
}

export function tabs(host: MockEditor): EditorTab[] {
  return [...host.documents.entries()].map(([documentId, document]) => ({
    documentId,
    name: document.name,
    dirty: document.dirty,
    problem: !document.canSave,
    isSubMacro: document.isSubMacro,
    ownerDocumentId: host.owners.get(documentId) ?? null,
  }));
}

/** One snapshot with the strip that belongs beside it and the action form the host holds. */
export function withTabs(host: MockEditor, document: EditorSnapshot): EditorSnapshot {
  host.snapshotRevision += 1;
  const ownerId = host.owners.get(document.documentId) ?? null;
  const root = ownerId === null ? document : host.documents.get(ownerId) ?? document;
  return {
    ...document,
    documentId: document.documentId,
    rootSubMacros: [...root.subMacros],
    tabs: tabs(host),
    activeDocumentId: host.activeDocumentId,
    actionForm: host.actionForm,
    revision: host.snapshotRevision,
  };
}

export function subOpen(host: MockEditor, name?: string | null,
  ownerDocumentId?: string | null): EditorSnapshot {
  const requested = ownerDocumentId !== undefined && ownerDocumentId !== null
    && ownerDocumentId.length > 0
    ? ownerDocumentId
    : host.activeDocumentId;
  if (requested === null) {
    throw new Error('先打开一个宏，才能打开它的子宏。');
  }

  const ownerId = rootDocumentId(host, requested);
  const owner = host.documents.get(ownerId);
  if (!owner) {
    throw new Error('编辑器里还没有打开的文档。');
  }

  const trimmed = name?.trim() ?? '';
  if (trimmed.length === 0) {
    throw new Error('子宏需要一个名字。');
  }

  if (!owner.subMacros.some((macro) => macro.trim() === trimmed)) {
    throw new Error('这个宏没有名为 ' + trimmed + ' 的子宏。');
  }

  const existing = [...host.documents.entries()].find(([documentId, document]) =>
    host.owners.get(documentId) === ownerId
    && document.name.toLocaleLowerCase() === trimmed.toLocaleLowerCase());
  if (existing) {
    return activate(host, existing[0]);
  }

  const documentId = host.nextDocumentId();
  const document = createDocumentSnapshot(documentId, trimmed, { isSubMacro: true });
  host.documents.set(documentId, document);
  host.owners.set(documentId, ownerId);
  host.activeDocumentId = documentId;
  return withTabs(host, document);
}

export function change(host: MockEditor,
  changeOne: (document: EditorSnapshot) => EditorSnapshot): EditorSnapshot | null {
  const documentId = host.activeDocumentId;
  if (documentId === null) {
    return null;
  }

  const document = host.documents.get(documentId);
  if (!document) {
    return null;
  }

  const next = presentDocument(changeOne(document));
  host.documents.set(documentId, next);
  return withTabs(host, next);
}

export function patch(host: MockEditor, params: Record<string, unknown>): EditorSnapshot | null {
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

export function macroSetting(host: MockEditor, params: Record<string, unknown>): EditorSnapshot | null {
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

export function activeSnapshot(host: MockEditor): EditorSnapshot | null {
  const document = host.activeDocumentId === null
    ? null
    : host.documents.get(host.activeDocumentId) ?? null;
  return document ? withTabs(host, document) : null;
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

/** Brings a project macro in as a child and opens it; the import leaves no call step behind. */
export function subMacroImport(host: MockEditor, reference: string,
  name?: string | null): EditorSnapshot | null {
  const source = reference.trim();
  const activeId = host.activeDocumentId;
  if (activeId === null || source.length === 0) {
    throw new Error('导入子宏需要一个来源名字。');
  }

  const rootId = rootDocumentId(host, activeId);
  const root = host.documents.get(rootId);
  if (!root) {
    throw new Error('编辑器里没有打开的文档。');
  }

  const known = host.macros().some((macro) => macro.name === source);
  if (!known) {
    throw new Error('没有名为“' + source + '”的宏。');
  }

  const wanted = name?.trim() ?? '';
  const childName = uniqueSubMacroName(root, wanted.length > 0 ? wanted : source,
    host.macros().map((macro) => macro.name));
  const childId = host.nextDocumentId();
  const child = createDocumentSnapshot(childId, childName, {
    isSubMacro: true,
    subMacros: [],
    rootSubMacros: [...root.subMacros, childName],
  });
  host.documents.set(rootId, {
    ...root,
    dirty: true,
    subMacros: [...root.subMacros, childName],
  });
  host.documents.set(childId, child);
  host.owners.set(childId, rootId);
  host.activeDocumentId = childId;
  return withTabs(host, child);
}

/** The root a document belongs to, following the owner chain a nested child would have. */
function rootDocumentId(host: MockEditor, documentId: string): string {
  let current = documentId;
  for (;;) {
    const owner = host.owners.get(current) ?? null;
    if (owner === null) {
      return current;
    }

    current = owner;
  }
}

/** A name that does not shadow one the document or the project already has. */
function uniqueSubMacroName(document: EditorSnapshot, wanted: string,
  takenNames: readonly string[] = []): string {
  const names = [...document.subMacros, ...takenNames];
  const base = wanted.trim().length > 0 ? wanted.trim() : '子宏';
  const taken = (candidate: string): boolean => names.some(
    (name) => name.toLocaleLowerCase() === candidate.toLocaleLowerCase());
  if (!taken(base)) {
    return base;
  }

  for (let number = 2; ; number++) {
    const candidate = base + ' ' + String(number);
    if (!taken(candidate)) {
      return candidate;
    }
  }
}

/**
 * The hosted submacro commands. These are the mock's own words: the page never reads them, and the
 * real host sends the words from its table.
 */
export function subMacroCreate(host: MockEditor, name?: string | null): EditorSnapshot | null {
  const parent = host.activeDocumentId === null ? null : host.documents.get(host.activeDocumentId);
  if (!parent) {
    return null;
  }

  const rootId = rootDocumentId(host, parent.documentId);
  const root = host.documents.get(rootId) ?? parent;
  const taken = [
    ...root.subMacros,
    ...host.macros().map((macro) => macro.name),
    ...[...host.documents.entries()]
      .filter(([id]) => host.owners.get(id) === rootId)
      .map(([, document]) => document.name),
  ];
  const childId = host.nextDocumentId();
  const child = createDocumentSnapshot(childId, uniqueSubMacroName(root, name ?? '', taken), {
    isSubMacro: true,
    subMacros: [],
    rootSubMacros: [...root.subMacros],
  });
  host.documents.set(childId, child);
  host.owners.set(childId, rootId);
  host.activeDocumentId = childId;
  return withTabs(host, child);
}

export function subMacroCopy(host: MockEditor, from: string, name?: string | null): EditorSnapshot | null {
  const source = from.trim();
  const document = host.activeSnapshot();
  if (!document || source.length === 0) {
    throw new Error('复制子宏需要一个来源名字。');
  }

  const known = document.subMacros.some((candidate) => candidate === source)
    || host.macros().some((macro) => macro.name === source);
  if (!known) {
    throw new Error('没有名为“' + source + '”的宏。');
  }

  // Like the host: the copy takes the source's name when none is asked for, and a call step
  // reaches it from the end of the root list, because a copy nothing calls would be unreachable.
  const wanted = name?.trim() ?? '';
  const copy = uniqueSubMacroName(document, wanted.length > 0 ? wanted : source,
    host.macros().map((macro) => macro.name));
  const call = callStepFor(host, copy);
  return host.change((current) => ({
    ...current,
    dirty: true,
    subMacros: [...current.subMacros, copy],
    steps: call ? [...current.steps, call] : current.steps,
  }));
}

/** A runMacro step naming the child, the way the host leaves a call after a copy. */
function callStepFor(host: MockEditor, name: string): EditorStep | null {
  const action = findAction('control.runMacro');
  if (!action) {
    return null;
  }

  const step = stepFromAction(action, host.nextStepId());
  const parameter = step.parameters.find((candidate) => candidate.name === 'macro');
  if (parameter) {
    parameter.value = name;
  }

  return step;
}

export function subMacroDelete(host: MockEditor, name: string): EditorSnapshot | null {
  const wanted = name.trim();
  const activeId = host.activeDocumentId;
  if (activeId === null || wanted.length === 0) {
    throw new Error('删除子宏需要一个名字。');
  }

  const rootId = rootDocumentId(host, activeId);
  const document = host.documents.get(rootId);
  if (!document) {
    throw new Error('编辑器里没有打开的文档。');
  }

  if (!document.subMacros.includes(wanted)) {
    throw new Error('没有名为“' + wanted + '”的子宏。');
  }

  if (callsMacro(document.steps, wanted)) {
    throw new Error('子宏“' + wanted + '”仍在被调用：请先移除“' + document.name
      + '”中对它的调用，再删除它。');
  }

  const next = {
    ...document,
    dirty: true,
    subMacros: document.subMacros.filter((candidate) => candidate !== wanted),
  };
  host.documents.set(rootId, next);
  return host.activeSnapshot();
}

/** True when any step (at any depth) calls the named macro. */
function callsMacro(steps: readonly EditorStep[], name: string): boolean {
  for (const step of steps) {
    const macro = step.parameters.find((parameter) => parameter.name === 'macro');
    if (step.type === 'control.runMacro' && macro?.value === name) {
      return true;
    }

    for (const section of step.sections) {
      if (callsMacro(section.steps, name)) {
        return true;
      }
    }
  }

  return false;
}

/** The open root document whose name is the given one. */
function findRoot(host: MockEditor, name: string): string | null {
  const wanted = name.toLocaleLowerCase();
  for (const [documentId, document] of host.documents) {
    if ((host.owners.get(documentId) ?? null) === null
        && document.name.toLocaleLowerCase() === wanted) {
      return documentId;
    }
  }

  return null;
}