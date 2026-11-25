import type { MockEditor } from './mock-editor.js';
import { actionCatalogue } from './mock-editor-actions.js';
import {
  stopRecording,
  subMacroCopy,
  subMacroCreate,
  subMacroDelete,
  undoRedo,
} from './mock-editor-documents.js';
import { handleDocumentCommand } from './mock-editor-session-documents.js';
import {
  addStep,
  addTrigger,
  conditionCommand,
  removeTrigger,
  rowCommand,
  setParameterCommand,
  setStepMeta,
  stepsCopy,
  stepsCut,
  stepsDuplicate,
  stepsEnabled,
  stepsGroup,
  stepsLift,
  stepsMoveBy,
  stepsMoveTo,
  stepsPaste,
  stepsRemove,
} from './mock-editor-step-commands.js';
import { handleDeclarationCommand } from './mock-editor-declarations.js';
import {
  addGlobal,
  addVariable,
  patchGlobal,
  patchVariable,
  removeGlobal,
  removeVariable,
} from './mock-editor-data.js';
import {
  colorTrigger,
  fileTrigger,
  interfaceTrigger,
  processTrigger,
  timerTrigger,
  windowTrigger,
} from './mock-editor-fixtures.js';
import {
  addConditionNode,
  columnIndex,
  removeConditionNode,
  setConditionCell,
  setConditionJoinAt,
} from './mock-editor-step-ops.js';

/** One editor command family, split out of MockEditor.run. */
export function handleSessionRun(host: MockEditor, method: string, params: Record<string, unknown>): unknown {
  switch (method) {
      case 'editor.session':
        return handleSessionCommand(host, params);
      case 'editor.sessionState':
        return host.activeSnapshot();
      case 'editor.declaration':
        return handleDeclarationCommand(host, params);
      case 'editor.step':
        return handleStepCommand(host, params);
      case 'editor.steps':
        return handleStepsCommand(host, params);

      case 'editor.condition':
        return handleConditionCommand(host, params);
      case 'editor.trigger':
        return handleTriggerCommand(host, params);
      case 'editor.hotstringKeyPad':
        return {
          maximumLength: 40,
          rows: [
            [{ normal: 'a', shifted: 'A', width: 1, kind: 'char' },
              { normal: '1', shifted: '!', width: 1, kind: 'char' },
              { normal: 'Space', shifted: 'Space', width: 4, kind: 'space' }],
            [{ normal: 'Shift', shifted: 'Shift', width: 2, kind: 'shift' },
              { normal: 'Backspace', shifted: 'Backspace', width: 2, kind: 'backspace' }],
          ],
        };
      case 'editor.variable':
        return handleVariableCommand(host, params);
      case 'editor.record':
        return handleRecordCommand(host, params);
      case 'editor.sessionActions':
        return actionCatalogue(host.language());
    default:
      throw new Error('不认识编辑器命令 ' + method + '。');
  }
}

function handleRecordCommand(host: MockEditor, params: Record<string, unknown>): unknown {
  switch (String(params['kind'] ?? '')) {
    case 'start':
      host.recording = true;
      host.captured = 0;
      return { recording: true, captured: 0 };
    case 'state':
      if (host.recording) {
        host.captured += 1;
      }

      return { recording: host.recording, captured: host.captured };
    case 'option':
      return host.change((document) => {
        const field = host.text(params, 'field');
        const value = host.text(params, 'value');
        const recording = { ...document.recording } as Record<string, unknown>;
        if (field === 'ignoreDelaysBelowMs') {
          recording[field] = Number(value);
        } else if (field === 'positionCapture') {
          recording['positionIndex'] = Number(value);
        } else {
          recording[field] = value === 'true';
        }
        return { ...document, recording: recording as unknown as typeof document.recording };
      });
    case 'delayScale':
      return host.change((document) => ({
        ...document,
        recording: {
          ...document.recording,
          delayScale: Number(params['factor'] ?? 1),
          delayScaleDisplay: '×' + String(params['factor'] ?? 1),
          hasDelayScale: Number(params['factor'] ?? 1) !== 1,
        },
      }));
    case 'stop':
      return stopRecording(host);
    default:
      throw new Error('不认识录制命令 ' + String(params['kind']) + '。');
  }
}

function handleStepCommand(host: MockEditor, params: Record<string, unknown>): unknown {
  switch (String(params['kind'] ?? '')) {
    case 'add':
      return addStep(host, params);
    case 'meta':
      return setStepMeta(host, params);
    case 'parameter':
      return setParameterCommand(host, params);
    case 'rowAdd':
      return rowCommand(host, params, (rows) => [...rows, { cells: ['', ''] }]);
    case 'rowRemove':
      return rowCommand(host, params, (rows) => rows.filter((_, index) =>
        index !== Number(params['index'])));
    case 'rowCell':
      return rowCommand(host, params, (rows) => rows.map((row, index) =>
        index !== Number(params['index'])
          ? row
          : {
              cells: row.cells.map((cell, cellIndex) =>
                cellIndex === columnIndex(host.text(params, 'column'))
                  ? host.text(params, 'value')
                  : cell),
            }));
    default:
      throw new Error('不认识步骤命令 ' + String(params['kind']) + '。');
  }
}

function handleStepsCommand(host: MockEditor, params: Record<string, unknown>): unknown {
  switch (String(params['kind'] ?? '')) {
    case 'enabled':
      return stepsEnabled(host, params);
    case 'remove':
      return stepsRemove(host, params);
    case 'duplicate':
      return stepsDuplicate(host, params);
    case 'moveBy':
      return stepsMoveBy(host, params);
    case 'copy':
      return stepsCopy(host, params);
    case 'cut':
      return stepsCut(host, params);
    case 'paste':
      return stepsPaste(host, params);
    case 'group':
      return stepsGroup(host, params);
    case 'lift':
      return stepsLift(host);
    case 'move':
      return stepsMoveTo(host, params);
    default:
      throw new Error('不认识多选命令 ' + String(params['kind']) + '。');
  }
}

function handleSessionCommand(host: MockEditor, params: Record<string, unknown>): unknown {
  const kind = String(params['kind'] ?? '');
  if (kind.startsWith('subMacro')) {
    return handleSubMacroCommand(host, kind, params);
  }

  if (kind === 'undo' || kind === 'redo') {
    return undoRedo(host, kind);
  }

  if (kind === 'export') {
    return true;
  }

  return handleDocumentCommand(host, kind, params);
}

/** The submacro kinds: an added, a copied or a removed one, all answering with the snapshot. */
function handleSubMacroCommand(host: MockEditor, kind: string,
    params: Record<string, unknown>): unknown {
  switch (kind) {
    case 'subMacroCreate':
      return subMacroCreate(host, host.optionalText(params, 'name'));
    case 'subMacroCopy':
      return subMacroCopy(host, host.text(params, 'from'), host.optionalText(params, 'name'));
    case 'subMacroDelete':
      return subMacroDelete(host, host.text(params, 'name'));
    default:
      throw new Error('不认识子宏命令 ' + kind + '。');
  }
}

/** One condition command from the page; the kind picks the edit. */
function handleConditionCommand(host: MockEditor, params: Record<string, unknown>): unknown {
  switch (String(params['kind'] ?? '')) {
    case 'rowAdd':
      return conditionCommand(host, params, (condition, path) =>
        addConditionNode(condition, path, 'row'));
    case 'groupAdd':
      return conditionCommand(host, params, (condition, path) =>
        addConditionNode(condition, path, 'group'));
    case 'rowRemove':
      return conditionCommand(host, params, (condition, path) =>
        removeConditionNode(condition, path));
    case 'row':
      return conditionCommand(host, params, (condition, path) =>
        setConditionCell(condition, path, host.text(params, 'field'), host.text(params, 'value')));
    case 'join':
      return conditionCommand(host, params, (condition, path) =>
        setConditionJoinAt(condition, path, host.text(params, 'join')));
    default:
      throw new Error('不认识条件命令 ' + String(params['kind']) + '。');
  }
}

function handleVariableCommand(host: MockEditor, params: Record<string, unknown>): unknown {
  const global = String(params['family'] ?? '') === 'global';
  switch (String(params['kind'] ?? '')) {
    case 'add':
      return host.change(global ? addGlobal : addVariable);
    case 'patch':
      return host.change((document) => global
        ? patchGlobal(document, params)
        : patchVariable(document, params));
    case 'remove':
      return host.change((document) => global
        ? removeGlobal(document, params)
        : removeVariable(document, params));
    default:
      throw new Error('不认识变量命令 ' + String(params['kind']) + '。');
  }
}

type TriggerHandler = (host: MockEditor, params: Record<string, unknown>) => unknown;

/** One trigger command from the page; the kind picks a handler. */
function handleTriggerCommand(host: MockEditor, params: Record<string, unknown>): unknown {
  const handler = triggerCommands[String(params['kind'] ?? '')];
  if (!handler) {
    throw new Error('不认识触发器命令 ' + String(params['kind']) + '。');
  }

  return handler(host, params);
}

const triggerCommands: Record<string, TriggerHandler> = {
  add: (host, params) => addTrigger(host, { ...params, kind: params['triggerKind'] }),
  remove: (host, params) => removeTrigger(host, params),
  binding: (host, params) => handleTriggerSwitchCommand(host, params),
  loop: (host, params) => handleTriggerSwitchCommand(host, params),
  once: (host, params) => handleTriggerSwitchCommand(host, params),
  enabled: (host, params) => handleTriggerSwitchCommand(host, params),
  field: (host, params) => handleTriggerField(host, params),
  hotstring: (host, params) => host.patchTrigger(params, (trigger) => ({
    ...trigger,
    hotstringText: host.text(params, 'value'),
  })),
  hotstringEndKey: (host, params) => handleHotstringEndCommand(host, params),
  hotstringEndReset: (host, params) => handleHotstringEndCommand(host, params),
  hotstringEndCapture: (host, params) => handleHotstringEndCommand(host, params),
  interfaceHotkey: (host, params) => host.patchTrigger(params, (trigger) => ({
    ...trigger,
    interfaceHotkey: host.text(params, 'binding'),
    interfaceHotkeyProblem: '',
  })),
  interfaceHotkeyClear: (host, params) => host.patchTrigger(params, (trigger) => ({
    ...trigger,
    interfaceHotkey: '',
    interfaceHotkeyProblem: '',
  })),
  idle: (host, params) => host.patchTrigger(params, (trigger) => ({
    ...trigger,
    idleSeconds: host.text(params, 'value'),
  })),
};

function handleTriggerSwitchCommand(host: MockEditor, params: Record<string, unknown>): unknown {
  switch (String(params['kind'] ?? '')) {
    case 'binding':
      return host.patchTrigger(params, (trigger) => ({
        ...trigger,
        binding: host.text(params, 'binding'),
      }));
    case 'loop':
      return host.patchTrigger(params, (trigger) => ({
        ...trigger,
        loop: host.text(params, 'loop'),
      }));
    case 'once':
      return host.patchTrigger(params, (trigger) => ({
        ...trigger,
        once: params['once'] === true,
      }));
    default:
      return host.patchTrigger(params, (trigger) => ({
        ...trigger,
        enabled: params['enabled'] === true,
      }));
  }
}

function handleHotstringEndCommand(host: MockEditor, params: Record<string, unknown>): unknown {
  switch (String(params['kind'] ?? '')) {
    case 'hotstringEndKey':
      return host.patchTrigger(params, (trigger) => ({
        ...trigger,
        hotstringEndKeys: trigger.hotstringEndKeys.map((key) => key.value === host.text(params, 'value')
          ? { ...key, checked: params['checked'] === true }
          : key),
      }));
    case 'hotstringEndReset':
      return host.patchTrigger(params, (trigger) => ({
        ...trigger,
        hotstringEndKeys: trigger.hotstringEndKeys.map((key) => ({ ...key, checked: true })),
      }));
    default: {
      const character = host.text(params, 'character');
      return host.patchTrigger(params, (trigger) => {
        const known = trigger.hotstringEndKeys.some((key) => key.value === character);
        return {
          ...trigger,
          hotstringEndKeys: known
            ? trigger.hotstringEndKeys.map((key) => key.value === character
              ? { ...key, checked: true } : key)
            : [...trigger.hotstringEndKeys, { value: character, label: character,
              checked: true, isCustom: true }],
        };
      });
    }
  }
}

function handleTriggerField(host: MockEditor, params: Record<string, unknown>): unknown {
  switch (String(params['family'] ?? '')) {
    case 'timer':
      return host.patchTrigger(params, (trigger) => timerTrigger(trigger, params));
    case 'file':
      return host.patchTrigger(params, (trigger) => fileTrigger(trigger, params));
    case 'process':
      return host.patchTrigger(params, (trigger) => processTrigger(trigger, params));
    case 'hotstringOption': {
      const field = host.text(params, 'field');
      const value = host.text(params, 'value');
      return host.patchTrigger(params, (trigger) => ({
        ...trigger,
        [field]: field === 'endChars' ? value : value === 'true',
      }));
    }
    case 'color':
      return host.patchTrigger(params, (trigger) => colorTrigger(trigger, params));
    case 'window':
      return host.patchTrigger(params, (trigger) => windowTrigger(trigger, params));
    case 'interface':
      return host.patchTrigger(params, (trigger) => interfaceTrigger(trigger, params));
    default:
      throw new Error('不认识触发器字段族 ' + String(params['family']) + '。');
  }
}