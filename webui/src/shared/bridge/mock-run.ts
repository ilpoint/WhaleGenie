import type { MockRunContext } from './mock-context.js';
import type { RunStep } from './protocol.js';

/** 一张 1×1 的 PNG，冒充当一步取景的画面：页面上只看大小与位置，不看像素。 */
const LOOK_IMAGE =
  'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==';

const LOOK_A = { stepId: 's1', type: 'control.findImage', looking: '找图：按钮', image: LOOK_IMAGE };
const LOOK_B = { stepId: 's2', type: 'control.findImage', looking: '找图：确认', image: LOOK_IMAGE };

/** 装一个宏时给出的那些区块：断点候选、触发等待、监视值、取景与每步结局。 */
const CHOICES = ['i', 'count', 'found'];

const TRIGGERS = [
  { name: '自动保存', kind: '定时：每 1 分钟', status: '现在在',
    checked: '上次判定 12:00:01，下次 12:00:11', detail: '' },
  { name: '游戏连招', kind: '键：F8', status: '', checked: '没有排在钟上', detail: '窗口有变化时机器会先说一声' },
];

const WATCHES = [
  { name: 'count', type: '数字', value: '3' },
  { name: 'found', type: '布尔', value: 'true' },
];

/** handleRun-domain answers for the mock host. The transport routes by the method prefix. */
export function handleRun(host: MockRunContext, method: string, params: Record<string, unknown>): unknown {
  switch (method) {
    case 'run.state':
    case 'run.answer':
      return host.runState;
    case 'run.unload':
      return unload(host);
    case 'run.load':
      return load(host, params);
    case 'run.command':
      return command(host, params);
    case 'run.lookSelect':
      return lookSelect(host, params);
    case 'run.argument':
      return setArgument(host, params);
    case 'run.breakpointRule':
      return setRule(host, params);
    case 'run.breakpoint':
      return toggleBreakpoint(host, params);
    default:
      return undefined;
  }
}

/** 装一个宏：既有程序算出的那些区块，也有页面要画的每步结局与结果。 */
function load(host: MockRunContext, params: Record<string, unknown>): unknown {
  const name = String(params['name'] ?? '');
  const was = host.runState.loaded;
  host.runState = {
    ...host.runState,
    loaded: name,
    status: 'Loaded', canRun: true, canStep: true, busy: false, paused: false,
    swapNote: was.length > 0 && was !== name ? swapNote(was, name) : '',
    inputRate: 120,
    inputRateOwner: `正在发输入：${name}`,
    triggers: TRIGGERS,
    watches: WATCHES,
    looks: [LOOK_A, LOOK_B],
    activeLook: 's1',
    look: LOOK_A,
    breakpointChoices: CHOICES,
    arguments: [{ name: 'target', label: 'target', hint: 'Text', required: true,
      value: '', given: false, missing: true }],
    globals: [{ name: 'count', type: 'Number', value: '3', defaultValue: '0' }],
    locals: [{ name: 'found', macro: 'Demo', type: 'Boolean' }],
    steps: [
      step('s1', 'Step one', '已完成', 'found = 12'),
      step('s2', 'Step two'),
    ],
  };
  return host.runState;
}

/** 换宏时程序写在面板上的那句话，与程序里的词表同形。 */
function swapNote(was: string, name: string): string {
  return `已停止“${was}”的运行，改为调试“${name}”。`;
}

function step(id: string, label: string, outcome = '', result = ''): RunStep {
  return {
    id, label, detail: 'action.key', depth: 0, shown: true,
    container: false, current: false, breakpoint: false, canBreak: true,
    time: '', failure: '', condition: '', hitTimes: 1, expression: '',
    outcome, result,
  };
}

/** 一次运行命令：按钮的可用状态跟着走，换宏的说明在真的开始运行时让开。 */
function command(host: MockRunContext, params: Record<string, unknown>): unknown {
  const running = params['command'] === 'run';
  host.runState = {
    ...host.runState,
    status: String(params['command'] ?? ''),
    busy: running,
    canStop: running,
    canRun: !running,
    canStep: !running,
    swapNote: running ? '' : host.runState.swapNote,
  };
  return host.runState;
}

/** 换一张取景：只把正在看的那张换成另一张，不重跑。 */
function lookSelect(host: MockRunContext, params: Record<string, unknown>): unknown {
  const stepId = String(params['stepId'] ?? '');
  const picked = (host.runState.looks ?? []).find((look) => look.stepId === stepId);
  host.runState = {
    ...host.runState,
    activeLook: stepId,
    look: picked ?? host.runState.look,
  };
  return host.runState;
}

function setArgument(host: MockRunContext, params: Record<string, unknown>): unknown {
  const value = String(params['value'] ?? '');
  host.runState = {
    ...host.runState,
    arguments: host.runState.arguments.map((argument) => argument.name === params['name']
      ? { ...argument, value, given: value.length > 0, missing: false }
      : argument),
  };
  return host.runState;
}

function setRule(host: MockRunContext, params: Record<string, unknown>): unknown {
  host.runState = {
    ...host.runState,
    steps: (host.runState.steps ?? []).map((item) => item.id === params['id']
      ? { ...item, hitTimes: Number(params['hitTimes'] ?? 1),
          expression: String(params['expression'] ?? '') }
      : item),
  };
  return host.runState;
}

function toggleBreakpoint(host: MockRunContext, params: Record<string, unknown>): unknown {
  host.runState = {
    ...host.runState,
    steps: (host.runState.steps ?? []).map((item) => item.id === params['id']
      ? { ...item, breakpoint: !item.breakpoint }
      : item),
  };
  return host.runState;
}

function unload(host: MockRunContext): unknown {
  host.runState = {
    ...host.runState,
    loaded: '', status: '', busy: false, paused: false,
    canRun: false, canStep: false, canStop: false, steps: [],
    look: null, looks: [], activeLook: '', triggers: [], watches: [],
    swapNote: '', inputRate: 0, inputRateOwner: '', breakpointChoices: [],
  };
  return host.runState;
}
