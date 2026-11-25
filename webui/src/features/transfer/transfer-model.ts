import type {
  BridgeMethod,
  ExportResult,
  ImportDecision,
  ImportResult,
  PackagePreview,
  ProjectExportPlan,
  ProjectImportPlan,
  TransferGlobal,
  TransferMacro,
} from '../../shared/bridge/protocol.js';

/** Which way the package is being used: opened, imported, or composed for export. */
export type TransferMode = 'open' | 'import' | 'export';

/** What a person may answer about a global both sides declare. */
export type ConflictAction = 'keep' | 'overwrite' | 'rename';

/** One macro row of the wizard. Covered means a ticked macro above it carries it along. */
export interface TransferRow {
  macro: TransferMacro;
  checked: boolean;
  covered: boolean;
}

/** The wizard's selection, its conflict answers, and the rows it draws. */
export interface TransferModel {
  mode: TransferMode;
  preview: PackagePreview;
  rows: TransferRow[];
  conflicts: TransferGlobal[];
  decisions: Record<string, ConflictAction>;
}

/** The calls the flows make. A test hands in a recorder instead of the bridge. */
export interface TransferCaller {
  call<T = void>(method: BridgeMethod, params?: unknown): Promise<T>;
}

/** An import starts with the picked file already read; the ticks are asked for next. */
export interface ImportTransfer {
  mode: 'import';
  path: string;
  plan: ProjectImportPlan;
}

/** An export starts with the list the project could give; the file is picked before writing. */
export interface ExportTransfer {
  mode: 'export';
  plan: ProjectExportPlan;
}

/** A package opened as a preview; the user may open it or import it after seeing its contents. */
export interface OpenTransfer {
  mode: 'open';
  intent: 'open' | 'library';
  path: string;
  plan: ProjectImportPlan;
}

export type Transfer = ImportTransfer | ExportTransfer | OpenTransfer;

/**
 * The rows that count: ticked and not carried by a ticked macro above. This is the one rule the
 * file depends on, so it is worked out here rather than in the dialog's click handler.
 */
export function chosenNames(model: TransferModel): string[] {
  return model.rows.filter((row) => row.checked && !row.covered).map((row) => row.macro.name);
}

/** True when what was ticked is all submacros, which is what goes to the public library. */
export function onlySubMacros(model: TransferModel): boolean {
  const chosen = model.rows.filter((row) => row.checked && !row.covered);
  return chosen.length > 0 && chosen.every((row) => row.macro.isSub);
}

/** The answer to every conflict, defaulting to a rename: the one that keeps both meanings. */
export function conflictDecisions(model: TransferModel): ImportDecision[] {
  return model.conflicts.map((global) => ({
    name: global.name,
    action: model.decisions[global.name] ?? 'rename',
  }));
}

/** A model for a package that is about to come in. Nothing is ticked, as the window did. */
export function importModel(plan: ProjectImportPlan): TransferModel {
  return {
    mode: 'import',
    preview: plan.preview,
    rows: build(plan.macros, []),
    conflicts: plan.globals.filter((global) => global.conflicts),
    decisions: Object.fromEntries(
      plan.globals.filter((global) => global.conflicts)
        .map((global) => [global.name, 'rename' as ConflictAction]),
    ),
  };
}

/** A package opened before deciding: the same content, with no ticks to collect yet. */
export function openModel(plan: ProjectImportPlan): TransferModel {
  return {
    mode: 'open',
    preview: plan.preview,
    rows: build(plan.macros, []),
    conflicts: plan.globals.filter((global) => global.conflicts),
    decisions: {},
  };
}

/** A model for the macros that could leave. A card's macro starts ticked. */
export function exportModel(plan: ProjectExportPlan, preselect: readonly string[] = []): TransferModel {
  return {
    mode: 'export',
    preview: plan.preview,
    rows: build(plan.macros, preselect),
    conflicts: [],
    decisions: {},
  };
}

/**
 * Ticks or unticks one row. A macro takes every submacro under it in either direction; a submacro
 * changes nothing but itself, which is how one piece of a long macro is handed over alone.
 */
export function toggleRow(model: TransferModel, index: number, checked: boolean): TransferModel {
  const rows = model.rows.map((row) => ({ ...row }));
  const row = rows[index];
  if (!row || row.covered) {
    return model;
  }

  row.checked = checked;
  if (!row.macro.isSub) {
    for (let next = index + 1; next < rows.length; next++) {
      const depth = rows[next]!.macro.depth ?? 0;
      if (depth === 0) {
        break;
      }

      rows[next]!.checked = checked;
      rows[next]!.covered = checked;
    }
  }

  return { ...model, rows };
}

/** Ticks or unticks every macro; submacros follow from their parents. */
export function setAll(model: TransferModel, checked: boolean): TransferModel {
  const rows = model.rows.map((row) => ({
    ...row,
    checked,
    covered: (row.macro.depth ?? 0) > 0 ? checked : false,
  }));
  return { ...model, rows };
}

/** True when every macro is ticked; submacros follow from their parents. */
export function allChecked(model: TransferModel): boolean {
  const roots = model.rows.filter((row) => !row.macro.isSub);
  return roots.length > 0 && roots.every((row) => row.checked);
}

/** Records the answer to one global conflict. */
export function setDecision(
  model: TransferModel, name: string, action: ConflictAction,
): TransferModel {
  return { ...model, decisions: { ...model.decisions, [name]: action } };
}

/** Where an import writes: into the project, or into the public library for lone submacros. */
export function importDestination(model: TransferModel): 'project' | 'library' {
  return onlySubMacros(model) ? 'library' : 'project';
}

/** The parameters of the write an import performs. */
export function importApplyParams(
  model: TransferModel, path: string,
): { path: string; names: string[]; decisions: ImportDecision[] } {
  return { path, names: chosenNames(model), decisions: conflictDecisions(model) };
}

/** What the open flow asks first: the package is read once and shown before any action. */
export async function startOpen(calls: TransferCaller): Promise<OpenTransfer | null> {
  const path = await calls.call<string | null>('dialog.pickOpenPackage');
  if (!path) {
    return null;
  }

  const plan = await calls.call<ProjectImportPlan>('project.preview', { path });
  return { mode: 'open', intent: 'open', path, plan };
}

/** The library page's import opens the same preview, with merging as its primary action. */
export async function startLibraryImport(calls: TransferCaller): Promise<OpenTransfer | null> {
  const path = await calls.call<string | null>('dialog.pickOpenPackage');
  if (!path) {
    return null;
  }

  const plan = await calls.call<ProjectImportPlan>('project.preview', { path });
  return { mode: 'open', intent: 'library', path, plan };
}

/** What the import flow asks first, then what it reads. */
export async function startImport(calls: TransferCaller): Promise<ImportTransfer | null> {
  const path = await calls.call<string | null>('dialog.pickOpenPackage');
  if (!path) {
    return null;
  }

  const plan = await calls.call<ProjectImportPlan>('project.preview', { path });
  return { mode: 'import', path, plan };
}

/** Writes the ticks the dialog collected and answers what the import did. */
export function applyImport(
  calls: TransferCaller, transfer: ImportTransfer, model: TransferModel,
): Promise<ImportResult> {
  return calls.call<ImportResult>('project.importApply',
    importApplyParams(model, transfer.path));
}

/**
 * What the export flow reads first. The file is picked only once the ticks are known, so the save
 * dialog opens with a name and a list that are the ones about to be written.
 */
export async function startExport(calls: TransferCaller): Promise<ExportTransfer> {
  const plan = await calls.call<ProjectExportPlan>('project.exportPlan');
  return { mode: 'export', plan };
}

/** Asks where the package goes, then writes the ticks there; a dismissed dialog writes nothing. */
export async function applyExport(
  calls: TransferCaller, transfer: ExportTransfer, model: TransferModel,
): Promise<ExportResult | null> {
  const path = await calls.call<string | null>('dialog.pickSavePackage', {
    suggested: transfer.plan.suggestedName,
  });
  if (!path) {
    return null;
  }

  return calls.call<ExportResult>('project.exportApply', { path, names: chosenNames(model) });
}

/**
 * Rows from the program's flat list, which is written the way a tree is read: a macro and then the
 * submacros inside it. A submacro after a ticked macro is covered by it; the next macro ends that
 * block, so no parent field has to travel over the wire.
 */
function build(macros: readonly TransferMacro[], preselect: readonly string[]): TransferRow[] {
  const wanted = new Set(preselect.map((name) => name.trim().toLocaleLowerCase()));
  const rows: TransferRow[] = macros.map((macro) => ({
    macro,
    checked: wanted.has(macro.name.trim().toLocaleLowerCase()),
    covered: false,
  }));

  rows.forEach((row, index) => {
    if (row.macro.isSub || !row.checked) {
      return;
    }

    for (let next = index + 1; next < rows.length; next++) {
      if ((rows[next]!.macro.depth ?? 0) === 0) {
        break;
      }

      rows[next]!.checked = true;
      rows[next]!.covered = true;
    }
  });

  return rows;
}
