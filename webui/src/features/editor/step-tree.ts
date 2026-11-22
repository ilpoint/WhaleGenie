import { LitElement, html, nothing, type TemplateResult } from 'lit';
import { stepTreeCss } from './step-tree-css.js';
import { customElement, state } from 'lit/decorators.js';
import { repeat } from 'lit/directives/repeat.js';
import { icon, iconForCategory, type IconName } from '../../shared/icons.js';
import { t } from '../../shared/strings/strings.js';
import type { EditorStep } from '../../shared/bridge/protocol.js';
import { editor } from './editor-store.js';
import { StoreController } from '../../shared/lit/store-controller.js';
import { categoryOfAction, findStep, visibleStepIds } from './editor-model.js';
import { handleStepTreeKeydown } from './step-tree-keyboard.js';
import { renderStepSections } from './step-tree-sections.js';
import { editorBase } from './editor-css.js';

/**
 * The step list. A step is a flat row; a block is a card whose child lists are sections ("then",
 * "else", a switch's branches). Selection and folding are page state; every edit goes back to C#.
 */
@customElement('wg-step-tree')
export class WgStepTree extends LitElement {
  static styles = [editorBase, stepTreeCss];

  protected readonly editorController = new StoreController<void>(this, editor);

  private dropTarget: { id: string; before: boolean } | null = null;

  @state()
  private menu: { x: number; y: number; stepId: string } | null = null;

  private dragIds: string[] = [];

  connectedCallback(): void {
    super.connectedCallback();
    editor.session.ensureLoaded();
  }

  disconnectedCallback(): void {
    super.disconnectedCallback();
  }

  render() {
    const state = editor.get();
    const snapshot = state.snapshot;
    if (!snapshot) {
      return html`<div class="empty">${t('Editor.NoDocument')}</div>`;
    }

    const notices = [
      snapshot.problemSummary,
      snapshot.declarationProblemSummary,
      snapshot.callImpactSummary,
      snapshot.notice,
    ].filter((text) => text.length > 0);

    return html`
      ${notices.length > 0
        ? html`
          <div class="notices" role="status">
            ${notices.map((text, index) => html`
              <p class="notice ${index < 2 ? 'warning' : ''}">${text}</p>
            `)}
          </div>
        `
        : nothing}
      <div class="scroll" @keydown=${this.onKeydown}>
        ${snapshot.steps.length === 0
          ? html`
            <div class="empty">
              <span>${t('Editor.NoSteps')}</span>
              <span class="hint">${t('Editor.NoStepsHint')}</span>
            </div>
          `
          : html`
            <div class="list" role="list" aria-label=${t('Editor.Steps')}>
              ${repeat(snapshot.steps, (step) => step.id, (step) => this.renderStep(step, 0))}
            </div>
          `}
      </div>
      ${this.renderMenu()}
    `;
  }

  renderStep(step: EditorStep, depth: number): TemplateResult {
    const state = editor.get();
    const selected = state.selectedStepIds.includes(step.id);
    const primary = state.selectedStepId === step.id;
    const folded = state.collapsedSteps.has(step.id);
    const block = step.sections.length > 0;
    return html`
      <div class="step ${block ? 'block' : ''}" data-depth=${String(depth)}>
        <div
          class="row"
          role="listitem"
          tabindex="0"
          data-selected=${selected ? 'true' : 'false'}
          data-primary=${primary ? 'true' : 'false'}
          data-drop=${this.dropTarget?.id === step.id
            ? (this.dropTarget.before ? 'before' : 'after')
            : 'none'}
          data-problem=${step.problem.length > 0 ? 'true' : 'false'}
          data-skipped=${step.enabled ? 'false' : 'true'}
          aria-current=${primary ? 'true' : 'false'}
          draggable="true"
          @click=${(event: MouseEvent) => this.onRowClick(event, step.id)}
          @dblclick=${() => { editor.ui.selectStep(step.id); editor.action.openStepEditor(); }}
          @contextmenu=${(event: MouseEvent) => this.onContextMenu(event, step.id)}
          @dragstart=${(event: DragEvent) => this.onDragStart(event, step.id)}
          @dragover=${(event: DragEvent) => this.onDragOver(event, step.id)}
          @dragleave=${() => { if (this.dropTarget?.id === step.id) { this.dropTarget = null; this.clearDropMarks(); } }}
          @drop=${(event: DragEvent) => this.onDrop(event, step.id)}
          @dragend=${() => { this.dropTarget = null; this.dragIds = []; }}
          @keydown=${(event: KeyboardEvent) => {
            if (event.target !== event.currentTarget) {
              return;
            }

            if (event.key === 'Enter' || event.key === ' ') {
              event.preventDefault();
              editor.ui.selectStep(step.id);
            }
          }}
        >
          ${block
            ? html`
              <button
                class="ghost twisty"
                aria-expanded=${folded ? 'false' : 'true'}
                title=${folded ? t('Editor.Unfold') : t('Editor.Fold')}
                aria-label=${folded ? t('Editor.Unfold') : t('Editor.Fold')}
                @click=${(event: Event) => {
                  event.stopPropagation();
                  editor.ui.toggleStep(step.id);
                }}
              >${icon(folded ? 'chevron-right' : 'chevron-down')}</button>
            `
            : html`<span class="twisty placeholder"></span>`}
          <span class="kind">${icon(iconForCategory(this.categoryOf(step.type)))}</span>
          <input
            class="enabled"
            type="checkbox"
            .checked=${step.enabled}
            title=${t('Editor.StepEnabled')}
            aria-label=${t('Editor.StepEnabled')}
            @click=${(event: Event) => event.stopPropagation()}
            @change=${(event: Event) =>
              void editor.steps.setStepEnabled(step.id, (event.target as HTMLInputElement).checked)}
          />
          <span class="name" title=${step.name}>${step.name}</span>
          <span class="badges">
            ${step.enabled ? nothing : html`<span class="badge skip">${t('Editor.StepDisabled')}</span>`}
            <span class="badge id" title=${t('Editor.StepIdHint').replace('{0}', step.id)}>${step.id}</span>
            ${step.inputMark.length > 0
              ? html`<span class="badge input" title=${step.inputMark}>${step.inputMark}</span>`
              : nothing}
          </span>
          <span class="detail" title=${step.detail}>${step.detail}</span>
          ${step.problem.length > 0
            ? html`<span class="problem-mark" title=${step.problem}>${icon('triangle-alert')}</span>`
            : html`<span></span>`}
          <span class="row-actions">
            ${this.actionButton('arrow-up', t('Palette.MoveUp'), () => void editor.steps.moveStep(step.id, -1))}
            ${this.actionButton('arrow-down', t('Palette.MoveDown'), () => void editor.steps.moveStep(step.id, 1))}
            ${this.actionButton('copy', t('Editor.DuplicateStep'), () => void editor.steps.duplicateStep(step.id))}
            ${this.actionButton('delete', t('Editor.Delete'), () => void editor.steps.removeStep(step.id))}
          </span>
        </div>
        ${primary
          ? html`
            <div class="expanded">
              ${step.detail.length > 0 ? html`<span class="line">${step.detail}</span>` : nothing}
              ${step.comment.length > 0
                ? html`<span class="line comment">${step.comment}</span>`
                : nothing}
              ${step.metaSummary.length > 0
                ? html`<span class="line meta">${step.metaSummary}</span>`
                : nothing}
            </div>
          `
          : nothing}
        ${block && !folded ? renderStepSections(this, step, depth) : nothing}
      </div>
    `;
  }

  private onRowClick(event: MouseEvent, id: string): void {
    const target = event.target as HTMLElement;
    if (target.closest('button, input, select, textarea')) {
      return;
    }

    if (event.shiftKey) {
      const ids = this.visibleIds();
      const anchor = editor.get().selectedStepId || id;
      const from = ids.indexOf(anchor);
      const to = ids.indexOf(id);
      if (from >= 0 && to >= 0) {
        editor.ui.selectStep(id, false, ids.slice(Math.min(from, to), Math.max(from, to) + 1));
        return;
      }
    }

    editor.ui.selectStep(id, event.ctrlKey || event.metaKey);
  }

  visibleIds(): string[] {
    const state = editor.get();
    return visibleStepIds(state.snapshot, state.collapsedSteps);
  }

  private onKeydown(event: KeyboardEvent): void {
    handleStepTreeKeydown(this, event);
  }

  private onContextMenu(event: MouseEvent, id: string): void {
    event.preventDefault();
    if (!editor.get().selectedStepIds.includes(id)) {
      editor.ui.selectStep(id);
    }

    this.menu = {
      x: Math.min(event.clientX, window.innerWidth - 220),
      y: Math.min(event.clientY, window.innerHeight - 340),
      stepId: id,
    };
  }

  private onDragStart(event: DragEvent, id: string): void {
    this.dragIds = editor.get().selectedStepIds.includes(id)
      ? editor.ui.selectedSteps()
      : [id];
    if (event.dataTransfer) {
      event.dataTransfer.effectAllowed = 'move';
      event.dataTransfer.setData('text/plain', this.dragIds.join(','));
    }
  }

  private onDragOver(event: DragEvent, id: string): void {
    const external = event.dataTransfer
      ? Array.from(event.dataTransfer.types).includes('application/x-whalegenie-action')
      : false;
    if (!external && (this.dragIds.length === 0 || this.dragIds.includes(id))) {
      return;
    }

    event.preventDefault();
    if (event.dataTransfer) {
      event.dataTransfer.dropEffect = 'move';
    }
    const rect = (event.currentTarget as HTMLElement).getBoundingClientRect();
    const before = event.clientY < rect.top + rect.height / 2;
    this.clearDropMarks();
    this.dropTarget = { id, before };
    (event.currentTarget as HTMLElement).dataset['drop'] = before ? 'before' : 'after';
  }

  private clearDropMarks(): void {
    for (const row of this.renderRoot.querySelectorAll<HTMLElement>('.row[data-drop]')) {
      row.dataset['drop'] = 'none';
    }
  }

  private onDrop(event: DragEvent, id: string): void {
    event.preventDefault();
    this.clearDropMarks();
    const target = this.dropTarget;
    const actionKey = event.dataTransfer?.getData('application/x-whalegenie-action') ?? '';
    const ids = this.dragIds;
    this.dropTarget = null;
    this.dragIds = [];
    if (!target || target.id !== id) {
      return;
    }

    if (actionKey.length > 0) {
      void editor.action.openAction(actionKey, { targetStepId: id, before: target.before });
      return;
    }

    void editor.steps.moveStepsTo(id, target.before, ids);
  }

  clearMenu(): void {
    this.menu = null;
  }

  private renderMenu(): TemplateResult | typeof nothing {
    if (!this.menu) {
      return nothing;
    }

    const state = editor.get();
    const selected = state.selectedStepIds;
    const single = selected.length === 1;
    const first = selected.length > 0 && state.snapshot
      ? findStep(state.snapshot.steps, selected[0] ?? '')
      : null;
    const close = () => { this.menu = null; };
    const run = (action: () => void) => {
      close();
      action();
    };

    return html`
      <div class="menu-backdrop" @click=${close} @contextmenu=${(event: Event) => event.preventDefault()}></div>
      <div class="menu" role="menu" style=${'left:' + this.menu.x + 'px;top:' + this.menu.y + 'px'}
        @contextmenu=${(event: Event) => event.preventDefault()}>
        ${this.menuButton('edit', t('Palette.Edit'), !single, () => run(() => editor.action.openStepEditor()))}
        ${this.menuButton('check', t('Palette.ToggleEnabled'), !first, () => run(() => {
          if (first) {
            void editor.steps.setStepEnabled(undefined, !first.enabled);
          }
        }))}
        <div class="sep"></div>
        ${this.menuButton('copy', t('Palette.Copy'), selected.length === 0,
          () => run(() => void editor.steps.copySteps()))}
        ${this.menuButton('cut', t('Palette.Cut'), selected.length === 0,
          () => run(() => void editor.steps.cutSteps()))}
        ${this.menuButton('paste', t('Palette.Paste'), false,
          () => run(() => void editor.steps.pasteSteps(state.selectedStepId)))}
        ${this.menuButton('group', t('Palette.Group'), selected.length === 0,
          () => run(() => void editor.steps.groupSteps()))}
        ${this.menuButton('export', t('Palette.LiftToMacro'), selected.length === 0,
          () => run(() => void editor.steps.liftSteps()))}
        ${this.menuButton('copy', t('Editor.DuplicateStep'), selected.length === 0,
          () => run(() => void editor.steps.duplicateStep()))}
        <div class="sep"></div>
        ${this.menuButton('arrow-up', t('Palette.MoveUp'), selected.length === 0,
          () => run(() => void editor.steps.moveStep(undefined, -1)))}
        ${this.menuButton('arrow-down', t('Palette.MoveDown'), selected.length === 0,
          () => run(() => void editor.steps.moveStep(undefined, 1)))}
        ${this.menuButton('delete', t('Palette.Delete'), selected.length === 0,
          () => run(() => void editor.steps.removeStep()))}
      </div>
    `;
  }

  private menuButton(iconName: IconName, label: string, disabled: boolean,
    run: () => void): TemplateResult {
    return html`
      <button type="button" role="menuitem" ?disabled=${disabled}
        @click=${(event: Event) => {
          event.stopPropagation();
          if (!disabled) {
            run();
          }
        }}>
        ${icon(iconName)}<span>${label}</span>
      </button>
    `;
  }

  private actionButton(iconName: 'arrow-up' | 'arrow-down' | 'copy' | 'delete', label: string,
    run: () => void) {
    return html`
      <button
        class="ghost"
        type="button"
        title=${label}
        aria-label=${label}
        @click=${(event: Event) => {
          event.stopPropagation();
          run();
        }}
      >${icon(iconName)}</button>
    `;
  }

  private categoryOf(type: string): string {
    return categoryOfAction(editor.get().actions, type);
  }
}

declare global {
  interface HTMLElementTagNameMap {
    'wg-step-tree': WgStepTree;
  }
}
