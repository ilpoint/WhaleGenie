import type { EditorSnapshot, EditorTrigger } from './protocol.js';
import { presentDocument } from './mock-editor-steps.js';
import raw from './data/editor-fixtures.json';

/**
 * 编辑器 mock 的固定样例：触发器默认值、取值墙的起始条目与空白文档的骨架都在
 * data/editor-fixtures.json；这里只负责按种类拼出它们，并与页面的命令对接。
 */

type TriggerDefaults = Omit<EditorTrigger, 'id' | 'kind' | 'kindLabel' | 'hotstringEndKeys'>;
type DocumentBase = Omit<EditorSnapshot, 'name' | 'valueChoices' | 'valueFunctions'
  | 'conditionCompares' | 'variableTypes' | 'triggerKinds'>;

interface EditorFixtures {
  triggerKinds: Record<string, string>;
  triggerDefaults: TriggerDefaults;
  hotstringEndKeys: EditorTrigger['hotstringEndKeys'];
  valueChoices: EditorSnapshot['valueChoices'];
  valueFunctions: EditorSnapshot['valueFunctions'];
  conditionCompares: EditorSnapshot['conditionCompares'];
  variableTypes: EditorSnapshot['variableTypes'];
  document: DocumentBase;
}

const DATA = raw as unknown as EditorFixtures;

function text(params: Record<string, unknown>, name: string): string {
  const value = params[name];
  return value === undefined || value === null ? '' : String(value);
}

export function newTrigger(id: string, kind: string): EditorTrigger {
  return {
    id,
    kind,
    kindLabel: DATA.triggerKinds[kind] ?? kind,
    ...structuredClone(DATA.triggerDefaults),
    hotstringEndKeys: kind === 'Hotstring' ? structuredClone(DATA.hotstringEndKeys) : [],
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
  return structuredClone(DATA.valueChoices);
}

export function defaultValueFunctions(): EditorSnapshot['valueFunctions'] {
  return structuredClone(DATA.valueFunctions);
}

export function defaultConditionCompares(): EditorSnapshot['conditionCompares'] {
  return structuredClone(DATA.conditionCompares);
}

export function defaultVariableTypes(): EditorSnapshot['variableTypes'] {
  return structuredClone(DATA.variableTypes);
}

export function defaultTriggerKinds(): EditorSnapshot['triggerKinds'] {
  return Object.entries(DATA.triggerKinds).map(([value, label]) => ({ value, label }));
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
    ...structuredClone(DATA.document),
    name,
    valueChoices: defaultValueChoices(),
    valueFunctions: defaultValueFunctions(),
    conditionCompares: defaultConditionCompares(),
    variableTypes: defaultVariableTypes(),
    triggerKinds: defaultTriggerKinds(),
    ...overrides,
  });
}
