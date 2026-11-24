import type {
  EditorMacroField,
  EditorMacroParameter,
  EditorMacroResult,
  EditorSnapshot,
} from './protocol.js';
import raw from './data/editor-data.json';

/**
 * The mock editor's declaration, global and local variable edits. They are whole-snapshot
 * transformations so the command dispatch stays a thin table of names. The blank records a new
 * row starts from live in data/editor-data.json.
 */

interface EditorDataTemplates {
  parameter: EditorMacroParameter;
  result: EditorMacroResult;
  field: EditorMacroField;
  global: EditorSnapshot['globals'][number];
  variable: EditorSnapshot['variables'][number];
}

const DATA = raw as unknown as EditorDataTemplates;
type DeclaredKind = 'parameters' | 'results';

function text(params: Record<string, unknown>, name: string): string {
  const value = params[name];
  return value === undefined || value === null ? '' : String(value);
}

function patchAt<T>(items: T[], index: number, patch: (item: T) => T): T[] {
  return items.map((item, at) => at === index ? patch(item) : item);
}

function addFieldAt<T extends { fields: EditorMacroField[] }>(items: T[], index: number): T[] {
  return items.map((item, at) => at === index
    ? { ...item, fields: [...item.fields, structuredClone(DATA.field)] }
    : item);
}

function patchFieldAt<T extends { fields: EditorMacroField[] }>(
  items: T[],
  index: number,
  fieldIndex: number,
  patch: (field: EditorMacroField) => EditorMacroField,
): T[] {
  return items.map((item, at) => at === index
    ? { ...item, fields: item.fields.map((field, part) => part === fieldIndex ? patch(field) : field) }
    : item);
}

function removeFieldAt<T extends { fields: EditorMacroField[] }>(
  items: T[],
  index: number,
  fieldIndex: number,
): T[] {
  return items.map((item, at) => at === index
    ? { ...item, fields: item.fields.filter((_, part) => part !== fieldIndex) }
    : item);
}

export function addDeclaredParameter(document: EditorSnapshot): EditorSnapshot {
  return {
    ...document,
    dirty: true,
    callableParameters: [...document.callableParameters, structuredClone(DATA.parameter)],
  };
}

export function patchDeclaredParameter(
  document: EditorSnapshot,
  params: Record<string, unknown>,
): EditorSnapshot {
  const index = Number(params['index']);
  const field = text(params, 'field');
  const value = text(params, 'value');
  return {
    ...document,
    dirty: true,
    callableParameters: patchAt(document.callableParameters, index, (parameter) => ({
      ...parameter,
      name: field === 'name' ? value : parameter.name,
      type: field === 'type' ? value : parameter.type,
      required: field === 'required' ? value === 'true' : parameter.required,
      default: field === 'default' ? value : parameter.default,
      description: field === 'description' ? value : parameter.description,
    })),
  };
}

export function addDeclaredResult(document: EditorSnapshot): EditorSnapshot {
  return {
    ...document,
    dirty: true,
    callableResults: [...document.callableResults, structuredClone(DATA.result)],
  };
}

export function patchDeclaredResult(
  document: EditorSnapshot,
  params: Record<string, unknown>,
): EditorSnapshot {
  const index = Number(params['index']);
  const field = text(params, 'field');
  const value = text(params, 'value');
  return {
    ...document,
    dirty: true,
    callableResults: patchAt(document.callableResults, index, (resultItem) => ({
      ...resultItem,
      name: field === 'name' ? value : resultItem.name,
      type: field === 'type' ? value : resultItem.type,
      description: field === 'description' ? value : resultItem.description,
    })),
  };
}

export function removeDeclared(
  document: EditorSnapshot,
  kind: DeclaredKind,
  params: Record<string, unknown>,
): EditorSnapshot {
  const index = Number(params['index']);
  return kind === 'parameters'
    ? { ...document, dirty: true, callableParameters: document.callableParameters.filter((_, at) => at !== index) }
    : { ...document, dirty: true, callableResults: document.callableResults.filter((_, at) => at !== index) };
}

export function moveDeclared(
  document: EditorSnapshot,
  kind: DeclaredKind,
  params: Record<string, unknown>,
): EditorSnapshot {
  const items = kind === 'parameters' ? document.callableParameters : document.callableResults;
  const index = Number(params['index']);
  const target = index + Number(params['offset']);
  if (target < 0 || target >= items.length) {
    return document;
  }

  const moved = [...items];
  [moved[index]!, moved[target]!] = [moved[target]!, moved[index]!];
  return kind === 'parameters'
    ? { ...document, dirty: true, callableParameters: moved as EditorMacroParameter[] }
    : { ...document, dirty: true, callableResults: moved as EditorMacroResult[] };
}

export function addDeclaredField(
  document: EditorSnapshot,
  kind: DeclaredKind,
  params: Record<string, unknown>,
): EditorSnapshot {
  const index = Number(params['index']);
  return kind === 'parameters'
    ? { ...document, dirty: true, callableParameters: addFieldAt(document.callableParameters, index) }
    : { ...document, dirty: true, callableResults: addFieldAt(document.callableResults, index) };
}

export function patchDeclaredField(
  document: EditorSnapshot,
  kind: DeclaredKind,
  params: Record<string, unknown>,
): EditorSnapshot {
  const index = Number(params['index']);
  const fieldIndex = Number(params['fieldIndex']);
  const field = text(params, 'field');
  const value = text(params, 'value');
  const patch = (entry: EditorMacroField): EditorMacroField => ({
    ...entry,
    name: field === 'name' ? value : entry.name,
    type: field === 'type' ? value : entry.type,
  });
  return kind === 'parameters'
    ? { ...document, dirty: true, callableParameters: patchFieldAt(document.callableParameters, index, fieldIndex, patch) }
    : { ...document, dirty: true, callableResults: patchFieldAt(document.callableResults, index, fieldIndex, patch) };
}

export function removeDeclaredField(
  document: EditorSnapshot,
  kind: DeclaredKind,
  params: Record<string, unknown>,
): EditorSnapshot {
  const index = Number(params['index']);
  const fieldIndex = Number(params['fieldIndex']);
  return kind === 'parameters'
    ? { ...document, dirty: true, callableParameters: removeFieldAt(document.callableParameters, index, fieldIndex) }
    : { ...document, dirty: true, callableResults: removeFieldAt(document.callableResults, index, fieldIndex) };
}

export function addGlobal(document: EditorSnapshot): EditorSnapshot {
  return {
    ...document,
    dirty: true,
    globals: [...document.globals, structuredClone(DATA.global)],
  };
}

export function patchGlobal(
  document: EditorSnapshot,
  params: Record<string, unknown>,
): EditorSnapshot {
  const originalName = text(params, 'originalName');
  const field = text(params, 'field');
  const value = text(params, 'value');
  return {
    ...document,
    dirty: true,
    globals: document.globals.map((global) => global.name === originalName
      ? {
          ...global,
          name: field === 'name' ? value : global.name,
          type: field === 'type' ? value : global.type,
          default: field === 'default' ? value : global.default,
          description: field === 'description' ? value : global.description,
          persisted: field === 'persist' ? value === 'true' : global.persisted,
        }
      : global),
  };
}

export function removeGlobal(
  document: EditorSnapshot,
  params: Record<string, unknown>,
): EditorSnapshot {
  return {
    ...document,
    dirty: true,
    globals: document.globals.filter((global) => global.name !== text(params, 'name')),
  };
}

export function addVariable(document: EditorSnapshot): EditorSnapshot {
  return {
    ...document,
    dirty: true,
    variables: [...document.variables, structuredClone(DATA.variable)],
  };
}

export function patchVariable(
  document: EditorSnapshot,
  params: Record<string, unknown>,
): EditorSnapshot {
  const originalName = text(params, 'originalName');
  const field = text(params, 'field');
  const value = text(params, 'value');
  return {
    ...document,
    dirty: true,
    variables: document.variables.map((variable) => variable.name === originalName
      ? {
          ...variable,
          name: field === 'name' ? value : variable.name,
          type: field === 'type' ? value : variable.type,
          initial: field === 'initial' ? value : variable.initial,
          description: field === 'description' ? value : variable.description,
        }
      : variable),
  };
}

export function removeVariable(
  document: EditorSnapshot,
  params: Record<string, unknown>,
): EditorSnapshot {
  return {
    ...document,
    dirty: true,
    variables: document.variables.filter((variable) => variable.name !== text(params, 'name')),
  };
}
