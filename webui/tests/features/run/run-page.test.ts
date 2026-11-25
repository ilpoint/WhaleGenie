import { beforeEach, describe, expect, it } from 'vitest';
import { html, render } from 'lit';
import { renderLooks } from '../../../src/features/run/run-look.js';
import { WgRun } from '../../../src/features/run/run-page.js';
import { renderSteps, type StepActions } from '../../../src/features/run/run-steps.js';
import { renderTriggers } from '../../../src/features/run/run-triggers.js';
import { renderVariables } from '../../../src/features/run/run-variables.js';
import { bridge } from '../../../src/shared/bridge/bridge.js';
import type { BridgeApi } from '../../../src/shared/bridge/client.js';
import type { BridgeMethod, ProjectState, RunLook, RunState, RunStep } from '../../../src/shared/bridge/protocol.js';
import wordTables from '../../../src/shared/bridge/generated/strings.json';
import { strings } from '../../../src/shared/strings/strings.js';

/**
 * 运行页与它的区块：新补的调试数据（触发器、监视值、每步结局与结果、历史取景、速率与换宏
 * 说明）都要画得出来；换一张取景只发 run.lookSelect，不重跑。
 */

const LOOK_A: RunLook = { stepId: 's1', type: 'control.findImage', looking: 'button', image: 'data:image/png;base64,AA' };
const LOOK_B: RunLook = { stepId: 's2', type: 'control.findImage', looking: 'confirm', image: 'data:image/png;base64,BB' };

function step(id: string, extra: Partial<RunStep> = {}): RunStep {
  return {
    id,
    label: 'Step ' + id,
    detail: 'action.key',
    depth: 0,
    shown: true,
    container: false,
    current: false,
    breakpoint: false,
    canBreak: true,
    time: '',
    failure: '',
    condition: '',
    hitTimes: 1,
    expression: '',
    outcome: '',
    result: '',
    ...extra,
  };
}

function state(): RunState {
  return {
    loaded: 'demo',
    status: 'Ready',
    statusKind: 'Ready',
    busy: false,
    paused: false,
    canRun: true,
    canStep: true,
    canStop: false,
    question: '',
    wantsTypedAnswer: false,
    answerText: '',
    isAskingUser: false,
    isAsking: false,
    arguments: [],
    look: LOOK_A,
    globals: [],
    locals: [],
    steps: [
      step('s1', { breakpoint: true, expression: 'i == 1', outcome: 'done', result: 'found = 12' }),
      step('s2'),
    ],
    log: [],
    logFrom: 0,
    inputRate: 120,
    inputRateOwner: 'Sending input: demo',
    swapNote: 'Stopped demo to debug other.',
    triggers: [{ name: 'auto', kind: 'timer', status: 'there', checked: 'last 12:00:01', detail: '' }],
    watches: [{ name: 'count', type: 'Number', value: '3' }],
    looks: [LOOK_A, LOOK_B],
    activeLook: 's1',
    breakpointChoices: ['count', 'found'],
  };
}

function project(): ProjectState {
  return {
    path: null,
    name: null,
    untitled: true,
    dirty: false,
    revision: 0,
    systemEnabled: false,
    autoSave: true,
    saving: false,
    saveError: null,
    enabledFirst: true,
    views: [],
    groups: [],
    macros: [{
      name: 'demo',
      group: '',
      description: '',
      enabled: true,
      running: false,
      triggerSummary: '',
      triggerActions: '',
      triggerPreview: '',
      views: [],
    }],
  };
}

beforeEach(() => {
  strings.load({ language: 'zh-Hans', entries: wordTables.zh });
});

/** 等一个条件成立：挂载后页面要等两次桥回复才画得出取景，单次 updateComplete 不够。 */
async function waitFor(check: () => boolean, timeoutMs = 2000): Promise<void> {
  const started = Date.now();
  while (!check()) {
    if (Date.now() - started > timeoutMs) {
      throw new Error('timed out waiting for the page to draw');
    }

    await new Promise((resolve) => setTimeout(resolve, 10));
  }
}

describe('run sections', () => {
  it('draws the triggers, the watched values, the kept frames and each step result', () => {
    const container = document.createElement('div');
    const model = state();
    render(html`
      ${renderTriggers(model)}
      ${renderVariables(model)}
      ${renderLooks(model, () => undefined)}
      ${renderSteps(model, { breakpoint: () => undefined, rule: () => undefined })}
    `, container);

    expect(container.querySelectorAll('.trigger-row')).toHaveLength(1);
    expect(container.querySelector('.trigger-row .name')?.textContent).toBe('auto');
    expect(container.querySelector('.trigger-row .status')?.textContent).toBe('there');
    expect(container.querySelectorAll('.watch')).toHaveLength(1);
    expect(container.querySelector('.rate')?.textContent).toContain('120');
    expect(container.querySelector('.rate .owner')?.textContent).toContain('Sending input: demo');
    expect(container.querySelectorAll('.look-thumb')).toHaveLength(2);
    expect(container.querySelector('.look-thumb.active')?.getAttribute('data-step')).toBe('s1');
    expect(container.querySelectorAll('.step')).toHaveLength(2);
    expect(container.querySelectorAll('.step .outcome')).toHaveLength(1);
    expect(container.querySelector('.step .result')?.textContent).toContain('found = 12');
  });

  it('offers the breakpoint choices and still takes one typed in by hand', () => {
    const asked: { id: string; hitTimes: number; expression: string }[] = [];
    const actions: StepActions = {
      breakpoint: () => undefined,
      rule: (id, hitTimes, expression) => {
        asked.push({ id, hitTimes, expression });
      },
    };
    const container = document.createElement('div');
    render(renderSteps(state(), actions), container);

    expect(container.querySelectorAll('.choice')).toHaveLength(2);
    container.querySelector<HTMLButtonElement>('.choice')?.click();
    // 候选接在已写的内容后面：s1 的条件是 i == 1，点第一个候选应得到 i == 1 count。
    expect(asked).toEqual([{ id: 's1', hitTimes: 1, expression: 'i == 1 count' }]);

    const expression = container.querySelector<HTMLInputElement>('input.expression');
    expect(expression).not.toBeNull();
    expression!.value = 'i == 1';
    expression!.dispatchEvent(new Event('change'));
    expect(asked.at(-1)).toEqual({ id: 's1', hitTimes: 1, expression: 'i == 1' });
  });
});

describe('run page', () => {
  it('switches the frame through run.lookSelect without running anything', async () => {
    const calls: { method: string; params: unknown }[] = [];
    const api: BridgeApi = {
      call<T = void>(method: BridgeMethod, params?: unknown): Promise<T> {
        calls.push({ method, params });
        return Promise.resolve((method === 'project.state' ? project() : state()) as T);
      },
      on: () => () => undefined,
    };
    bridge.connect(api);

    const element = document.createElement('wg-run') as WgRun;
    expect(element).toBeInstanceOf(WgRun);
    document.body.appendChild(element);
    await waitFor(() => element.shadowRoot!.querySelector('.look-thumb.active') !== null);

    const thumb = element.shadowRoot!.querySelector<HTMLButtonElement>('.look-thumb.active');
    expect(thumb).not.toBeNull();
    thumb!.click();
    await Promise.resolve();
    await element.updateComplete;

    const switched = calls.filter((call) => call.method === 'run.lookSelect');
    expect(switched).toHaveLength(1);
    expect(switched[0]?.params).toEqual({ stepId: 's1' });
    expect(calls.some((call) => call.method === 'run.command')).toBe(false);
    element.remove();
  });
});
