import type { MockEditor } from './mock-editor.js';

import type {
  EditorActionField,
  EditorActionForm,
  EditorActionPage,
  EditorActionRow,
  EditorParameter,
  EditorSnapshot,
  EditorStep,
} from './protocol.js';
import { findAction } from './mock-editor-actions.js';
import { duration } from './mock-editor-duration.js';
import { locateStep } from './mock-editor-step-ops.js';
import { stepFromAction } from './mock-editor-steps.js';
import { changeStep } from './mock-editor-step-ops.js';
import raw from './data/action-form.json';

/**
 * 动作表单的骨架与字段默认值都是固定样例，放在 data/action-form.json；这里只把步骤与参数
 * 填进去（页签、行、返回值与 JSON 预览都要跟着步骤走）。
 */

interface ActionFormData {
  mockImage: string;
  emptyForm: EditorActionForm;
  pages: Record<string, EditorActionPage[]>;
  returnsDefault: EditorActionForm['returns'];
  fieldBase: EditorActionField;
  fieldKinds: Record<string, Partial<EditorActionField>>;
}

const DATA = raw as unknown as ActionFormData;
const MOCK_IMAGE = DATA.mockImage;

export function emptyActionForm(): EditorActionForm {
  return structuredClone(DATA.emptyForm);
}

export function actionOpen(host: MockEditor, params: Record<string, unknown>): EditorActionForm {
  const document = host.activeSnapshot();
  if (!document) {
    throw new Error('没有打开的编辑器会话。');
  }

  const stepId = host.optionalText(params, 'stepId') ?? '';
  const type = host.optionalText(params, 'type') ?? '';
  let step: EditorStep;
  let isEditing = false;
  if (stepId.length > 0) {
    const found = locateStep(document.steps, stepId);
    if (!found) {
      throw new Error('没有找到步骤 ' + stepId + '。');
    }

    step = structuredClone(found.list[found.index]!);
    isEditing = true;
  } else {
    const action = findAction(type);
    if (!action) {
      throw new Error('没有找到动作 ' + type + '。');
    }

    step = stepFromAction(action, host.nextStepId());
  }

  host.actionTarget = {
    targetStepId: host.optionalText(params, 'targetStepId') ?? '',
    before: params['before'] === true,
    parentId: host.optionalText(params, 'parentId') ?? '',
    list: host.optionalText(params, 'list') ?? '',
  };
  host.actionStep = step;
  host.actionIsEditing = isEditing;
  host.actionPage = 'base';
  host.refreshActionForm();
  return host.actionForm!;
}

export function refreshActionForm(host: MockEditor): void {
  const step = host.actionStep;
  if (!step) {
    host.actionForm = null;
    return;
  }

  const english = host.language() === 'en';
  const pages = host.actionForm?.pages ?? structuredClone(DATA.pages[english ? 'en' : 'zh'] ?? []);
  const parameters = step.parameters.filter((parameter) =>
    parameter.kind !== 'Steps' && parameter.kind !== 'Returns' && parameter.name !== 'returns');
  const rows = parameters
    .filter((parameter) => !parameter.advanced)
    .map((parameter, index) => actionRow(parameter, index === 0));
  const advancedRows = parameters
    .filter((parameter) => parameter.advanced)
    .map((parameter, index) => actionRow(parameter, index === 0));
  const returnsParameter = step.parameters.find((parameter) =>
    parameter.kind === 'Returns' || parameter.name === 'returns');
  const returns = (returnsParameter?.rows ?? []).map((row, index) => ({
    key: row.cells[0] ?? '',
    label: row.cells[0] ?? '',
    hint: '',
    structure: '',
    structureTip: '',
    ticked: (row.cells[0] ?? '').length > 0,
    name: row.cells[1] ?? '',
    isNameTaken: false,
    nameWarning: '',
    isPrimary: index === 0,
  }));
  const returnRows = returns.length > 0 ? returns : structuredClone(DATA.returnsDefault);
  const missing = parameters.filter((parameter) =>
    parameter.required && parameter.value.trim().length === 0);
  const canConfirm = missing.length === 0;
  // A macro field offers the document's own submacros, exactly what the page's select shows.
  const document = host.activeDocumentId === null ? null : host.documents.get(host.activeDocumentId);
  const macros = (document?.subMacros ?? []).map((name) => ({
    value: name, label: name,
  }));
  for (const row of [...rows, ...advancedRows]) {
    for (const cell of row.cells) {
      if (cell.isMacro) {
        cell.macros = macros;
      }
    }
  }

  host.actionForm = {
    ...structuredClone(DATA.emptyForm),
    open: true,
    isEditing: host.actionIsEditing,
    canConfirm,
    runLabel: english ? 'Run' : '运行',
    validationMessage: canConfirm ? '' : (english ? 'Still needed: ' : '还缺：')
      + missing.map((parameter) => parameter.label).join(english ? ', ' : '、'),
    validationIsProblem: !canConfirm,
    actionKey: step.type,
    name: step.name,
    stepId: step.id,
    commitLabel: host.actionIsEditing
      ? (english ? 'Save step' : '保存步骤')
      : (english ? 'Add step' : '添加步骤'),
    page: host.actionPage,
    pages,
    rows,
    advancedRows,
    returns: returnRows,
    meta: {
      enabled: step.enabled,
      comment: step.comment,
      timeout: duration(step.timeoutMs),
      retryCount: step.retryCount,
      retryDelay: duration(step.retryDelayMs),
      delayBefore: duration(),
      delayAfter: duration(),
      retryBackoff: 'fixed',
      onError: step.onError,
      retryPlan: '',
      tuned: step.retryCount > 0 || step.timeoutMs > 0 || step.onError !== 'Stop',
    },
    isBlock: step.acceptsSteps,
    showsBranches: step.type === 'control.switch',
    showsAttempt: step.type === 'control.try',
    json: JSON.stringify(step, null, 2),
  };
}

export function actionRow(parameter: EditorParameter, isGroupStart: boolean): EditorActionRow {
  return {
    groupKey: '', groupTitle: '', groupHint: '', groupIndex: 0, isGroupStart, isQuad: false,
    cells: [actionField(parameter)],
    picker: null,
  };
}

export function actionField(parameter: EditorParameter): EditorActionField {
  const rows = parameter.rows ?? [];
  return {
    ...structuredClone(DATA.fieldBase),
    ...structuredClone(DATA.fieldKinds[parameter.kind] ?? {}),
    name: parameter.name,
    label: parameter.label,
    kind: parameter.kind,
    valueForm: parameter.valueForm,
    value: parameter.value,
    display: parameter.display || parameter.value,
    parts: parameter.parts ?? [],
    options: parameter.options ?? [],
    columns: parameter.columns ?? [],
    condition: parameter.condition,
    isDefault: parameter.value.trim().length === 0,
    isMissing: parameter.required && parameter.value.trim().length === 0,
    required: parameter.required,
    advanced: parameter.advanced,
    group: parameter.group,
    groupLabel: parameter.groupLabel,
    offersValue: parameter.offersValue,
    takesFormula: parameter.takesFormula,
    showsNameMark: parameter.showsNameMark,
    isNumber: parameter.isNumber,
    isNumberFormula: parameter.isNumberFormula,
    isMultiline: parameter.isMultiline,
    isColor: parameter.isColor,
    colour: parameter.isColor ? parameter.value : '',
    isKey: parameter.isKey,
    placeholder: parameter.placeholder,
    isColourRows: parameter.kind === 'Color' && rows.length > 0,
    regions: parameter.kind === 'Region' ? rows.map((row) => ({
      x: row.cells[0] ?? '', y: row.cells[1] ?? '',
      width: row.cells[2] ?? '', height: row.cells[3] ?? '', variable: row.cells[4] ?? '',
    })) : [],
    images: parameter.kind === 'Images' ? rows.map((row, index) => ({
      text: row.cells[0] ?? '', canMoveUp: index > 0, canMoveDown: index < rows.length - 1,
      preview: row.cells[0] ? { dataUrl: MOCK_IMAGE, fileName: row.cells[0], problem: '' } : null,
    })) : [],
    colourPoints: parameter.kind === 'ColourPoints' ? rows.map((row) => ({
      dx: row.cells[0] ?? '', dy: row.cells[1] ?? '', colour: row.cells[2] ?? '',
      mustNot: row.cells[3] === 'true', tolerance: row.cells[4] ?? '',
    })) : [],
    keys: parameter.kind === 'KeySequence' ? rows.map((row, index) => ({
      number: index + 1, keys: row.cells[0] ?? '',
      hold: row.cells[1] ?? '', gap: row.cells[2] ?? '',
    })) : [],
    narrowRows: parameter.kind === 'Narrow' ? rows.map((row) => ({
      how: row.cells[0] ?? 'HasText', value: row.cells[1] ?? '', count: '',
      needsValue: true, wantsWords: true, onPage: true, ways: [],
    })) : [],
    rows: rows.length > 0 && !['Region', 'Images', 'ColourPoints', 'KeySequence', 'Narrow', 'Color']
      .includes(parameter.kind)
      ? rows.map((row) => ({ cells: row.cells }))
      : [],
    steps: parameter.options ?? [],
    imagePreview: (parameter.kind === 'Image' || parameter.kind === 'Path') && parameter.value
      ? { dataUrl: MOCK_IMAGE, fileName: parameter.value, problem: '' }
      : null,
  };
}

export function actionConfirm(host: MockEditor): EditorSnapshot | null {
  const form = host.actionForm;
  if (!form?.canConfirm || !host.actionStep) {
    return host.activeSnapshot();
  }

  const step = structuredClone(host.actionStep);
  const ok = host.change((document) => {
    if (form.isEditing) {
      return {
        ...document,
        dirty: true,
        canUndo: true,
        canRedo: false,
        steps: changeStep(document.steps, form.stepId, () => step),
      };
    }

    const steps = insertMockStep(host, document.steps, step);
    return { ...document, dirty: true, canUndo: true, canRedo: false, steps };
  });
  host.actionForm = null;
  host.actionStep = null;
  return ok;
}

function insertMockStep(host: MockEditor, steps: EditorStep[], step: EditorStep): EditorStep[] {
  const target = host.actionTarget;
  if (target.targetStepId.length > 0) {
    const found = locateStep(steps, target.targetStepId);
    if (found) {
      found.list.splice(found.index + (target.before ? 0 : 1), 0, step);
      return steps;
    }
  }

  if (target.parentId.length > 0) {
    const parent = locateStep(steps, target.parentId);
    if (parent) {
      const parentStep = parent.list[parent.index]!;
      const section = parentStep.sections.find((candidate) =>
        target.list.length === 0 || candidate.name === target.list);
      if (section) {
        section.steps.push(step);
        return steps;
      }
    }
  }

  return [...steps, step];
}
