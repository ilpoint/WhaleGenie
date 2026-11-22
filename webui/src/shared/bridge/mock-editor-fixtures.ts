import type { EditorSnapshot, EditorTrigger } from './protocol.js';
import { presentDocument } from './mock-editor-steps.js';

/**
 * The mock editor's fixed data: triggers and the value wall's first entries. They are fixtures
 * rather than edits, so they stay out of the step tree and the command dispatch.
 */

function text(params: Record<string, unknown>, name: string): string {
  const value = params[name];
  return value === undefined || value === null ? '' : String(value);
}

const TRIGGER_KIND_LABELS: Record<string, string> = {
  Key: '按键',
  Timer: '定时',
  Color: '颜色',
  File: '文件',
  Process: '程序',
  Hotstring: '字串',
  Idle: '空闲',
  Window: '窗口',
  Interface: '窗口内接口',
};

export function newTrigger(id: string, kind: string): EditorTrigger {
  return {
    id,
    kind,
    kindLabel: TRIGGER_KIND_LABELS[kind] ?? kind,
    summary: '',
    enabled: true,
    binding: '',
    loop: 'Press',
    once: false,
    timerMode: 'Interval',
    time: '08:00',
    interval: '5',
    unit: 'Seconds',
    filePath: '',
    fileChange: 'Any',
    fileFilter: '*',
    fileSubfolders: false,
    processName: '',
    processChange: 'Started',
    hotstringText: '',
    colorMatch: 'ColorMatches',
    colorHex: '#000000',
    colorTolerance: '5',
    colorX: '0',
    colorY: '0',
    followCursor: false,
    idleSeconds: '60',
    windowTitle: '',
    windowProcess: '',
    windowClass: '',
    windowHow: 'TitleContains',
    windowChange: 'Appeared',
    interfaceTitle: '',
    interfaceProcess: '',
    interfaceHow: 'TitleContains',
    windowCaseSensitive: false,
    windowTrim: true,
    windowMultiline: false,
    hotstringEndChars: ' -()[]{}:;,.?!',
    hotstringCaseSensitive: false,
    hotstringInsideWord: false,
    hotstringRequireEnd: true,
    hotstringEraseTyped: true,
    hotstringKeepEnd: true,
    hotstringMaximumLength: 40,
    hotstringEndKeys: kind === 'Hotstring'
      ? [
        { value: ' ', label: 'Space', checked: true, isCustom: false },
        { value: '\n', label: 'Enter', checked: true, isCustom: false },
        { value: ';', label: ';', checked: true, isCustom: false },
        { value: '.', label: '.', checked: true, isCustom: false },
      ]
      : [],
    hotstringEndCaptureProblem: '',
    interfaceHotkey: '',
    interfaceHotkeyProblem: '',
    interfaceGroupMembers: [],
  };
}

export function changeTrigger(
  document: EditorSnapshot,
  triggerId: string,
  change: (trigger: EditorTrigger) => EditorTrigger,
): EditorSnapshot {
  return {
    ...document,
    dirty: true,
    triggers: document.triggers.map((trigger) => trigger.id === triggerId
      ? change(trigger)
      : trigger),
  };
}

export function defaultValueChoices(): EditorSnapshot['valueChoices'] {
  return [
    {
      name: 'found', type: 'text', origin: 'Local', source: 'find.text', description: '',
      group: 'Local', display: 'found', persistent: false,
    },
    {
      name: 'count', type: 'number', origin: 'Global', source: 'global.count', description: '',
      group: 'Global', display: 'count', persistent: true,
    },
    {
      name: 'date', type: 'text', origin: 'System', source: 'sys.date', description: '',
      group: 'System', display: '日期', persistent: false,
    },
    {
      name: 'last', type: 'text', origin: 'StepResult', source: 'step.last', description: '',
      group: 'StepResult', display: '上一步状态', persistent: false,
    },
  ];
}

export function defaultValueFunctions(): EditorSnapshot['valueFunctions'] {
  return [
    { name: 'upper', signature: 'upper(text)', description: 'Capitals', group: 'Text' },
    { name: 'length', signature: 'length(value)', description: 'How many', group: 'Text' },
  ];
}

export function defaultConditionCompares(): EditorSnapshot['conditionCompares'] {
  return [
    { value: 'Equal', label: '=' },
    { value: 'NotEqual', label: '≠' },
    { value: 'Greater', label: '>' },
    { value: 'Less', label: '<' },
    { value: 'Holds', label: 'holds' },
  ];
}

export function defaultVariableTypes(): EditorSnapshot['variableTypes'] {
  return [
    { value: 'text', label: '文本' },
    { value: 'number', label: '数字' },
    { value: 'bool', label: '布尔' },
    { value: 'record', label: '记录' },
  ];
}

export function defaultTriggerKinds(): EditorSnapshot['triggerKinds'] {
  return Object.entries(TRIGGER_KIND_LABELS).map(([value, label]) => ({ value, label }));
}

export function timerTrigger(
  trigger: EditorTrigger,
  params: Record<string, unknown>,
): EditorTrigger {
  const field = text(params, 'field');
  const value = text(params, 'value');
  return {
    ...trigger,
    timerMode: field === 'mode' ? value : trigger.timerMode,
    time: field === 'time' ? value : trigger.time,
    interval: field === 'interval' ? value : trigger.interval,
    unit: field === 'unit' ? value : trigger.unit,
  };
}

export function fileTrigger(
  trigger: EditorTrigger,
  params: Record<string, unknown>,
): EditorTrigger {
  const field = text(params, 'field');
  const value = text(params, 'value');
  return {
    ...trigger,
    filePath: field === 'path' ? value : trigger.filePath,
    fileChange: field === 'change' ? value : trigger.fileChange,
    fileFilter: field === 'filter' ? value : trigger.fileFilter,
    fileSubfolders: field === 'subfolders' ? value === 'true' : trigger.fileSubfolders,
  };
}

export function processTrigger(
  trigger: EditorTrigger,
  params: Record<string, unknown>,
): EditorTrigger {
  const field = text(params, 'field');
  const value = text(params, 'value');
  return {
    ...trigger,
    processName: field === 'name' ? value : trigger.processName,
    processChange: field === 'change' ? value : trigger.processChange,
  };
}

export function colorTrigger(
  trigger: EditorTrigger,
  params: Record<string, unknown>,
): EditorTrigger {
  const field = text(params, 'field');
  const value = text(params, 'value');
  return {
    ...trigger,
    colorMatch: field === 'match' ? value : trigger.colorMatch,
    colorHex: field === 'hex' ? value : trigger.colorHex,
    colorTolerance: field === 'tolerance' ? value : trigger.colorTolerance,
    colorX: field === 'x' ? value : trigger.colorX,
    colorY: field === 'y' ? value : trigger.colorY,
    followCursor: field === 'follow' ? value === 'true' : trigger.followCursor,
  };
}

export function windowTrigger(
  trigger: EditorTrigger,
  params: Record<string, unknown>,
): EditorTrigger {
  const field = text(params, 'field');
  const value = text(params, 'value');
  return {
    ...trigger,
    windowTitle: field === 'title' ? value : trigger.windowTitle,
    windowProcess: field === 'process' ? value : trigger.windowProcess,
    windowClass: field === 'class' ? value : trigger.windowClass,
    windowHow: field === 'how' ? value : trigger.windowHow,
    windowChange: field === 'change' ? value : trigger.windowChange,
  };
}

export function interfaceTrigger(
  trigger: EditorTrigger,
  params: Record<string, unknown>,
): EditorTrigger {
  const field = text(params, 'field');
  const value = text(params, 'value');
  return {
    ...trigger,
    interfaceTitle: field === 'title' ? value : trigger.interfaceTitle,
    interfaceProcess: field === 'process' ? value : trigger.interfaceProcess,
    interfaceHow: field === 'how' ? value : trigger.interfaceHow,
  };
}

export function createDocumentSnapshot(
  name: string,
  overrides: Partial<EditorSnapshot>,
): EditorSnapshot {
  return presentDocument({
    name,
    description: '',
    group: '',
    subMacros: [],
    callableParameters: [],
    callableResults: [],
    followInputLimit: true,
    inputLimit: 20,
    toggleKey: '',
    dirty: false,
    stepCount: 0,
    steps: [],
    triggers: [],
    variables: [],
    globals: [],
    valueChoices: defaultValueChoices(),
    valueFunctions: defaultValueFunctions(),
    conditionCompares: defaultConditionCompares(),
    variableTypes: defaultVariableTypes(),
    triggerKinds: defaultTriggerKinds(),
    canUndo: false,
    canRedo: false,
    canSave: true,
    nameProblem: null,
    problemSummary: '',
    declarationProblemSummary: '',
    callImpactSummary: '',
    notice: '',
    triggerProblemSummary: '',
    toggleKeyProblem: '',
    recording: {
      positionIndex: 0, positionOptions: ['当前', '差值'], mouseMovement: true, mouseButtons: true,
      keyboardKeys: true, gamepadButtons: true, delays: true, ignoreDelaysBelowMs: 50,
      ignoreInjected: true, delayScale: 1, delayScaleDisplay: '×1', hasDelayScale: false,
    },
    isSubMacro: false,
    tabs: [],
    activeKey: null,
    ...overrides,
  });
}
