import type { MockRunContext } from './mock-context.js';
import type {
  RunArgument,
  RunGlobal,
  RunLocal,
  RunLook,
  RunState,
  RunStep,
  RunTrigger,
  RunWatch,
} from './protocol.js';
import raw from './data/run.json';

/**
 * 运行页 mock：装宏时给出的取景、断点候选、触发等待、监视值、参数与每步结局都是固定样例，
 * 放在 data/run.json；这里只留下随命令推进的状态机。
 */

interface RunFixtures {
  lookImage: string;
  looks: Omit<RunLook, 'image'>[];
  breakpointChoices: string[];
  triggers: RunTrigger[];
  watches: RunWatch[];
  arguments: RunArgument[];
  globals: RunGlobal[];
  locals: RunLocal[];
  steps: { id: string; label: string; outcome: string; result: string }[];
  stepDefaults: Omit<RunStep, 'id' | 'label' | 'outcome' | 'result'>;
  unload: Partial<RunState>;
}

const DATA = raw as unknown as RunFixtures;
const LOOK_A: RunLook = { ...DATA.looks[0]!, image: DATA.lookImage };
const LOOK_B: RunLook = { ...DATA.looks[1]!, image: DATA.lookImage };

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
    inputRateOwner: '正在发输入：' + name,
    triggers: DATA.triggers,
    watches: DATA.watches,
    looks: [LOOK_A, LOOK_B],
    activeLook: 's1',
    look: LOOK_A,
    breakpointChoices: DATA.breakpointChoices,
    arguments: DATA.arguments,
    globals: DATA.globals,
    locals: DATA.locals,
    steps: DATA.steps.map((entry) => step(entry.id, entry.label, entry.outcome, entry.result)),
  };
  return host.runState;
}

/** 换宏时程序写在面板上的那句话，与程序里的词表同形。 */
function swapNote(was: string, name: string): string {
  return '已停止“' + was + '”的运行，改为调试“' + name + '”。';
}

function step(id: string, label: string, outcome = '', result = ''): RunStep {
  return { ...structuredClone(DATA.stepDefaults), id, label, outcome, result };
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
  host.runState = { ...host.runState, ...structuredClone(DATA.unload) };
  return host.runState;
}
