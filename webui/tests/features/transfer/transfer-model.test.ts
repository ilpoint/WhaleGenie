import { describe, expect, it } from 'vitest';
import {
  allChecked,
  applyExport,
  applyImport,
  chosenNames,
  conflictDecisions,
  exportModel,
  importApplyParams,
  importDestination,
  importModel,
  onlySubMacros,
  setAll,
  setDecision,
  startExport,
  startImport,
  toggleRow,
  type TransferCaller,
} from '../../../src/features/transfer/transfer-model.js';
import type {
  BridgeMethod,
  ProjectExportPlan,
  ProjectImportPlan,
  TransferMacro,
} from '../../../src/shared/bridge/protocol.js';

function macro(name: string, isSub = false): TransferMacro {
  return { name, kind: '', steps: 1, problem: '', isSub };
}

function importPlan(): ProjectImportPlan {
  return {
    source: '/incoming.wgmacro',
    fileName: 'incoming.wgmacro',
    macros: [macro('父'), macro('子', true), macro('另')],
    globals: [
      { name: '计数', type: '数字', conflicts: true },
      { name: '新名', type: '文本', conflicts: false },
    ],
  };
}

function exportPlan(): ProjectExportPlan {
  return { suggestedName: '父.wgmacro', macros: [macro('父'), macro('子', true)] };
}

class Recorder implements TransferCaller {
  readonly calls: { method: BridgeMethod; params?: unknown }[] = [];

  constructor(private readonly answers: Record<string, unknown>) {}

  call<T = void>(method: BridgeMethod, params?: unknown): Promise<T> {
    this.calls.push({ method, params });
    return Promise.resolve(this.answers[method] as T);
  }
}

describe('transfer selection', () => {
  it('covers a macro\'s submacro and takes both away with one tick', () => {
    const model = importModel(importPlan());
    expect(chosenNames(model)).toEqual([]);

    const ticked = toggleRow(model, 0, true);
    expect(ticked.rows[1]!.checked).toBe(true);
    expect(ticked.rows[1]!.covered).toBe(true);
    expect(chosenNames(ticked)).toEqual(['父']);
    expect(onlySubMacros(ticked)).toBe(false);
    expect(importDestination(ticked)).toBe('project');

    // A covered row cannot be changed on its own; the macro above owns it.
    expect(toggleRow(ticked, 1, false)).toBe(ticked);

    const cleared = toggleRow(ticked, 0, false);
    expect(cleared.rows[1]!.checked).toBe(false);
    expect(cleared.rows[1]!.covered).toBe(false);
    expect(chosenNames(cleared)).toEqual([]);
  });

  it('ticks every macro from the one box and covers their submacros', () => {
    const model = setAll(importModel(importPlan()), true);

    expect(allChecked(model)).toBe(true);
    expect(chosenNames(model)).toEqual(['父', '另']);
    expect(model.rows[1]!.covered).toBe(true);

    const cleared = setAll(model, false);
    expect(allChecked(cleared)).toBe(false);
    expect(chosenNames(cleared)).toEqual([]);
  });

  it('hands a submacro ticked on its own to the public library', () => {
    const model = toggleRow(importModel(importPlan()), 1, true);

    expect(chosenNames(model)).toEqual(['子']);
    expect(onlySubMacros(model)).toBe(true);
    expect(importDestination(model)).toBe('library');
  });

  it('defaults a conflict to a rename and records the chosen answer', () => {
    const model = importModel(importPlan());
    expect(model.conflicts.map((global) => global.name)).toEqual(['计数']);
    expect(conflictDecisions(model)).toEqual([{ name: '计数', action: 'rename' }]);

    const kept = setDecision(model, '计数', 'keep');
    expect(conflictDecisions(kept)).toEqual([{ name: '计数', action: 'keep' }]);
  });
});

describe('transfer commands', () => {
  it('imports by picking, reading the plan, then applying the ticks', async () => {
    const recorder = new Recorder({
      'dialog.pickOpenPackage': '/incoming.wgmacro',
      'project.importPlan': importPlan(),
      'project.importApply': { report: 'done', state: {} },
    });
    const session = await startImport(recorder);
    expect(recorder.calls.map((call) => call.method)).toEqual([
      'dialog.pickOpenPackage',
      'project.importPlan',
    ]);
    expect(recorder.calls[1]!.params).toEqual({ path: '/incoming.wgmacro' });
    expect(session).not.toBeNull();

    const model = setDecision(toggleRow(importModel(session!.plan), 0, true), '计数', 'keep');
    await applyImport(recorder, session!, model);

    expect(recorder.calls.map((call) => call.method)).toEqual([
      'dialog.pickOpenPackage',
      'project.importPlan',
      'project.importApply',
    ]);
    expect(recorder.calls[2]!.params).toEqual({
      path: '/incoming.wgmacro',
      names: ['父'],
      decisions: [{ name: '计数', action: 'keep' }],
    });
  });

  it('stops an import when the picker is called off', async () => {
    const recorder = new Recorder({ 'dialog.pickOpenPackage': null });

    expect(await startImport(recorder)).toBeNull();
    expect(recorder.calls.map((call) => call.method)).toEqual(['dialog.pickOpenPackage']);
  });

  it('exports by planning, asking where, then writing the ticks', async () => {
    const recorder = new Recorder({
      'project.exportPlan': exportPlan(),
      'dialog.pickSavePackage': '/outgoing.wgmacro',
      'project.exportApply': { path: '/outgoing.wgmacro' },
    });
    const session = await startExport(recorder);
    expect(recorder.calls.map((call) => call.method)).toEqual(['project.exportPlan']);

    const model = toggleRow(exportModel(session.plan), 0, true);
    const result = await applyExport(recorder, session, model);

    expect(recorder.calls.map((call) => call.method)).toEqual([
      'project.exportPlan',
      'dialog.pickSavePackage',
      'project.exportApply',
    ]);
    expect(recorder.calls[1]!.params).toEqual({ suggested: '父.wgmacro' });
    expect(recorder.calls[2]!.params).toEqual({
      path: '/outgoing.wgmacro',
      names: ['父'],
    });
    expect(result).toEqual({ path: '/outgoing.wgmacro' });
  });

  it('writes nothing when the save dialog is called off', async () => {
    const recorder = new Recorder({
      'project.exportPlan': exportPlan(),
      'dialog.pickSavePackage': null,
    });
    const session = await startExport(recorder);

    expect(await applyExport(recorder, session, exportModel(session.plan))).toBeNull();
    expect(recorder.calls.map((call) => call.method)).toEqual([
      'project.exportPlan',
      'dialog.pickSavePackage',
    ]);
  });

  it('builds the apply parameters from the model', () => {
    const model = toggleRow(importModel(importPlan()), 1, true);

    expect(importApplyParams(model, '/incoming.wgmacro')).toEqual({
      path: '/incoming.wgmacro',
      names: ['子'],
      decisions: [{ name: '计数', action: 'rename' }],
    });
    expect(chosenNames(model)).toEqual(['子']);
  });
});