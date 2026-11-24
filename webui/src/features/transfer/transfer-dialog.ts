import { LitElement, html, nothing, type TemplateResult } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import { bridge } from '../../shared/bridge/bridge.js';
import { describeError } from '../../shared/errors.js';
import { strings, t } from '../../shared/strings/strings.js';
import { StoreController } from '../../shared/lit/store-controller.js';
import { Store } from '../../shared/store.js';
import { transferDialogCss } from './transfer-dialog-css.js';
import {
  allChecked,
  applyExport,
  applyImport,
  chosenNames,
  exportModel,
  importModel,
  onlySubMacros,
  setAll,
  setDecision,
  toggleRow,
  type ConflictAction,
  type ExportTransfer,
  type ImportTransfer,
  type Transfer,
  type TransferModel,
} from './transfer-model.js';

/**
 * The import/export wizard: one dialog for both directions. A macro row is a tick, a global both
 * sides declare is a three-way answer, and the result is the program's own report. The dialog only
 * collects answers; the calls are the ones the model names, so the order is testable without a
 * browser.
 */

interface TransferState {
  open: boolean;
  mode: 'import' | 'export';
  session: Transfer | null;
  preselect: string[];
  error: string;
}

const closed: TransferState = { open: false, mode: 'import', session: null, preselect: [], error: '' };

class TransferStore {
  private readonly state = new Store<TransferState>(closed);

  subscribe(listener: (value: TransferState) => void): () => void {
    return this.state.subscribe(listener);
  }

  show(session: Transfer, preselect: readonly string[] = []): void {
    this.state.set({ open: true, mode: session.mode, session, preselect: [...preselect], error: '' });
  }

  fail(mode: 'import' | 'export', message: string): void {
    this.state.set({ open: true, mode, session: null, preselect: [], error: message });
  }

  close(): void {
    this.state.set(closed);
  }
}

export const transfer = new TransferStore();

@customElement('wg-transfer')
export class WgTransfer extends LitElement {
  @state()
  private openState: TransferState = closed;

  @state()
  private model: TransferModel | null = null;

  @state()
  private busy = false;

  @state()
  private error = '';

  @state()
  private resultTitle = '';

  @state()
  private result = '';


  static styles = transferDialogCss;

  protected readonly stringsController = new StoreController(this, strings);

  protected readonly transferController = new StoreController(this, transfer, (value) => {
    this.openState = value;
    this.model = value.session === null
      ? null
      : value.session.mode === 'import'
        ? importModel((value.session as ImportTransfer).plan)
        : exportModel((value.session as ExportTransfer).plan, value.preselect);
    this.error = value.error;
    this.result = '';
    this.resultTitle = '';
    this.busy = false;
  });

  render() {
    if (!this.openState.open) {
      return nothing;
    }

    if (this.result.length > 0) {
      return this.frame(html`
        <h2>${this.resultTitle}</h2>
        <p class="report">${this.result}</p>
        <div class="footer">
          <fluent-button appearance="accent" @click=${this.close}>${t('Common.Close')}</fluent-button>
        </div>
      `);
    }

    if (this.model === null || this.openState.session === null) {
      return this.frame(html`
        <h2>${this.heading()}</h2>
        <p class="error">${this.openState.error}</p>
        <div class="footer">
          <fluent-button appearance="accent" @click=${this.close}>${t('Common.Close')}</fluent-button>
        </div>
      `);
    }

    return this.frame(html`
      <div>
        <h2>${this.heading()}</h2>
        <p class="hint">${t(this.openState.mode === 'import' ? 'Import.Hint' : 'Export.Hint')}</p>
        ${this.openState.session.mode === 'import'
          ? html`<p class="hint">${t('Import.File').replace('{0}',
              (this.openState.session as ImportTransfer).plan.fileName)}</p>`
          : nothing}
      </div>
      ${this.rows()}
      ${this.conflictRows()}
      ${this.error.length > 0 ? html`<p class="error">${this.error}</p>` : nothing}
      <div class="footer">
        <span class="count">${t('Transfer.Selected').replace('{0}', String(chosenNames(this.model).length))}</span>
        ${this.openState.session.mode === 'import'
          ? html`<span class="count">${t(onlySubMacros(this.model) ? 'Import.ToLibrary' : 'Import.ToProject')}</span>`
          : nothing}
        <fluent-button appearance="subtle" ?disabled=${this.busy} @click=${this.close}>
          ${t('Common.Cancel')}
        </fluent-button>
        <fluent-button appearance="accent" ?disabled=${this.busy || chosenNames(this.model).length === 0}
          @click=${this.execute}>
          ${t(this.openState.mode === 'import' ? 'Import.Do' : 'Export.Do')}
        </fluent-button>
      </div>
    `);
  }

  private heading(): string {
    return t(this.openState.mode === 'import' ? 'Import.Title' : 'Export.Title');
  }

  private frame(body: TemplateResult) {
    return html`
      <div class="overlay" @click=${this.onBackdrop}>
        <div class="dialog" role="dialog" aria-modal="true" aria-label=${this.heading()}
          @click=${(event: Event) => event.stopPropagation()}>
          ${body}
        </div>
      </div>
    `;
  }

  private rows() {
    if (this.model === null) {
      return nothing;
    }

    if (this.model.rows.length === 0) {
      return html`<p class="hint">${t(this.openState.mode === 'import' ? 'Import.Nothing' : 'Export.Nothing')}</p>`;
    }

    return html`
      <div class="rows">
        <label class="row">
          <input type="checkbox" .checked=${allChecked(this.model)}
            @change=${(event: Event) => this.toggleAll((event.target as HTMLInputElement).checked)} />
          <span class="name">${t('Export.All')}</span>
        </label>
        ${this.model.rows.map((row, index) => html`
          <label class="row" data-covered=${String(row.covered)} data-sub=${String(row.macro.isSub)}>
            <input type="checkbox" .checked=${row.checked} ?disabled=${row.covered}
              @change=${(event: Event) => this.toggle(index, (event.target as HTMLInputElement).checked)} />
            <span class="name">${row.macro.name}</span>
            <span class="meta">${row.macro.kind}</span>
            <span class="meta">${t('Transfer.Steps').replace('{0}', String(row.macro.steps))}</span>
          </label>
          ${row.macro.problem.length > 0
            ? html`<p class="problem">${row.macro.problem}</p>`
            : nothing}
        `)}
      </div>
    `;
  }

  private conflictRows() {
    if (this.model === null || this.model.conflicts.length === 0) {
      return nothing;
    }

    return html`
      <div class="conflicts">
        <div>
          <span class="hint">${t('GlobalConflict.Title')}</span>
          <p class="hint">${t('GlobalConflict.Hint')}</p>
        </div>
        ${this.model.conflicts.map((global) => html`
          <div class="conflict">
            <span>${global.name} · ${global.type}</span>
            <select .value=${this.model?.decisions[global.name] ?? 'rename'}
              aria-label=${global.name}
              @change=${(event: Event) => this.decide(global.name,
                (event.target as HTMLSelectElement).value as ConflictAction)}>
              <option value="rename">${t('GlobalConflict.Rename')}</option>
              <option value="keep">${t('GlobalConflict.UseProject')}</option>
              <option value="overwrite">${t('GlobalConflict.Overwrite')}</option>
            </select>
          </div>
        `)}
      </div>
    `;
  }

  private toggleAll(checked: boolean): void {
    if (this.model !== null) {
      this.model = setAll(this.model, checked);
    }
  }

  private toggle(index: number, checked: boolean): void {
    if (this.model !== null) {
      this.model = toggleRow(this.model, index, checked);
    }
  }

  private decide(name: string, action: ConflictAction): void {
    if (this.model !== null) {
      this.model = setDecision(this.model, name, action);
    }
  }

  private async execute(): Promise<void> {
    const session = this.openState.session;
    const model = this.model;
    if (session === null || model === null) {
      return;
    }

    this.busy = true;
    this.error = '';
    try {
      if (session.mode === 'import') {
        const answer = await applyImport(bridge.value, session, model);
        this.resultTitle = t('Import.ReportTitle');
        this.result = answer.report;
      } else {
        const answer = await applyExport(bridge.value, session, model);
        if (answer === null) {
          return;
        }

        this.resultTitle = t('Export.Title');
        this.result = t('Transfer.Exported').replace('{0}', answer.path);
      }
    } catch (error) {
      this.error = describeError(error);
    } finally {
      this.busy = false;
    }
  }

  private readonly close = (): void => {
    transfer.close();
  };

  private readonly onBackdrop = (event: PointerEvent): void => {
    if (event.target === event.currentTarget) {
      this.close();
    }
  };
}

declare global {
  interface HTMLElementTagNameMap {
    'wg-transfer': WgTransfer;
  }
}