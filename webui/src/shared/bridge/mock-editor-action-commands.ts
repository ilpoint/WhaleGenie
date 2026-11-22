import type { MockEditor } from './mock-editor.js';
import { changeRows, changeStep, setParameter } from './mock-editor-step-ops.js';

type ActionHandler = (host: MockEditor, params: Record<string, unknown>) => unknown;

/** The action form's draft edits; the kind picks a handler. */
export function handleActionCommand(host: MockEditor, params: Record<string, unknown>): unknown {
  const handler = actionCommands[String(params['kind'] ?? '')];
  if (!handler) {
    throw new Error('不认识动作命令 ' + String(params['kind']) + '。');
  }

  return handler(host, params);
}

const actionCommands: Record<string, ActionHandler> = {
  page: (host, params) => {
    host.actionPage = host.text(params, 'page');
    host.refreshActionForm();
    return host.actionForm;
  },
  field: (host, params) => {
    const name = host.text(params, 'name');
    const value = host.text(params, 'value');
    if (host.actionStep) {
      host.actionStep = setParameter([host.actionStep!], host.actionStep.id, name, value)[0]!;
      host.refreshActionForm();
    }
    return host.actionForm;
  },
  rowAdd: (host, params) => {
    const name = host.text(params, 'name');
    if (host.actionStep) {
      host.actionStep = changeRows([host.actionStep!], host.actionStep.id, name,
        (rows) => [...rows, { cells: [] }])[0]!;
      host.refreshActionForm();
    }
    return host.actionForm;
  },
  rowRemove: (host, params) => {
    const name = host.text(params, 'name');
    const index = Number(params['index'] ?? 0);
    if (host.actionStep) {
      host.actionStep = changeRows([host.actionStep!], host.actionStep.id, name,
        (rows) => rows.filter((_row, at) => at !== index))[0]!;
      host.refreshActionForm();
    }
    return host.actionForm;
  },
  rowMove: (host, params) => {
    const name = host.text(params, 'name');
    const index = Number(params['index'] ?? 0);
    const by = Number(params['by'] ?? 0);
    if (host.actionStep) {
      host.actionStep = changeRows([host.actionStep!], host.actionStep.id, name, (rows) => {
        const to = index + by;
        if (to < 0 || to >= rows.length) {
          return rows;
        }
        const copy = [...rows];
        [copy[index], copy[to]] = [copy[to]!, copy[index]!];
        return copy;
      })[0]!;
      host.refreshActionForm();
    }
    return host.actionForm;
  },
  rowCell: (host, params) => {
    const name = host.text(params, 'name');
    const index = Number(params['index'] ?? 0);
    const column = host.text(params, 'column');
    const value = host.text(params, 'value');
    const cell = { x: 0, y: 1, width: 2, height: 3, image: 0, keys: 0, hold: 1, gap: 2,
      dx: 0, dy: 1, colour: 2, mustNot: 3, tolerance: 4, how: 0, variable: 4 }[column] ?? 0;
    if (host.actionStep) {
      host.actionStep = changeRows([host.actionStep!], host.actionStep.id, name,
        (rows) => rows.map((row, at) => at === index
          ? { ...row, cells: row.cells.map((old, cellIndex) => cellIndex === cell ? value : old) }
          : row))[0]!;
      host.refreshActionForm();
    }
    return host.actionForm;
  },
  returns: (host, params) => {
    const key = host.text(params, 'key');
    const name = host.optionalText(params, 'name');
    const ticked = typeof params['ticked'] === 'boolean' ? params['ticked'] as boolean : null;
    if (host.actionStep) {
      const returns = host.actionStep.parameters.find((parameter) => parameter.kind === 'Returns');
      if (returns) {
        const rows = returns.rows.flatMap((row) => {
          if ((row.cells[0] ?? '') !== key) {
            return [row];
          }
          if (ticked === false) {
            return [];
          }
          return [{ ...row, cells: [row.cells[0] ?? key, name ?? row.cells[1] ?? ''] }];
        });
        host.actionStep = changeStep([host.actionStep!], host.actionStep.id, (step) => ({
          ...step,
          parameters: step.parameters.map((parameter) => parameter.kind === 'Returns'
            ? { ...parameter, rows } : parameter),
        }))[0]!;
        host.refreshActionForm();
      }
    }
    return host.actionForm;
  },
  returnsFixName: (host, params) => {
    const key = host.text(params, 'key');
    if (host.actionStep) {
      const returns = host.actionStep.parameters.find((parameter) => parameter.kind === 'Returns');
      if (returns) {
        const rows = returns.rows.map((row) => (row.cells[0] ?? '') === key
          ? { ...row, cells: [row.cells[0] ?? key, (row.cells[1] ?? key) + '2'] }
          : row);
        host.actionStep = changeStep([host.actionStep!], host.actionStep.id, (step) => ({
          ...step,
          parameters: step.parameters.map((parameter) => parameter.kind === 'Returns'
            ? { ...parameter, rows } : parameter),
        }))[0]!;
        host.refreshActionForm();
      }
    }
    return host.actionForm;
  },
  callArgument: (host) => host.actionForm,
  global: (host) => host.actionForm,
  branch: (host) => host.actionForm,
  window: (host) => host.actionForm,
  windowRule: (host) => host.actionForm,
  padKey: (host) => host.actionForm,
  meta: (host, params) => {
    const field = host.text(params, 'field');
    const value = host.text(params, 'value');
    if (host.actionStep) {
      host.actionStep = changeStep([host.actionStep!], host.actionStep.id, (step) => ({
        ...step,
        comment: field === 'comment' ? value : step.comment,
        enabled: field === 'enabled' ? value === 'true' : step.enabled,
        timeoutMs: field === 'timeoutMs' ? Number(value) : step.timeoutMs,
        retryCount: field === 'retryCount' ? Number(value) : step.retryCount,
        retryDelayMs: field === 'retryDelayMs' ? Number(value) : step.retryDelayMs,
        onError: field === 'onError' ? value : step.onError,
      }))[0]!;
      host.refreshActionForm();
    }
    return host.actionForm;
  },
};
