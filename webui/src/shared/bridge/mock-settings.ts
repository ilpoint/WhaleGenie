import type {
  SettingsMenuState,
  SettingsState,
  SettingsWheelSlot,
  SettingsWheelState,
  WindowRuleState,
} from './protocol.js';
import type { MockContext } from './mock-context.js';

/**
 * Settings-domain answers for the mock host. The transport routes by the settings. prefix.
 * Most commands are a patch of one nested state, so the two small helpers below keep each
 * case to its own edit instead of repeating the whole snapshot.
 */
export function handleSettings(host: MockContext, method: string, params: Record<string, unknown>): unknown {
  let result: unknown;
  switch (method) {
        case 'settings.state':
          result = host.settingsState;
          break;
        case 'settings.update': {
          const patch = (params['patch'] ?? {}) as Partial<SettingsState>;
          host.settingsState = { ...host.settingsState, ...patch };
          result = host.settingsState;
          if (patch.language !== undefined) {
            host.info = {
              ...host.info,
              language: patch.language,
            };
            host.push('strings.changed', host.bundle());
          }
          break;
        }
        case 'settings.menu.capture': {
          const binding = String(params['binding'] ?? '');
          result = setMenu(host, {
            launcher: binding,
            display: binding,
            isMiddle: binding === 'Mouse Middle',
            isCustom: binding !== 'Mouse Middle',
            status: binding ? '已设置' : '未设置',
          });
          break;
        }
        case 'settings.menu.clear':
          result = setMenu(host, {
            launcher: '', display: '无', isMiddle: false, isCustom: false, status: '未设置',
          });
          break;
        case 'settings.menu.rule.add': {
          const rule = params['rule'] as WindowRuleState;
          result = setMenu(host, {
            rules: [...host.settingsState.menu.rules, {
              key: 'rule-' + Date.now(), summary: rule.title || rule.process || '窗口规则',
              pageCountLabel: '1 页', isDefault: false, isBlocked: false, canAddPage: true, rule,
            }],
          });
          break;
        }
        case 'settings.menu.rule.edit': {
          const rule = params['rule'] as WindowRuleState;
          result = setMenu(host, {
            rules: host.settingsState.menu.rules.map((row) => row.key === params['key']
              ? { ...row, rule, summary: rule.title || rule.process || '窗口规则' }
              : row),
          });
          break;
        }
        case 'settings.menu.rule.remove':
          result = setMenu(host, {
            rules: host.settingsState.menu.rules.filter((row) => row.key !== params['key']),
          });
          break;
        case 'settings.menu.rule.addPage':
          result = host.settingsState;
          break;
        case 'settings.menu.rule.move': {
          const step = Number(params['step'] ?? 0);
          const rules = [...host.settingsState.menu.rules];
          const at = rules.findIndex((row) => row.key === params['key']);
          const to = Math.max(0, Math.min(rules.length - 1, at + step));
          if (at >= 0 && to !== at) {
            const [row] = rules.splice(at, 1);
            if (row) {
              rules.splice(to, 0, row);
            }
          }
          result = setMenu(host, { rules });
          break;
        }
        case 'settings.menu.rule.moveTo': {
          const rules = [...host.settingsState.menu.rules];
          const from = rules.findIndex((row) => row.key === params['key']);
          const to = rules.findIndex((row) => row.key === params['target']);
          if (from >= 0 && to >= 0) {
            const [row] = rules.splice(from, 1);
            if (row) {
              rules.splice(to, 0, row);
            }
          }
          result = setMenu(host, { rules });
          break;
        }
        case 'settings.wheel.update': {
          const field = String(params['field'] ?? '');
          const value = String(params['value'] ?? '');
          const next: Record<string, unknown> = { ...host.settingsState.wheel };
          if (['diameter', 'fontSize', 'thirdCapacity', 'timeoutMs'].includes(field)) {
            next[field] = Number(value);
          } else if (field === 'enabled') {
            next[field] = value === 'true';
          } else {
            next[field] = value === 'true' ? true : value === 'false' ? false : value;
          }
          result = setWheel(host, next as unknown as Partial<SettingsWheelState>);
          break;
        }
        case 'settings.wheel.launcher': {
          const launcher = String(params['value'] ?? '');
          result = setWheel(host, {
            launcher,
            enabled: launcher.length > 0,
            launcherStatus: launcher ? '就绪' : '未设置',
          });
          break;
        }
        case 'settings.wheel.enabled': {
          const enabled = params['value'] === true;
          result = setWheel(host, {
            enabled,
            launcher: enabled ? (host.settingsState.wheel.launcher || 'Mouse Right') : '',
          });
          break;
        }
        case 'settings.wheel.enableSystem':
          result = setWheel(host, { isSystemOff: false });
          break;
        case 'settings.wheel.repeat': {
          const key = String(params['binding'] ?? '');
          result = setWheel(host, { repeatKey: key, repeatDisplay: key });
          break;
        }
        case 'settings.wheel.scene.select':
          result = setWheel(host, { selectedScene: String(params['key'] ?? 'global') });
          break;
        case 'settings.wheel.scene.add': {
          const rule = params['rule'] as WindowRuleState;
          const key = 'scene-' + Date.now();
          result = setWheel(host, {
            selectedScene: key,
            scenes: [...host.settingsState.wheel.scenes, {
              key, summary: rule.title || rule.process || '窗口规则', isGlobal: false, rule,
            }],
          });
          break;
        }
        case 'settings.wheel.scene.edit': {
          const rule = params['rule'] as WindowRuleState;
          result = setWheel(host, {
            scenes: host.settingsState.wheel.scenes.map((scene) => scene.key === params['key']
              ? { ...scene, rule, summary: rule.title || rule.process || '窗口规则' }
              : scene),
          });
          break;
        }
        case 'settings.wheel.scene.remove':
          result = setWheel(host, {
            selectedScene: 'global',
            scenes: host.settingsState.wheel.scenes.filter((scene) => scene.key !== params['key']),
          });
          break;
        case 'settings.wheel.scene.move': {
          const scenes = [...host.settingsState.wheel.scenes];
          const at = scenes.findIndex((scene) => scene.key === host.settingsState.wheel.selectedScene);
          const to = Math.max(1, Math.min(scenes.length - 1, at + Number(params['step'] ?? 0)));
          if (at > 0 && to !== at) {
            const [scene] = scenes.splice(at, 1);
            if (scene) {
              scenes.splice(to, 0, scene);
            }
          }
          result = setWheel(host, { scenes });
          break;
        }
        case 'settings.wheel.slot.set': {
          const ring = String(params['ring'] ?? '');
          const index = Number(params['index'] ?? 0);
          const macro = String(params['macro'] ?? '');
          const note = String(params['note'] ?? '');
          const icon = params['icon'] as { kind: string; value: string } | null;
          const update = (slot: SettingsWheelSlot): SettingsWheelSlot => ({
            ...slot,
            macro,
            note,
            label: note || macro,
            icon,
            inherited: false,
            overridden: macro.length > 0,
            empty: macro.length === 0,
            tone: macro.length === 0 ? 'empty' : 'override',
          });
          result = setWheel(host, {
            slots: host.settingsState.wheel.slots.map((slot) =>
              slot.ring === ring && slot.index === index ? update(slot) : slot),
          });
          break;
        }
        case 'settings.menu.rememberPin':
          result = setMenu(host, { rememberPinned: params['remember'] === true });
          break;
        case 'settings.openFolder':
          result = null;
          break;
  }

  return result;
}

function commit(host: MockContext, next: SettingsState): SettingsState {
  host.settingsState = next;
  return next;
}

function setMenu(host: MockContext, patch: Partial<SettingsMenuState>): SettingsState {
  return commit(host, {
    ...host.settingsState,
    menu: { ...host.settingsState.menu, ...patch },
  });
}

function setWheel(host: MockContext, patch: Partial<SettingsWheelState>): SettingsState {
  return commit(host, {
    ...host.settingsState,
    wheel: { ...host.settingsState.wheel, ...patch },
  });
}
