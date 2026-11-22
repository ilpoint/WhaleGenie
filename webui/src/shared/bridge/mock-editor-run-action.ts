import type { MockEditor } from './mock-editor.js';
import { changeRows, changeStep, setParameter } from './mock-editor-step-ops.js';

/** One editor command family, split out of MockEditor.run. */
export function handleActionRun(host: MockEditor, method: string, params: Record<string, unknown>): unknown {
  switch (method) {
      case 'editor.actionOpen':
        return host.actionOpen(params);
      case 'editor.actionState':
        return host.actionForm ?? host.emptyActionForm();
      case 'editor.actionConfirm':
        return host.actionConfirm();
      case 'editor.actionCancel':
        host.actionForm = null;
        host.actionStep = null;
        return host.activeSnapshot();
      case 'editor.actionPage':
        host.actionPage = host.text(params, 'page');
        host.refreshActionForm();
        return host.actionForm;
      case 'editor.actionField': {
        const name = host.text(params, 'name');
        const value = host.text(params, 'value');
        if (host.actionStep) {
          host.actionStep = setParameter([host.actionStep!], host.actionStep.id, name, value)[0]!;
          host.refreshActionForm();
        }
        return host.actionForm;
      }
      case 'editor.actionRowAdd': {
        const name = host.text(params, 'name');
        if (host.actionStep) {
          host.actionStep = changeRows([host.actionStep!], host.actionStep.id, name,
            (rows) => [...rows, { cells: [] }])[0]!;
          host.refreshActionForm();
        }
        return host.actionForm;
      }
      case 'editor.actionRowRemove': {
        const name = host.text(params, 'name');
        const index = Number(params['index'] ?? 0);
        if (host.actionStep) {
          host.actionStep = changeRows([host.actionStep!], host.actionStep.id, name,
            (rows) => rows.filter((_row, at) => at !== index))[0]!;
          host.refreshActionForm();
        }
        return host.actionForm;
      }
      case 'editor.actionRowMove': {
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
      }
      case 'editor.actionRowCell': {
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
      }
      case 'editor.actionReturns': {
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
      }
      case 'editor.actionReturnsFixName': {
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
      }
      case 'editor.actionCallArgument':
      case 'editor.actionGlobal':
      case 'editor.actionBranch':
      case 'editor.actionWindow':
      case 'editor.actionWindowRule':
        return host.actionForm;
      case 'editor.actionPick':
        return host.actionForm;
      case 'editor.actionLook':
        return host.actionForm;
      case 'editor.actionWindowPreview':
        return null;
      case 'editor.actionTestBrowser':
      case 'editor.actionTestElement':
      case 'editor.actionTestNarrow':
        return host.actionForm;
      case 'editor.actionKeyPad':
        return {
          mode: 'key', maxLength: 0,
          rows: [
            [{ label: 'Esc', value: 'Esc', width: 1 }, { label: 'F1', value: 'F1', width: 1 },
              { label: 'F13', value: 'F13', width: 1 }],
            [{ label: 'Num 7', value: 'NumPad7', width: 1 },
              { label: 'MediaPlay', value: 'MediaPlay', width: 1 },
              { label: 'BrowserHome', value: 'BrowserHome', width: 1 }],
          ],
        };
      case 'editor.actionPadKey':
        return host.actionForm;
      case 'editor.actionWindowPick':
        return { chosen: false, rule: '', title: '' };
      case 'editor.actionRun':
        return params['confirmed'] === true
          ? {
            needsConfirm: false,
            result: {
              success: true, status: 'Completed', key: '', detail: '', image: null,
              boxes: [], chosenIndex: 0, looking: '', originX: 0, originY: 0,
              frameWidth: 0, frameHeight: 0,
            },
          }
          : { needsConfirm: false, refused: '' };
      case 'editor.actionMeta': {
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
      }

    default:
      throw new Error('不认识编辑器命令 ' + method + '。');
  }
}
