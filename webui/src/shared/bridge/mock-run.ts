import type { MockRunContext } from './mock-context.js';

/** handleRun-domain answers for the mock host. The transport routes by the method prefix. */
export function handleRun(host: MockRunContext, method: string, params: Record<string, unknown>): unknown {
  let result: unknown;
  switch (method) {
        case 'run.state':
          result = host.runState;
          break;
        case 'run.load':
          host.runState = {
            ...host.runState,
            loaded: String(params['name'] ?? ''),
            status: 'Loaded', canRun: true, canStep: true, busy: false, paused: false,
            arguments: [{ name: 'target', label: 'target', hint: 'Text', required: true,
              value: '', given: false, missing: true }],
            globals: [{ name: 'count', type: 'Number', value: '3', defaultValue: '0' }],
            locals: [{ name: 'found', macro: 'Demo', type: 'Boolean' }],
            steps: [
              { id: 's1', label: 'Step one', detail: 'action.key', depth: 0, shown: true,
                container: false, current: false, breakpoint: false, canBreak: true,
                time: '', failure: '', condition: '', hitTimes: 1, expression: '' },
              { id: 's2', label: 'Step two', detail: 'action.key', depth: 0, shown: true,
                container: false, current: false, breakpoint: false, canBreak: true,
                time: '', failure: '', condition: '', hitTimes: 1, expression: '' },
            ],
          };
          result = host.runState;
          break;
        case 'run.command':
          host.runState = {
            ...host.runState,
            status: String(params['command'] ?? ''),
            busy: params['command'] === 'run',
            canStop: params['command'] === 'run',
            canRun: params['command'] !== 'run',
            canStep: params['command'] !== 'run',
          };
          result = host.runState;
          break;
        case 'run.answer':
          result = host.runState;
          break;
        case 'run.argument':
          host.runState = {
            ...host.runState,
            arguments: host.runState.arguments.map((argument) => argument.name === params['name']
              ? { ...argument, value: String(params['value'] ?? ''),
                  given: String(params['value'] ?? '').length > 0,
                  missing: false }
              : argument),
          };
          result = host.runState;
          break;
        case 'run.breakpointRule':
          host.runState = {
            ...host.runState,
            steps: host.runState.steps.map((step) => step.id === params['id']
              ? { ...step, hitTimes: Number(params['hitTimes'] ?? 1),
                  expression: String(params['expression'] ?? '') }
              : step),
          };
          result = host.runState;
          break;
        case 'run.breakpoint':
          host.runState = {
            ...host.runState,
            steps: host.runState.steps.map((step) => step.id === params['id']
              ? { ...step, breakpoint: !step.breakpoint }
              : step),
          };
          result = host.runState;
          break;
  }

  return result;
}
