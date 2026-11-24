import type { MockEditor } from './mock-editor.js';
import type {
  EditorCondition,
  EditorRow,
  EditorSnapshot,
  EditorStep,
  EditorTrigger,
} from './protocol.js';
import type { StepLocation } from './mock-editor-step-ops.js';
import {
  changeCondition,
  changeRows,
  changeStep,
  conditionPath,
  duplicateStepInTree,
  locateStep,
  moveStepInTree,
  removeStepFromTree,
  setParameter,
  setStepEnabled,
  addStepToTree,
} from './mock-editor-step-ops.js';
import { findAction } from './mock-editor-actions.js';
import { newTrigger, changeTrigger } from './mock-editor-fixtures.js';


export function addStep(host: MockEditor, params: Record<string, unknown>): EditorSnapshot | null {
    const type = host.text(params, 'type');
    if (type.length === 0) {
      throw new Error('没有指定要添加的动作。');
    }

    const action = findAction(type);
    if (!action) {
      throw new Error('动作目录里没有 ' + type + '。');
    }

    const parentId = host.text(params, 'parentId');
    const list = host.text(params, 'list');
    const presets = host.presets(params['parameters']);
    return host.change((document) => ({
      ...document,
      dirty: true,
      canUndo: true,
      canRedo: false,
      steps: addStepToTree(
        document.steps,
        parentId.length > 0 ? parentId : undefined,
        list.length > 0 ? list : undefined,
        action,
        host.nextStepId(),
        presets,
      ),
    }));
  }

export function requestedSteps(_host: MockEditor, params: Record<string, unknown>): string[] {
    const value = params['steps'];
    return Array.isArray(value)
      ? value.filter((item): item is string => typeof item === 'string' && item.length > 0)
      : [];
  }

export function stepsEnabled(host: MockEditor, params: Record<string, unknown>): EditorSnapshot | null {
    const ids = requestedSteps(host, params);
    const enabled = params['enabled'] === true;
    return host.change((document) => {
      let steps = document.steps;
      for (const id of ids) {
        steps = setStepEnabled(steps, id, enabled);
      }
      return { ...document, dirty: true, steps };
    });
  }

export function stepsRemove(host: MockEditor, params: Record<string, unknown>): EditorSnapshot | null {
    const ids = requestedSteps(host, params);
    return host.change((document) => {
      let steps = document.steps;
      for (const id of ids) {
        steps = removeStepFromTree(steps, id).steps;
      }
      return { ...document, dirty: true, steps };
    });
  }

export function stepsDuplicate(host: MockEditor, params: Record<string, unknown>): EditorSnapshot | null {
    const ids = requestedSteps(host, params);
    return host.change((document) => {
      let steps = document.steps;
      for (const id of ids) {
        steps = duplicateStepInTree(steps, id, () => host.nextStepId()).steps;
      }
      return { ...document, dirty: true, canUndo: true, canRedo: false, steps };
    });
  }

export function stepsMoveBy(host: MockEditor, params: Record<string, unknown>): EditorSnapshot | null {
    const ids = requestedSteps(host, params);
    const delta = Number(params['delta'] ?? 0);
    return host.change((document) => {
      let steps = document.steps;
      for (const id of ids) {
        steps = moveStepInTree(steps, id, delta).steps;
      }
      return { ...document, dirty: true, steps };
    });
  }

export function stepsCopy(host: MockEditor, params: Record<string, unknown>): string {
    const ids = requestedSteps(host, params);
    const document = host.activeSnapshot();
    const steps = document
      ? ids
        .map((id) => locateStep(document.steps, id))
        .filter((found): found is NonNullable<typeof found> => found !== null)
        .map((found) => found.list[found.index]!)
      : [];
    host.stepClipboard = JSON.stringify(steps);
    return host.stepClipboard;
  }

export function stepsCut(host: MockEditor, params: Record<string, unknown>): string {
    const text = stepsCopy(host, params);
    stepsRemove(host, params);
    return text;
  }

export function stepsPaste(host: MockEditor, params: Record<string, unknown>): EditorSnapshot | null {
    const payload = typeof params['text'] === 'string' && params['text'].length > 0
      ? params['text'] as string
      : host.stepClipboard;
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
      return host.activeSnapshot();
    }

    const copies = pasted.map((step) => cloneStep(host, step));
    const after = typeof params['after'] === 'string' ? params['after'] as string : '';
    return host.change((document) => {
      const found = after.length > 0 ? locateStep(document.steps, after) : null;
      const list = found?.list ?? document.steps;
      const index = found ? found.index + 1 : list.length;
      list.splice(index, 0, ...copies);
      return { ...document, dirty: true, steps: document.steps };
    });
  }

export function stepsGroup(host: MockEditor, params: Record<string, unknown>): EditorSnapshot | null {
    const ids = requestedSteps(host, params);
    const document = host.activeSnapshot();
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
    group.id = host.nextStepId();
    group.type = 'control.sequence';
    group.name = '分组';
    group.detail = '按顺序执行';
    group.enabled = true;
    group.acceptsSteps = true;
    group.parameters = [];
    group.sections = [{ name: 'steps', label: '步骤', steps: selected }];
    first.list.splice(first.index, 0, group);
    return host.change((documentNow) => ({ ...documentNow, dirty: true, steps: documentNow.steps }));
  }

export function stepsLift(host: MockEditor): EditorSnapshot | null {
    const document = host.activeSnapshot();
    if (!document) {
      return null;
    }

    return document;
  }

export function stepsMoveTo(host: MockEditor, params: Record<string, unknown>): EditorSnapshot | null {
    const document = host.activeSnapshot();
    const target = typeof params['target'] === 'string' ? params['target'] as string : '';
    const parentId = typeof params['parentId'] === 'string' ? params['parentId'] as string : '';
    if (!document || (target.length === 0 && parentId.length === 0)) {
      return document;
    }

    const selected = located(document.steps, requestedSteps(host, params));
    if (selected.length === 0) {
      return document;
    }

    // The child list is the section the snapshot names; the page sends it as data-list.
    if (parentId.length > 0) {
      return moveIntoSection(host, document, selected, parentId,
        typeof params['list'] === 'string' ? params['list'] as string : '', params['index']);
    }

    return moveBesideTarget(host, document, selected, target, params['before'] === true);
  }

/** The requested ids that still exist, in tree order. */
function located(steps: EditorStep[], ids: string[]): StepLocation[] {
  return ids
    .map((id) => locateStep(steps, id))
    .filter((found): found is StepLocation => found !== null)
    .sort((left, right) => left.index - right.index);
}

/** Take the selected steps out of their lists and hand them back in tree order. */
function detach(selected: StepLocation[]): EditorStep[] {
  const moved = selected.map((found) => found.list[found.index]!);
  for (const found of [...selected].reverse()) {
    found.list.splice(found.index, 1);
  }

  return moved;
}

function moveIntoSection(host: MockEditor, document: EditorSnapshot, selected: StepLocation[],
    parentId: string, list: string, index: unknown): EditorSnapshot | null {
  const slot = sectionList(document.steps, parentId, list);
  if (!slot) {
    throw new Error('步骤 ' + parentId + ' 没有名为 ' + list + ' 的子列表。');
  }

  const moved = detach(selected);
  const wanted = typeof index === 'number' ? index : slot.length;
  slot.splice(Math.max(0, Math.min(wanted, slot.length)), 0, ...moved);
  return host.change((documentNow) => ({ ...documentNow, dirty: true, steps: documentNow.steps }));
}

function moveBesideTarget(host: MockEditor, document: EditorSnapshot, selected: StepLocation[],
    target: string, before: boolean): EditorSnapshot | null {
  const location = locateStep(document.steps, target);
  if (!location) {
    return document;
  }

  const moved = detach(selected);
  const at = location.list.findIndex((step) => step.id === target);
  location.list.splice(Math.max(0, at + (before ? 0 : 1)), 0, ...moved);
  return host.change((documentNow) => ({ ...documentNow, dirty: true, steps: documentNow.steps }));
}

/** The child list of a block by the snapshot's own section name. */
function sectionList(steps: EditorStep[], parentId: string, list: string): EditorStep[] | null {
  const found = locateStep(steps, parentId);
  if (!found) {
    return null;
  }

  const parent = found.list[found.index]!;
  const section = list.length > 0
    ? parent.sections.find((candidate) => candidate.name === list)
    : parent.sections[0];
  return section ? section.steps : null;
}

export function cloneStep(host: MockEditor, step: EditorStep): EditorStep {
    const copy = structuredClone(step);
    const renumber = (current: EditorStep): void => {
      current.id = host.nextStepId();
      for (const section of current.sections) {
        for (const child of section.steps) {
          renumber(child);
        }
      }
    };
    renumber(copy);
    return copy;
  }

export function setStepEnabledCommand(host: MockEditor, params: Record<string, unknown>): EditorSnapshot | null {
    const stepId = host.text(params, 'stepId');
    const enabled = params['enabled'] === true;
    return host.change((document) => ({
      ...document,
      dirty: true,
      steps: setStepEnabled(document.steps, stepId, enabled),
    }));
  }

export function setStepMeta(host: MockEditor, params: Record<string, unknown>): EditorSnapshot | null {
    const stepId = host.text(params, 'stepId');
    const field = host.text(params, 'field');
    const value = host.text(params, 'value');
    if (host.actionForm && stepId === host.actionForm.stepId) {
      host.actionStep = changeStep([host.actionStep!], stepId, (step) => ({
        ...step,
        comment: field === 'comment' ? value : step.comment,
        timeoutMs: field === 'timeoutMs' ? Number(value) : step.timeoutMs,
        retryCount: field === 'retryCount' ? Number(value) : step.retryCount,
        retryDelayMs: field === 'retryDelayMs' ? Number(value) : step.retryDelayMs,
        onError: field === 'onError' ? value : step.onError,
      }))[0]!;
      host.refreshActionForm();
      return host.activeSnapshot();
    }

    return host.change((document) => ({
      ...document,
      dirty: true,
      steps: changeStep(document.steps, stepId, (step) => ({
        ...step,
        comment: field === 'comment' ? value : step.comment,
        timeoutMs: field === 'timeoutMs' ? host.number(value, step.timeoutMs) : step.timeoutMs,
        retryCount: field === 'retryCount' ? host.number(value, step.retryCount) : step.retryCount,
        retryDelayMs: field === 'retryDelayMs'
          ? host.number(value, step.retryDelayMs)
          : step.retryDelayMs,
        onError: field === 'onError' ? value : step.onError,
      })),
    }));
  }

export function duplicateStep(host: MockEditor, params: Record<string, unknown>): EditorSnapshot | null {
    const stepId = host.text(params, 'stepId');
    return host.change((document) => {
      const steps = structuredClone(document.steps);
      const result = duplicateStepInTree(steps, stepId, () => host.nextStepId());
      return result.changed
        ? { ...document, dirty: true, canUndo: true, canRedo: false, steps: result.steps }
        : document;
    });
  }

export function moveStep(host: MockEditor, params: Record<string, unknown>): EditorSnapshot | null {
    const stepId = host.text(params, 'stepId');
    const delta = host.number(host.text(params, 'delta'), 0);
    return host.change((document) => {
      const steps = structuredClone(document.steps);
      const result = moveStepInTree(steps, stepId, delta);
      return result.changed ? { ...document, dirty: true, steps: result.steps } : document;
    });
  }

export function removeStep(host: MockEditor, params: Record<string, unknown>): EditorSnapshot | null {
    const stepId = host.text(params, 'stepId');
    return host.change((document) => {
      const steps = structuredClone(document.steps);
      const result = removeStepFromTree(steps, stepId);
      return result.changed ? { ...document, dirty: true, steps: result.steps } : document;
    });
  }

export function conditionCommand(host: MockEditor, params: Record<string, unknown>,
    change: (condition: EditorCondition, path: number[]) => EditorCondition,): EditorSnapshot | null {
    const stepId = host.text(params, 'stepId');
    const name = host.text(params, 'name');
    const path = conditionPath(params);
    if (host.actionForm && stepId === host.actionForm.stepId) {
      host.actionStep = changeCondition([host.actionStep!], stepId, name,
        (condition) => change(condition, path))[0]!;
      host.refreshActionForm();
      return host.activeSnapshot();
    }

    return host.change((document) => ({
      ...document,
      dirty: true,
      steps: changeCondition(document.steps, stepId, name, (condition) => change(condition, path)),
    }));
  }

export function setParameterCommand(host: MockEditor, params: Record<string, unknown>): EditorSnapshot | null {
    const stepId = host.text(params, 'stepId');
    const name = host.text(params, 'name');
    const value = host.text(params, 'value');
    if (host.actionForm && stepId === host.actionForm.stepId) {
      host.actionStep = setParameter([host.actionStep!], stepId, name, value)[0]!;
      host.refreshActionForm();
      return host.activeSnapshot();
    }

    return host.change((document) => ({
      ...document,
      dirty: true,
      steps: setParameter(document.steps, stepId, name, value),
    }));
  }

export function rowCommand(host: MockEditor, params: Record<string, unknown>,
    change: (rows: EditorRow[]) => EditorRow[],): EditorSnapshot | null {
    const stepId = host.text(params, 'stepId');
    const name = host.text(params, 'name');
    if (host.actionForm && stepId === host.actionForm.stepId) {
      host.actionStep = changeRows([host.actionStep!], stepId, name, change)[0]!;
      host.refreshActionForm();
      return host.activeSnapshot();
    }

    return host.change((document) => ({
      ...document,
      dirty: true,
      steps: changeRows(document.steps, stepId, name, change),
    }));
  }

export function addTrigger(host: MockEditor, params: Record<string, unknown>): EditorSnapshot | null {
    const kind = host.text(params, 'kind').length > 0 ? host.text(params, 'kind') : 'Key';
    return host.change((document) => ({
      ...document,
      dirty: true,
      triggers: [...document.triggers, newTrigger(host.nextTriggerId(), kind)],
    }));
  }

export function removeTrigger(host: MockEditor, params: Record<string, unknown>): EditorSnapshot | null {
    const triggerId = host.text(params, 'triggerId');
    return host.change((document) => ({
      ...document,
      dirty: true,
      triggers: document.triggers.filter((trigger) => trigger.id !== triggerId),
    }));
  }

export function patchTrigger(host: MockEditor, params: Record<string, unknown>,
    update: (trigger: EditorTrigger) => EditorTrigger,): EditorSnapshot | null {
    const triggerId = host.text(params, 'triggerId');
    return host.change((document) => changeTrigger(document, triggerId, update));
  }
