import { LitElement, css, html, nothing, type TemplateResult } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import { repeat } from 'lit/directives/repeat.js';
import { icon, iconForCategory, type IconName } from '../../shared/icons.js';
import { t } from '../../shared/strings/strings.js';
import type { EditorStep } from '../../shared/bridge/protocol.js';
import { editor } from './editor-store.js';
import { findStep } from './editor-model.js';
import { editorBase } from './editor-css.js';

/**
 * The step list. A step is a flat row; a block is a card whose child lists are sections ("then",
 * "else", a switch's branches). Selection and folding are page state; every edit goes back to C#.
 */
@customElement('wg-step-tree')
export class WgStepTree extends LitElement {
  static styles = [editorBase, css`
    :host {
      display: flex;
      flex-direction: column;
      min-height: 0;
      min-width: 0;
      height: 100%;
      background: var(--colorNeutralBackground1);
    }

    .notices {
      flex: none;
      display: grid;
      gap: 2px;
      padding: var(--spacingVerticalXS) var(--spacingHorizontalL);
      border-bottom: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
    }

    .notice {
      margin: 0;
      color: var(--colorNeutralForeground2);
      font-size: var(--fontSizeBase200);
      line-height: 1.4;
      white-space: pre-line;
    }

    .notice.warning {
      color: var(--colorStatusWarningForeground1);
    }

    .scroll {
      flex: 1;
      min-height: 0;
      overflow: auto;
      padding: var(--spacingVerticalS) var(--spacingHorizontalL) var(--spacingVerticalXXL);
    }

    .empty {
      display: grid;
      place-content: center;
      gap: var(--spacingVerticalXS);
      height: 100%;
      min-height: 180px;
      padding: var(--spacingVerticalXXL);
      color: var(--colorNeutralForeground3);
      text-align: center;
      font-size: var(--fontSizeBase300);
    }

    .empty .hint {
      font-size: var(--fontSizeBase200);
    }

    .list {
      display: grid;
      gap: 1px;
      margin: 0;
      padding: 0;
      list-style: none;
    }

    .step {
      display: flex;
      flex-direction: column;
      min-width: 0;
    }

    .row {
      display: grid;
      grid-template-columns: 18px 16px 16px minmax(0, max-content) max-content minmax(0, 1fr) auto auto;
      align-items: center;
      gap: 5px;
      min-height: 30px;
      padding: 2px 6px;
      border-left: 2px solid transparent;
      border-radius: var(--borderRadiusMedium);
      color: var(--colorNeutralForeground1);
      cursor: default;
      outline: none;
    }

    .row:hover {
      background: var(--colorSubtleBackgroundHover);
    }

    .row[data-selected='true'] {
      background: var(--colorBrandBackground2);
      border-left-color: var(--colorBrandStroke1);
    }

    .row:focus-visible {
      outline: var(--strokeWidthThicker) solid var(--colorStrokeFocus2);
      outline-offset: -2px;
    }

    .row[data-problem='true'] {
      border-left-color: var(--colorStatusWarningBorder2);
    }

    .row[data-skipped='true'] {
      opacity: .45;
    }

    .twisty {
      width: 18px;
      height: 18px;
    }

    .twisty svg {
      width: 12px;
      height: 12px;
    }

    .twisty.placeholder {
      visibility: hidden;
    }

    .kind {
      display: grid;
      place-items: center;
      color: var(--colorNeutralForeground2);
    }

    .kind svg {
      width: 15px;
      height: 15px;
    }

    .enabled {
      width: 13px;
      height: 13px;
      margin: 0;
      accent-color: var(--colorBrandBackground);
      cursor: pointer;
    }

    .name {
      min-width: 0;
      max-width: 260px;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
      font-size: var(--fontSizeBase300);
    }

    .badges {
      display: flex;
      align-items: center;
      gap: 4px;
      min-width: 0;
      flex: none;
    }

    .badge {
      display: inline-flex;
      align-items: center;
      height: 17px;
      padding: 0 5px;
      border-radius: var(--borderRadiusSmall);
      font-size: var(--fontSizeBase100);
      line-height: 1;
      white-space: nowrap;
    }

    .badge.id {
      background: var(--colorBrandBackground2);
      color: var(--colorBrandForeground2);
      font-family: var(--fontFamilyMonospace);
    }

    .badge.skip {
      background: var(--colorStatusWarningBackground1);
      color: var(--colorStatusWarningForeground1);
    }

    .badge.input {
      background: var(--colorNeutralBackground2);
      color: var(--colorNeutralForeground2);
    }

    .detail {
      min-width: 0;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
      color: var(--colorNeutralForeground3);
      font-size: var(--fontSizeBase200);
    }

    .problem-mark {
      display: grid;
      place-items: center;
      color: var(--colorStatusWarningForeground1);
    }

    .problem-mark svg {
      width: 14px;
      height: 14px;
    }

    .row-actions {
      display: flex;
      align-items: center;
      gap: 1px;
      opacity: 0;
    }

    .row:hover .row-actions,
    .row[data-selected='true'] .row-actions,
    .row:focus-within .row-actions {
      opacity: 1;
    }

    .row-actions .ghost {
      width: 22px;
      height: 22px;
    }

    .row-actions .ghost svg {
      width: 12px;
      height: 12px;
    }

    .expanded {
      display: grid;
      gap: 2px;
      margin: 0 0 3px 39px;
      padding: 3px 8px 5px;
      border-left: 2px solid var(--colorBrandBackground2);
      color: var(--colorNeutralForeground2);
      font-size: var(--fontSizeBase200);
    }

    .expanded .line {
      white-space: pre-wrap;
      overflow-wrap: anywhere;
    }

    .expanded .comment {
      color: var(--colorNeutralForeground1);
    }

    .expanded .meta {
      color: var(--colorNeutralForeground3);
    }

    .sections {
      display: grid;
      gap: 1px;
      margin: 1px 0 4px 14px;
      padding-left: 8px;
      border-left: 1px dashed var(--colorNeutralStroke2);
      position: relative;
    }

    .sections > .section-head,
    .sections > .section-empty,
    .sections > .list > .step {
      position: relative;
    }

    .sections > .section-head::before,
    .sections > .section-empty::before,
    .sections > .list > .step::before {
      content: '';
      position: absolute;
      left: -9px;
      top: 15px;
      width: 9px;
      border-top: 1px dashed var(--colorNeutralStroke2);
      pointer-events: none;
    }

    .sections > .section-empty::before {
      top: 9px;
    }

    .section-head {
      display: flex;
      align-items: center;
      gap: var(--spacingHorizontalS);
      min-height: 24px;
      color: var(--colorNeutralForeground3);
      font-size: var(--fontSizeBase100);
    }

    .section-head .label {
      font-weight: var(--fontWeightSemibold);
    }

    .section-head .count {
      opacity: .8;
    }

    .section-add {
      margin-left: auto;
      width: auto;
      height: 22px;
      padding: 0 6px;
      gap: 2px;
      display: inline-flex;
      align-items: center;
      color: var(--colorNeutralForeground2);
      font-size: var(--fontSizeBase100);
    }

    .section-add svg {
      width: 11px;
      height: 11px;
    }

    .section-add:disabled {
      color: var(--colorNeutralForegroundDisabled);
    }

    .section-empty {
      padding: 2px 0 2px 24px;
      color: var(--colorNeutralForeground3);
      font-size: var(--fontSizeBase100);
    }

    .block > .row {
      background: var(--colorNeutralBackground2);
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
      border-left-width: 2px;
      border-radius: var(--borderRadiusLarge);
      padding: 4px 6px;
    }

    .block > .row:hover {
      background: var(--colorNeutralBackground2Hover);
    }

    .block > .row[data-selected='true'] {
      background: var(--colorBrandBackground2);
      border-color: var(--colorBrandStroke1);
    }
    .row[data-drop='before'] {
      box-shadow: inset 0 2px 0 var(--colorBrandStroke1);
    }

    .row[data-drop='after'] {
      box-shadow: inset 0 -2px 0 var(--colorBrandStroke1);
    }

    .menu-backdrop {
      position: fixed;
      inset: 0;
      z-index: 40;
    }

    .menu {
      position: fixed;
      z-index: 41;
      min-width: 196px;
      padding: 4px;
      background: var(--colorNeutralBackground1);
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke1);
      border-radius: var(--borderRadiusMedium);
      box-shadow: var(--shadow16);
    }

    .menu button {
      display: flex;
      align-items: center;
      gap: var(--spacingHorizontalS);
      width: 100%;
      height: 30px;
      padding: 0 var(--spacingHorizontalS);
      border: none;
      border-radius: var(--borderRadiusSmall);
      background: transparent;
      color: var(--colorNeutralForeground1);
      font-size: var(--fontSizeBase200);
      text-align: left;
      cursor: default;
    }

    .menu button:hover:not(:disabled) {
      background: var(--colorSubtleBackgroundHover);
    }

    .menu button:disabled {
      color: var(--colorNeutralForegroundDisabled);
    }

    .menu button svg {
      width: 14px;
      height: 14px;
      flex: none;
    }

    .menu .sep {
      height: 1px;
      margin: 4px;
      background: var(--colorNeutralStroke2);
    }
  `];

  private unsubscribe?: () => void;

  private dropTarget: { id: string; before: boolean } | null = null;

  @state()
  private menu: { x: number; y: number; stepId: string } | null = null;

  private dragIds: string[] = [];

  connectedCallback(): void {
    super.connectedCallback();
    editor.ensureLoaded();
    this.unsubscribe = editor.subscribe(() => this.requestUpdate());
  }

  disconnectedCallback(): void {
    this.unsubscribe?.();
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

  private renderStep(step: EditorStep, depth: number): TemplateResult {
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
          @dblclick=${() => { editor.selectStep(step.id); editor.openStepEditor(); }}
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
              editor.selectStep(step.id);
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
                  editor.toggleStep(step.id);
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
              void editor.setStepEnabled(step.id, (event.target as HTMLInputElement).checked)}
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
            ${this.actionButton('arrow-up', t('Palette.MoveUp'), () => void editor.moveStep(step.id, -1))}
            ${this.actionButton('arrow-down', t('Palette.MoveDown'), () => void editor.moveStep(step.id, 1))}
            ${this.actionButton('copy', t('Editor.DuplicateStep'), () => void editor.duplicateStep(step.id))}
            ${this.actionButton('delete', t('Editor.Delete'), () => void editor.removeStep(step.id))}
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
        ${block && !folded ? this.renderSections(step, depth) : nothing}
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
        editor.selectStep(id, false, ids.slice(Math.min(from, to), Math.max(from, to) + 1));
        return;
      }
    }

    editor.selectStep(id, event.ctrlKey || event.metaKey);
  }

  private visibleIds(): string[] {
    const snapshot = editor.get().snapshot;
    const result: string[] = [];
    if (!snapshot) {
      return result;
    }

    const walk = (steps: EditorStep[]) => {
      for (const step of steps) {
        result.push(step.id);
        if (!editor.get().collapsedSteps.has(step.id)) {
          for (const section of step.sections) {
            walk(section.steps);
          }
        }
      }
    };
    walk(snapshot.steps);
    return result;
  }

  private onKeydown(event: KeyboardEvent): void {
    if (event.target instanceof HTMLInputElement || event.target instanceof HTMLTextAreaElement
        || event.target instanceof HTMLSelectElement) {
      return;
    }

    event.stopPropagation();
    const state = editor.get();
    const selected = state.selectedStepIds;
    const ctrl = event.ctrlKey || event.metaKey;
    if (event.key === 'Escape') {
      event.preventDefault();
      editor.selectStep('');
      this.menu = null;
      return;
    }

    if (ctrl && event.key.toLowerCase() === 'a') {
      event.preventDefault();
      const ids = this.visibleIds();
      if (ids.length > 0) {
        editor.selectStep(ids.at(-1) ?? '', false, ids);
      }
      return;
    }

    if (ctrl && event.key.toLowerCase() === 'c') {
      event.preventDefault();
      void editor.copySteps();
      return;
    }

    if (ctrl && event.key.toLowerCase() === 'x') {
      event.preventDefault();
      void editor.cutSteps();
      return;
    }

    if (ctrl && event.key.toLowerCase() === 'v') {
      event.preventDefault();
      void editor.pasteSteps(state.selectedStepId);
      return;
    }

    if (ctrl && event.key.toLowerCase() === 'g') {
      event.preventDefault();
      void editor.groupSteps();
      return;
    }

    if (ctrl && event.key.toLowerCase() === 'e') {
      event.preventDefault();
      const first = selected.length > 0 && state.snapshot
        ? findStep(state.snapshot.steps, selected[0] ?? '')
        : null;
      if (first) {
        void editor.setStepEnabled(undefined, !first.enabled);
      }
      return;
    }

    if (ctrl && (event.key === 'ArrowUp' || event.key === 'ArrowDown')) {
      event.preventDefault();
      void editor.moveStep(undefined, event.key === 'ArrowUp' ? -1 : 1);
      return;
    }

    if (event.key === 'Delete' || event.key === 'Backspace') {
      event.preventDefault();
      void editor.removeStep();
      return;
    }

    if (event.key === 'F2') {
      event.preventDefault();
      editor.openStepEditor();
    }
  }

  private onContextMenu(event: MouseEvent, id: string): void {
    event.preventDefault();
    if (!editor.get().selectedStepIds.includes(id)) {
      editor.selectStep(id);
    }

    this.menu = {
      x: Math.min(event.clientX, window.innerWidth - 220),
      y: Math.min(event.clientY, window.innerHeight - 340),
      stepId: id,
    };
  }

  private onDragStart(event: DragEvent, id: string): void {
    this.dragIds = editor.get().selectedStepIds.includes(id)
      ? editor.selectedSteps()
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
      void editor.openAction(actionKey, { targetStepId: id, before: target.before });
      return;
    }

    void editor.moveStepsTo(id, target.before, ids);
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
        ${this.menuButton('edit', t('Palette.Edit'), !single, () => run(() => editor.openStepEditor()))}
        ${this.menuButton('check', t('Palette.ToggleEnabled'), !first, () => run(() => {
          if (first) {
            void editor.setStepEnabled(undefined, !first.enabled);
          }
        }))}
        <div class="sep"></div>
        ${this.menuButton('copy', t('Palette.Copy'), selected.length === 0,
          () => run(() => void editor.copySteps()))}
        ${this.menuButton('cut', t('Palette.Cut'), selected.length === 0,
          () => run(() => void editor.cutSteps()))}
        ${this.menuButton('paste', t('Palette.Paste'), false,
          () => run(() => void editor.pasteSteps(state.selectedStepId)))}
        ${this.menuButton('group', t('Palette.Group'), selected.length === 0,
          () => run(() => void editor.groupSteps()))}
        ${this.menuButton('export', t('Palette.LiftToMacro'), selected.length === 0,
          () => run(() => void editor.liftSteps()))}
        ${this.menuButton('copy', t('Editor.DuplicateStep'), selected.length === 0,
          () => run(() => void editor.duplicateStep()))}
        <div class="sep"></div>
        ${this.menuButton('arrow-up', t('Palette.MoveUp'), selected.length === 0,
          () => run(() => void editor.moveStep(undefined, -1)))}
        ${this.menuButton('arrow-down', t('Palette.MoveDown'), selected.length === 0,
          () => run(() => void editor.moveStep(undefined, 1)))}
        ${this.menuButton('delete', t('Palette.Delete'), selected.length === 0,
          () => run(() => void editor.removeStep()))}
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

  private renderSections(step: EditorStep, depth: number): TemplateResult {
    const state = editor.get();
    return html`
      <div class="sections" data-depth=${String(depth + 1)}>
        ${step.sections.map((section) => {
          const cases = section.name === 'cases';
          const disabled = !cases && state.selectedActionKey.length === 0;
          return html`
            <div class="section-head" data-depth=${String(depth + 1)}>
              <span class="label">${section.label}</span>
              <span class="count">${t('Editor.StepCount').replace('{0}', String(section.steps.length))}</span>
              <button
                class="ghost section-add"
                ?disabled=${disabled}
                title=${cases ? t('Editor.AddBranchHint') : t('Editor.AddStep')}
                aria-label=${cases ? t('Editor.AddBranch') : t('Editor.AddStep')}
                @click=${(event: Event) => {
                  event.stopPropagation();
                  void (cases
                    ? editor.addBranch(step.id)
                    : editor.addSelectedStep(step.id, section.name));
                }}
              >${icon('plus')}${cases ? t('Editor.AddBranch') : t('Editor.AddStep')}</button>
            </div>
            ${section.steps.length === 0
              ? html`<div class="section-empty" data-depth=${String(depth + 1)}>${cases
                ? t('Editor.EmptyBranchList')
                : t('Editor.NoStepsHint')}</div>`
              : html`
                <div class="list" role="list">
                  ${repeat(section.steps, (child) => child.id,
                    (child) => this.renderStep(child, depth + 1))}
                </div>
              `}
          `;
        })}
      </div>
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
    const action = editor.get().actions.find((candidate) => candidate.key === type);
    if (action) {
      return action.category;
    }

    return type.startsWith('control.') ? 'Control' : 'Control';
  }
}

declare global {
  interface HTMLElementTagNameMap {
    'wg-step-tree': WgStepTree;
  }
}
