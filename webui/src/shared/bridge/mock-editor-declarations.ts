import type { MockEditor } from './mock-editor.js';
import {
  addDeclaredField,
  addDeclaredParameter,
  addDeclaredResult,
  moveDeclared,
  patchDeclaredField,
  patchDeclaredParameter,
  patchDeclaredResult,
  removeDeclared,
  removeDeclaredField,
} from './mock-editor-data.js';

type DeclarationHandler = (host: MockEditor, params: Record<string, unknown>) => unknown;

/** The callable parameter/result declaration edits; family and kind pick a handler. */
export function handleDeclarationCommand(host: MockEditor,
  params: Record<string, unknown>): unknown {
  const key = String(params['family'] ?? '') + '.' + String(params['kind'] ?? '');
  const handler = declarationCommands[key];
  if (!handler) {
    throw new Error('不认识声明命令 ' + key + '。');
  }

  return handler(host, params);
}

const declarationCommands: Record<string, DeclarationHandler> = {
  'parameter.add': (host) => host.change(addDeclaredParameter),
  'parameter.patch': (host, params) => host.change((document) =>
    patchDeclaredParameter(document, params)),
  'parameter.remove': (host, params) => host.change((document) =>
    removeDeclared(document, 'parameters', params)),
  'parameter.move': (host, params) => host.change((document) =>
    moveDeclared(document, 'parameters', params)),
  'result.add': (host) => host.change(addDeclaredResult),
  'result.patch': (host, params) => host.change((document) =>
    patchDeclaredResult(document, params)),
  'result.remove': (host, params) => host.change((document) =>
    removeDeclared(document, 'results', params)),
  'result.move': (host, params) => host.change((document) =>
    moveDeclared(document, 'results', params)),
  'parameter.fieldAdd': (host, params) => host.change((document) =>
    addDeclaredField(document, 'parameters', params)),
  'parameter.fieldPatch': (host, params) => host.change((document) =>
    patchDeclaredField(document, 'parameters', params)),
  'parameter.fieldRemove': (host, params) => host.change((document) =>
    removeDeclaredField(document, 'parameters', params)),
  'result.fieldAdd': (host, params) => host.change((document) =>
    addDeclaredField(document, 'results', params)),
  'result.fieldPatch': (host, params) => host.change((document) =>
    patchDeclaredField(document, 'results', params)),
  'result.fieldRemove': (host, params) => host.change((document) =>
    removeDeclaredField(document, 'results', params)),
};
