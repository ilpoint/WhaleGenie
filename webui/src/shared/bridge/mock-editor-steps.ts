import type {
  EditorCondition,
  EditorConditionNode,
  EditorSnapshot,
  EditorStep,
} from './protocol.js';
import type { MockAction } from './mock-editor-actions.js';

/**
 * The mock editor's step tree: the actions it is built from live in mock-editor-actions.ts, while
 * this file owns the shape the page reads back and the structural edits the commands apply.
 */

function emptyCondition(): EditorCondition {
  return { join: 'All', nodes: [], editable: true };
}

export function stepFromAction(action: MockAction, id: string): EditorStep {
  return {
    id,
    type: action.key,
    name: action.name,
    enabled: true,
    comment: '',
    timeoutMs: 0,
    retryCount: 0,
    retryDelayMs: 0,
    onError: 'Stop',
    acceptsSteps: (action.sections?.length ?? 0) > 0,
    detail: '',
    problem: '',
    inputMark: '',
    metaSummary: '',
    parameters: action.parameters.map((entry) => {
      const value = entry.initial ?? '';
      const valueForm = entry.kind === 'Number'
        ? 'Value'
        : entry.kind === 'Text' ? 'Run' : 'Value';
      return {
        name: entry.name,
        label: entry.label,
        kind: entry.kind,
        value,
        options: entry.options ?? [],
        columns: entry.columns ?? [],
        rows: [],
        condition: entry.kind === 'Condition' ? emptyCondition() : null,
        display: value,
        valueForm,
        offersValue: !['Select', 'Rows', 'Condition', 'Key'].includes(entry.kind),
        takesFormula: entry.kind === 'Number' || valueForm === 'Value',
        showsNameMark: false,
        isNumber: entry.kind === 'Number',
        isNumberFormula: false,
        isMultiline: false,
        isColor: entry.kind === 'Color',
        isPath: false,
        isKey: entry.kind === 'Key',
        placeholder: entry.placeholder ?? '',
        required: false,
        parts: value.length > 0
          ? [{ kind: 'text', text: value, name: '' }]
          : [],
        advanced: false,
        group: '',
        groupLabel: '',
        isReturns: entry.kind === 'Returns',
      };
    }),
    sections: (action.sections ?? []).map((section) => ({
      name: section.name,
      label: section.label,
      steps: [],
    })),
  };
}

function parameterValue(step: EditorStep, name: string): string {
  return step.parameters.find((parameter) => parameter.name === name)?.value ?? '';
}

function conditionOf(step: EditorStep): EditorCondition | null {
  return step.parameters.find((parameter) => parameter.kind === 'Condition')?.condition ?? null;
}

const COMPARE_LABELS: Record<string, string> = {
  Equal: '=', NotEqual: '≠', Greater: '>', Less: '<', Holds: '包含',
};

function compareLabel(compare: string): string {
  return COMPARE_LABELS[compare] ?? compare;
}

function nodeIsFilled(node: EditorConditionNode): boolean {
  if (node.kind === 'group') {
    return node.children.some((child) => nodeIsFilled(child));
  }

  return node.left.trim().length > 0 || node.right.trim().length > 0;
}

/** A condition counts once one of its rows names a side; an added blank row still waits. */
export function conditionFilled(condition: EditorCondition | null): boolean {
  return condition !== null && condition.nodes.some((node) => nodeIsFilled(node));
}

function summarizeNode(node: EditorConditionNode): string {
  if (node.kind === 'group') {
    const parts = node.children.map((child) => summarizeNode(child)).filter((part) => part.length > 0);
    if (parts.length === 0) {
      return '';
    }

    const joiner = node.join === 'Any' ? ' 或 ' : ' 且 ';
    return parts.length === 1 ? parts[0]! : '(' + parts.join(joiner) + ')';
  }

  const left = node.left.trim();
  const right = node.right.trim();
  if (left.length === 0 && right.length === 0) {
    return '';
  }

  return (left.length > 0 ? left : '…') + ' ' + compareLabel(node.compare)
    + ' ' + (right.length > 0 ? right : '…');
}

export function conditionSummary(condition: EditorCondition | null): string {
  if (condition === null) {
    return '';
  }

  const parts = condition.nodes.map((node) => summarizeNode(node)).filter((part) => part.length > 0);
  if (parts.length === 0) {
    return '';
  }

  const joiner = condition.join === 'Any' ? ' 或 ' : ' 且 ';
  return parts.length === 1 ? parts[0]! : '(' + parts.join(joiner) + ')';
}

function stepDetail(step: EditorStep): string {
  switch (step.type) {
    case 'control.delay': {
      const ms = parameterValue(step, 'ms');
      return ms.length > 0 ? '等待 ' + ms + ' 毫秒' : '等待';
    }
    case 'input.keyPress': {
      const key = parameterValue(step, 'key');
      return key.length > 0 ? '按 ' + key : '按键';
    }
    case 'input.mouseClick': {
      const button = parameterValue(step, 'button');
      return button.length > 0 ? '点击 ' + button : '点击';
    }
    case 'input.text': {
      const text = parameterValue(step, 'text');
      return text.length > 0 ? '输入 ' + text : '输入文字';
    }
    case 'screen.color': {
      const hex = parameterValue(step, 'hex');
      return hex.length > 0 ? '取色 ' + hex : '取色';
    }
    case 'image.find': {
      const path = parameterValue(step, 'path');
      return path.length > 0 ? '找图 ' + path : '找图';
    }
    case 'control.waitUntil': {
      const summary = conditionSummary(conditionOf(step));
      return summary.length > 0 ? '等待 ' + summary : '等待条件';
    }
    case 'control.runMacro': {
      const macro = parameterValue(step, 'macro');
      return macro.length > 0 ? '调用 ' + macro : '调用宏';
    }
    case 'control.if': {
      const summary = conditionSummary(conditionOf(step));
      return summary.length > 0 ? '如果 ' + summary : '如果';
    }
    case 'control.repeat': {
      const times = parameterValue(step, 'times');
      return times.length > 0 ? '重复 ' + times + ' 次' : '重复';
    }
    case 'control.forEach': {
      const items = parameterValue(step, 'items');
      return items.length > 0 ? '遍历 ' + items : '遍历';
    }
    case 'control.for': {
      const from = parameterValue(step, 'from');
      const to = parameterValue(step, 'to');
      return from.length > 0 || to.length > 0 ? '从 ' + from + ' 到 ' + to : '计数循环';
    }
    case 'control.while': {
      const summary = conditionSummary(conditionOf(step));
      return summary.length > 0 ? '当 ' + summary : '当条件';
    }
    case 'control.switch': {
      const value = parameterValue(step, 'value');
      return value.length > 0 ? '按 ' + value + ' 分支' : '多分支';
    }
    case 'control.case': {
      const values = parameterValue(step, 'values');
      return values.length > 0 ? '分支 ' + values : '分支';
    }
    case 'control.note':
    case 'control.log': {
      const text = parameterValue(step, 'text');
      return text.length > 0 ? text : step.name;
    }
    default:
      return step.name;
  }
}

function stepProblem(step: EditorStep): string {
  if ((step.type === 'control.if' || step.type === 'control.waitUntil')
    && !conditionFilled(conditionOf(step))) {
    return '这一步没有填写条件；请先选一个条件再运行宏。';
  }

  return '';
}

function stepInputMark(step: EditorStep): string {
  if (!step.type.startsWith('input.')) {
    return '';
  }

  const mode = parameterValue(step, 'mode');
  if (mode === 'background') {
    return '后台消息';
  }

  if (mode === 'driver') {
    return '驱动级';
  }

  return mode === 'foreground' ? '前台' : '';
}

function stepMetaSummary(step: EditorStep): string {
  const parts: string[] = [];
  if (step.timeoutMs > 0) {
    parts.push('超时 ' + String(step.timeoutMs) + ' 毫秒');
  }

  if (step.retryCount > 0) {
    parts.push(step.retryDelayMs > 0
      ? '重试 ' + String(step.retryCount) + ' 次，间隔 ' + String(step.retryDelayMs) + ' 毫秒'
      : '重试 ' + String(step.retryCount) + ' 次');
  }

  if (step.onError.length > 0 && step.onError !== 'Stop') {
    parts.push(step.onError === 'Continue'
      ? '失败后继续'
      : step.onError === 'NextIteration'
        ? '失败后进入下一轮'
        : step.onError === 'AskUser'
          ? '失败后询问'
          : step.onError);
  }

  return parts.join('，');
}

/** Recomputes everything the page reads but does not store: summaries, marks and counts. */
export function presentDocument(document: EditorSnapshot): EditorSnapshot {
  const steps = presentSteps(document.steps);
  const nameProblem = document.name.trim().length === 0 ? '宏需要一个名字。' : null;
  const problemSteps = countProblemSteps(steps);
  return {
    ...document,
    steps,
    stepCount: countSteps(steps),
    nameProblem,
    canSave: nameProblem === null,
    problemSummary: problemSteps > 0
      ? '有 ' + String(problemSteps) + ' 个步骤被编辑器标了出来。'
      : '',
  };
}

function presentSteps(steps: EditorStep[]): EditorStep[] {
  return steps.map((step) => {
    const sections = step.sections.map((section) => ({
      ...section,
      steps: presentSteps(section.steps),
    }));
    return {
      ...step,
      sections,
      acceptsSteps: step.sections.length > 0,
      detail: stepDetail(step),
      problem: stepProblem(step),
      inputMark: stepInputMark(step),
      metaSummary: stepMetaSummary(step),
    };
  });
}

export function countSteps(steps: readonly EditorStep[]): number {
  let total = 0;
  for (const step of steps) {
    total += 1;
    for (const section of step.sections) {
      total += countSteps(section.steps);
    }
  }

  return total;
}

function countProblemSteps(steps: readonly EditorStep[]): number {
  let total = 0;
  for (const step of steps) {
    if (step.problem.length > 0) {
      total += 1;
    }

    for (const section of step.sections) {
      total += countProblemSteps(section.steps);
    }
  }

  return total;
}

export function addStepToTree(
  steps: EditorStep[],
  parentId: string | undefined,
  listName: string | undefined,
  action: MockAction,
  id: string,
  presets: Record<string, string>,
): EditorStep[] {
  const step = stepFromAction(action, id);
  applyPresets(step, presets);
  if (parentId === undefined || parentId.length === 0) {
    if (listName !== undefined && listName.length > 0) {
      throw new Error('根步骤没有名为 ' + listName + ' 的子列表。');
    }

    return [...steps, step];
  }

  const result = appendChild(steps, parentId, listName, step);
  if (!result.inserted) {
    throw new Error('没有找到步骤 ' + parentId + '。');
  }

  return result.steps;
}

function applyPresets(step: EditorStep, presets: Record<string, string>): void {
  for (const [name, value] of Object.entries(presets)) {
    const parameter = step.parameters.find((candidate) => candidate.name === name);
    if (!parameter) {
      throw new Error('动作 ' + step.type + ' 没有参数 ' + name + '。');
    }

    parameter.value = value;
  }
}

function appendChild(
  steps: EditorStep[],
  parentId: string,
  listName: string | undefined,
  child: EditorStep,
): { steps: EditorStep[]; inserted: boolean } {
  for (let index = 0; index < steps.length; index++) {
    const step = steps[index]!;
    if (step.id === parentId) {
      const section = listName !== undefined && listName.length > 0
        ? step.sections.find((candidate) => candidate.name === listName)
        : step.sections[0];
      if (!section) {
        throw new Error(listName !== undefined && listName.length > 0
          ? '步骤 ' + parentId + ' 没有名为 ' + listName + ' 的子列表。'
          : '步骤 ' + parentId + ' 不接受子步骤。');
      }

      const sections = step.sections.map((candidate) => candidate.name === section.name
        ? { ...candidate, steps: [...candidate.steps, child] }
        : candidate);
      return {
        steps: steps.map((candidate, at) => at === index ? { ...step, sections } : candidate),
        inserted: true,
      };
    }

    for (const section of step.sections) {
      const nested = appendChild(section.steps, parentId, listName, child);
      if (nested.inserted) {
        const sections = step.sections.map((candidate) => candidate.name === section.name
          ? { ...candidate, steps: nested.steps }
          : candidate);
        return {
          steps: steps.map((candidate, at) => at === index ? { ...step, sections } : candidate),
          inserted: true,
        };
      }
    }
  }

  return { steps, inserted: false };
}

export interface StepLocation {
  list: EditorStep[];
  index: number;
}

export function locateStep(steps: EditorStep[], stepId: string): StepLocation | null {
  for (let index = 0; index < steps.length; index++) {
    const step = steps[index]!;
    if (step.id === stepId) {
      return { list: steps, index };
    }

    for (const section of step.sections) {
      const found = locateStep(section.steps, stepId);
      if (found) {
        return found;
      }
    }
  }

  return null;
}

export function duplicateStepInTree(
  steps: EditorStep[],
  stepId: string,
  nextId: () => string,
): { steps: EditorStep[]; changed: boolean } {
  const found = locateStep(steps, stepId);
  if (!found) {
    return { steps, changed: false };
  }

  const copy = structuredClone(found.list[found.index]!);
  renumberStep(copy, nextId);
  found.list.splice(found.index + 1, 0, copy);
  return { steps, changed: true };
}

function renumberStep(step: EditorStep, nextId: () => string): void {
  step.id = nextId();
  for (const section of step.sections) {
    for (const child of section.steps) {
      renumberStep(child, nextId);
    }
  }
}

export function moveStepInTree(
  steps: EditorStep[],
  stepId: string,
  delta: number,
): { steps: EditorStep[]; changed: boolean } {
  const found = locateStep(steps, stepId);
  if (!found) {
    return { steps, changed: false };
  }

  const target = Math.min(Math.max(found.index + delta, 0), found.list.length - 1);
  if (target === found.index) {
    return { steps, changed: false };
  }

  const [step] = found.list.splice(found.index, 1);
  if (step) {
    found.list.splice(target, 0, step);
  }

  return { steps, changed: true };
}

export function removeStepFromTree(
  steps: EditorStep[],
  stepId: string,
): { steps: EditorStep[]; changed: boolean } {
  const found = locateStep(steps, stepId);
  if (!found) {
    return { steps, changed: false };
  }

  found.list.splice(found.index, 1);
  return { steps, changed: true };
}

export function changeStep(
  steps: EditorStep[],
  stepId: string,
  change: (step: EditorStep) => EditorStep,
): EditorStep[] {
  return steps.map((step) => step.id === stepId
    ? change(step)
    : {
        ...step,
        sections: step.sections.map((section) => ({
          ...section,
          steps: changeStep(section.steps, stepId, change),
        })),
      });
}

export function changeCondition(
  steps: EditorStep[],
  stepId: string,
  name: string,
  change: (condition: EditorCondition) => EditorCondition,
): EditorStep[] {
  return changeStep(steps, stepId, (step) => ({
    ...step,
    parameters: step.parameters.map((parameter) => parameter.name === name && parameter.condition
      ? { ...parameter, condition: change(parameter.condition) }
      : parameter),
  }));
}

export function conditionPath(params: Record<string, unknown>): number[] {
  const path = params['path'];
  if (Array.isArray(path)) {
    return path.map((item) => Number(item));
  }

  return params['index'] === undefined ? [] : [Number(params['index'])];
}

export function addConditionNode(
  condition: EditorCondition,
  path: number[],
  kind: 'row' | 'group',
): EditorCondition {
  const root = conditionRoot(condition);
  const next = changeConditionGroup(root, path, (group) => ({
    ...group,
    children: [...group.children, newConditionNode(kind)],
  }));
  return conditionFromRoot(next);
}

export function removeConditionNode(
  condition: EditorCondition,
  path: number[],
): EditorCondition {
  if (path.length === 0) {
    return condition;
  }

  const parentPath = path.slice(0, -1);
  const index = path[path.length - 1]!;
  const root = conditionRoot(condition);
  const next = changeConditionGroup(root, parentPath, (group) => ({
    ...group,
    children: group.children.filter((_, at) => at !== index),
  }));
  return conditionFromRoot(next);
}

export function setConditionCell(
  condition: EditorCondition,
  path: number[],
  field: string,
  value: string,
): EditorCondition {
  if (path.length === 0) {
    return condition;
  }

  const root = conditionRoot(condition);
  const next = changeConditionNode(root, path, (node) => ({
    ...node,
    left: field === 'left' ? value : node.left,
    compare: field === 'compare' ? value : node.compare,
    right: field === 'right' ? value : node.right,
  }));
  return conditionFromRoot(next);
}

export function setConditionJoinAt(
  condition: EditorCondition,
  path: number[],
  join: string,
): EditorCondition {
  const root = conditionRoot(condition);
  const next = changeConditionGroup(root, path, (group) => ({ ...group, join }));
  return conditionFromRoot(next);
}

function conditionRoot(condition: EditorCondition): EditorConditionNode {
  return {
    kind: 'group',
    join: condition.join,
    left: '',
    compare: '',
    right: '',
    children: condition.nodes,
  };
}

function conditionFromRoot(root: EditorConditionNode): EditorCondition {
  return { join: root.join, nodes: root.children, editable: true };
}

function newConditionNode(kind: 'row' | 'group'): EditorConditionNode {
  return kind === 'row'
    ? { kind: 'row', join: '', left: '', compare: 'Equal', right: '', children: [] }
    : { kind: 'group', join: 'All', left: '', compare: '', right: '', children: [] };
}

function changeConditionGroup(
  group: EditorConditionNode,
  path: number[],
  change: (group: EditorConditionNode) => EditorConditionNode,
): EditorConditionNode {
  if (path.length === 0) {
    return change(group);
  }

  const [index, ...rest] = path;
  return {
    ...group,
    children: group.children.map((node, at) => at === index
      ? changeConditionGroup(node, rest, change)
      : node),
  };
}

function changeConditionNode(
  group: EditorConditionNode,
  path: number[],
  change: (node: EditorConditionNode) => EditorConditionNode,
): EditorConditionNode {
  const [index, ...rest] = path;
  return {
    ...group,
    children: group.children.map((node, at) => {
      if (at !== index) {
        return node;
      }

      return rest.length === 0 ? change(node) : changeConditionNode(node, rest, change);
    }),
  };
}

export function changeRows(
  steps: EditorStep[],
  stepId: string,
  name: string,
  change: (rows: { cells: string[] }[]) => { cells: string[] }[],
): EditorStep[] {
  return changeStep(steps, stepId, (step) => ({
    ...step,
    parameters: step.parameters.map((parameter) => parameter.name === name
      ? { ...parameter, rows: change(parameter.rows) }
      : parameter),
  }));
}

export function columnIndex(column: string): number {
  return column === 'name' ? 1 : 0;
}

export function setParameter(
  steps: EditorStep[],
  stepId: string,
  name: string,
  value: string,
): EditorStep[] {
  return changeStep(steps, stepId, (step) => ({
    ...step,
    parameters: step.parameters.map((parameter) => parameter.name === name
      ? {
          ...parameter,
          value,
          display: value,
          parts: value.length > 0 ? [{ kind: 'text', text: value, name: '' }] : [],
        }
      : parameter),
  }));
}

export function setStepEnabled(
  steps: EditorStep[],
  stepId: string,
  enabled: boolean,
): EditorStep[] {
  return changeStep(steps, stepId, (step) => ({ ...step, enabled }));
}
