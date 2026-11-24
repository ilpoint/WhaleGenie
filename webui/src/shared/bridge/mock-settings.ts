import type {
  SettingsState,
  SettingsWheelSlot,
  SettingsWheelState,
  WindowRuleState,
} from './protocol.js';
import type { MockContext } from './mock-context.js';

/** Settings-domain answers for the mock host. The transport routes by the settings. prefix. */
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
          host.settingsState = {
            ...host.settingsState,
            menu: {
              ...host.settingsState.menu,
              launcher: binding,
              display: binding,
              isMiddle: binding === 'Mouse Middle',
              isCustom: binding !== 'Mouse Middle',
              status: binding ? '已设置' : '未设置',
            },
          };
          result = host.settingsState;
          break;
        }
        case 'settings.menu.clear':
          host.settingsState = {
            ...host.settingsState,
            menu: {
              ...host.settingsState.menu,
              launcher: '', display: '无', isMiddle: false, isCustom: false, status: '未设置',
            },
          };
          result = host.settingsState;
          break;
        case 'settings.menu.rule.add': {
          const rule = params['rule'] as WindowRuleState;
          host.settingsState = {
            ...host.settingsState,
            menu: {
              ...host.settingsState.menu,
              rules: [...host.settingsState.menu.rules, {
                key: 'rule-' + Date.now(), summary: rule.title || rule.process || '窗口规则',
                pageCountLabel: '1 页', isDefault: false, isBlocked: false, canAddPage: true, rule,
              }],
            },
          };
          result = host.settingsState;
          break;
        }
        case 'settings.menu.rule.edit': {
          const rule = params['rule'] as WindowRuleState;
          host.settingsState = {
            ...host.settingsState,
            menu: {
              ...host.settingsState.menu,
              rules: host.settingsState.menu.rules.map((row) => row.key === params['key']
                ? { ...row, rule, summary: rule.title || rule.process || '窗口规则' }
                : row),
            },
          };
          result = host.settingsState;
          break;
        }
        case 'settings.menu.rule.remove':
          host.settingsState = {
            ...host.settingsState,
            menu: {
              ...host.settingsState.menu,
              rules: host.settingsState.menu.rules.filter((row) => row.key !== params['key']),
            },
          };
          result = host.settingsState;
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
          host.settingsState = { ...host.settingsState, menu: { ...host.settingsState.menu, rules } };
          result = host.settingsState;
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
          host.settingsState = { ...host.settingsState, menu: { ...host.settingsState.menu, rules } };
          result = host.settingsState;
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
          host.settingsState = {
            ...host.settingsState,
            wheel: next as unknown as SettingsWheelState,
          };
          result = host.settingsState;
          break;
        }
        case 'settings.wheel.launcher': {
          const launcher = String(params['value'] ?? '');
          host.settingsState = {
            ...host.settingsState,
            wheel: {
              ...host.settingsState.wheel,
              launcher,
              enabled: launcher.length > 0,
              launcherStatus: launcher ? '就绪' : '未设置',
            },
          };
          result = host.settingsState;
          break;
        }
        case 'settings.wheel.enabled': {
          const enabled = params['value'] === true;
          host.settingsState = {
            ...host.settingsState,
            wheel: {
              ...host.settingsState.wheel,
              enabled,
              launcher: enabled ? (host.settingsState.wheel.launcher || 'Mouse Right') : '',
            },
          };
          result = host.settingsState;
          break;
        }
        case 'settings.wheel.enableSystem':
          host.settingsState = {
            ...host.settingsState,
            wheel: { ...host.settingsState.wheel, isSystemOff: false },
          };
          result = host.settingsState;
          break;
        case 'settings.wheel.repeat': {
          const key = String(params['binding'] ?? '');
          host.settingsState = {
            ...host.settingsState,
            wheel: { ...host.settingsState.wheel, repeatKey: key, repeatDisplay: key },
          };
          result = host.settingsState;
          break;
        }
        case 'settings.wheel.scene.select':
          host.settingsState = {
            ...host.settingsState,
            wheel: { ...host.settingsState.wheel, selectedScene: String(params['key'] ?? 'global') },
          };
          result = host.settingsState;
          break;
        case 'settings.wheel.scene.add': {
          const rule = params['rule'] as WindowRuleState;
          const key = 'scene-' + Date.now();
          host.settingsState = {
            ...host.settingsState,
            wheel: {
              ...host.settingsState.wheel,
              selectedScene: key,
              scenes: [...host.settingsState.wheel.scenes, {
                key, summary: rule.title || rule.process || '窗口规则', isGlobal: false, rule,
              }],
            },
          };
          result = host.settingsState;
          break;
        }
        case 'settings.wheel.scene.edit': {
          const rule = params['rule'] as WindowRuleState;
          host.settingsState = {
            ...host.settingsState,
            wheel: {
              ...host.settingsState.wheel,
              scenes: host.settingsState.wheel.scenes.map((scene) => scene.key === params['key']
                ? { ...scene, rule, summary: rule.title || rule.process || '窗口规则' }
                : scene),
            },
          };
          result = host.settingsState;
          break;
        }
        case 'settings.wheel.scene.remove':
          host.settingsState = {
            ...host.settingsState,
            wheel: {
              ...host.settingsState.wheel,
              selectedScene: 'global',
              scenes: host.settingsState.wheel.scenes.filter((scene) => scene.key !== params['key']),
            },
          };
          result = host.settingsState;
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
          host.settingsState = {
            ...host.settingsState,
            wheel: { ...host.settingsState.wheel, scenes },
          };
          result = host.settingsState;
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
          host.settingsState = {
            ...host.settingsState,
            wheel: {
              ...host.settingsState.wheel,
              slots: host.settingsState.wheel.slots.map((slot) =>
                slot.ring === ring && slot.index === index ? update(slot) : slot),
            },
          };
          result = host.settingsState;
          break;
        }
        case 'settings.menu.rememberPin':
          host.settingsState = {
            ...host.settingsState,
            menu: {
              ...host.settingsState.menu,
              rememberPinned: params['remember'] === true,
            },
          };
          result = host.settingsState;
          break;
        case 'settings.openFolder':
          result = null;
          break;
  }

  return result;
}
