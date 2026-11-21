import { LitElement, css, html, nothing } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import { bridge } from '../../shared/bridge/bridge.js';
import type { EditorSnapshot, ProjectState } from '../../shared/bridge/protocol.js';
import { project } from '../../shared/project/store.js';
import { strings, t } from '../../shared/strings/strings.js';

/**
 * C0's editor gate: one real macro, read and changed through the C# session, then saved back
 * through the same rule the native editor uses. The full document/step UI belongs to C1; this page
 * exists to prove the snapshot-and-command path before the native editor is removed.
 */
@customElement('wg-editor-gate')
export class WgEditorGate extends LitElement {
  @state()
  private snapshot: EditorSnapshot | null = null;

  @state()
  private actions = 0;

  @state()
  private error = '';

  private unsubscribeStrings?: () => void;
  private unsubscribeProject?: () => void;
  private opened = false;

  static styles = css`
    :host {
      display: block;
      max-width: 720px;
      padding: var(--spacingVerticalL) var(--spacingHorizontalXXL);
    }

    h1 {
      font-size: var(--font-size-base-500);
      font-weight: var(--font-weight-semibold);
      margin: 0 0 var(--spacingVerticalM);
    }

    .fields {
      display: grid;
      gap: var(--spacingVerticalS);
      margin-bottom: var(--spacingVerticalL);
    }

    input,
    textarea {
      font: inherit;
      padding: var(--spacingVerticalSN) var(--spacingHorizontalM);
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke1);
      border-radius: var(--borderRadiusMedium);
      background: var(--colorNeutralBackground1);
      color: var(--colorNeutralForeground1);
    }

    textarea {
      min-height: 80px;
      resize: vertical;
    }

    .steps {
      margin: 0 0 var(--spacingVerticalL);
      color: var(--colorNeutralForeground2);
    }

    .steps li {
      font-family: var(--font-family-monospace);
      font-size: var(--font-size-base-200);
    }

    .actions {
      display: flex;
      gap: var(--spacingHorizontalS);
    }

    .error {
      color: var(--colorPaletteRedForeground1);
      margin-bottom: var(--spacingVerticalM);
    }
  `;

  connectedCallback(): void {
    super.connectedCallback();
    this.unsubscribeStrings = strings.subscribe(() => this.requestUpdate());
    this.unsubscribeProject = project.subscribe((state) => this.openFirst(state));
    void this.loadActions();
  }

  disconnectedCallback(): void {
    this.unsubscribeStrings?.();
    this.unsubscribeProject?.();
    super.disconnectedCallback();
  }

  render() {
    if (this.snapshot === null) {
      return html`<h1>${t('Shell.Editor')}</h1><p>${t('Editor.NoSteps')}</p>`;
    }

    return html`
      <h1>${t('Shell.Editor')}</h1>
      ${this.error ? html`<p class="error">${this.error}</p>` : nothing}
      <div class="fields">
        <input
          .value=${this.snapshot.name}
          placeholder=${t('Editor.NamePlaceholder')}
          aria-label=${t('Editor.NamePlaceholder')}
          @input=${(event: Event) => this.editName(event)}
        />
        <textarea
          .value=${this.snapshot.description}
          placeholder=${t('Editor.DescriptionPlaceholder')}
          aria-label=${t('Editor.DescriptionPlaceholder')}
          @input=${(event: Event) => this.editDescription(event)}
        ></textarea>
      </div>
      <h2>${t('Run.Steps')}</h2>
      ${this.snapshot.steps.length === 0
        ? html`<p class="steps">${t('Editor.NoSteps')}</p>`
        : html`<ol class="steps">
            ${this.snapshot.steps.map((step) => html`<li>${step.name}</li>`)}
          </ol>`}
      <p class="steps">${t('Editor.GateActions').replace('{0}', String(this.actions))}</p>
      <div class="actions">
        <fluent-button appearance="accent" @click=${this.save}>${t('Common.Save')}</fluent-button>
        <fluent-button appearance="subtle" @click=${this.close}>${t('Common.Close')}</fluent-button>
      </div>
    `;
  }

  private async loadActions(): Promise<void> {
    try {
      const actions = await bridge.value.call<{ count: number }>('editor.sessionActions');
      this.actions = actions.count;
    } catch {
      // The gate can still open a session even if the count fails; the count is diagnostic.
    }
  }

  private openFirst(state: ProjectState | null): void {
    if (this.opened || state === null) {
      return;
    }

    this.opened = true;
    const first = state.macros[0];
    void this.open(first?.name ?? null);
  }

  private async open(name: string | null): Promise<void> {
    try {
      this.snapshot = await bridge.value.call<EditorSnapshot | null>('editor.sessionOpen',
        name ? { name } : {});
    } catch (error) {
      this.error = String(error);
    }
  }

  private editName(event: Event): void {
    this.snapshot = { ...this.snapshot!, name: (event.target as HTMLInputElement).value };
  }

  private editDescription(event: Event): void {
    this.snapshot = { ...this.snapshot!, description: (event.target as HTMLTextAreaElement).value };
  }

  private async save(): Promise<void> {
    if (this.snapshot === null) {
      return;
    }

    try {
      this.snapshot = await bridge.value.call<EditorSnapshot>('editor.sessionPatch', {
        name: this.snapshot.name,
        description: this.snapshot.description,
      });
      this.snapshot = await bridge.value.call<EditorSnapshot>('editor.sessionSave');
      this.error = '';
    } catch (error) {
      this.error = String(error);
    }
  }

  private async close(): Promise<void> {
    try {
      await bridge.value.call('editor.sessionClose');
    } finally {
      this.snapshot = null;
    }
  }
}

declare global {
  interface HTMLElementTagNameMap {
    'wg-editor-gate': WgEditorGate;
  }
}
