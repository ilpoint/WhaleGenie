import type { MockEditor } from './mock-editor.js';
import type {
  EditorConditionNode,
  EditorParameter,
  EditorRow,
  EditorStep,
  EditorValueSnapshot,
} from './protocol.js';
import { MockValueEditor } from './mock-value-editor.js';
import {
  changeCondition,
  changeRows,
  columnIndex,
  setConditionCell,
  setParameter,
} from './mock-editor-step-ops.js';

export function openValue(host: MockEditor, params: Record<string, unknown>): EditorValueSnapshot {
    const document = host.activeSnapshot();
    if (!document) {
      throw new Error('没有打开的编辑器会话。');
    }

    const stepId = host.text(params, 'step');
    const parameterName = host.text(params, 'parameter');
    const draftStep = host.actionForm && stepId === host.actionForm.stepId
      ? host.actionStep
      : null;
    const step = draftStep ?? findStep(document.steps, stepId);
    const parameter = step?.parameters.find((candidate) => candidate.name === parameterName);
    if (!step || !parameter) {
      throw new Error('这一步没有字段 ' + parameterName + '。');
    }

    const conditionField = host.optionalText(params, 'conditionField');
    const conditionPath = Array.isArray(params['conditionPath'])
      ? (params['conditionPath'] as unknown[]).map((item) => Number(item))
      : null;
    const rowIndex = Number(params['rowIndex'] ?? -1);
    const column = host.optionalText(params, 'column') ?? '';
    if (conditionField && conditionPath) {
      const node = conditionAt(parameter.condition?.nodes ?? [], conditionPath);
      const seed = node ? (conditionField === 'left' ? node.left : node.right) : '';
      const synthetic: EditorParameter = {
        ...parameter,
        value: seed,
        display: seed,
        parts: seed.length > 0 ? [{ kind: 'text', text: seed, name: '' }] : [],
        valueForm: 'Value',
        isNumber: false,
      };
      host.valueEditor = new MockValueEditor(document, synthetic, (value) => {
        if (draftStep && host.actionForm) {
          host.actionStep = changeCondition([host.actionStep!], step.id, parameter.name,
            (condition) => setConditionCell(condition, conditionPath, conditionField, value))[0]!;
          host.refreshActionForm();
          return;
        }

        host.change((current) => ({
          ...current,
          dirty: true,
          steps: changeCondition(current.steps, step.id, parameter.name, (condition) =>
            setConditionCell(condition, conditionPath, conditionField, value)),
        }));
      });
      return host.valueEditor.snapshot();
    }

    if (rowIndex >= 0 && column.length > 0) {
      const cell = columnIndex(column);
      const seed = parameter.rows[rowIndex]?.cells[cell] ?? '';
      const synthetic: EditorParameter = {
        ...parameter,
        value: seed,
        display: seed,
        parts: seed.length > 0 ? [{ kind: 'text', text: seed, name: '' }] : [],
        valueForm: 'Value',
        isNumber: false,
      };
      host.valueEditor = new MockValueEditor(document, synthetic, (value) => {
        const update = (rows: EditorRow[]): EditorRow[] => rows.map((row, index) => index === rowIndex
          ? { ...row, cells: row.cells.map((content, cellIndex) =>
            cellIndex === cell ? value : content) }
          : row);
        if (draftStep && host.actionForm) {
          host.actionStep = changeRows([host.actionStep!], step.id, parameter.name, update)[0]!;
          host.refreshActionForm();
          return;
        }

        host.change((current) => ({
          ...current,
          dirty: true,
          steps: changeRows(current.steps, step.id, parameter.name, update),
        }));
      });
      return host.valueEditor.snapshot();
    }

    host.valueEditor = new MockValueEditor(document, parameter, (value) => {
      if (draftStep && host.actionForm) {
        host.actionStep = setParameter([host.actionStep!], step.id, parameter.name, value)[0]!;
        host.refreshActionForm();
        return;
      }

      host.change((current) => ({
        ...current,
        dirty: true,
        steps: setParameter(current.steps, step.id, parameter.name, value),
      }));
    });
    return host.valueEditor.snapshot();
  }

export function valueCommand(host: MockEditor, params: Record<string, unknown>): EditorValueSnapshot {
    if (!host.valueEditor) {
      throw new Error('没有打开的取值窗口。');
    }

    return host.valueEditor.command(host.text(params, 'action'), params);
  }

function conditionAt(nodes: EditorConditionNode[], path: number[]): EditorConditionNode | null {
  if (path.length === 0) {
    return null;
  }

  const node = nodes[path[0] ?? -1];
  if (!node) {
    return null;
  }

  return path.length === 1 ? node : conditionAt(node.children, path.slice(1));
}

function findStep(steps: EditorStep[], id: string): EditorStep | null {
  for (const step of steps) {
    if (step.id === id) {
      return step;
    }

    for (const section of step.sections) {
      const found = findStep(section.steps, id);
      if (found) {
        return found;
      }
    }
  }

  return null;
}
