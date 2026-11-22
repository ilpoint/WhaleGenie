import { LitElement, css, html, nothing, type TemplateResult } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import type { EditorValueNode, EditorValueSnapshot } from '../../shared/bridge/protocol.js';
import { icon } from '../../shared/icons.js';
import { t } from '../../shared/strings/strings.js';
import { editor } from './editor-store.js';
import { editorBase } from './editor-css.js';

/** The restored value window: token band, signs, variables and functions on one screen. */
@customElement('wg-value-editor')
export class WgValueEditor extends LitElement {
  @state()
  private textTimer = 0;

  @state()
  private bandHeight = 280;

  private bandStartY = 0;

  private bandStartHeight = 280;

  static styles = [editorBase, css`
    :host { position: fixed; inset: 0; z-index: 60; display: grid; place-items: center; padding: 16px; background: var(--shell-scrim); box-sizing: border-box; }
    .dialog { display: flex; flex-direction: column; width: min(1140px, 100%); height: min(720px, 100%); min-height: 0; overflow: hidden; background: var(--colorNeutralBackground1); border: var(--strokeWidthThin) solid var(--colorNeutralStroke1); border-radius: var(--borderRadiusLarge); box-shadow: var(--shadow64); }
    header { display: flex; align-items: flex-start; gap: var(--spacingHorizontalM); padding: var(--spacingVerticalM) var(--spacingHorizontalL) var(--spacingVerticalS); border-bottom: var(--strokeWidthThin) solid var(--colorNeutralStroke2); background: var(--colorNeutralBackground2); }
    header h2 { margin: 0; font-size: var(--fontSizeBase400); font-weight: var(--fontWeightSemibold); }
    header p { margin: 2px 0 0; color: var(--colorNeutralForeground3); font-size: var(--fontSizeBase100); }
    header .close { margin-left: auto; }
    .body { display: flex; flex-direction: column; gap: var(--spacingVerticalS); flex: 1; min-height: 0; padding: var(--spacingVerticalM) var(--spacingHorizontalL); overflow: auto; }
    .band-panel, .panel { border: var(--strokeWidthThin) solid var(--colorNeutralStroke2); border-radius: var(--borderRadiusMedium); background: var(--colorNeutralBackground2); }
    .band-panel { display: flex; flex-direction: column; height: var(--band-height, 280px); min-height: 190px; flex: none; overflow: hidden; }
    .band-splitter { flex: none; height: 6px; cursor: row-resize; border-top: var(--strokeWidthThin) solid var(--colorNeutralStroke2); border-bottom: var(--strokeWidthThin) solid var(--colorNeutralStroke2); background: var(--colorNeutralBackground1); }
    .band-splitter:hover { background: var(--colorBrandBackground2); }
    .band-head { display: flex; align-items: center; gap: var(--spacingHorizontalXS); padding: var(--spacingVerticalXS) var(--spacingHorizontalS); border-bottom: var(--strokeWidthThin) solid var(--colorNeutralStroke2); }
    .band-head .label { font-size: var(--fontSizeBase100); color: var(--colorNeutralForeground2); }
    .band-head .hint { margin-left: auto; color: var(--colorNeutralForeground3); font-size: var(--fontSizeBase100); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .band { position: relative; flex: 1; min-height: 60px; padding: 10px; overflow: auto; outline: none; }
    .band:focus-visible { box-shadow: inset 0 0 0 2px var(--colorStrokeFocus2); }
    .band .empty-hint { color: var(--colorNeutralForeground3); font-size: var(--fontSizeBase100); font-style: italic; }
    .wrap { display: flex; flex-wrap: wrap; align-items: center; gap: 3px; min-height: 24px; }
    .token { position: relative; display: inline-flex; align-items: center; gap: 3px; margin: 1px; padding: 2px 5px; border: var(--strokeWidthThin) solid var(--colorNeutralStroke2); border-radius: var(--borderRadiusMedium); background: var(--colorNeutralBackground1); color: var(--colorNeutralForeground1); font-family: var(--fontFamilyMonospace); font-size: var(--fontSizeBase200); line-height: 18px; }
    .token.selected { outline: var(--strokeWidthThick) solid var(--colorBrandStroke1); outline-offset: 1px; }
    .token.problem { box-shadow: inset 0 0 0 1px var(--colorPaletteRedBorder2, var(--colorStatusDangerBorder2)); }
    .token.empty { min-width: 28px; min-height: 22px; justify-content: center; border-style: dashed; color: var(--colorNeutralForeground3); font-family: var(--fontFamilyBase); font-size: var(--fontSizeBase100); font-style: italic; }
    .token.empty[data-required='true'] { border-color: var(--colorPaletteRedBorder2, var(--colorStatusDangerBorder2)); }
    .token .call-name { color: var(--colorBrandForeground2); font-weight: var(--fontWeightSemibold); }
    .token .bracket { color: var(--colorNeutralForeground3); }
    .token .op { border: 0; background: transparent; color: var(--colorBrandForeground2); font: inherit; padding: 0 2px; cursor: pointer; }
    .token .remove { display: none; align-items: center; justify-content: center; width: 16px; height: 16px; padding: 0; border: 0; border-radius: var(--borderRadiusSmall); background: var(--colorNeutralBackground3); color: var(--colorNeutralForeground3); cursor: pointer; }
    .token:hover .remove { display: inline-flex; }
    .token .remove:hover { background: var(--colorPaletteRedBackground2, var(--colorStatusDangerBackground2)); color: var(--colorPaletteRedForeground1, var(--colorStatusDangerForeground1)); }
    .token.stack { flex-direction: column; align-items: stretch; gap: 3px; }
    .arg-row { display: flex; align-items: flex-start; gap: 4px; padding-left: 8px; border-left: 1px solid var(--colorNeutralStroke2); }
    .arg-label { color: var(--colorNeutralForeground3); font-family: var(--fontFamilyBase); font-size: var(--fontSizeBase100); padding-top: 3px; white-space: nowrap; }
    .caret { display: inline-block; width: 2px; height: 17px; margin: 0 -1px; background: var(--colorBrandBackground); animation: blink 1.05s steps(2, start) infinite; vertical-align: text-bottom; }
    @keyframes blink { 50% { opacity: 0; } }
    .band-foot { display: flex; gap: var(--spacingHorizontalS); align-items: baseline; padding: 4px var(--spacingHorizontalS) 6px; border-top: var(--strokeWidthThin) solid var(--colorNeutralStroke2); font-size: var(--fontSizeBase100); color: var(--colorNeutralForeground3); }
    .band-foot .problem { color: var(--colorPaletteRedForeground1, var(--colorStatusDangerForeground1)); }
    .band-foot .saying { color: var(--colorNeutralForeground2); }
    .materials { display: flex; flex-direction: column; gap: var(--spacingVerticalS); flex: none; }
    .signs, .typed { display: flex; flex-wrap: wrap; align-items: center; gap: 4px; padding: var(--spacingVerticalXS) var(--spacingHorizontalS); border: var(--strokeWidthThin) solid var(--colorNeutralStroke2); border-radius: var(--borderRadiusMedium); background: var(--colorNeutralBackground2); }
    .signs .label { color: var(--colorNeutralForeground3); font-size: var(--fontSizeBase100); margin-right: 2px; }
    .signs button, .typed .mode { min-width: 28px; height: 26px; padding: 0 6px; border: var(--strokeWidthThin) solid var(--colorNeutralStroke2); border-radius: var(--borderRadiusMedium); background: var(--colorNeutralBackground1); color: var(--colorNeutralForeground1); font-size: var(--fontSizeBase100); cursor: pointer; }
    .signs button:disabled { opacity: 0.45; cursor: default; }
    .signs button:hover:not(:disabled), .typed .mode:hover { background: var(--colorNeutralBackground1Hover); }
    .typed .mode.on { border-color: var(--colorBrandStroke1); background: var(--colorBrandBackground2); color: var(--colorBrandForeground2); }
    .typed input { flex: 1; min-width: 160px; height: 28px; padding: 0 var(--spacingHorizontalS); border: var(--strokeWidthThin) solid var(--colorNeutralStroke1); border-radius: var(--borderRadiusMedium); background: var(--colorNeutralBackground1); color: var(--colorNeutralForeground1); font-family: var(--fontFamilyMonospace); font-size: var(--fontSizeBase200); }
    .typed .note { color: var(--colorPaletteRedForeground1, var(--colorStatusDangerForeground1)); font-size: var(--fontSizeBase100); }
    .panels { display: flex; gap: var(--spacingHorizontalS); min-height: 160px; flex: 1; }
    .panel { display: flex; flex-direction: column; flex: 1; min-width: 0; padding: var(--spacingVerticalS); background: var(--colorNeutralBackground2); }
    .panel h3 { margin: 0 0 var(--spacingVerticalXS); color: var(--colorNeutralForeground2); font-size: var(--fontSizeBase100); font-weight: var(--fontWeightSemibold); }
    .panel .search { height: 28px; margin-bottom: var(--spacingVerticalXS); padding: 0 var(--spacingHorizontalS); border: var(--strokeWidthThin) solid var(--colorNeutralStroke1); border-radius: var(--borderRadiusMedium); background: var(--colorNeutralBackground1); color: var(--colorNeutralForeground1); font: inherit; font-size: var(--fontSizeBase200); }
    .panel .filters { display: flex; flex-wrap: wrap; gap: 3px; margin-bottom: var(--spacingVerticalXS); }
    .panel .filters button { min-height: 22px; padding: 0 6px; border: var(--strokeWidthThin) solid var(--colorNeutralStroke2); border-radius: var(--borderRadiusSmall); background: var(--colorNeutralBackground1); color: var(--colorNeutralForeground2); font-size: var(--fontSizeBase100); cursor: pointer; }
    .panel .filters button.on { border-color: var(--colorBrandStroke1); background: var(--colorBrandBackground2); color: var(--colorBrandForeground2); }
    .chips { display: flex; flex-wrap: wrap; align-content: flex-start; gap: 4px; flex: 1; min-height: 0; overflow: auto; }
    .chips button { max-width: 100%; min-height: 25px; padding: 2px 7px; border: var(--strokeWidthThin) solid var(--colorNeutralStroke2); border-radius: var(--borderRadiusMedium); background: var(--colorNeutralBackground1); color: var(--colorNeutralForeground1); font-size: var(--fontSizeBase200); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; cursor: pointer; }
    .chips button:hover { border-color: var(--colorBrandStroke2); background: var(--colorBrandBackground2); }
    .chips .empty { color: var(--colorNeutralForeground3); font-size: var(--fontSizeBase200); }
    .chips .new-name { width: 100%; border-style: dashed; color: var(--colorBrandForeground2); }
    .text-body { flex: 1; min-height: 280px; padding: var(--spacingVerticalS); border: var(--strokeWidthThin) solid var(--colorNeutralStroke1); border-radius: var(--borderRadiusMedium); background: var(--colorNeutralBackground2); color: var(--colorNeutralForeground1); font: inherit; font-size: var(--fontSizeBase300); resize: vertical; }
    footer { display: flex; align-items: center; gap: var(--spacingHorizontalS); padding: var(--spacingVerticalS) var(--spacingHorizontalL); border-top: var(--strokeWidthThin) solid var(--colorNeutralStroke2); background: var(--colorNeutralBackground2); }
    footer .hint { flex: 1; color: var(--colorNeutralForeground3); font-size: var(--fontSizeBase100); overflow: hidden; text-overflow: ellipsis; }
    footer .error { color: var(--colorPaletteRedForeground1, var(--colorStatusDangerForeground1)); font-size: var(--fontSizeBase100); }
    footer button { white-space: nowrap; }
    .tone-maths, .tone-numbers { border-color: var(--colorPaletteBlueBorder2, var(--colorNeutralStroke2)); }
    .tone-dates { border-color: var(--colorPaletteGreenBorder2, var(--colorNeutralStroke2)); }
    .tone-records { border-color: var(--colorPalettePurpleBorder2, var(--colorNeutralStroke2)); }
    .tone-text { border-color: var(--colorPaletteGoldBorder2, var(--colorNeutralStroke2)); }
    .tone-lists { border-color: var(--colorPaletteTealBorder2, var(--colorNeutralStroke2)); }
    .tone-logic { border-color: var(--colorPaletteRedBorder2, var(--colorNeutralStroke2)); }
  `];

  private unsubscribe?: () => void;

  connectedCallback(): void {
    super.connectedCallback();
    this.unsubscribe = editor.subscribe(() => this.requestUpdate());
    window.addEventListener('keydown', this.onWindowKeydown);
  }

  disconnectedCallback(): void {
    this.unsubscribe?.();
    window.removeEventListener('keydown', this.onWindowKeydown);
    window.clearTimeout(this.textTimer);
    super.disconnectedCallback();
  }

  private onWindowKeydown = (event: KeyboardEvent): void => {
    if (!editor.get().valueEditor) {
      return;
    }

    if (event.key === 'Escape') {
      event.preventDefault();
      void editor.cancelValue();
    } else if (event.ctrlKey && event.key.toLowerCase() === 'z') {
      event.preventDefault();
      void editor.valueCommand(event.shiftKey ? 'redo' : 'undo');
    } else if (event.ctrlKey && event.key.toLowerCase() === 'y') {
      event.preventDefault();
      void editor.valueCommand('redo');
    }
  };

  private send(action: string, params: Record<string, unknown> = {}): void {
    void editor.valueCommand(action, params);
  }

  private samePath(path: number[] | null, other: number[]): boolean {
    return path !== null && path.length === other.length
      && path.every((value, index) => value === other[index]);
  }

  private selected(node: EditorValueNode, snapshot: EditorValueSnapshot): boolean {
    return this.samePath(node.path, snapshot.selectedPath ?? []);
  }

  private caretAt(node: EditorValueNode, after: boolean, snapshot: EditorValueSnapshot): boolean {
    return this.samePath(node.path, snapshot.caretPath) && snapshot.caretAfter === after;
  }

  private caret(): TemplateResult {
    return html`<span class="caret" aria-hidden="true"></span>`;
  }

  private removeToken(node: EditorValueNode): TemplateResult | typeof nothing {
    if (!node.canRemove) {
      return nothing;
    }

    return html`<button class="remove" type="button" title=${t('ValuePicker.Slot.Remove')}
      @click=${(event: Event) => { event.stopPropagation(); this.send('remove', { path: node.path }); }}>${icon('close')}</button>`;
  }

  private renderNode(node: EditorValueNode, snapshot: EditorValueSnapshot): TemplateResult | typeof nothing {
    if (node.kind === 'root') {
      return node.children.length > 0 ? this.renderNode(node.children[0]!, snapshot) : nothing;
    }

    if (node.kind === 'sequence') {
      return html`<span class="wrap">${node.children.map((child) => this.renderNode(child, snapshot))}</span>`;
    }

    if (node.kind === 'empty') {
      return html`<span class="token empty" data-required=${node.isRequired ? 'true' : 'false'}
        title=${node.hoverHint ?? ''}
        @click=${() => this.send('select', { path: node.path })}>${node.text || (node.isGhost ? '+' : '')}</span>`;
    }

    const classes = ['token', node.kind, node.stacked ? 'stack' : '',
      this.selected(node, snapshot) ? 'selected' : '',
      node.hasProblem ? 'problem' : ''].filter((part) => part.length > 0).join(' ');
    const operator = node.kind === 'binary' || node.kind === 'unary'
      ? html`<select class="op" aria-label=${t('ValuePicker.Operator.Change')}
          @change=${(event: Event) => this.send('changeOperator', {
            path: node.path, sign: (event.target as HTMLSelectElement).value })}>
          ${snapshot.operators.map((choice) => html`
            <option value=${choice.insert.trim()} ?selected=${choice.insert.trim() === node.operator}>${choice.token}</option>
          `)}
        </select>`
      : nothing;

    if (node.kind === 'binary') {
      return html`<span class=${classes} title=${node.hoverHint ?? ''}
        @click=${() => this.send('select', { path: node.path })}>
        ${this.caretAt(node, false, snapshot) ? this.caret() : nothing}
        ${node.children.length > 0 ? this.renderNode(node.children[0]!, snapshot) : nothing}
        ${operator}
        ${node.children.length > 1 ? this.renderNode(node.children[1]!, snapshot) : nothing}
        ${this.removeToken(node)}
        ${this.caretAt(node, true, snapshot) ? this.caret() : nothing}
      </span>`;
    }

    if (node.kind === 'unary') {
      return html`<span class=${classes} title=${node.hoverHint ?? ''}
        @click=${() => this.send('select', { path: node.path })}>
        ${operator}
        ${node.children.length > 0 ? this.renderNode(node.children[0]!, snapshot) : nothing}
        ${this.removeToken(node)}
      </span>`;
    }

    if (node.kind === 'group') {
      return html`<span class=${classes} @click=${() => this.send('select', { path: node.path })}>
        <span class="bracket">(</span>
        ${node.children.length > 0 ? this.renderNode(node.children[0]!, snapshot) : nothing}
        <span class="bracket">)</span>
        ${this.removeToken(node)}
      </span>`;
    }

    if (node.kind === 'list') {
      return html`<span class=${classes} @click=${() => this.send('select', { path: node.path })}>
        <span class="bracket">[</span>
        ${node.children.map((child, index) => html`
          ${index > 0 ? html`<span class="bracket">,</span>` : nothing}
          ${this.renderNode(child, snapshot)}
        `)}
        ${node.ghost ? this.renderNode(node.ghost, snapshot) : nothing}
        <span class="bracket">]</span>
        ${this.removeToken(node)}
      </span>`;
    }

    if (node.kind === 'call') {
      const args = node.children.map((argument) => node.stacked
        ? html`<span class="arg-row">
            <span class="arg-label">${argument.argumentLabel}</span>
            ${this.renderNode(argument, snapshot)}
          </span>`
        : this.renderNode(argument, snapshot));
      return html`<span class=${classes} title=${node.hoverHint ?? ''}
        @click=${() => this.send('select', { path: node.path })}>
        <span class="call-name">${node.name}</span><span class="bracket">(</span>
        ${args}
        ${node.ghost ? this.renderNode(node.ghost, snapshot) : nothing}
        <span class="bracket">)</span>
        ${this.removeToken(node)}
      </span>`;
    }

    return html`<span class=${classes} title=${node.hoverHint ?? ''}
      @click=${() => this.send('select', { path: node.path })}>
      ${this.caretAt(node, false, snapshot) ? this.caret() : nothing}
      ${node.text}
      ${this.removeToken(node)}
      ${this.caretAt(node, true, snapshot) ? this.caret() : nothing}
    </span>`;
  }

  private renderWall(snapshot: EditorValueSnapshot): TemplateResult {
    const wall = snapshot.wall;
    return html`<section class="panel">
      <h3>${t('ValuePicker.Panel.Variables')}</h3>
      <input class="search" type="search" .value=${wall.search} placeholder=${t('VarWall.Search')}
        @input=${(event: Event) => this.send('wallSearch', { text: (event.target as HTMLInputElement).value })} />
      <div class="filters">
        ${wall.filters.map((filter) => html`
          <button type="button" class=${filter.selected ? 'on' : ''} title=${filter.hint}
            @click=${() => this.send('wallFilter', { group: filter.group })}>${filter.label}</button>
        `)}
      </div>
      <div class="chips">
        ${wall.shown.length === 0
          ? html`<span class="empty">${wall.details}</span>`
          : wall.shown.map((variable) => html`
            <button type="button" title=${variable.details}
              @click=${() => this.send('variable', { name: variable.name })}>
              ${variable.display}${variable.display === variable.name ? '' : ' (' + variable.name + ')'}
            </button>
          `)}
        ${snapshot.isNameMode && snapshot.canCreateName
          ? html`<button type="button" class="new-name"
              @click=${() => this.send('typingText', { text: snapshot.newName })}>+${snapshot.newNameLine}</button>`
          : nothing}
      </div>
    </section>`;
  }

  private renderFunctions(snapshot: EditorValueSnapshot): TemplateResult {
    return html`<section class="panel">
      <h3>${t('ValuePicker.Panel.Functions')}</h3>
      <input class="search" type="search" .value=${snapshot.functionQuery}
        placeholder=${t('ExprBuilder.FunctionsFilter')}
        @input=${(event: Event) => this.send('functionSearch', { text: (event.target as HTMLInputElement).value })} />
      <div class="filters">
        ${snapshot.functionGroups.map((group) => html`
          <button type="button" class=${group.selected ? 'on' : ''}
            @click=${() => this.send('functionGroup', { group: group.group })}>${group.label}</button>
        `)}
      </div>
      <div class="chips">
        ${snapshot.functions.length === 0
          ? html`<span class="empty">${t('Add.NoActionFound')}</span>`
          : snapshot.functions.map((fn) => html`
            <button type="button" title=${fn.heading + ' — ' + fn.writtenAs + ' — ' + fn.hint}
              @click=${() => this.send('function', { name: fn.name })}>${fn.tag}</button>
          `)}
      </div>
    </section>`;
  }

  private onBandSplitDown = (event: PointerEvent): void => {
    event.preventDefault();
    this.bandStartY = event.clientY;
    this.bandStartHeight = this.bandHeight;
    (event.target as HTMLElement).setPointerCapture(event.pointerId);
  };

  private onBandSplitMove = (event: PointerEvent): void => {
    const target = event.target as HTMLElement;
    if (!target.hasPointerCapture(event.pointerId)) {
      return;
    }

    this.bandHeight = Math.min(420, Math.max(190,
      this.bandStartHeight + (event.clientY - this.bandStartY)));
  };

  private onBandSplitUp = (event: PointerEvent): void => {
    const target = event.target as HTMLElement;
    if (target.hasPointerCapture(event.pointerId)) {
      target.releasePointerCapture(event.pointerId);
    }
  };

  private renderBand(snapshot: EditorValueSnapshot): TemplateResult {
    return html`<section class="band-panel">
      <div class="band-head">
        <button class="ghost" type="button" title=${t('ValuePicker.Undo')} ?disabled=${!snapshot.canUndo}
          @click=${() => this.send('undo')}>${icon('undo')}</button>
        <button class="ghost" type="button" title=${t('ValuePicker.Redo')} ?disabled=${!snapshot.canRedo}
          @click=${() => this.send('redo')}>${icon('redo')}</button>
        <button class="ghost" type="button" title=${t('ValuePicker.Clear')} ?disabled=${!snapshot.canClear}
          @click=${() => this.send('clear')}>${icon('delete')}</button>
        <span class="label">${t('ValuePicker.Structure')}</span>
        <span class="hint">${snapshot.structureHint}</span>
      </div>
      <div class="band" tabindex="0" @keydown=${(event: KeyboardEvent) => this.onBandKeydown(event)}>
        ${snapshot.recipe ? this.renderNode(snapshot.recipe, snapshot) : nothing}
        ${snapshot.isValueEmpty ? html`<span class="empty-hint">${snapshot.emptyBandHint}</span>` : nothing}
        ${snapshot.caretPath.length === 0 && snapshot.caretAfter ? this.caret() : nothing}
      </div>
      <div class="band-foot">
        <span class=${snapshot.noteIsProblem ? 'problem' : ''}>${snapshot.note}</span>
        ${snapshot.hasSaying ? html`<span class="saying">${snapshot.saying}</span>` : nothing}
      </div>
      <div class="band-splitter" role="separator"
        @pointerdown=${this.onBandSplitDown}
        @pointermove=${this.onBandSplitMove}
        @pointerup=${this.onBandSplitUp}></div>
    </section>`;
  }

  private onBandKeydown(event: KeyboardEvent): void {
    if (event.target instanceof HTMLInputElement || event.target instanceof HTMLTextAreaElement) {
      return;
    }

    if (event.key === 'Backspace') {
      event.preventDefault();
      this.send('cutBefore');
    } else if (event.key === 'Delete') {
      event.preventDefault();
      this.send('cutAfter');
    } else if (event.key === 'ArrowLeft') {
      event.preventDefault();
      this.send('caret', { direction: -1 });
    } else if (event.key === 'ArrowRight') {
      event.preventDefault();
      this.send('caret', { direction: 1 });
    }
  }

  private renderMaterials(snapshot: EditorValueSnapshot): TemplateResult {
    return html`<div class="materials">
      <div class="signs">
        <span class="label">${t('ValuePicker.Panel.Operators')}</span>
        ${snapshot.operators.map((operator) => html`
          <button type="button" title=${operator.hint} ?disabled=${!operator.enabled}
            @click=${() => this.send('place', { token: operator.insert })}>${operator.token}</button>
        `)}
        <button type="button" title=${t('Expr.Op.Group')} @click=${() => this.send('wrapGroup')}>${t('ValuePicker.Group')}</button>
        <button type="button" title=${t('Expr.Op.List')} @click=${() => this.send('wrapList')}>${t('ValuePicker.List')}</button>
        <span class="label">${t('ValuePicker.Panel.Values')}</span>
        ${snapshot.values.map((value) => html`
          <button type="button" @click=${() => this.send('place', { token: value.insert })}>${value.text}</button>
        `)}
      </div>
      <div class="typed">
        <button class=${'mode ' + (snapshot.isTypingNumber ? 'on' : '')} type="button"
          @click=${() => this.send('typingMode', { number: true })}>${t('ValuePicker.Value.Number')}</button>
        <button class=${'mode ' + (snapshot.isTypingNumber ? '' : 'on')} type="button"
          @click=${() => this.send('typingMode', { number: false })}>${t('ValuePicker.Value.Text')}</button>
        <input .value=${snapshot.typingText} placeholder=${snapshot.typingPlaceholder}
          @input=${(event: Event) => this.send('typingText', { text: (event.target as HTMLInputElement).value })}
          @keydown=${(event: KeyboardEvent) => { if (event.key === 'Enter') { event.preventDefault(); this.send('insertTyped'); } }} />
        <button type="button" ?disabled=${!snapshot.canInsertTyped}
          @click=${() => this.send('insertTyped')}>${t('ValuePicker.Value.Insert')}</button>
        ${snapshot.hasTypingNote ? html`<span class="note">${snapshot.typingNote}</span>` : nothing}
      </div>
    </div>`;
  }

  render() {
    const snapshot = editor.get().valueEditor;
    this.style.display = snapshot ? 'grid' : 'none';
    if (!snapshot) {
      return nothing;
    }

    return html`<div class="dialog" role="dialog" aria-modal="true" aria-label=${snapshot.title}>
      <header>
        <div>
          <h2>${snapshot.title}</h2>
          <p>${snapshot.targetLine || snapshot.hint}</p>
        </div>
        <button class="ghost close" type="button" title=${t('Common.Close')}
          @click=${() => void editor.cancelValue()}>${icon('close')}</button>
      </header>
      <div class="body" style=${'--band-height:' + String(this.bandHeight) + 'px'}>
        ${snapshot.isTextMode
          ? html`<textarea class="text-body" .value=${snapshot.textBody} aria-label=${snapshot.title}
              @input=${(event: Event) => {
                window.clearTimeout(this.textTimer);
                const value = (event.target as HTMLTextAreaElement).value;
                this.textTimer = window.setTimeout(() => this.send('textBody', { text: value }), 80);
              }}></textarea>`
          : nothing}
        ${snapshot.isNameMode
          ? html`<div class="panels">${this.renderWall(snapshot)}</div>`
          : html`
              ${snapshot.showsBand ? this.renderBand(snapshot) : nothing}
              ${snapshot.showsFormulaMaterials ? this.renderMaterials(snapshot) : nothing}
              <div class="panels">
                ${this.renderWall(snapshot)}
                ${snapshot.showsFormulaMaterials ? this.renderFunctions(snapshot) : nothing}
              </div>
            `}
      </div>
      <footer>
        <span class="hint">${snapshot.hint}</span>
        ${snapshot.noteIsProblem ? html`<span class="error">${snapshot.note}</span>` : nothing}
        <button class="ghost" type="button" @click=${() => void editor.cancelValue()}>${t('Common.Cancel')}</button>
        <fluent-button appearance="accent" ?disabled=${!snapshot.canConfirm}
          @click=${() => void editor.confirmValue()}>${t('Common.Ok')}</fluent-button>
      </footer>
    </div>`;
  }
}

declare global {
  interface HTMLElementTagNameMap {
    'wg-value-editor': WgValueEditor;
  }
}
