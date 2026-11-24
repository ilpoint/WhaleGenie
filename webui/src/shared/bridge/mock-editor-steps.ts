import type {
  EditorCondition,
  EditorConditionNode,
  EditorParameter,
  EditorSnapshot,
  EditorStep,
} from './protocol.js';
import type { MockAction, MockActionParameter } from './mock-editor-actions.js';
import raw from './data/step-templates.json';

/**
 * The mock editor's step tree: the actions it is built from live in mock-editor-actions.ts, the
 * step and parameter skeletons plus the summary wordings live in data/step-templates.json, and
 * this file owns the shape the page reads back and the structural edits the commands apply.
 */

interface DetailSpec {
  template?: string;
  fields?: string[];
  condition?: string;
  fallback?: string;
  fallbackName?: boolean;
}

interface StepTemplates {
  step: EditorStep;
  parameter: EditorParameter;
  compareLabels: Record<string, string>;
  details: Record<string, DetailSpec>;
}

const DATA = raw as unknown as StepTemplates;

function emptyCondition(): EditorCondition {
  return { join: 'All', nodes: [], editable: true };
}

function parameterFrom(entry: MockActionParameter): EditorParameter {
  const value = entry.initial ?? '';
  const valueForm = entry.kind === 'Number'
    ? 'Value'
    : entry.kind === 'Text' ? 'Run' : 'Value';
  return {
    ...structuredClone(DATA.parameter),
    name: entry.name,
    label: entry.label,
    kind: entry.kind,
    value,
    options: entry.options ?? [],
    columns: entry.columns ?? [],
    rows: entry.kind === 'Returns' ? [{ cells: ['found', ''] }] : [],
    condition: entry.kind === 'Condition' ? emptyCondition() : null,
    display: value,
    valueForm,
    offersValue: !['Select', 'Rows', 'Condition', 'Key'].includes(entry.kind),
    takesFormula: entry.kind === 'Number' || valueForm === 'Value',
    isNumber: entry.kind === 'Number',
    isColor: entry.kind === 'Color',
    isKey: entry.kind === 'Key',
    placeholder: entry.placeholder ?? '',
    parts: value.length > 0 ? [{ kind: 'text', text: value, name: '' }] : [],
    isReturns: entry.kind === 'Returns',
  };
}

export function stepFromAction(action: MockAction, id: string): EditorStep {
  return {
    ...structuredClone(DATA.step),
    id,
    type: action.key,
    name: action.name,
    acceptsSteps: (action.sections?.length ?? 0) > 0,
    parameters: action.parameters.map((entry) => parameterFrom(entry)),
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

function compareLabel(compare: string): string {
  return DATA.compareLabels[compare] ?? compare;
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

function fill(template: string, values: Record<string, string>): string {
  return template.replace(/\{(\w+)\}/g, (_match, name: string) => values[name] ?? '');
}

function stepDetail(step: EditorStep): string {
  const spec = DATA.details[step.type];
  if (!spec) {
    return step.name;
  }

  if (spec.condition !== undefined) {
    const summary = conditionSummary(conditionOf(step));
    return summary.length > 0 ? fill(spec.condition, { summary }) : spec.fallback ?? step.name;
  }

  const values: Record<string, string> = {};
  for (const name of spec.fields ?? []) {
    values[name] = parameterValue(step, name);
  }

  const empty = (spec.fields ?? []).every((name) => (values[name] ?? '').length === 0);
  if (empty) {
    return spec.fallbackName === true ? step.name : spec.fallback ?? step.name;
  }

  return fill(spec.template ?? step.name, values);
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
