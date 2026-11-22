import { html, nothing } from 'lit';
import type { SettingsMenuState } from '../../shared/bridge/protocol.js';
import { t } from '../../shared/strings/strings.js';

export interface MenuActions {
  beginCapture(): void;
  captureKey(event: KeyboardEvent): void;
  capturePointer(event: PointerEvent): void;
  setMiddle(): void;
  clear(): void;
  addRule(): void;
  editRule(key: string): void;
  removeRule(key: string): void;
  addPage(key: string): void;
  moveRule(key: string, step: number): void;
  moveRuleTo(key: string, target: string): void;
}

export function renderMenu(state: SettingsMenuState, capturing: boolean, actions: MenuActions) {
  return html`
    <section class="card" data-settings-section="menu">
      <div class="card-head">
        <h2>${t('Settings.MenuLauncher')}</h2>
        <span class="note">${t('Settings.MenuLauncherNote')}</span>
      </div>
      <p class="hint">${t('Settings.MenuLauncherHint')}</p>
      <div class="hotkey-row">
        <code class="binding">${state.display}</code>
        ${capturing
          ? html`<button type="button" class="capture-target" data-menu-capture
              @keydown=${(event: KeyboardEvent) => actions.captureKey(event)}
              @pointerdown=${(event: PointerEvent) => actions.capturePointer(event)}
              @auxclick=${(event: MouseEvent) => event.preventDefault()}
              @contextmenu=${(event: MouseEvent) => event.preventDefault()}>
              ${t('Settings.HotkeyPress')}
            </button>`
          : html`<fluent-button appearance="subtle" @click=${actions.beginCapture}>
              ${t('Settings.HotkeySet')}
            </fluent-button>`}
        <fluent-button appearance="subtle" @click=${actions.setMiddle}>
          ${t('Settings.MiddleButton')}
        </fluent-button>
        <fluent-button appearance="subtle" @click=${actions.clear}>
          ${t('Settings.HotkeyClear')}
        </fluent-button>
      </div>
      <p class="status">${state.status}</p>
    </section>

    <section class="card" data-settings-section="menu">
      <div class="card-head">
        <h2>${t('Settings.MenuWindowRules')}</h2>
        <span class="note">${t('Settings.MenuWindowRulesNote')}</span>
      </div>
      <p class="hint">${t('Settings.MenuRuleHint')}</p>
      <div class="rule-list" role="list">
        ${state.rules.map((rule) => html`
          <div class="rule" role="listitem"
            draggable=${String(!rule.isDefault && !rule.isBlocked)}
            @dragstart=${(event: DragEvent) => startDrag(event, rule.key)}
            @dragover=${(event: DragEvent) => allowDrop(event, rule)}
            @drop=${(event: DragEvent) => drop(event, rule.key, actions)}>
            <div class="rule-text">
              <strong>${rule.summary}</strong>
              <span>
                ${rule.pageCountLabel}
                ${rule.isDefault ? html` · ${t('Settings.MenuRuleDefault')}` : nothing}
                ${rule.isBlocked ? html` · ${t('Settings.MenuRuleBlocked')}` : nothing}
              </span>
            </div>
            <div class="rule-buttons">
              <fluent-button appearance="subtle" ?disabled=${rule.isDefault}
                @click=${() => actions.editRule(rule.key)}>
                ${t('Settings.MenuRuleEdit')}
              </fluent-button>
              <fluent-button appearance="subtle" ?disabled=${rule.isDefault}
                @click=${() => actions.removeRule(rule.key)}>
                ${t('Settings.MenuRuleRemove')}
              </fluent-button>
              <fluent-button appearance="subtle" ?disabled=${!rule.canAddPage}
                @click=${() => actions.addPage(rule.key)}>
                ${t('Settings.MenuRuleAddPage')}
              </fluent-button>
              <fluent-button appearance="subtle" ?disabled=${rule.isDefault || rule.isBlocked}
                @click=${() => actions.moveRule(rule.key, -1)}>
                ${t('Settings.MenuRuleUp')}
              </fluent-button>
              <fluent-button appearance="subtle" ?disabled=${rule.isDefault || rule.isBlocked}
                @click=${() => actions.moveRule(rule.key, 1)}>
                ${t('Settings.MenuRuleDown')}
              </fluent-button>
            </div>
          </div>
        `)}
      </div>
      <div class="actions">
        <fluent-button appearance="accent" @click=${actions.addRule}>
          ${t('Settings.MenuRuleAdd')}
        </fluent-button>
      </div>
      ${state.problem ? html`<p class="error">${state.problem}</p>` : nothing}
    </section>
  `;
}

function startDrag(event: DragEvent, key: string): void {
  if (event.dataTransfer) {
    event.dataTransfer.effectAllowed = 'move';
    event.dataTransfer.setData('text/plain', key);
  }
}

function allowDrop(event: DragEvent, rule: { isDefault: boolean; isBlocked: boolean }): void {
  if (!rule.isDefault && !rule.isBlocked) {
    event.preventDefault();
  }
}

function drop(event: DragEvent, target: string, actions: MenuActions): void {
  event.preventDefault();
  const source = event.dataTransfer?.getData('text/plain');
  if (source && source !== target) {
    actions.moveRuleTo(source, target);
  }
}