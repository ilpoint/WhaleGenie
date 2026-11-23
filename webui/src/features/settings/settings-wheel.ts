import { html, nothing } from 'lit';
import type {
  SettingsWheelIcon,
  SettingsWheelSlot,
  SettingsWheelState,
} from '../../shared/bridge/protocol.js';
import { t } from '../../shared/strings/strings.js';

export interface WheelSlotDraft {
  ring: string;
  index: number;
  position: string;
  macro: string;
  note: string;
  icon: SettingsWheelIcon | null;
  inherited: boolean;
  overridden: boolean;
}

export interface WheelActions {
  update(field: string, value: string): void;
  setLauncher(value: string): void;
  setEnabled(value: boolean): void;
  enableSystem(): void;
  beginRepeatCapture(): void;
  captureRepeatKey(event: KeyboardEvent): void;
  selectScene(key: string): void;
  addScene(): void;
  editScene(key: string): void;
  removeScene(key: string): void;
  moveScene(step: number): void;
  selectSlot(slot: SettingsWheelSlot): void;
  editDraft(field: 'macro' | 'note', value: string): void;
  pickIcon(): void;
  clearIcon(): void;
  saveSlot(): void;
  clearSlot(): void;
}

export function renderWheelSettings(
  state: SettingsWheelState,
  capturingRepeat: boolean,
  actions: WheelActions,
) {
  return html`
    <section class="card" data-settings-section="wheel">
      <div class="card-head">
        <h2>${t('Settings.WheelTitle')}</h2>
        <span class="note">${t('Settings.WheelTitleNote')}</span>
      </div>
      <p class="hint">${t('Settings.WheelGestureHint')}</p>

      <div class="settings-grid">
        <label class="field">
          <span>${t('Settings.WheelLauncher')}</span>
          <select ?disabled=${!state.enabled}
            @change=${(event: Event) => actions.setLauncher(
              (event.target as HTMLSelectElement).value)}>
            <option value="" ?selected=${state.launcher === ''}>${t('Common.None')}</option>
            ${state.launcherChoices.map((choice) => html`
              <option value=${choice.value} ?selected=${choice.value === state.launcher}>
                ${choice.label}
              </option>
            `)}
          </select>
        </label>
        <label class="check-row">
          <input type="checkbox" .checked=${state.enabled}
            @change=${(event: Event) => actions.setEnabled(
              (event.target as HTMLInputElement).checked)} />
          <span>${t('Settings.WheelEnabled')}</span>
        </label>
        <label class="check-row">
          <input type="checkbox" .checked=${state.onlyInPrograms}
            @change=${(event: Event) => actions.update('onlyInPrograms',
              String((event.target as HTMLInputElement).checked))} />
          <span>${t('Settings.WheelOnlyInPrograms')}</span>
        </label>
      </div>
      <p class="hint">${state.launcherStatus}</p>
      ${state.isSystemOff
        ? html`<div class="actions">
            <fluent-button appearance="accent" @click=${actions.enableSystem}>
              ${t('Settings.WheelEnableSystem')}
            </fluent-button>
          </div>`
        : nothing}

      <div class="settings-grid compact">
        <label class="field">
          <span>${t('Settings.WheelSize')}</span>
          <span class="inline-unit">
            <input type="number" min="150" max="600" step="10" .value=${String(state.diameter)}
              @change=${(event: Event) => actions.update('diameter',
                (event.target as HTMLInputElement).value)} />
            <em>${t('Settings.WheelSizeUnit')}</em>
          </span>
        </label>
        <label class="field">
          <span>${t('Settings.WheelTextSize')}</span>
          <span class="inline-unit">
            <input type="number" min="8" max="24" step="1" .value=${String(state.fontSize)}
              @change=${(event: Event) => actions.update('fontSize',
                (event.target as HTMLInputElement).value)} />
            <em>${t('Settings.WheelSizeUnit')}</em>
          </span>
        </label>
        <label class="field">
          <span>${t('Settings.WheelThird')}</span>
          <select
            @change=${(event: Event) => actions.update('thirdCapacity',
              (event.target as HTMLSelectElement).value)}>
            ${state.thirdChoices.map((choice) => html`
              <option value=${String(choice.value)}
                ?selected=${String(choice.value) === String(state.thirdCapacity)}>
                ${choice.label}
              </option>
            `)}
          </select>
        </label>
        <label class="field">
          <span>${t('Settings.WheelTimeout')}</span>
          <span class="inline-unit">
            <input type="number" min="0" max="120000" step="1000" .value=${String(state.timeoutMs)}
              @change=${(event: Event) => actions.update('timeoutMs',
                (event.target as HTMLInputElement).value)} />
            <em>${t('Settings.WheelTimeoutUnit')}</em>
          </span>
        </label>
        <div class="field">
          <span>${t('Settings.WheelRepeat')}</span>
          <div class="hotkey-row">
            <code class="binding">${state.repeatDisplay}</code>
            ${capturingRepeat
              ? html`<button type="button" class="capture-target" data-wheel-repeat
                  @keydown=${(event: KeyboardEvent) => actions.captureRepeatKey(event)}
                  @auxclick=${(event: MouseEvent) => event.preventDefault()}>
                  ${t('Settings.HotkeyPress')}
                </button>`
              : html`<fluent-button appearance="subtle" @click=${actions.beginRepeatCapture}>
                  ${t('Settings.HotkeySet')}
                </fluent-button>`}
          </div>
        </div>
      </div>

      <div class="settings-grid compact">
        ${toggle('wideOuter', 'Settings.WheelWideOuter', state.wideOuter, actions)}
        ${toggle('hideEmptyOuter', 'Settings.WheelHideEmptyOuter', state.hideEmptyOuter, actions)}
        ${toggle('hideLabelWithIcon', 'Settings.WheelIconOnly', state.hideLabelWithIcon, actions)}
        ${toggle('centerName', 'Settings.WheelCenterName', state.centerName, actions)}
        ${toggle('clampToScreen', 'Settings.WheelClamp', state.clampToScreen, actions)}
        ${toggle('movePointerToCenter', 'Settings.WheelMovePointer', state.movePointerToCenter, actions)}
        ${toggle('showGuide', 'Settings.WheelGuide', state.showGuide, actions)}
        ${toggle('extendedHint', 'Settings.WheelExtendedHint', state.extendedHint, actions)}
        ${toggle('followTheme', 'Settings.WheelFollowTheme', state.followTheme, actions)}
        ${toggle('gloss', 'Settings.WheelGloss', state.gloss, actions)}
        ${toggle('hoverBorder', 'Settings.WheelHoverBorder', state.hoverBorder, actions)}
        ${toggle('popAnimation', 'Settings.WheelPopAnimation', state.popAnimation, actions)}
      </div>
      ${state.problem ? html`<p class="error">${state.problem}</p>` : nothing}

      <h3>${t('Settings.WheelPreview')}</h3>
      <p class="hint">${t('Settings.WheelPreviewHint')}</p>
      ${renderWheel(state, state.preview, null, false)}
    </section>
  `;
}

export function renderWheelActions(
  state: SettingsWheelState,
  selected: { ring: string; index: number } | null,
  draft: WheelSlotDraft | null,
  actions: WheelActions,
) {
  const scene = state.scenes.find((candidate) => candidate.key === state.selectedScene);
  return html`
    <section class="card" data-settings-section="wheel">
      <div class="card-head">
        <h2>${t('Settings.WheelActions')}</h2>
        <span class="note">${t('Settings.WheelActionsNote')}</span>
      </div>
      <p class="hint">${t('Settings.WheelActionsHint')}</p>
      <div class="wheel-layout">
        <div class="scene-panel">
          <label class="field">
            <span>${t('Wheel.Scenes')}</span>
            <select
              @change=${(event: Event) => actions.selectScene(
                (event.target as HTMLSelectElement).value)}>
              ${state.scenes.map((item) => html`
                <option value=${item.key} ?selected=${item.key === state.selectedScene}>
                  ${item.summary}
                </option>
              `)}
            </select>
          </label>
          <div class="actions">
            <fluent-button appearance="subtle" @click=${actions.addScene}>
              ${t('Wheel.SceneAdd')}
            </fluent-button>
            <fluent-button appearance="subtle" ?disabled=${scene?.isGlobal ?? true}
              @click=${() => actions.editScene(state.selectedScene)}>
              ${t('Wheel.SceneEdit')}
            </fluent-button>
            <fluent-button appearance="subtle" ?disabled=${scene?.isGlobal ?? true}
              @click=${() => actions.removeScene(state.selectedScene)}>
              ${t('Wheel.SceneRemove')}
            </fluent-button>
            <fluent-button appearance="subtle" ?disabled=${scene?.isGlobal ?? true}
              @click=${() => actions.moveScene(-1)}>
              ${t('Wheel.SceneUp')}
            </fluent-button>
            <fluent-button appearance="subtle" ?disabled=${scene?.isGlobal ?? true}
              @click=${() => actions.moveScene(1)}>
              ${t('Wheel.SceneDown')}
            </fluent-button>
          </div>
          <p class="hint">${t('Wheel.EditorHint')}</p>
          <p class="hint">${t('Wheel.SlotHint')}</p>
        </div>
        <div class="wheel-stage">
          ${renderWheel(state, state.slots, selected, true, actions)}
          <div class="legend">
            <span data-tone="global">${t('Wheel.LegendGlobal')}</span>
            <span data-tone="override">${t('Wheel.LegendOverride')}</span>
            <span data-tone="extended">${t('Wheel.LegendExtended')}</span>
          </div>
          ${selected && draft
            ? renderSlotEditor(state, draft, actions)
            : html`<p class="hint">${t('Wheel.SlotHint')}</p>`}
        </div>
      </div>
    </section>
  `;
}

function renderWheel(
  state: SettingsWheelState,
  slots: SettingsWheelSlot[],
  selected: { ring: string; index: number } | null,
  editable: boolean,
  actions?: WheelActions,
) {
  const geometry = state.geometry;
  const center = geometry.diameter / 2;
  return html`
    <div class="wheel" style="width: ${geometry.diameter}px; height: ${geometry.diameter}px">
      <svg class="wheel-rings" viewBox="0 0 ${geometry.diameter} ${geometry.diameter}"
        aria-hidden="true">
        <circle cx=${center} cy=${center} r=${geometry.center} />
        <circle cx=${center} cy=${center} r=${geometry.inner} />
        ${geometry.hasOuter
          ? html`<circle cx=${center} cy=${center} r=${geometry.outer} />`
          : nothing}
        ${geometry.hasThird
          ? html`<circle cx=${center} cy=${center} r=${geometry.third} />`
          : nothing}
        <circle cx=${center} cy=${center} r=${geometry.edge} />
      </svg>
      ${slots.map((slot) => slot.empty && !editable ? nothing : html`
        <button type="button" class="wheel-slot" data-tone=${slot.tone}
          data-selected=${String(selected?.ring === slot.ring && selected.index === slot.index)}
          style="left: calc(50% + ${slot.x}px); top: calc(50% + ${slot.y}px);
            font-size: ${Math.max(8, Math.min(14, state.fontSize))}px"
          ?disabled=${!editable}
          title=${slot.position}
          @click=${() => { if (editable) actions?.selectSlot(slot); }}>
          ${slot.empty
            ? html`<span class="empty">+</span>`
            : html`<span class="slot-label">${slot.inherited
                ? t('Wheel.Inherited').replace('{0}', slot.label)
                : slot.label}</span>`}
        </button>
      `)}
      <div class="wheel-centre">${state.scenes.find((item) => item.key === state.selectedScene)?.summary ?? ''}</div>
    </div>
  `;
}

function renderSlotEditor(state: SettingsWheelState, draft: WheelSlotDraft, actions: WheelActions) {
  return html`
    <div class="slot-editor">
      <div class="card-head">
        <h3>${draft.position}</h3>
        <span class="note">${draft.inherited ? t('Wheel.ActionInherited') : ''}</span>
      </div>
      <label class="field">
        <span>${t('Wheel.ActionMacro')}</span>
        <select
          @change=${(event: Event) => actions.editDraft('macro',
            (event.target as HTMLSelectElement).value)}>
          <option value="" ?selected=${draft.macro === ''}>${t('Common.None')}</option>
          ${state.macros.map((macro) => html`
            <option value=${macro.name} ?disabled=${!macro.enabled}
              ?selected=${macro.name === draft.macro}>${macro.name}</option>
          `)}
        </select>
      </label>
      <label class="field">
        <span>${t('Wheel.ActionNote')}</span>
        <input .value=${draft.note} placeholder=${t('Wheel.ActionNotePlaceholder')}
          @input=${(event: Event) => actions.editDraft('note',
            (event.target as HTMLInputElement).value)} />
      </label>
      <div class="field">
        <span>${t('Wheel.ActionIcon')}</span>
        <div class="icon-row">
          <code class="binding">${iconName(draft.icon)}</code>
          <fluent-button appearance="subtle" @click=${actions.pickIcon}>
            ${t('Wheel.ActionPickIcon')}
          </fluent-button>
          <fluent-button appearance="subtle" ?disabled=${draft.icon === null}
            @click=${actions.clearIcon}>
            ${t('Wheel.ActionClearIcon')}
          </fluent-button>
        </div>
      </div>
      <div class="actions">
        <fluent-button appearance="subtle" @click=${actions.clearSlot}>
          ${t('Wheel.ActionClearSlot')}
        </fluent-button>
        <fluent-button appearance="accent" @click=${actions.saveSlot}>
          ${t('Common.Save')}
        </fluent-button>
      </div>
    </div>
  `;
}

function toggle(field: string, label: string, value: boolean, actions: WheelActions) {
  return html`
    <label class="check-row">
      <input type="checkbox" .checked=${value}
        @change=${(event: Event) => actions.update(field,
          String((event.target as HTMLInputElement).checked))} />
      <span>${t(label)}</span>
    </label>
  `;
}

function iconName(icon: SettingsWheelIcon | null): string {
  if (!icon) {
    return t('Common.None');
  }
  if (icon.kind === 'image') {
    return icon.value.split(/[\\/]/).pop() ?? icon.value;
  }
  return icon.value || t('Common.None');
}