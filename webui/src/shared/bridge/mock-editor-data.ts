import type { EditorSnapshot } from './protocol.js';

/**
 * The mock editor's declaration, global and local variable edits. They are whole-snapshot
 * transformations so the command dispatch stays a thin table of names.
 */

function text(params: Record<string, unknown>, name: string): string {
  const value = params[name];
  return value === undefined || value === null ? '' : String(value);
}

export function addDeclaredParameter(document: EditorSnapshot): EditorSnapshot {
  return {
    ...document,
    dirty: true,
    callableParameters: [...document.callableParameters, {
      name: 'parameter',
      type: 'text',
      required: false,
      default: '',
      description: '',
      fields: [],
    }],
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
    callableParameters: document.callableParameters.map((parameter, at) => at !== index
      ? parameter
      : {
          ...parameter,
          name: field === 'name' ? value : parameter.name,
          type: field === 'type' ? value : parameter.type,
          required: field === 'required' ? value === 'true' : parameter.required,
          default: field === 'default' ? value : parameter.default,
          description: field === 'description' ? value : parameter.description,
        }),
  };
}

export function addDeclaredResult(document: EditorSnapshot): EditorSnapshot {
  return {
    ...document,
    dirty: true,
    callableResults: [...document.callableResults, {
      name: 'result',
      type: 'text',
      description: '',
      fields: [],
    }],
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
    callableResults: document.callableResults.map((resultItem, at) => at !== index
      ? resultItem
      : {
          ...resultItem,
          name: field === 'name' ? value : resultItem.name,
          type: field === 'type' ? value : resultItem.type,
          description: field === 'description' ? value : resultItem.description,
        }),
  };
}

export function removeDeclared(
  document: EditorSnapshot,
  kind: 'parameters' | 'results',
  params: Record<string, unknown>,
): EditorSnapshot {
  const index = Number(params['index']);
  return kind === 'parameters'
    ? {
        ...document,
        dirty: true,
        callableParameters: document.callableParameters.filter((_, at) => at !== index),
      }
    : {
        ...document,
        dirty: true,
        callableResults: document.callableResults.filter((_, at) => at !== index),
      };
}

export function moveDeclared(
  document: EditorSnapshot,
  kind: 'parameters' | 'results',
  params: Record<string, unknown>,
): EditorSnapshot {
  const index = Number(params['index']);
  const target = index + Number(params['offset']);
  if (kind === 'parameters') {
    if (target < 0 || target >= document.callableParameters.length) {
      return document;
    }

    const parameters = [...document.callableParameters];
    [parameters[index]!, parameters[target]!] = [parameters[target]!, parameters[index]!];
    return { ...document, dirty: true, callableParameters: parameters };
  }

  if (target < 0 || target >= document.callableResults.length) {
    return document;
  }

  const results = [...document.callableResults];
  [results[index]!, results[target]!] = [results[target]!, results[index]!];
  return { ...document, dirty: true, callableResults: results };
}

export function addDeclaredField(
  document: EditorSnapshot,
  kind: 'parameters' | 'results',
  params: Record<string, unknown>,
): EditorSnapshot {
  const index = Number(params['index']);
  return kind === 'parameters'
    ? {
        ...document,
        dirty: true,
        callableParameters: document.callableParameters.map((parameter, at) => at !== index
          ? parameter
          : { ...parameter, fields: [...parameter.fields, { name: '', type: 'text' }] }),
      }
    : {
        ...document,
        dirty: true,
        callableResults: document.callableResults.map((resultItem, at) => at !== index
          ? resultItem
          : { ...resultItem, fields: [...resultItem.fields, { name: '', type: 'text' }] }),
      };
}

export function patchDeclaredField(
  document: EditorSnapshot,
  kind: 'parameters' | 'results',
  params: Record<string, unknown>,
): EditorSnapshot {
  const index = Number(params['index']);
  const fieldIndex = Number(params['fieldIndex']);
  const field = text(params, 'field');
  const value = text(params, 'value');
  const patchField = (fields: { name: string; type: string }[]): { name: string; type: string }[] =>
    fields.map((entry, at) => at !== fieldIndex
      ? entry
      : {
          ...entry,
          name: field === 'name' ? value : entry.name,
          type: field === 'type' ? value : entry.type,
        });
  return kind === 'parameters'
    ? {
        ...document,
        dirty: true,
        callableParameters: document.callableParameters.map((parameter, at) => at !== index
          ? parameter
          : { ...parameter, fields: patchField(parameter.fields) }),
      }
    : {
        ...document,
        dirty: true,
        callableResults: document.callableResults.map((resultItem, at) => at !== index
          ? resultItem
          : { ...resultItem, fields: patchField(resultItem.fields) }),
      };
}

export function removeDeclaredField(
  document: EditorSnapshot,
  kind: 'parameters' | 'results',
  params: Record<string, unknown>,
): EditorSnapshot {
  const index = Number(params['index']);
  const fieldIndex = Number(params['fieldIndex']);
  const keep = (fields: { name: string; type: string }[]): { name: string; type: string }[] =>
    fields.filter((_, at) => at !== fieldIndex);
  return kind === 'parameters'
    ? {
        ...document,
        dirty: true,
        callableParameters: document.callableParameters.map((parameter, at) => at !== index
          ? parameter
          : { ...parameter, fields: keep(parameter.fields) }),
      }
    : {
        ...document,
        dirty: true,
        callableResults: document.callableResults.map((resultItem, at) => at !== index
          ? resultItem
          : { ...resultItem, fields: keep(resultItem.fields) }),
      };
}

export function addGlobal(document: EditorSnapshot): EditorSnapshot {
  return {
    ...document,
    dirty: true,
    globals: [...document.globals, {
      name: 'variable',
      type: 'text',
      default: '',
      description: '',
      persisted: false,
    }],
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
    variables: [...document.variables, {
      name: '',
      type: 'text',
      initial: '',
      description: '',
      source: '声明',
      isDeclared: true,
    }],
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
