import type { MockEditor } from './mock-editor.js';
import { actionCatalogue } from './mock-editor-actions.js';
import {
  addDeclaredField,
  addDeclaredParameter,
  addDeclaredResult,
  addGlobal,
  addVariable,
  moveDeclared,
  patchDeclaredField,
  patchDeclaredParameter,
  patchDeclaredResult,
  patchGlobal,
  patchVariable,
  removeDeclared,
  removeDeclaredField,
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
      case 'editor.sessionOpen':
        return host.open(host.text(params, 'name'));
      case 'editor.sessionTabs':
        return host.tabs();
      case 'editor.sessionNew':
        return host.newDocument();
      case 'editor.sessionSubOpen':
        return host.subOpen(host.text(params, 'name'));
      case 'editor.sessionActivate':
        return host.activate(host.text(params, 'key'));
      case 'editor.sessionState':
        return host.activeSnapshot();
      case 'editor.sessionClose':
        return host.close(host.optionalText(params, 'key'));
      case 'editor.sessionPatch':
        return host.patch(params);
      case 'editor.sessionMacroSetting':
        return host.macroSetting(params);
      case 'editor.sessionParameterDeclAdd':
        return host.change(addDeclaredParameter);
      case 'editor.sessionParameterDeclPatch':
        return host.change((document) => patchDeclaredParameter(document, params));
      case 'editor.sessionParameterDeclRemove':
        return host.change((document) => removeDeclared(document, 'parameters', params));
      case 'editor.sessionParameterDeclMove':
        return host.change((document) => moveDeclared(document, 'parameters', params));
      case 'editor.sessionResultDeclAdd':
        return host.change(addDeclaredResult);
      case 'editor.sessionResultDeclPatch':
        return host.change((document) => patchDeclaredResult(document, params));
      case 'editor.sessionResultDeclRemove':
        return host.change((document) => removeDeclared(document, 'results', params));
      case 'editor.sessionResultDeclMove':
        return host.change((document) => moveDeclared(document, 'results', params));
      case 'editor.sessionParameterFieldAdd':
        return host.change((document) => addDeclaredField(document, 'parameters', params));
      case 'editor.sessionParameterFieldPatch':
        return host.change((document) => patchDeclaredField(document, 'parameters', params));
      case 'editor.sessionParameterFieldRemove':
        return host.change((document) => removeDeclaredField(document, 'parameters', params));
      case 'editor.sessionResultFieldAdd':
        return host.change((document) => addDeclaredField(document, 'results', params));
      case 'editor.sessionResultFieldPatch':
        return host.change((document) => patchDeclaredField(document, 'results', params));
      case 'editor.sessionResultFieldRemove':
        return host.change((document) => removeDeclaredField(document, 'results', params));
      case 'editor.sessionStep':
        return host.addStep(params);
      case 'editor.sessionStepEnabled':
        return host.setStepEnabledCommand(params);
      case 'editor.sessionStepMeta':
        return host.setStepMeta(params);
      case 'editor.sessionStepDuplicate':
        return host.duplicateStep(params);
      case 'editor.sessionStepsEnabled':
        return host.stepsEnabled(params);
      case 'editor.sessionStepsRemove':
        return host.stepsRemove(params);
      case 'editor.sessionStepsDuplicate':
        return host.stepsDuplicate(params);
      case 'editor.sessionStepsMoveBy':
        return host.stepsMoveBy(params);
      case 'editor.sessionStepsCopy':
        return host.stepsCopy(params);
      case 'editor.sessionStepsCut':
        return host.stepsCut(params);
      case 'editor.sessionStepsPaste':
        return host.stepsPaste(params);
      case 'editor.sessionStepsGroup':
        return host.stepsGroup(params);
      case 'editor.sessionStepsLift':
        return host.stepsLift();
      case 'editor.sessionStepsMove':
        return host.stepsMoveTo(params);

      case 'editor.sessionConditionRowAdd':
        return host.conditionCommand(params, (condition, path) =>
          addConditionNode(condition, path, 'row'));
      case 'editor.sessionConditionGroupAdd':
        return host.conditionCommand(params, (condition, path) =>
          addConditionNode(condition, path, 'group'));
      case 'editor.sessionConditionRowRemove':
        return host.conditionCommand(params, (condition, path) =>
          removeConditionNode(condition, path));
      case 'editor.sessionConditionRow':
        return host.conditionCommand(params, (condition, path) =>
          setConditionCell(condition, path, host.text(params, 'field'), host.text(params, 'value')));
      case 'editor.sessionConditionJoin':
        return host.conditionCommand(params, (condition, path) =>
          setConditionJoinAt(condition, path, host.text(params, 'join')));
      case 'editor.sessionParameter':
        return host.setParameterCommand(params);
      case 'editor.sessionParameterRowAdd':
        return host.rowCommand(params, (rows) => [...rows, { cells: ['', ''] }]);
      case 'editor.sessionParameterRowRemove':
        return host.rowCommand(params, (rows) => rows.filter((_, index) =>
          index !== Number(params['index'])));
      case 'editor.sessionParameterRowCell':
        return host.rowCommand(params, (rows) => rows.map((row, index) =>
          index !== Number(params['index'])
            ? row
            : {
                cells: row.cells.map((cell, cellIndex) =>
                  cellIndex === columnIndex(host.text(params, 'column'))
                    ? host.text(params, 'value')
                    : cell),
              }));
      case 'editor.sessionUndo':
        return host.change((document) => ({ ...document, canUndo: false, canRedo: true }));
      case 'editor.sessionRedo':
        return host.change((document) => ({ ...document, canUndo: true, canRedo: false }));
      case 'editor.sessionMove':
        return host.moveStep(params);
      case 'editor.sessionRemove':
        return host.removeStep(params);
      case 'editor.sessionTriggerAdd':
        return host.addTrigger(params);
      case 'editor.sessionTriggerRemove':
        return host.removeTrigger(params);
      case 'editor.sessionTriggerBinding':
        return host.patchTrigger(params, (trigger) => ({
          ...trigger,
          binding: host.text(params, 'binding'),
        }));
      case 'editor.sessionTriggerLoop':
        return host.patchTrigger(params, (trigger) => ({
          ...trigger,
          loop: host.text(params, 'loop'),
        }));
      case 'editor.sessionTriggerOnce':
        return host.patchTrigger(params, (trigger) => ({
          ...trigger,
          once: params['once'] === true,
        }));
      case 'editor.sessionTriggerEnabled':
        return host.patchTrigger(params, (trigger) => ({
          ...trigger,
          enabled: params['enabled'] === true,
        }));
      case 'editor.sessionTriggerTimer':
        return host.patchTrigger(params, (trigger) => timerTrigger(trigger, params));
      case 'editor.sessionTriggerFile':
        return host.patchTrigger(params, (trigger) => fileTrigger(trigger, params));
      case 'editor.sessionTriggerProcess':
        return host.patchTrigger(params, (trigger) => processTrigger(trigger, params));
      case 'editor.sessionTriggerHotstring':
        return host.patchTrigger(params, (trigger) => ({
          ...trigger,
          hotstringText: host.text(params, 'value'),
        }));
      case 'editor.sessionTriggerHotstringOption': {
        const field = host.text(params, 'field');
        const value = host.text(params, 'value');
        return host.patchTrigger(params, (trigger) => ({
          ...trigger,
          [field]: field === 'endChars' ? value : value === 'true',
        }));
      }
      case 'editor.sessionTriggerHotstringEndKey':
        return host.patchTrigger(params, (trigger) => ({
          ...trigger,
          hotstringEndKeys: trigger.hotstringEndKeys.map((key) => key.value === host.text(params, 'value')
            ? { ...key, checked: params['checked'] === true }
            : key),
        }));
      case 'editor.sessionTriggerHotstringEndReset':
        return host.patchTrigger(params, (trigger) => ({
          ...trigger,
          hotstringEndKeys: trigger.hotstringEndKeys.map((key) => ({ ...key, checked: true })),
        }));
      case 'editor.sessionTriggerHotstringEndCapture': {
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
      case 'editor.sessionTriggerInterfaceHotkey':
        return host.patchTrigger(params, (trigger) => ({
          ...trigger,
          interfaceHotkey: host.text(params, 'binding'),
          interfaceHotkeyProblem: '',
        }));
      case 'editor.sessionTriggerInterfaceHotkeyClear':
        return host.patchTrigger(params, (trigger) => ({
          ...trigger,
          interfaceHotkey: '',
          interfaceHotkeyProblem: '',
        }));
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
      case 'editor.sessionTriggerColor':
        return host.patchTrigger(params, (trigger) => colorTrigger(trigger, params));
      case 'editor.sessionTriggerWindow':
        return host.patchTrigger(params, (trigger) => windowTrigger(trigger, params));
      case 'editor.sessionTriggerInterface':
        return host.patchTrigger(params, (trigger) => interfaceTrigger(trigger, params));
      case 'editor.sessionTriggerIdle':
        return host.patchTrigger(params, (trigger) => ({
          ...trigger,
          idleSeconds: host.text(params, 'value'),
        }));
      case 'editor.sessionGlobalAdd':
        return host.change(addGlobal);
      case 'editor.sessionGlobalPatch':
        return host.change((document) => patchGlobal(document, params));
      case 'editor.sessionGlobalRemove':
        return host.change((document) => removeGlobal(document, params));
      case 'editor.sessionVariableAdd':
        return host.change(addVariable);
      case 'editor.sessionVariablePatch':
        return host.change((document) => patchVariable(document, params));
      case 'editor.sessionVariableRemove':
        return host.change((document) => removeVariable(document, params));
      case 'editor.sessionExport':
        return true;
      case 'editor.sessionRecordStart':
        host.recording = true;
        host.captured = 0;
        return { recording: true, captured: 0 };
      case 'editor.sessionRecordState':
        if (host.recording) {
          host.captured += 1;
        }

        return { recording: host.recording, captured: host.captured };
      case 'editor.sessionRecordOption':
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
      case 'editor.sessionDelayScale':
        return host.change((document) => ({
          ...document,
          recording: {
            ...document.recording,
            delayScale: Number(params['factor'] ?? 1),
            delayScaleDisplay: '×' + String(params['factor'] ?? 1),
            hasDelayScale: Number(params['factor'] ?? 1) !== 1,
          },
        }));
      case 'editor.sessionRecordStop':
        return host.stopRecording();
      case 'editor.sessionSave':
        return host.save();
      case 'editor.sessionActions':
        return actionCatalogue(host.language());
      case 'editor.sessionPublicMacroAdd':
        return host.addStep({ type: 'control.runMacro' });
    default:
      throw new Error('不认识编辑器命令 ' + method + '。');
  }
}
