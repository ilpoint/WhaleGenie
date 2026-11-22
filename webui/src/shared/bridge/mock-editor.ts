import type {
  EditorActionField,
  EditorActionForm,
  EditorActionRow,
  EditorCondition,
  EditorDuration,
  EditorConditionNode,
  EditorParameter,
  EditorRow,
  EditorSnapshot,
  EditorStep,
  EditorTab,
  EditorTrigger,
  EditorValueSnapshot,
} from './protocol.js';
import { findAction } from './mock-editor-actions.js';

const MOCK_IMAGE = 'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScL5WQAAAABJRU5ErkJggg==';
import { MockValueEditor } from './mock-value-editor.js';
import { handleSessionRun } from './mock-editor-run-session.js';
import { handleValueRun } from './mock-editor-run-value.js';
import { handleActionRun } from './mock-editor-run-action.js';
import {
  changeTrigger,
  createDocumentSnapshot,
  newTrigger,
} from './mock-editor-fixtures.js';
import {
  presentDocument,
  stepFromAction,
} from './mock-editor-steps.js';
import {
  addStepToTree,
  changeCondition,
  changeRows,
  changeStep,
  columnIndex,
  conditionPath,
  duplicateStepInTree,
  locateStep,
  moveStepInTree,
  removeStepFromTree,
  setConditionCell,
  setParameter,
  setStepEnabled,
} from './mock-editor-step-ops.js';

/** One project macro the mock editor may open; the mock passes the project list through. */
export interface MockMacroEntry {
  name: string;
  description: string;
  group: string;
}

const NEW_KEY = '\u0000new';

function duration(milliseconds = 0): EditorDuration {
  return { milliseconds, written: milliseconds > 0 ? String(milliseconds) : '', unit: '', units: [] };
}

/**
 * The editor half of the mock host. Each open macro is a document of its own with its own dirty
 * state, exactly like the host's editor workspace; the main mock only carries commands in and
 * snapshots out, so the non-editor domains stay untouched.
 */
export class MockEditor {
  readonly documents = new Map<string, EditorSnapshot>();

  activeKey: string | null = null;

  recording = false;

  captured = 0;

  private stepSequence = 0;

  private triggerSequence = 0;

  valueEditor: MockValueEditor | null = null;

  private stepClipboard = '';

  actionForm: EditorActionForm | null = null;

  actionStep: EditorStep | null = null;

  private actionIsEditing = false;

  actionPage = 'base';

  private readonly macros: () => readonly MockMacroEntry[];

  readonly language: () => string;

  constructor(
    macros: readonly MockMacroEntry[] | (() => readonly MockMacroEntry[]) = () => [],
    language: string | (() => string) = 'zh-Hans',
  ) {
    this.macros = typeof macros === 'function' ? macros : () => macros;
    this.language = typeof language === 'function' ? language : () => language;
  }

  /** Opens a named macro, or the single unnamed scratch document when no name is given. */
  open(name?: string | null): EditorSnapshot {
    const trimmed = name?.trim() ?? '';
    const key = trimmed.length > 0 ? trimmed : NEW_KEY;
    let document = this.documents.get(key);
    if (!document) {
      const macro = trimmed.length > 0
        ? this.macros().find((entry) => entry.name.trim().toLocaleLowerCase()
          === trimmed.toLocaleLowerCase())
        : undefined;
      document = createDocumentSnapshot(trimmed.length > 0 ? trimmed : '未命名宏', {
        description: macro?.description ?? '',
        group: macro?.group ?? '',
        subMacros: trimmed.length > 0 ? ['辅助宏'] : [],
      });
      this.documents.set(key, document);
    }

    this.activeKey = key;
    return document;
  }

  /** Adds another unnamed document; the key never collides with one already open. */
  newDocument(): EditorSnapshot {
    const key = this.nextNewKey();
    const document = createDocumentSnapshot('未命名宏', {});
    this.documents.set(key, document);
    this.activeKey = key;
    return document;
  }

  /** Alias for newDocument, matching the host's New command. */
  new(): EditorSnapshot {
    return this.newDocument();
  }

  activate(key: string): EditorSnapshot {
    const document = this.documents.get(key);
    if (!document) {
      throw new Error('编辑器里没有这个文档：' + key + '。');
    }

    this.activeKey = key;
    return document;
  }

  /** Closes the named document (or the active one) and answers with what is active afterwards. */
  close(key?: string | null): EditorSnapshot | null {
    const target = key !== undefined && key !== null && key.length > 0 ? key : this.activeKey;
    if (target === null) {
      return null;
    }

    const document = this.documents.get(target);
    if (!document) {
      return this.activeSnapshot();
    }

    if (document.dirty) {
      throw new Error('这份文档还有未保存修改，请先保存再关闭。');
    }

    const wasActive = this.activeKey === target;
    this.documents.delete(target);
    if (wasActive) {
      const keys = [...this.documents.keys()];
      this.activeKey = keys.length > 0 ? keys[keys.length - 1]! : null;
    }

    return this.activeSnapshot();
  }

  /** The tab strip lists only documents that are still open, in the order they were opened. */
  tabs(): { activeKey: string | null; tabs: EditorTab[] } {
    return {
      activeKey: this.activeKey,
      tabs: [...this.documents.entries()].map(([key, document]) => ({
        key,
        name: document.name,
        dirty: document.dirty,
        problem: !document.canSave,
        isSubMacro: document.isSubMacro,
      })),
    };
  }

  /** Opens a submacro of the active document as a document of its own. */
  subOpen(name?: string | null): EditorSnapshot {
    const parentKey = this.activeKey;
    if (parentKey === null) {
      throw new Error('先打开一个宏，才能打开它的子宏。');
    }

    const trimmed = name?.trim() ?? '';
    if (trimmed.length === 0) {
      throw new Error('子宏需要一个名字。');
    }

    const parent = this.documents.get(parentKey);
    if (!parent) {
      throw new Error('编辑器里还没有打开的文档。');
    }

    if (!parent.subMacros.some((macro) => macro.trim() === trimmed)) {
      throw new Error('这个宏没有名为 ' + trimmed + ' 的子宏。');
    }

    const key = 'sub:' + parentKey + ':' + trimmed;
    let document = this.documents.get(key);
    if (!document) {
      document = createDocumentSnapshot(trimmed, { isSubMacro: true });
      this.documents.set(key, document);
    }

    this.activeKey = key;
    return document;
  }

  /** Every editor command the host serves; the main mock only forwards the method and params. */
  run(method: string, params: Record<string, unknown>): unknown {
    if (method.startsWith('editor.action')) {
      return handleActionRun(this, method, params);
    }

    if (method.startsWith('editor.value')) {
      return handleValueRun(this, method, params);
    }

    if (method.startsWith('editor.session') || method === 'editor.hotstringKeyPad') {
      return handleSessionRun(this, method, params);
    }

    throw new Error('不认识编辑器命令 ' + method + '。');
  }

  emptyActionForm(): EditorActionForm {
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

  actionOpen(params: Record<string, unknown>): EditorActionForm {
    const document = this.activeSnapshot();
    if (!document) {
      throw new Error('没有打开的编辑器会话。');
    }

    const stepId = this.optionalText(params, 'stepId') ?? '';
    const type = this.optionalText(params, 'type') ?? '';
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

      step = stepFromAction(action, this.nextStepId());
    }

    this.actionTarget = {
      targetStepId: this.optionalText(params, 'targetStepId') ?? '',
      before: params['before'] === true,
      parentId: this.optionalText(params, 'parentId') ?? '',
      list: this.optionalText(params, 'list') ?? '',
    };
    this.actionStep = step;
    this.actionIsEditing = isEditing;
    this.actionPage = 'base';
    this.refreshActionForm();
    return this.actionForm!;
  }

  refreshActionForm(): void {
    const step = this.actionStep;
    if (!step) {
      this.actionForm = null;
      return;
    }

    const english = this.language() === 'en';
    const pages = this.actionForm?.pages ?? [
      { key: 'base', label: english ? 'Basics' : '基础设置', present: true, hasDot: false },
      { key: 'exec', label: english ? 'Execution' : '执行与容错', present: true, hasDot: false },
      { key: 'advanced', label: english ? 'Advanced' : '高级设置', present: false, hasDot: false },
      { key: 'other', label: english ? 'More' : '其它设置', present: true, hasDot: false },
    ];
    const parameters = step.parameters.filter((parameter) =>
      parameter.kind !== 'Steps' && parameter.kind !== 'Returns' && parameter.name !== 'returns');
    const rows = parameters
      .filter((parameter) => !parameter.advanced)
      .map((parameter, index) => this.actionRow(parameter, index === 0));
    const advancedRows = parameters
      .filter((parameter) => parameter.advanced)
      .map((parameter, index) => this.actionRow(parameter, index === 0));
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
    this.actionForm = {
      open: true,
      isEditing: this.actionIsEditing,
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
      commitLabel: this.actionIsEditing
        ? (english ? 'Save step' : '保存步骤')
        : (english ? 'Add step' : '添加步骤'),
      page: this.actionPage,
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

  private actionRow(parameter: EditorParameter, isGroupStart: boolean): EditorActionRow {
    return {
      groupKey: '', groupTitle: '', groupHint: '', groupIndex: 0, isGroupStart, isQuad: false,
      cells: [this.actionField(parameter)],
      picker: null,
    };
  }

  private actionField(parameter: EditorParameter): EditorActionField {
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

  private actionTarget: { targetStepId: string; before: boolean; parentId: string; list: string } = {
    targetStepId: '', before: false, parentId: '', list: '',
  };

  actionConfirm(): EditorSnapshot | null {
    const form = this.actionForm;
    if (!form?.canConfirm || !this.actionStep) {
      return this.activeSnapshot();
    }

    const step = structuredClone(this.actionStep);
    const ok = this.change((document) => {
      if (form.isEditing) {
        return {
          ...document,
          dirty: true,
          canUndo: true,
          canRedo: false,
          steps: changeStep(document.steps, form.stepId, () => step),
        };
      }

      const steps = this.insertMockStep(document.steps, step);
      return { ...document, dirty: true, canUndo: true, canRedo: false, steps };
    });
    this.actionForm = null;
    this.actionStep = null;
    return ok;
  }

  private insertMockStep(steps: EditorStep[], step: EditorStep): EditorStep[] {
    const target = this.actionTarget;
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

  openValue(params: Record<string, unknown>): EditorValueSnapshot {
    const document = this.activeSnapshot();
    if (!document) {
      throw new Error('没有打开的编辑器会话。');
    }

    const stepId = this.text(params, 'step');
    const parameterName = this.text(params, 'parameter');
    const draftStep = this.actionForm && stepId === this.actionForm.stepId
      ? this.actionStep
      : null;
    const step = draftStep ?? findStep(document.steps, stepId);
    const parameter = step?.parameters.find((candidate) => candidate.name === parameterName);
    if (!step || !parameter) {
      throw new Error('这一步没有字段 ' + parameterName + '。');
    }

    const conditionField = this.optionalText(params, 'conditionField');
    const conditionPath = Array.isArray(params['conditionPath'])
      ? (params['conditionPath'] as unknown[]).map((item) => Number(item))
      : null;
    const rowIndex = Number(params['rowIndex'] ?? -1);
    const column = this.optionalText(params, 'column') ?? '';
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
      this.valueEditor = new MockValueEditor(document, synthetic, (value) => {
        if (draftStep && this.actionForm) {
          this.actionStep = changeCondition([this.actionStep!], step.id, parameter.name,
            (condition) => setConditionCell(condition, conditionPath, conditionField, value))[0]!;
          this.refreshActionForm();
          return;
        }

        this.change((current) => ({
          ...current,
          dirty: true,
          steps: changeCondition(current.steps, step.id, parameter.name, (condition) =>
            setConditionCell(condition, conditionPath, conditionField, value)),
        }));
      });
      return this.valueEditor.snapshot();
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
      this.valueEditor = new MockValueEditor(document, synthetic, (value) => {
        const update = (rows: EditorRow[]): EditorRow[] => rows.map((row, index) => index === rowIndex
          ? { ...row, cells: row.cells.map((content, cellIndex) =>
            cellIndex === cell ? value : content) }
          : row);
        if (draftStep && this.actionForm) {
          this.actionStep = changeRows([this.actionStep!], step.id, parameter.name, update)[0]!;
          this.refreshActionForm();
          return;
        }

        this.change((current) => ({
          ...current,
          dirty: true,
          steps: changeRows(current.steps, step.id, parameter.name, update),
        }));
      });
      return this.valueEditor.snapshot();
    }

    this.valueEditor = new MockValueEditor(document, parameter, (value) => {
      if (draftStep && this.actionForm) {
        this.actionStep = setParameter([this.actionStep!], step.id, parameter.name, value)[0]!;
        this.refreshActionForm();
        return;
      }

      this.change((current) => ({
        ...current,
        dirty: true,
        steps: setParameter(current.steps, step.id, parameter.name, value),
      }));
    });
    return this.valueEditor.snapshot();
  }

  valueCommand(params: Record<string, unknown>): EditorValueSnapshot {
    if (!this.valueEditor) {
      throw new Error('没有打开的取值窗口。');
    }

    return this.valueEditor.command(this.text(params, 'action'), params);
  }

  activeSnapshot(): EditorSnapshot | null {
    return this.activeKey === null ? null : this.documents.get(this.activeKey) ?? null;
  }

  private nextNewKey(): string {
    let key = NEW_KEY;
    for (let index = 2; this.documents.has(key); index++) {
      key = NEW_KEY + ':' + String(index);
    }

    return key;
  }

  /** Applies one edit to the active document and stores the refreshed snapshot in its place. */
  change(change: (document: EditorSnapshot) => EditorSnapshot): EditorSnapshot | null {
    const key = this.activeKey;
    if (key === null) {
      return null;
    }

    const document = this.documents.get(key);
    if (!document) {
      return null;
    }

    const next = presentDocument(change(document));
    this.documents.set(key, next);
    return next;
  }

  patch(params: Record<string, unknown>): EditorSnapshot | null {
    const name = this.optionalText(params, 'name');
    const description = this.optionalText(params, 'description');
    const group = this.optionalText(params, 'group');
    return this.change((document) => ({
      ...document,
      name: name ?? document.name,
      description: description ?? document.description,
      group: group ?? document.group,
      dirty: true,
    }));
  }

  macroSetting(params: Record<string, unknown>): EditorSnapshot | null {
    const field = this.text(params, 'field');
    const value = this.text(params, 'value');
    return this.change((document) => {
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

  addStep(params: Record<string, unknown>): EditorSnapshot | null {
    const type = this.text(params, 'type');
    if (type.length === 0) {
      throw new Error('没有指定要添加的动作。');
    }

    const action = findAction(type);
    if (!action) {
      throw new Error('动作目录里没有 ' + type + '。');
    }

    const parentId = this.text(params, 'parentId');
    const list = this.text(params, 'list');
    const presets = this.presets(params['parameters']);
    return this.change((document) => ({
      ...document,
      dirty: true,
      canUndo: true,
      canRedo: false,
      steps: addStepToTree(
        document.steps,
        parentId.length > 0 ? parentId : undefined,
        list.length > 0 ? list : undefined,
        action,
        this.nextStepId(),
        presets,
      ),
    }));
  }

  private requestedSteps(params: Record<string, unknown>): string[] {
    const value = params['steps'];
    return Array.isArray(value)
      ? value.filter((item): item is string => typeof item === 'string' && item.length > 0)
      : [];
  }

  stepsEnabled(params: Record<string, unknown>): EditorSnapshot | null {
    const ids = this.requestedSteps(params);
    const enabled = params['enabled'] === true;
    return this.change((document) => {
      let steps = document.steps;
      for (const id of ids) {
        steps = setStepEnabled(steps, id, enabled);
      }
      return { ...document, dirty: true, steps };
    });
  }

  stepsRemove(params: Record<string, unknown>): EditorSnapshot | null {
    const ids = this.requestedSteps(params);
    return this.change((document) => {
      let steps = document.steps;
      for (const id of ids) {
        steps = removeStepFromTree(steps, id).steps;
      }
      return { ...document, dirty: true, steps };
    });
  }

  stepsDuplicate(params: Record<string, unknown>): EditorSnapshot | null {
    const ids = this.requestedSteps(params);
    return this.change((document) => {
      let steps = document.steps;
      for (const id of ids) {
        steps = duplicateStepInTree(steps, id, () => this.nextStepId()).steps;
      }
      return { ...document, dirty: true, canUndo: true, canRedo: false, steps };
    });
  }

  stepsMoveBy(params: Record<string, unknown>): EditorSnapshot | null {
    const ids = this.requestedSteps(params);
    const delta = Number(params['delta'] ?? 0);
    return this.change((document) => {
      let steps = document.steps;
      for (const id of ids) {
        steps = moveStepInTree(steps, id, delta).steps;
      }
      return { ...document, dirty: true, steps };
    });
  }

  stepsCopy(params: Record<string, unknown>): string {
    const ids = this.requestedSteps(params);
    const document = this.activeSnapshot();
    const steps = document
      ? ids
        .map((id) => locateStep(document.steps, id))
        .filter((found): found is NonNullable<typeof found> => found !== null)
        .map((found) => found.list[found.index]!)
      : [];
    this.stepClipboard = JSON.stringify(steps);
    return this.stepClipboard;
  }

  stepsCut(params: Record<string, unknown>): string {
    const text = this.stepsCopy(params);
    this.stepsRemove(params);
    return text;
  }

  stepsPaste(params: Record<string, unknown>): EditorSnapshot | null {
    const payload = typeof params['text'] === 'string' && params['text'].length > 0
      ? params['text'] as string
      : this.stepClipboard;
    let pasted: EditorStep[] = [];
    try {
      const parsed: unknown = JSON.parse(payload);
      if (Array.isArray(parsed)) {
        pasted = parsed.filter((item): item is EditorStep =>
          typeof item === 'object' && item !== null && typeof (item as EditorStep).id === 'string');
      }
    } catch {
      pasted = [];
    }

    if (pasted.length === 0) {
      return this.activeSnapshot();
    }

    const copies = pasted.map((step) => this.cloneStep(step));
    const after = typeof params['after'] === 'string' ? params['after'] as string : '';
    return this.change((document) => {
      const found = after.length > 0 ? locateStep(document.steps, after) : null;
      const list = found?.list ?? document.steps;
      const index = found ? found.index + 1 : list.length;
      list.splice(index, 0, ...copies);
      return { ...document, dirty: true, steps: document.steps };
    });
  }

  stepsGroup(params: Record<string, unknown>): EditorSnapshot | null {
    const ids = this.requestedSteps(params);
    const document = this.activeSnapshot();
    if (!document) {
      return null;
    }

    const locations = ids
      .map((id) => locateStep(document.steps, id))
      .filter((found): found is NonNullable<typeof found> => found !== null)
      .sort((left, right) => left.index - right.index);
    if (locations.length === 0) {
      return document;
    }

    const first = locations[0]!;
    const selected = locations.map((found) => found.list[found.index]!);
    for (const found of [...locations].reverse()) {
      found.list.splice(found.index, 1);
    }

    const group = structuredClone(first.list[first.index] ?? selected[0]!);
    group.id = this.nextStepId();
    group.type = 'control.sequence';
    group.name = '分组';
    group.detail = '按顺序执行';
    group.enabled = true;
    group.acceptsSteps = true;
    group.parameters = [];
    group.sections = [{ name: 'steps', label: '步骤', steps: selected }];
    first.list.splice(first.index, 0, group);
    return this.change((documentNow) => ({ ...documentNow, dirty: true, steps: documentNow.steps }));
  }

  stepsLift(): EditorSnapshot | null {
    const document = this.activeSnapshot();
    if (!document) {
      return null;
    }

    return document;
  }

  stepsMoveTo(params: Record<string, unknown>): EditorSnapshot | null {
    const ids = this.requestedSteps(params);
    const target = typeof params['target'] === 'string' ? params['target'] as string : '';
    const before = params['before'] === true;
    const document = this.activeSnapshot();
    if (!document || target.length === 0) {
      return document;
    }

    const targetLocation = locateStep(document.steps, target);
    if (!targetLocation) {
      return document;
    }

    const selected = ids
      .map((id) => locateStep(document.steps, id))
      .filter((found): found is NonNullable<typeof found> => found !== null)
      .sort((left, right) => left.index - right.index);
    if (selected.length === 0) {
      return document;
    }

    const moved = selected.map((found) => found.list[found.index]!);
    for (const found of [...selected].reverse()) {
      found.list.splice(found.index, 1);
    }

    const targetIndex = targetLocation.list.findIndex((step) => step.id === target);
    const at = targetIndex + (before ? 0 : 1);
    targetLocation.list.splice(Math.max(0, at), 0, ...moved);
    return this.change((documentNow) => ({ ...documentNow, dirty: true, steps: documentNow.steps }));
  }

  private cloneStep(step: EditorStep): EditorStep {
    const copy = structuredClone(step);
    const renumber = (current: EditorStep): void => {
      current.id = this.nextStepId();
      for (const section of current.sections) {
        for (const child of section.steps) {
          renumber(child);
        }
      }
    };
    renumber(copy);
    return copy;
  }

  setStepEnabledCommand(params: Record<string, unknown>): EditorSnapshot | null {
    const stepId = this.text(params, 'stepId');
    const enabled = params['enabled'] === true;
    return this.change((document) => ({
      ...document,
      dirty: true,
      steps: setStepEnabled(document.steps, stepId, enabled),
    }));
  }

  setStepMeta(params: Record<string, unknown>): EditorSnapshot | null {
    const stepId = this.text(params, 'stepId');
    const field = this.text(params, 'field');
    const value = this.text(params, 'value');
    if (this.actionForm && stepId === this.actionForm.stepId) {
      this.actionStep = changeStep([this.actionStep!], stepId, (step) => ({
        ...step,
        comment: field === 'comment' ? value : step.comment,
        timeoutMs: field === 'timeoutMs' ? Number(value) : step.timeoutMs,
        retryCount: field === 'retryCount' ? Number(value) : step.retryCount,
        retryDelayMs: field === 'retryDelayMs' ? Number(value) : step.retryDelayMs,
        onError: field === 'onError' ? value : step.onError,
      }))[0]!;
      this.refreshActionForm();
      return this.activeSnapshot();
    }

    return this.change((document) => ({
      ...document,
      dirty: true,
      steps: changeStep(document.steps, stepId, (step) => ({
        ...step,
        comment: field === 'comment' ? value : step.comment,
        timeoutMs: field === 'timeoutMs' ? this.number(value, step.timeoutMs) : step.timeoutMs,
        retryCount: field === 'retryCount' ? this.number(value, step.retryCount) : step.retryCount,
        retryDelayMs: field === 'retryDelayMs'
          ? this.number(value, step.retryDelayMs)
          : step.retryDelayMs,
        onError: field === 'onError' ? value : step.onError,
      })),
    }));
  }

  duplicateStep(params: Record<string, unknown>): EditorSnapshot | null {
    const stepId = this.text(params, 'stepId');
    return this.change((document) => {
      const steps = structuredClone(document.steps);
      const result = duplicateStepInTree(steps, stepId, () => this.nextStepId());
      return result.changed
        ? { ...document, dirty: true, canUndo: true, canRedo: false, steps: result.steps }
        : document;
    });
  }

  moveStep(params: Record<string, unknown>): EditorSnapshot | null {
    const stepId = this.text(params, 'stepId');
    const delta = this.number(this.text(params, 'delta'), 0);
    return this.change((document) => {
      const steps = structuredClone(document.steps);
      const result = moveStepInTree(steps, stepId, delta);
      return result.changed ? { ...document, dirty: true, steps: result.steps } : document;
    });
  }

  removeStep(params: Record<string, unknown>): EditorSnapshot | null {
    const stepId = this.text(params, 'stepId');
    return this.change((document) => {
      const steps = structuredClone(document.steps);
      const result = removeStepFromTree(steps, stepId);
      return result.changed ? { ...document, dirty: true, steps: result.steps } : document;
    });
  }

  conditionCommand(
    params: Record<string, unknown>,
    change: (condition: EditorCondition, path: number[]) => EditorCondition,
  ): EditorSnapshot | null {
    const stepId = this.text(params, 'stepId');
    const name = this.text(params, 'name');
    const path = conditionPath(params);
    if (this.actionForm && stepId === this.actionForm.stepId) {
      this.actionStep = changeCondition([this.actionStep!], stepId, name,
        (condition) => change(condition, path))[0]!;
      this.refreshActionForm();
      return this.activeSnapshot();
    }

    return this.change((document) => ({
      ...document,
      dirty: true,
      steps: changeCondition(document.steps, stepId, name, (condition) => change(condition, path)),
    }));
  }

  setParameterCommand(params: Record<string, unknown>): EditorSnapshot | null {
    const stepId = this.text(params, 'stepId');
    const name = this.text(params, 'name');
    const value = this.text(params, 'value');
    if (this.actionForm && stepId === this.actionForm.stepId) {
      this.actionStep = setParameter([this.actionStep!], stepId, name, value)[0]!;
      this.refreshActionForm();
      return this.activeSnapshot();
    }

    return this.change((document) => ({
      ...document,
      dirty: true,
      steps: setParameter(document.steps, stepId, name, value),
    }));
  }

  rowCommand(
    params: Record<string, unknown>,
    change: (rows: EditorRow[]) => EditorRow[],
  ): EditorSnapshot | null {
    const stepId = this.text(params, 'stepId');
    const name = this.text(params, 'name');
    if (this.actionForm && stepId === this.actionForm.stepId) {
      this.actionStep = changeRows([this.actionStep!], stepId, name, change)[0]!;
      this.refreshActionForm();
      return this.activeSnapshot();
    }

    return this.change((document) => ({
      ...document,
      dirty: true,
      steps: changeRows(document.steps, stepId, name, change),
    }));
  }

  addTrigger(params: Record<string, unknown>): EditorSnapshot | null {
    const kind = this.text(params, 'kind').length > 0 ? this.text(params, 'kind') : 'Key';
    return this.change((document) => ({
      ...document,
      dirty: true,
      triggers: [...document.triggers, newTrigger(this.nextTriggerId(), kind)],
    }));
  }

  removeTrigger(params: Record<string, unknown>): EditorSnapshot | null {
    const triggerId = this.text(params, 'triggerId');
    return this.change((document) => ({
      ...document,
      dirty: true,
      triggers: document.triggers.filter((trigger) => trigger.id !== triggerId),
    }));
  }

  patchTrigger(
    params: Record<string, unknown>,
    update: (trigger: EditorTrigger) => EditorTrigger,
  ): EditorSnapshot | null {
    const triggerId = this.text(params, 'triggerId');
    return this.change((document) => changeTrigger(document, triggerId, update));
  }

  stopRecording(): {
    captured: number;
    steps: number;
    snapshot: EditorSnapshot | null;
  } {
    const wasRecording = this.recording;
    const captured = this.captured;
    this.recording = false;
    this.captured = 0;
    let steps = 0;
    const snapshot = wasRecording
      ? this.change((document) => {
          const action = findAction('control.delay');
          const delays: EditorStep[] = action
            ? [stepFromAction(action, this.nextStepId()), stepFromAction(action, this.nextStepId())]
            : [];
          steps = delays.length;
          return { ...document, dirty: true, steps: [...document.steps, ...delays] };
        })
      : this.activeSnapshot();
    return { captured, steps, snapshot };
  }

  save(): EditorSnapshot | null {
    return this.change((document) => {
      if (document.name.trim().length === 0) {
        throw new Error('宏需要一个名字。');
      }

      return {
        ...document,
        name: document.name.trim(),
        dirty: false,
      };
    });
  }

  private nextStepId(): string {
    this.stepSequence += 1;
    return 'mock-' + String(this.stepSequence);
  }

  private nextTriggerId(): string {
    this.triggerSequence += 1;
    return 'trigger-' + String(this.triggerSequence);
  }

  text(params: Record<string, unknown>, name: string): string {
    const value = params[name];
    return value === undefined || value === null ? '' : String(value);
  }

  optionalText(params: Record<string, unknown>, name: string): string | undefined {
    const value = params[name];
    if (value === undefined) {
      return undefined;
    }

    return value === null ? '' : String(value);
  }

  private number(value: string, fallback: number): number {
    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : fallback;
  }

  private presets(value: unknown): Record<string, string> {
    if (value === null || typeof value !== 'object' || Array.isArray(value)) {
      return {};
    }

    const presets: Record<string, string> = {};
    for (const [name, entry] of Object.entries(value as Record<string, unknown>)) {
      presets[name] = typeof entry === 'string' ? entry : JSON.stringify(entry) ?? '';
    }

    return presets;
  }
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

