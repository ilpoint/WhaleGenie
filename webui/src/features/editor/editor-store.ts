import { bridge } from '../../shared/bridge/bridge.js';
import type {
  EditorAction,
  EditorActionCategory,
  EditorActionForm,
  EditorCatalogue,
  EditorConditionNode,
  EditorParameter,
  EditorSnapshot,
  EditorStep,
  EditorTab,
  EditorValueOutcome,
  EditorValueSnapshot,
  EditorPublicMacro,
  ProjectState,
} from '../../shared/bridge/protocol.js';
import { project } from '../../shared/project/store.js';
import { strings, t } from '../../shared/strings/strings.js';
import { findStep, flattenSteps, type PaletteSource } from './editor-model.js';

export type InspectorTab = 'step' | 'variables' | 'macro';

export interface EditorUiState {
  snapshot: EditorSnapshot | null;
  tabs: EditorTab[];
  activeKey: string;
  actions: EditorAction[];
  categories: EditorActionCategory[];
  publicMacros: EditorPublicMacro[];
  selectedStepId: string;
  selectedStepIds: string[];
  selectedActionKey: string;
  collapsedSteps: Set<string>;
  paletteSource: PaletteSource;
  category: string | null;
  search: string;
  inspectorTab: InspectorTab;
  inspectorVisible: boolean;
  inspectorWidth: number;
  recording: boolean;
  captured: number;
  error: string;
  notice: string;
  saved: boolean;
  closeRequest: EditorTab | null;
  loading: boolean;
  draftName: string;
  draftDescription: string;
  draftGroup: string;
  valueEditor: EditorValueSnapshot | null;
  valueError: string;
  stepEditorOpen: boolean;
  actionForm: EditorActionForm | null;
  actionPage: string;
  actionError: string;
}

export function describeError(error: unknown): string {
  if (typeof error === 'string') {
    return error;
  }

  if (error && typeof error === 'object' && 'message' in error) {
    const message = (error as { message: unknown }).message;
    return typeof message === 'string' ? message : String(message);
  }

  return String(error);
}

const initial: EditorUiState = {
  snapshot: null,
  tabs: [],
  activeKey: '',
  actions: [],
  categories: [],
  publicMacros: [],
  selectedStepId: '',
  selectedStepIds: [],
  selectedActionKey: '',
  collapsedSteps: new Set<string>(),
  paletteSource: 'basic',
  category: null,
  search: '',
  inspectorTab: 'variables',
  // The default window is 1100 wide; the column is a nuisance below 1000 and the toolbar can
  // bring it back. The choice is kept for the rest of the session once the person toggles it.
  inspectorVisible: window.innerWidth >= 1000,
  inspectorWidth: 360,
  recording: false,
  captured: 0,
  error: '',
  notice: '',
  saved: false,
  closeRequest: null,
  loading: true,
  draftName: '',
  draftDescription: '',
  draftGroup: '',
  valueEditor: null,
  valueError: '',
  stepEditorOpen: false,
  actionForm: null,
  actionPage: 'base',
  actionError: '',
};

/**
 * The editor's page-side state and commands. C# remains the only owner of the macro; this store
 * mirrors the snapshot and the open documents, keeps the purely visual choices (selection, folds,
 * palette filter, inspector tab) and sends every edit through the bridge.
 */
class EditorStore {
  private readonly state: EditorUiState = { ...initial, collapsedSteps: new Set<string>() };

  private readonly listeners = new Set<() => void>();

  private started = false;

  private startedLoaded = false;

  private initialOpenDone = false;

  private openingFirst = false;

  private projectState: ProjectState | null = null;

  private recordTimer = 0;

  private savedTimer = 0;

  get(): EditorUiState {
    return this.state;
  }

  subscribe(listener: () => void): () => void {
    this.listeners.add(listener);
    listener();
    return () => this.listeners.delete(listener);
  }

  /** The page and the palette both call this; the first call restores the C# workspace. */
  ensureLoaded(): void {
    if (this.started) {
      return;
    }

    this.started = true;
    strings.subscribe(() => {
      if (this.startedLoaded) {
        void this.onLanguage();
      }
    });
    project.subscribe((state) => this.onProject(state));
    void this.start();
  }

  /** The editor route mounted again: pick up a document another page opened in the meantime. */
  refresh(): void {
    if (!this.startedLoaded) {
      return;
    }

    void this.reload();
  }

  /** Leaving the editor must not leave a recording running with no way to stop it. */
  leaveEditor(): void {
    this.state.actionForm = null;
    this.state.actionError = '';
    this.closeValue();
    if (this.state.recording) {
      void this.stopRecording();
    }

    window.clearInterval(this.recordTimer);
    this.recordTimer = 0;
  }

  clearError(): void {
    if (this.state.error.length > 0) {
      this.state.error = '';
      this.notify();
    }
  }

  setError(message: string): void {
    this.state.error = message;
    this.notify();
  }

  private async start(): Promise<void> {
    await this.loadActions();
    await this.reload();
    this.startedLoaded = true;
    this.state.loading = false;
    this.notify();
    this.openFirstIfNeeded();
  }

  private notify(): void {
    for (const listener of this.listeners) {
      listener();
    }
  }

  private async loadActions(): Promise<void> {
    try {
      const catalogue = await bridge.value.call<EditorCatalogue>('editor.sessionActions');
      this.state.actions = catalogue.actions;
      this.state.categories = catalogue.categories;
      this.state.publicMacros = catalogue.publicMacros;
      if (this.state.selectedActionKey.length > 0
          && !catalogue.actions.some((action) => action.key === this.state.selectedActionKey)) {
        this.state.selectedActionKey = '';
      }
    } catch (error) {
      this.state.error = describeError(error);
    }

    this.notify();
  }

  private async reload(): Promise<void> {
    try {
      const snapshot = await bridge.value.call<EditorSnapshot | null>('editor.sessionState');
      const tabs = await bridge.value.call<{ activeKey: string | null; tabs: EditorTab[] }>(
        'editor.sessionTabs');
      this.state.tabs = tabs.tabs;
      this.state.activeKey = tabs.activeKey ?? '';
      if (snapshot) {
        this.applySnapshot(snapshot);
      } else if (tabs.activeKey) {
        const activated = await bridge.value.call<EditorSnapshot>('editor.sessionActivate',
          { key: tabs.activeKey });
        this.applySnapshot(activated);
      } else {
        this.applySnapshot(null);
      }
    } catch (error) {
      this.state.error = describeError(error);
    }

    this.notify();
    this.openFirstIfNeeded();
  }

  private onProject(state: ProjectState | null): void {
    this.projectState = state;
    if (!this.state.loading && this.state.snapshot === null) {
      this.openFirstIfNeeded();
    }
  }

  private openFirstIfNeeded(): void {
    if (this.initialOpenDone || this.openingFirst || this.state.snapshot !== null
        || this.projectState === null) {
      return;
    }

    this.openingFirst = true;
    this.initialOpenDone = true;
    const first = this.projectState.macros[0];
    if (!first) {
      this.openingFirst = false;
      return;
    }

    void this.open(first.name).finally(() => {
      this.openingFirst = false;
    });
  }

  private async onLanguage(): Promise<void> {
    await this.loadActions();
    await this.reload();
  }

  private applySnapshot(snapshot: EditorSnapshot | null): void {
    this.state.snapshot = snapshot;
    if (snapshot === null) {
      this.state.selectedStepId = '';
      this.state.selectedStepIds = [];
      this.state.draftName = '';
      this.state.draftDescription = '';
      this.state.draftGroup = '';
      return;
    }

    this.state.draftName = snapshot.name;
    this.state.draftDescription = snapshot.description;
    this.state.draftGroup = snapshot.group;
    this.state.selectedStepIds = this.state.selectedStepIds
      .filter((id) => findStep(snapshot.steps, id) !== null);
    if (this.state.selectedStepId.length > 0
        && findStep(snapshot.steps, this.state.selectedStepId) === null) {
      this.state.selectedStepId = this.state.selectedStepIds.at(-1) ?? '';
    }

    if (this.state.selectedStepId.length > 0
        && !this.state.selectedStepIds.includes(this.state.selectedStepId)) {
      this.state.selectedStepIds = [this.state.selectedStepId];
    }
  }

  private fail(error: unknown): void {
    this.state.error = describeError(error);
    this.notify();
  }

  private async mutate<T>(method: string, params: unknown,
    apply: (value: T) => void): Promise<boolean> {
    try {
      const value = await bridge.value.call<T>(method, params);
      apply(value);
      this.state.error = '';
      this.notify();
      return true;
    } catch (error) {
      this.fail(error);
      return false;
    }
  }

  private async snapshotCommand(method: string, params?: unknown): Promise<boolean> {
    const ok = await this.mutate<EditorSnapshot | null>(method, params,
      (value) => this.applySnapshot(value));
    if (ok) {
      await this.refreshTabs();
      if (this.state.actionForm) {
        await this.refreshActionForm();
        this.notify();
      }
    }

    return ok;
  }

  private async refreshActionForm(): Promise<void> {
    try {
      const form = await bridge.value.call<EditorActionForm>('editor.actionState');
      this.state.actionForm = form.open ? form : null;
    } catch {
      // A closed document has no form; the next snapshot handles the state.
    }
  }

  private async refreshTabs(): Promise<void> {
    try {
      const tabs = await bridge.value.call<{ activeKey: string | null; tabs: EditorTab[] }>(
        'editor.sessionTabs');
      this.state.tabs = tabs.tabs;
      this.state.activeKey = tabs.activeKey ?? '';
      this.notify();
    } catch (error) {
      this.fail(error);
    }
  }

  async open(name: string): Promise<boolean> {
    this.state.actionForm = null;
    this.state.actionError = '';
    this.closeValue();
    const ok = await this.snapshotCommand('editor.sessionOpen', { name });
    if (ok) {
      this.state.selectedStepId = '';
      this.state.selectedStepIds = [];
      this.state.inspectorTab = 'variables';
      this.notify();
    }

    return ok;
  }

  async newDocument(): Promise<boolean> {
    this.state.actionForm = null;
    this.state.actionError = '';
    this.closeValue();
    const ok = await this.snapshotCommand('editor.sessionNew', {});
    if (ok) {
      this.state.selectedStepId = '';
      this.state.selectedStepIds = [];
      this.state.inspectorTab = 'macro';
      this.notify();
    }

    return ok;
  }

  async openSubmacro(name: string): Promise<boolean> {
    this.state.actionForm = null;
    this.state.actionError = '';
    this.closeValue();
    const ok = await this.snapshotCommand('editor.sessionSubOpen', { name });
    if (ok) {
      this.state.selectedStepId = '';
      this.state.selectedStepIds = [];
      this.notify();
    }

    return ok;
  }

  async activateTab(key: string): Promise<boolean> {
    this.state.actionForm = null;
    this.state.actionError = '';
    this.closeValue();
    if (key === this.state.activeKey) {
      return true;
    }

    const ok = await this.snapshotCommand('editor.sessionActivate', { key });
    if (ok) {
      this.state.selectedStepId = '';
      this.state.selectedStepIds = [];
      this.notify();
    }

    return ok;
  }

  requestCloseTab(key: string): void {
    const tab = this.state.tabs.find((candidate) => candidate.key === key);
    if (!tab) {
      return;
    }

    if (tab.dirty) {
      this.state.closeRequest = tab;
      this.notify();
      return;
    }

    void this.closeTab(key);
  }

  cancelClose(): void {
    this.state.closeRequest = null;
    this.notify();
  }

  async confirmClose(): Promise<void> {
    const tab = this.state.closeRequest;
    if (!tab) {
      return;
    }

    this.state.closeRequest = null;
    this.notify();
    if (tab.key !== this.state.activeKey && !(await this.activateTab(tab.key))) {
      return;
    }

    if (!(await this.save())) {
      return;
    }

    await this.closeTab(tab.key);
  }

  private async closeTab(key: string): Promise<void> {
    const ok = await this.mutate<EditorSnapshot | null>('editor.sessionClose', { key },
      (value) => this.applySnapshot(value));
    if (ok) {
      await this.refreshTabs();
      this.state.selectedStepId = '';
      this.state.selectedStepIds = [];
      this.notify();
    }
  }

  setDraft(field: 'name' | 'description' | 'group', value: string): void {
    if (field === 'name') {
      this.state.draftName = value;
    } else if (field === 'description') {
      this.state.draftDescription = value;
    } else {
      this.state.draftGroup = value;
    }

    this.notify();
  }

  async commitDrafts(): Promise<boolean> {
    const snapshot = this.state.snapshot;
    if (!snapshot) {
      return false;
    }

    if (this.state.draftName === snapshot.name
        && this.state.draftDescription === snapshot.description
        && this.state.draftGroup === snapshot.group) {
      return true;
    }

    return this.snapshotCommand('editor.sessionPatch', {
      name: this.state.draftName,
      description: this.state.draftDescription,
      group: this.state.draftGroup,
    });
  }

  async exportDocument(): Promise<void> {
    const snapshot = this.state.snapshot;
    if (!snapshot) {
      return;
    }

    try {
      const path = await bridge.value.call<string | null>('dialog.pickSavePackage', {
        suggestedName: snapshot.name + '.wgmacro',
      });
      if (!path) {
        return;
      }

      await bridge.value.call<boolean>('editor.sessionExport', { path });
      this.state.notice = t('Editor.Exported');
      this.state.error = '';
    } catch (error) {
      this.state.error = describeError(error);
    }

    this.notify();
  }

  async save(): Promise<boolean> {
    if (this.state.snapshot === null) {
      return false;
    }

    if (!(await this.commitDrafts())) {
      return false;
    }

    const ok = await this.snapshotCommand('editor.sessionSave', {});
    if (ok) {
      this.state.saved = true;
      this.notify();
      window.clearTimeout(this.savedTimer);
      this.savedTimer = window.setTimeout(() => {
        this.state.saved = false;
        this.notify();
      }, 2500);
    }

    return ok;
  }

  async run(): Promise<void> {
    const snapshot = this.state.snapshot;
    if (!snapshot || snapshot.isSubMacro || snapshot.steps.length === 0) {
      return;
    }

    if (!(await this.save())) {
      return;
    }

    try {
      await bridge.value.call('app.openRun', { name: snapshot.name });
    } catch (error) {
      this.fail(error);
    }
  }

  openStepEditor(): void {
    const id = this.state.selectedStepIds.length === 1 ? this.state.selectedStepId : '';
    if (id.length > 0) {
      void this.openStepAction(id);
    }
  }

  closeStepEditor(): void {
    void this.cancelAction();
  }

  async openAction(type: string,
    target: { targetStepId?: string; before?: boolean; parentId?: string; list?: string } = {}): Promise<boolean> {
    try {
      const form = await bridge.value.call<EditorActionForm>('editor.actionOpen', {
        type,
        ...target,
      });
      this.state.actionForm = form;
      this.state.actionPage = form.pages.find((page) => page.present)?.key ?? 'base';
      this.state.actionError = '';
      this.state.stepEditorOpen = false;
      this.notify();
      return true;
    } catch (error) {
      this.state.actionError = describeError(error);
      this.state.error = this.state.actionError;
      this.notify();
      return false;
    }
  }

  async openStepAction(stepId: string): Promise<boolean> {
    try {
      const form = await bridge.value.call<EditorActionForm>('editor.actionOpen', { stepId });
      this.state.actionForm = form;
      this.state.actionPage = 'base';
      this.state.actionError = '';
      this.state.stepEditorOpen = false;
      this.notify();
      return true;
    } catch (error) {
      this.state.actionError = describeError(error);
      this.state.error = this.state.actionError;
      this.notify();
      return false;
    }
  }

  setActionPage(page: string): void {
    this.state.actionPage = page;
    this.notify();
  }

  async confirmAction(): Promise<boolean> {
    const form = this.state.actionForm;
    if (!form?.canConfirm) {
      return false;
    }

    const before = this.state.snapshot
      ? new Set(flattenSteps(this.state.snapshot.steps).map((step) => step.id))
      : new Set<string>();
    try {
      const next = await bridge.value.call<EditorSnapshot>('editor.actionConfirm');
      this.applySnapshot(next);
      this.state.actionForm = null;
      this.state.actionError = '';
      const selected = form.isEditing
        ? form.stepId
        : flattenSteps(next.steps).find((step) => !before.has(step.id))?.id ?? '';
      this.state.selectedStepId = selected;
      this.state.selectedStepIds = selected.length > 0 ? [selected] : [];
      await this.refreshTabs();
      this.notify();
      return true;
    } catch (error) {
      this.state.actionError = describeError(error);
      this.state.error = this.state.actionError;
      this.notify();
      return false;
    }
  }

  async cancelAction(): Promise<void> {
    if (!this.state.actionForm) {
      return;
    }

    try {
      await bridge.value.call<EditorSnapshot>('editor.actionCancel');
    } catch {
      // The form is page state; closing it must not be blocked by a failed discard.
    }

    this.state.actionForm = null;
    this.state.actionError = '';
    this.state.stepEditorOpen = false;
    this.notify();
  }

  selectStep(id: string, additive = false, range: string[] | null = null): void {
    if (range !== null) {
      this.state.selectedStepIds = [...new Set(range)];
      this.state.selectedStepId = id;
      this.notify();
      return;
    }

    if (!additive) {
      this.state.selectedStepIds = id.length > 0 ? [id] : [];
      this.state.selectedStepId = id;
      this.notify();
      return;
    }

    if (this.state.selectedStepIds.includes(id)) {
      this.state.selectedStepIds = this.state.selectedStepIds.filter((candidate) => candidate !== id);
      this.state.selectedStepId = this.state.selectedStepIds.at(-1) ?? '';
    } else if (id.length > 0) {
      this.state.selectedStepIds = [...this.state.selectedStepIds, id];
      this.state.selectedStepId = id;
    }

    this.notify();
  }

  selectedSteps(): string[] {
    return [...this.state.selectedStepIds];
  }

  hasStepSelection(): boolean {
    return this.state.selectedStepIds.length > 0;
  }

  canEditStep(): boolean {
    return this.state.selectedStepIds.length === 1;
  }

  selectAction(key: string): void {
    this.state.selectedActionKey = key;
    this.notify();
  }

  toggleStep(id: string): void {
    if (this.state.collapsedSteps.has(id)) {
      this.state.collapsedSteps.delete(id);
    } else {
      this.state.collapsedSteps.add(id);
    }

    this.notify();
  }

  setPaletteSource(source: PaletteSource): void {
    this.state.paletteSource = source;
    this.state.category = null;
    this.notify();
  }

  setPaletteCategory(category: string | null): void {
    this.state.category = category;
    this.notify();
  }

  setPaletteSearch(search: string): void {
    this.state.search = search;
    this.notify();
  }

  setInspectorTab(tab: InspectorTab): void {
    this.state.inspectorTab = tab;
    this.notify();
  }

  setInspectorVisible(visible: boolean): void {
    this.state.inspectorVisible = visible;
    this.notify();
  }

  setInspectorWidth(width: number): void {
    this.state.inspectorWidth = Math.max(280, Math.min(480, width));
    this.notify();
  }

  private selectedAction(): EditorAction | null {
    return this.state.actions.find((action) => action.key === this.state.selectedActionKey)
      ?? null;
  }

  async addPublicMacro(id: string): Promise<boolean> {
    const snapshot = this.state.snapshot;
    if (!snapshot) {
      return false;
    }

    const before = new Set(flattenSteps(snapshot.steps).map((step) => step.id));
    const ok = await this.mutate<EditorSnapshot>('editor.sessionPublicMacroAdd', { id }, (next) => {
      this.applySnapshot(next);
      const added = flattenSteps(next.steps).find((step) => !before.has(step.id));
      if (added) {
        this.state.selectedStepId = added.id;
        this.state.selectedStepIds = [added.id];
      }
    });

    if (ok) {
      await this.refreshTabs();
    }

    return ok;
  }

  async addStep(type: string, parentId?: string, list?: string): Promise<boolean> {
    const snapshot = this.state.snapshot;
    if (!snapshot) {
      return false;
    }

    const before = new Set(flattenSteps(snapshot.steps).map((step) => step.id));
    const params: Record<string, unknown> = { type };
    if (parentId) {
      params['parentId'] = parentId;
    }

    if (list) {
      params['list'] = list;
    }

    const ok = await this.mutate<EditorSnapshot>('editor.sessionStep', params, (next) => {
      this.applySnapshot(next);
      const added = flattenSteps(next.steps).find((step) => !before.has(step.id));
      if (added) {
        this.state.selectedStepId = added.id;
        this.state.selectedStepIds = [added.id];
        this.state.collapsedSteps.delete(parentId ?? '');
        this.state.inspectorTab = 'step';
      }
    });

    if (ok) {
      await this.refreshTabs();
    }

    return ok;
  }

  async addSelectedStep(parentId?: string, list?: string): Promise<boolean> {
    const action = this.selectedAction();
    if (!action) {
      return false;
    }

    return this.openAction(action.key, { parentId, list });
  }

  async addBranch(parentId: string): Promise<boolean> {
    return this.addStep('control.case', parentId, 'cases');
  }

  async duplicateStep(stepId?: string): Promise<boolean> {
    const steps = stepId ? [stepId] : this.selectedSteps();
    if (steps.length === 0) {
      return false;
    }

    return this.snapshotCommand('editor.sessionStepsDuplicate', { steps });
  }

  async removeStep(stepId?: string): Promise<boolean> {
    const steps = stepId ? [stepId] : this.selectedSteps();
    if (steps.length === 0) {
      return false;
    }

    const ok = await this.snapshotCommand('editor.sessionStepsRemove', { steps });
    if (ok) {
      this.state.selectedStepId = '';
      this.state.selectedStepIds = [];
      this.notify();
    }

    return ok;
  }

  async moveStep(stepId: string | undefined, delta: number): Promise<boolean> {
    const steps = stepId ? [stepId] : this.selectedSteps();
    if (steps.length === 0) {
      return false;
    }

    return this.snapshotCommand('editor.sessionStepsMoveBy', { steps, delta });
  }

  async moveStepsTo(target: string, before: boolean, ids?: string[]): Promise<boolean> {
    const steps = ids && ids.length > 0 ? ids : this.selectedSteps();
    if (steps.length === 0) {
      return false;
    }

    return this.snapshotCommand('editor.sessionStepsMove', { steps, target, before });
  }

  async groupSteps(stepId?: string): Promise<boolean> {
    const steps = stepId ? [stepId] : this.selectedSteps();
    if (steps.length === 0) {
      return false;
    }

    const ok = await this.snapshotCommand('editor.sessionStepsGroup', { steps });
    if (ok) {
      this.state.selectedStepId = '';
      this.state.selectedStepIds = [];
      this.notify();
    }

    return ok;
  }

  async liftSteps(stepId?: string): Promise<boolean> {
    const steps = stepId ? [stepId] : this.selectedSteps();
    if (steps.length === 0) {
      return false;
    }

    const ok = await this.snapshotCommand('editor.sessionStepsLift', { steps });
    if (ok) {
      this.state.selectedStepId = '';
      this.state.selectedStepIds = [];
      this.notify();
    }

    return ok;
  }

  async setStepEnabled(stepId: string | undefined, enabled: boolean): Promise<boolean> {
    const steps = stepId ? [stepId] : this.selectedSteps();
    if (steps.length === 0) {
      return false;
    }

    return this.snapshotCommand('editor.sessionStepsEnabled', { steps, enabled });
  }

  async copySteps(stepId?: string): Promise<boolean> {
    const steps = stepId ? [stepId] : this.selectedSteps();
    if (steps.length === 0) {
      return false;
    }

    try {
      const text = await bridge.value.call<string>('editor.sessionStepsCopy', { steps });
      if (text.length > 0 && navigator.clipboard) {
        void navigator.clipboard.writeText(text).catch(() => undefined);
      }
      this.state.notice = t('Palette.Copy');
      this.notify();
      return true;
    } catch (error) {
      this.fail(error);
      return false;
    }
  }

  async cutSteps(stepId?: string): Promise<boolean> {
    const steps = stepId ? [stepId] : this.selectedSteps();
    if (steps.length === 0) {
      return false;
    }

    try {
      const text = await bridge.value.call<string>('editor.sessionStepsCut', { steps });
      if (text.length > 0 && navigator.clipboard) {
        void navigator.clipboard.writeText(text).catch(() => undefined);
      }
      this.state.selectedStepId = '';
      this.state.selectedStepIds = [];
      await this.reload();
      return true;
    } catch (error) {
      this.fail(error);
      return false;
    }
  }

  async pasteSteps(after?: string): Promise<boolean> {
    try {
      await bridge.value.call<EditorSnapshot>('editor.sessionStepsPaste', {
        after: after ?? this.state.selectedStepId,
      });
      this.state.selectedStepId = '';
      this.state.selectedStepIds = [];
      await this.reload();
      return true;
    } catch (error) {
      this.fail(error);
      return false;
    }
  }


  async setStepMeta(stepId: string, field: string, value: string | number): Promise<boolean> {
    return this.snapshotCommand('editor.sessionStepMeta', {
      stepId, field, value: String(value),
    });
  }

  async openValue(stepId: string, parameter: string): Promise<void> {
    try {
      const snapshot = await bridge.value.call<EditorValueSnapshot>('editor.valueOpen', {
        step: stepId,
        parameter,
      });
      this.state.valueEditor = snapshot;
      this.state.valueError = '';
    } catch (error) {
      this.state.valueError = describeError(error);
      this.state.error = this.state.valueError;
    }

    this.notify();
  }

  async openConditionValue(stepId: string, parameter: string, path: number[],
    field: 'left' | 'right'): Promise<void> {
    await this.openValueAt({
      step: stepId,
      parameter,
      conditionField: field,
      conditionPath: path,
    });
  }

  async openRowValue(stepId: string, parameter: string, rowIndex: number,
    column: string): Promise<void> {
    await this.openValueAt({
      step: stepId,
      parameter,
      rowIndex,
      column,
    });
  }

  private async openValueAt(params: Record<string, unknown>): Promise<void> {
    try {
      const snapshot = await bridge.value.call<EditorValueSnapshot>('editor.valueOpen', params);
      this.state.valueEditor = snapshot;
      this.state.valueError = '';
    } catch (error) {
      this.state.valueError = describeError(error);
      this.state.error = this.state.valueError;
    }

    this.notify();
  }

  async valueCommand(action: string, params: Record<string, unknown> = {}): Promise<void> {
    try {
      const snapshot = await bridge.value.call<EditorValueSnapshot>('editor.valueCommand', {
        action,
        ...params,
      });
      this.state.valueEditor = snapshot;
      this.state.valueError = '';
    } catch (error) {
      this.state.valueError = describeError(error);
      this.state.error = this.state.valueError;
    }

    this.notify();
  }

  async confirmValue(): Promise<void> {
    try {
      const outcome = await bridge.value.call<EditorValueOutcome>('editor.valueConfirm');
      if (outcome.closed) {
        this.state.valueEditor = null;
        this.state.valueError = '';
        if (outcome.applied) {
          await this.reload();
          if (this.state.actionForm) {
            await this.refreshActionForm();
          }
        }
      } else {
        this.state.valueEditor = outcome.snapshot;
      }
    } catch (error) {
      this.state.valueError = describeError(error);
      this.state.error = this.state.valueError;
    }

    this.notify();
  }

  async cancelValue(): Promise<void> {
    try {
      await bridge.value.call<EditorValueOutcome>('editor.valueCancel');
    } catch (error) {
      this.state.error = describeError(error);
    }

    this.state.valueEditor = null;
    this.state.valueError = '';
    this.notify();
  }

  closeValue(): void {
    if (this.state.valueEditor) {
      this.state.valueEditor = null;
      this.state.valueError = '';
      this.notify();
    }
  }

  async patchParameter(stepId: string, name: string, value: string): Promise<boolean> {
    return this.snapshotCommand('editor.sessionParameter', { stepId, name, value });
  }

  async undo(): Promise<boolean> {
    return this.snapshotCommand('editor.sessionUndo', {});
  }

  async redo(): Promise<boolean> {
    return this.snapshotCommand('editor.sessionRedo', {});
  }

  async addParameterRow(stepId: string, name: string): Promise<boolean> {
    return this.snapshotCommand('editor.sessionParameterRowAdd', { stepId, name });
  }

  async removeParameterRow(stepId: string, name: string, index: number): Promise<boolean> {
    return this.snapshotCommand('editor.sessionParameterRowRemove', { stepId, name, index });
  }

  async patchRowCell(stepId: string, name: string, index: number, column: string,
    value: string): Promise<boolean> {
    return this.snapshotCommand('editor.sessionParameterRowCell', {
      stepId, name, index, column, value,
    });
  }

  async addConditionRow(stepId: string, name: string, path: number[] = []): Promise<boolean> {
    return this.snapshotCommand('editor.sessionConditionRowAdd', { stepId, name, path });
  }

  async addConditionGroup(stepId: string, name: string, path: number[] = []): Promise<boolean> {
    return this.snapshotCommand('editor.sessionConditionGroupAdd', { stepId, name, path });
  }

  async removeConditionNode(stepId: string, name: string, path: number[]): Promise<boolean> {
    return this.snapshotCommand('editor.sessionConditionRowRemove', { stepId, name, path });
  }

  async setConditionRow(stepId: string, name: string, path: number[], field: string,
    value: string): Promise<boolean> {
    return this.snapshotCommand('editor.sessionConditionRow', {
      stepId, name, path, field, value,
    });
  }

  async setConditionJoin(stepId: string, name: string, path: number[], join: string): Promise<boolean> {
    return this.snapshotCommand('editor.sessionConditionJoin', { stepId, name, path, join });
  }

  async setMacroSetting(field: string, value: string): Promise<boolean> {
    return this.snapshotCommand('editor.sessionMacroSetting', { field, value });
  }

  private declaration(method: string, params: Record<string, unknown>): Promise<boolean> {
    return this.snapshotCommand(method, params);
  }

  addCallableParameter(): Promise<boolean> {
    return this.declaration('editor.sessionParameterDeclAdd', {});
  }

  patchCallableParameter(index: number, field: string, value: string): Promise<boolean> {
    return this.declaration('editor.sessionParameterDeclPatch', { index, field, value });
  }

  removeCallableParameter(index: number): Promise<boolean> {
    return this.declaration('editor.sessionParameterDeclRemove', { index });
  }

  moveCallableParameter(index: number, offset: number): Promise<boolean> {
    return this.declaration('editor.sessionParameterDeclMove', { index, offset });
  }

  addCallableResult(): Promise<boolean> {
    return this.declaration('editor.sessionResultDeclAdd', {});
  }

  patchCallableResult(index: number, field: string, value: string): Promise<boolean> {
    return this.declaration('editor.sessionResultDeclPatch', { index, field, value });
  }

  removeCallableResult(index: number): Promise<boolean> {
    return this.declaration('editor.sessionResultDeclRemove', { index });
  }

  moveCallableResult(index: number, offset: number): Promise<boolean> {
    return this.declaration('editor.sessionResultDeclMove', { index, offset });
  }

  addCallableParameterField(index: number): Promise<boolean> {
    return this.declaration('editor.sessionParameterFieldAdd', { index });
  }

  patchCallableParameterField(index: number, fieldIndex: number, field: string,
    value: string): Promise<boolean> {
    return this.declaration('editor.sessionParameterFieldPatch', {
      index, fieldIndex, field, value,
    });
  }

  removeCallableParameterField(index: number, fieldIndex: number): Promise<boolean> {
    return this.declaration('editor.sessionParameterFieldRemove', { index, fieldIndex });
  }

  addCallableResultField(index: number): Promise<boolean> {
    return this.declaration('editor.sessionResultFieldAdd', { index });
  }

  patchCallableResultField(index: number, fieldIndex: number, field: string,
    value: string): Promise<boolean> {
    return this.declaration('editor.sessionResultFieldPatch', {
      index, fieldIndex, field, value,
    });
  }

  removeCallableResultField(index: number, fieldIndex: number): Promise<boolean> {
    return this.declaration('editor.sessionResultFieldRemove', { index, fieldIndex });
  }

  private trigger(method: string, params: Record<string, unknown>): Promise<boolean> {
    return this.snapshotCommand(method, params);
  }

  addTrigger(kind: string): Promise<boolean> {
    return this.trigger('editor.sessionTriggerAdd', { kind });
  }

  removeTrigger(triggerId: string): Promise<boolean> {
    return this.trigger('editor.sessionTriggerRemove', { triggerId });
  }

  setTriggerEnabled(triggerId: string, enabled: boolean): Promise<boolean> {
    return this.trigger('editor.sessionTriggerEnabled', { triggerId, enabled });
  }

  setTriggerBinding(triggerId: string, binding: string): Promise<boolean> {
    return this.trigger('editor.sessionTriggerBinding', { triggerId, binding });
  }

  setTriggerLoop(triggerId: string, loop: string): Promise<boolean> {
    return this.trigger('editor.sessionTriggerLoop', { triggerId, loop });
  }

  setTriggerOnce(triggerId: string, once: boolean): Promise<boolean> {
    return this.trigger('editor.sessionTriggerOnce', { triggerId, once });
  }

  setTriggerTimer(triggerId: string, field: string, value: string): Promise<boolean> {
    return this.trigger('editor.sessionTriggerTimer', { triggerId, field, value });
  }

  setTriggerFile(triggerId: string, field: string, value: string): Promise<boolean> {
    return this.trigger('editor.sessionTriggerFile', { triggerId, field, value });
  }

  setTriggerProcess(triggerId: string, field: string, value: string): Promise<boolean> {
    return this.trigger('editor.sessionTriggerProcess', { triggerId, field, value });
  }

  setTriggerHotstring(triggerId: string, value: string): Promise<boolean> {
    return this.trigger('editor.sessionTriggerHotstring', { triggerId, value });
  }

  setTriggerColor(triggerId: string, field: string, value: string): Promise<boolean> {
    return this.trigger('editor.sessionTriggerColor', { triggerId, field, value });
  }

  setTriggerWindow(triggerId: string, field: string, value: string): Promise<boolean> {
    return this.trigger('editor.sessionTriggerWindow', { triggerId, field, value });
  }

  setTriggerInterface(triggerId: string, field: string, value: string): Promise<boolean> {
    return this.trigger('editor.sessionTriggerInterface', { triggerId, field, value });
  }

  setTriggerIdle(triggerId: string, value: string): Promise<boolean> {
    return this.trigger('editor.sessionTriggerIdle', { triggerId, value });
  }

  addVariable(): Promise<boolean> {
    return this.snapshotCommand('editor.sessionVariableAdd', {});
  }

  patchVariable(originalName: string, field: string, value: string): Promise<boolean> {
    return this.snapshotCommand('editor.sessionVariablePatch', { originalName, field, value });
  }

  removeVariable(name: string): Promise<boolean> {
    return this.snapshotCommand('editor.sessionVariableRemove', { name });
  }

  addGlobal(): Promise<boolean> {
    return this.snapshotCommand('editor.sessionGlobalAdd', {});
  }

  patchGlobal(originalName: string, field: string, value: string): Promise<boolean> {
    return this.snapshotCommand('editor.sessionGlobalPatch', { originalName, field, value });
  }

  removeGlobal(name: string): Promise<boolean> {
    return this.snapshotCommand('editor.sessionGlobalRemove', { name });
  }

  async pickImage(stepId: string, name: string): Promise<void> {
    try {
      const picked = await bridge.value.call<{ path: string | null }>('dialog.pickImage');
      if (picked.path) {
        await this.patchParameter(stepId, name, picked.path);
      }
    } catch (error) {
      this.fail(error);
    }
  }

  async startRecording(): Promise<void> {
    try {
      const state = await bridge.value.call<{ recording: boolean; captured: number }>(
        'editor.sessionRecordStart');
      this.state.recording = state.recording;
      this.state.captured = state.captured;
      this.state.error = '';
      this.notify();
      window.clearInterval(this.recordTimer);
      if (state.recording) {
        this.recordTimer = window.setInterval(() => void this.pollRecording(), 500);
      }
    } catch (error) {
      this.fail(error);
    }
  }

  private async pollRecording(): Promise<void> {
    try {
      const state = await bridge.value.call<{
        recording?: boolean;
        captured: number;
        snapshot?: EditorSnapshot;
      }>('editor.sessionRecordState');
      this.state.captured = state.captured;
      if (state.snapshot) {
        this.applySnapshot(state.snapshot);
        await this.refreshTabs();
      }

      if (!state.recording) {
        window.clearInterval(this.recordTimer);
        this.recordTimer = 0;
        this.state.recording = false;
      }

      this.notify();
    } catch (error) {
      this.state.recording = false;
      window.clearInterval(this.recordTimer);
      this.recordTimer = 0;
      this.fail(error);
    }
  }

  async stopRecording(): Promise<void> {
    window.clearInterval(this.recordTimer);
    this.recordTimer = 0;
    try {
      const result = await bridge.value.call<{
        captured: number;
        steps: number;
        snapshot: EditorSnapshot;
      }>('editor.sessionRecordStop');
      this.state.recording = false;
      this.state.captured = result.captured;
      this.applySnapshot(result.snapshot);
      if (result.steps === 0) {
        this.state.error = t('Editor.RecordEmpty');
      }

      await this.refreshTabs();
      this.notify();
    } catch (error) {
      this.state.recording = false;
      this.fail(error);
    }
  }

  async toggleRecording(): Promise<void> {
    if (this.state.recording) {
      await this.stopRecording();
    } else {
      await this.startRecording();
    }
  }
}

export const editor = new EditorStore();

export function stepParameter(step: EditorStep, name: string): EditorParameter | null {
  return step.parameters.find((parameter) => parameter.name === name) ?? null;
}

export function conditionNodes(step: EditorStep, name: string): EditorConditionNode[] {
  return stepParameter(step, name)?.condition?.nodes ?? [];
}
