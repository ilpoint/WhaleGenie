import type { MockEditor } from './mock-editor.js';

const MOCK_IMAGE = 'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScL5WQAAAABJRU5ErkJggg==';
import type {
  EditorActionField,
  EditorActionForm,
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

export function emptyActionForm(): EditorActionForm {
    return {
      open: false, isEditing: false, canConfirm: false, canRun: false, running: false,
      runLabel: '', validationMessage: '', validationIsProblem: false,
      actionKey: '', name: '', description: '', stepId: '', commitLabel: '', page: 'base',
      pages: [], rows: [], advancedRows: [], returns: [], callArguments: [],
      meta: {
        enabled: true, comment: '', timeout: duration(), retryCount: null,
        retryDelay: duration(), delayBefore: duration(), delayAfter: duration(),
        retryBackoff: 'fixed', onError: 'stop', retryPlan: '', tuned: false,
      },
      globalDraft: null, windowRules: [], windowAbove: '',
      branchSummary: '', attemptSummary: '', isBlock: false, showsBranches: false,
      showsAttempt: false, json: '', notice: '', noticeIsWarning: false,
      clipboardPreview: { shown: false, note: '' },
    };
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
    const pages = host.actionForm?.pages ?? [
      { key: 'base', label: english ? 'Basics' : '基础设置', present: true, hasDot: false },
      { key: 'exec', label: english ? 'Execution' : '执行与容错', present: true, hasDot: false },
      { key: 'advanced', label: english ? 'Advanced' : '高级设置', present: false, hasDot: false },
      { key: 'other', label: english ? 'More' : '其它设置', present: true, hasDot: false },
    ];
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
    const returnRows = returns.length > 0 ? returns : [
      {
        key: 'found', label: '是否找到', hint: '', structure: 'true | false', structureTip: '',
        ticked: false, name: '', isNameTaken: false, nameWarning: '', isPrimary: true,
      },
    ];
    const missing = parameters.filter((parameter) =>
      parameter.required && parameter.value.trim().length === 0);
    const canConfirm = missing.length === 0;
    host.actionForm = {
      open: true,
      isEditing: host.actionIsEditing,
      canConfirm,
      canRun: false,
      running: false,
      runLabel: english ? 'Run' : '运行',
      validationMessage: canConfirm ? '' : (english ? 'Still needed: ' : '还缺：')
        + missing.map((parameter) => parameter.label).join(english ? ', ' : '、'),
      validationIsProblem: !canConfirm,
      actionKey: step.type,
      name: step.name,
      description: '',
      stepId: step.id,
      commitLabel: host.actionIsEditing
        ? (english ? 'Save step' : '保存步骤')
        : (english ? 'Add step' : '添加步骤'),
      page: host.actionPage,
      pages,
      rows,
      advancedRows,
      returns: returnRows,
      callArguments: [],
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
      globalDraft: null,
      windowRules: [],
      windowAbove: '',
      branchSummary: '',
      attemptSummary: '',
      isBlock: step.acceptsSteps,
      showsBranches: step.type === 'control.switch',
      showsAttempt: step.type === 'control.try',
      json: JSON.stringify(step, null, 2),
      notice: '',
      noticeIsWarning: false,
      clipboardPreview: { shown: false, note: '' },
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
      name: parameter.name,
      label: parameter.label,
      hint: '',
      kind: parameter.kind,
      valueForm: parameter.valueForm,
      value: parameter.value,
      display: parameter.display || parameter.value,
      parts: parameter.parts ?? [],
      options: parameter.options ?? [],
      columns: parameter.columns ?? [],
      condition: parameter.condition,
      isApplicable: true,
      isDefault: parameter.value.trim().length === 0,
      isMissing: parameter.required && parameter.value.trim().length === 0,
      required: parameter.required,
      advanced: parameter.advanced,
      group: parameter.group,
      groupLabel: parameter.groupLabel,
      offersValue: parameter.offersValue,
      takesFormula: parameter.takesFormula,
      showsNameMark: parameter.showsNameMark,
      allowsNewName: false,
      isNumber: parameter.isNumber,
      isNumberFormula: parameter.isNumberFormula,
      isDuration: false,
      unit: '',
      unitLabel: '',
      durationUnits: [],
      isPercent: false,
      percentValue: 0,
      channelNote: '',
      jitterRange: '',
      isMultiline: parameter.isMultiline,
      isCode: false,
      isColor: parameter.isColor,
      colour: parameter.isColor ? parameter.value : '',
      isPath: parameter.kind === 'Path',
      pathIntent: '',
      pathFilter: '',
      isKey: parameter.isKey,
      isKeys: parameter.kind === 'Keys',
      isSelector: parameter.kind === 'Selector',
      isBrowserTarget: parameter.kind === 'Browser',
      isImage: parameter.kind === 'Image',
      isImageList: parameter.kind === 'Images',
      isRegion: parameter.kind === 'Region',
      isRegionList: parameter.kind === 'Region',
      isColourPoints: parameter.kind === 'ColourPoints',
      isColourRows: parameter.kind === 'Color' && rows.length > 0,
      isKeySequence: parameter.kind === 'KeySequence',
      isNarrow: parameter.kind === 'Narrow',
      isWindow: parameter.kind === 'WindowRule',
      isWindowRule: parameter.kind === 'WindowRule',
      isMacro: parameter.kind === 'Macro',
      isStepChoice: parameter.kind === 'Step',
      isRecord: parameter.kind === 'Record',
      isPoint: parameter.kind === 'Point',
      isRect: parameter.kind === 'Rect',
      placeholder: parameter.placeholder,
      hasToBeAnswered: false,
      isFollowingWindow: false,
      windowRuleSummary: '',
      regions: parameter.kind === 'Region'
        ? rows.map((row) => ({
            x: row.cells[0] ?? '', y: row.cells[1] ?? '',
            width: row.cells[2] ?? '', height: row.cells[3] ?? '', variable: row.cells[4] ?? '',
          }))
        : [],
      images: parameter.kind === 'Images'
        ? rows.map((row, index) => ({
            text: row.cells[0] ?? '', canMoveUp: index > 0, canMoveDown: index < rows.length - 1,
            preview: row.cells[0]
              ? { dataUrl: MOCK_IMAGE, fileName: row.cells[0], problem: '' }
              : null,
          }))
        : [],
      colours: [],
      colourPoints: parameter.kind === 'ColourPoints'
        ? rows.map((row) => ({
            dx: row.cells[0] ?? '', dy: row.cells[1] ?? '', colour: row.cells[2] ?? '',
            mustNot: row.cells[3] === 'true', tolerance: row.cells[4] ?? '',
          }))
        : [],
      keys: parameter.kind === 'KeySequence'
        ? rows.map((row, index) => ({
            number: index + 1, keys: row.cells[0] ?? '',
            hold: row.cells[1] ?? '', gap: row.cells[2] ?? '',
          }))
        : [],
      narrowRows: parameter.kind === 'Narrow'
        ? rows.map((row) => ({
            how: row.cells[0] ?? 'HasText', value: row.cells[1] ?? '', count: '',
            needsValue: true, wantsWords: true, onPage: true, ways: [],
          }))
        : [],
      rows: rows.length > 0 && !['Region', 'Images', 'ColourPoints', 'KeySequence', 'Narrow', 'Color']
        .includes(parameter.kind)
        ? rows.map((row) => ({ cells: row.cells }))
        : [],
      macros: [],
      steps: parameter.options ?? [],
      candidates: [],
      windowPreview: null,
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
