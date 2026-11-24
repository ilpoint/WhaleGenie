import type { EditorCore } from './editor-core.js';
import type { EditorCatalogue, EditorSnapshot, ProjectState } from '../../../shared/bridge/protocol.js';
import { describeError } from '../../../shared/errors.js';
import { t } from '../../../shared/strings/strings.js';
import type { EditorValueService } from './value-service.js';

/** The session command family. All state lives in EditorCore; C# remains the source of truth. */
export class EditorSessionService {
  private started = false;

  private startedLoaded = false;

  private initialOpenDone = false;

  private openingFirst = false;

  private projectState: ProjectState | null = null;

  private savedTimer = 0;

  constructor(
    private readonly core: EditorCore,
    private readonly strings: { subscribe(listener: () => void): () => void },
    private readonly project: { subscribe(listener: (state: ProjectState | null) => void): () => void },
    private readonly value: EditorValueService,
  ) {}

  private async reload(): Promise<void> {
    await this.core.reload();
    this.openFirstIfNeeded();
  }

  ensureLoaded(): void {
    if (this.started) {
      return;
    }

    this.started = true;
    this.strings.subscribe(() => {
      if (this.startedLoaded) {
        void this.onLanguage();
      }
    });
    this.project.subscribe((state) => this.onProject(state));
    void this.start();
  }

  refresh(): void {
    if (!this.startedLoaded) {
      return;
    }

    void this.reload();
  }

  private async start(): Promise<void> {
    await this.loadActions();
    await this.reload();
    this.startedLoaded = true;
    this.core.state.loading = false;
    this.core.notify();
    this.openFirstIfNeeded();
  }

  private async loadActions(): Promise<void> {
    try {
      const catalogue = await this.core.client.call<EditorCatalogue>('editor.sessionActions');
      this.core.state.actions = catalogue.actions;
      this.core.state.categories = catalogue.categories;
      this.core.state.publicMacros = catalogue.publicMacros;
      if (this.core.state.selectedActionKey.length > 0
          && !catalogue.actions.some((action) => action.key === this.core.state.selectedActionKey)) {
        this.core.state.selectedActionKey = '';
      }
    } catch (error) {
      this.core.state.error = describeError(error);
    }

    this.core.notify();
  }

  private onProject(state: ProjectState | null): void {
    this.projectState = state;
    if (!this.core.state.loading && this.core.state.snapshot === null) {
      this.openFirstIfNeeded();
    }
  }

  private openFirstIfNeeded(): void {
    if (this.initialOpenDone || this.openingFirst || this.core.state.snapshot !== null
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

  async open(name: string): Promise<boolean> {
    this.core.state.actionForm = null;
    this.core.state.actionError = '';
    this.value.closeValue();
    const ok = await this.core.snapshotCommand('editor.session', { kind: 'open', name });
    if (ok) {
      this.core.state.selectedStepId = '';
      this.core.state.selectedStepIds = [];
      this.core.state.inspectorTab = 'variables';
      this.core.notify();
    }

    return ok;
  }

  async newDocument(): Promise<boolean> {
    this.core.state.actionForm = null;
    this.core.state.actionError = '';
    this.value.closeValue();
    const ok = await this.core.snapshotCommand('editor.session', { kind: 'new' });
    if (ok) {
      this.core.state.selectedStepId = '';
      this.core.state.selectedStepIds = [];
      this.core.state.inspectorTab = 'macro';
      this.core.notify();
    }

    return ok;
  }

  async openSubmacro(name: string): Promise<boolean> {
    this.core.state.actionForm = null;
    this.core.state.actionError = '';
    this.value.closeValue();
    const ok = await this.core.snapshotCommand('editor.session', { kind: 'subOpen', name });
    if (ok) {
      this.core.state.selectedStepId = '';
      this.core.state.selectedStepIds = [];
      this.core.notify();
    }

    return ok;
  }

  async activateTab(key: string): Promise<boolean> {
    this.core.state.actionForm = null;
    this.core.state.actionError = '';
    this.value.closeValue();
    if (key === this.core.state.activeKey) {
      return true;
    }

    const ok = await this.core.snapshotCommand('editor.session', { kind: 'activate', key });
    if (ok) {
      this.core.state.selectedStepId = '';
      this.core.state.selectedStepIds = [];
      this.core.notify();
    }

    return ok;
  }

  requestCloseTab(key: string): void {
    const tab = this.core.state.tabs.find((candidate) => candidate.key === key);
    if (!tab) {
      return;
    }

    if (tab.dirty) {
      this.core.state.closeRequest = tab;
      this.core.notify();
      return;
    }

    void this.closeTab(key);
  }

  cancelClose(): void {
    this.core.state.closeRequest = null;
    this.core.notify();
  }

  async confirmClose(): Promise<void> {
    const tab = this.core.state.closeRequest;
    if (!tab) {
      return;
    }

    this.core.state.closeRequest = null;
    this.core.notify();
    if (tab.key !== this.core.state.activeKey && !(await this.activateTab(tab.key))) {
      return;
    }

    if (!(await this.save())) {
      return;
    }

    await this.closeTab(tab.key);
  }

  private async closeTab(key: string): Promise<void> {
    const ok = await this.core.mutate<EditorSnapshot | null>('editor.session',
      { kind: 'close', key },
      (value) => this.core.applySnapshot(value));
    if (ok) {
      this.core.state.selectedStepId = '';
      this.core.state.selectedStepIds = [];
      this.core.notify();
    }
  }

  setDraft(field: 'name' | 'description' | 'group', value: string): void {
    if (field === 'name') {
      this.core.state.draftName = value;
    } else if (field === 'description') {
      this.core.state.draftDescription = value;
    } else {
      this.core.state.draftGroup = value;
    }

    this.core.notify();
  }

  async commitDrafts(): Promise<boolean> {
    const snapshot = this.core.state.snapshot;
    if (!snapshot) {
      return false;
    }

    if (this.core.state.draftName === snapshot.name
        && this.core.state.draftDescription === snapshot.description
        && this.core.state.draftGroup === snapshot.group) {
      return true;
    }

    return this.core.snapshotCommand('editor.session', {
      kind: 'patch',
      name: this.core.state.draftName,
      description: this.core.state.draftDescription,
      group: this.core.state.draftGroup,
    });
  }

  async exportDocument(): Promise<void> {
    const snapshot = this.core.state.snapshot;
    if (!snapshot) {
      return;
    }

    try {
      const path = await this.core.client.call<string | null>('dialog.pickSavePackage', {
        suggested: snapshot.name + '.wgmacro',
      });
      if (!path) {
        return;
      }

      await this.core.client.call<boolean>('editor.session', { kind: 'export', path });
      this.core.state.notice = t('Editor.Exported');
      this.core.state.error = '';
    } catch (error) {
      this.core.state.error = describeError(error);
    }

    this.core.notify();
  }

  async save(): Promise<boolean> {
    if (this.core.state.snapshot === null) {
      return false;
    }

    if (!(await this.commitDrafts())) {
      return false;
    }

    const ok = await this.core.snapshotCommand('editor.session', { kind: 'save' });
    if (ok) {
      this.core.state.saved = true;
      this.core.notify();
      window.clearTimeout(this.savedTimer);
      this.savedTimer = window.setTimeout(() => {
        this.core.state.saved = false;
        this.core.notify();
      }, 2500);
    }

    return ok;
  }

  async run(): Promise<void> {
    const snapshot = this.core.state.snapshot;
    if (!snapshot || snapshot.isSubMacro || snapshot.steps.length === 0) {
      return;
    }

    if (!(await this.save())) {
      return;
    }

    try {
      await this.core.client.call('app.openRun', { name: snapshot.name });
    } catch (error) {
      this.core.fail(error);
    }
  }

  async setRecordOption(field: string, value: string): Promise<boolean> {
    return this.core.snapshotCommand('editor.record', { kind: 'option', field, value });
  }

  async setDelayScale(factor: number): Promise<boolean> {
    return this.core.snapshotCommand('editor.record', { kind: 'delayScale', factor });
  }

  async setMacroSetting(field: string, value: string): Promise<boolean> {
    return this.core.snapshotCommand('editor.session', { kind: 'macroSetting', field, value });
  }
}
