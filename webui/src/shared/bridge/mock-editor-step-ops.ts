import type {
  EditorCondition,
  EditorConditionNode,
  EditorStep,
} from './protocol.js';
import type { MockAction } from './mock-editor-actions.js';
import { stepFromAction } from './mock-editor-steps.js';

/**
 * Structural edits the mock editor commands apply to its step tree. The presentation helpers
 * (summary, problems, input marks) stay in mock-editor-steps.ts.
 */

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

