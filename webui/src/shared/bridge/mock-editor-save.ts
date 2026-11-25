import type { MockEditor } from './mock-editor.js';
import type {
  EditorDocumentSaveResult,
  EditorSaveResponse,
  EditorSnapshot,
} from './protocol.js';
import { activeSnapshot, close } from './mock-editor-documents.js';

/** The save half of the editor host: editor.save, editor.saveAll and the two leave helpers. */
export function handleSaveRun(host: MockEditor, method: string,
  params: Record<string, unknown>): unknown {
  switch (method) {
    case 'editor.save':
      return save(host, host.optionalText(params, 'documentId'));
    case 'editor.saveAll':
      return saveAll(host);
    case 'editor.discardAll':
      return discardAll(host);
    case 'editor.unsavedDocuments':
      return { names: unsavedNames(host) };
    default:
      throw new Error('不认识保存命令 ' + method + '。');
  }
}

/** Saves the named document, or the active one; a root saves its dirty children first. */
export function save(host: MockEditor, documentId?: string): EditorSaveResponse {
  const id = documentId !== undefined && documentId.length > 0
    ? documentId
    : host.activeDocumentId;
  if (id === null || !host.documents.has(id)) {
    throw new Error('没有打开的编辑器会话。');
  }

  const results: EditorDocumentSaveResult[] = [];
  if ((host.owners.get(id) ?? null) !== null) {
    if (host.documents.get(id)?.dirty === true) {
      results.push(saveOne(host, id));
    }
  } else {
    for (const child of childrenOf(host, id)) {
      if (host.documents.get(child)?.dirty === true) {
        results.push(saveOne(host, child));
      }
    }

    if (host.documents.get(id)?.dirty === true) {
      results.push(saveOne(host, id));
    }
  }

  return { snapshot: activeSnapshot(host), results };
}

/** Saves every dirty document once: children first, then the macros that own them. */
export function saveAll(host: MockEditor): EditorSaveResponse {
  const results: EditorDocumentSaveResult[] = [];
  for (const [documentId] of host.documents) {
    if ((host.owners.get(documentId) ?? null) !== null
        && host.documents.get(documentId)?.dirty === true) {
      results.push(saveOne(host, documentId));
    }
  }

  for (const [documentId] of host.documents) {
    if ((host.owners.get(documentId) ?? null) === null
        && host.documents.get(documentId)?.dirty === true) {
      results.push(saveOne(host, documentId));
    }
  }

  return { snapshot: activeSnapshot(host), results };
}

/** Closes every dirty document; the project keeps the version the next open will read. */
export function discardAll(host: MockEditor): EditorSnapshot | null {
  for (const [documentId, document] of [...host.documents.entries()]) {
    if (document.dirty && host.documents.has(documentId)) {
      close(host, documentId, true);
    }
  }

  return activeSnapshot(host);
}

export function unsavedNames(host: MockEditor): string[] {
  return [...host.documents.values()]
    .filter((document) => document.dirty)
    .map((document) => document.name);
}

function childrenOf(host: MockEditor, ownerId: string): string[] {
  return [...host.documents.keys()].filter((id) => host.owners.get(id) === ownerId);
}

/** One document's save; a refused one stays dirty and names its reason. */
function saveOne(host: MockEditor, documentId: string): EditorDocumentSaveResult {
  const document = host.documents.get(documentId);
  if (!document) {
    return {
      documentId,
      name: '',
      outcome: 'missing',
      problem: '这份文档已经关闭。',
    };
  }

  if (!document.canSave || document.name.trim().length === 0) {
    return {
      documentId,
      name: document.name,
      outcome: 'blocked',
      problem: document.nameProblem ?? '这个宏还有必须先修正的问题。',
    };
  }

  const ownerId = host.owners.get(documentId) ?? null;
  if (ownerId !== null) {
    const owner = host.documents.get(ownerId);
    if (!owner) {
      return {
        documentId,
        name: document.name,
        outcome: 'conflict',
        problem: '这个子宏所属的宏已经关闭。',
      };
    }

    host.documents.set(ownerId, { ...owner, dirty: true });
    host.documents.set(documentId, { ...document, dirty: false });
    return {
      documentId,
      name: document.name,
      outcome: 'saved',
      problem: null,
    };
  }

  const previousName = host.savedNames.get(documentId) ?? null;
  const saved = {
    ...document,
    name: document.name.trim(),
    dirty: false,
  };
  host.documents.set(documentId, saved);
  host.savedNames.set(documentId, saved.name);
  host.onSaved(saved, previousName);
  return {
    documentId,
    name: saved.name,
    outcome: 'saved',
    problem: null,
  };
}