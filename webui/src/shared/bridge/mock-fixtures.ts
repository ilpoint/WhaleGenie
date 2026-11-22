import type {
  SettingsMenuState,
  SettingsWheelSlot,
  SettingsWheelState,
  WindowEntryState,
  WindowRuleState,
} from './protocol.js';

/** Static settings fixtures the mock host starts from. */
const demoRule: WindowRuleState = {
  title: 'Untitled - Notepad', process: 'notepad', className: 'Notepad',
  handle: 0, how: 'titleContains', caseSensitive: false, trim: true, multiline: false,
};

const demoWheelGeometry = {
  diameter: 340, center: 37.4, inner: 95.2, outer: 159.8, third: 0, edge: 159.8,
  outerCount: 8, thirdCount: 0, hasOuter: true, hasThird: false,
};

function demoWheelSlots(): SettingsWheelSlot[] {
  const slots: SettingsWheelSlot[] = [];
  const rings = [
    { name: 'inner', count: 8, radius: 66 },
    { name: 'outer', count: 8, radius: 128 },
    { name: 'extended', count: 8, radius: 165 },
  ];
  for (const ring of rings) {
    for (let index = 0; index < ring.count; index++) {
      const angle = (index * Math.PI * 2) / ring.count;
      const macro = ring.name === 'inner' && index === 0 ? '演示宏' : '';
      const angleLabel = ring.name === 'inner' ? '内圈' : ring.name === 'outer' ? '外圈' : '划动圈';
      slots.push({
        ring: ring.name,
        index,
        x: ring.radius * Math.sin(angle),
        y: -ring.radius * Math.cos(angle),
        position: `${angleLabel}第 ${index + 1} 格`,
        macro,
        note: '',
        label: macro,
        icon: null,
        inherited: false,
        overridden: macro.length > 0,
        empty: macro.length === 0,
        tone: ring.name === 'extended' ? 'extended' : macro ? 'global' : 'empty',
      });
    }
  }
  return slots;
}

export const demoMenu: SettingsMenuState = {
  launcher: 'Mouse Middle', display: '鼠标中键', status: '就绪',
  isMiddle: true, isCustom: false, problem: '',
  rules: [
    {
      key: 'default', summary: 'Default', pageCountLabel: '1 页', isDefault: true,
      isBlocked: false, canAddPage: true, rule: { ...demoRule, title: '', process: '', className: '' },
    },
    {
      key: 'notepad', summary: 'Notepad · 标题包含', pageCountLabel: '1 页', isDefault: false,
      isBlocked: false, canAddPage: true, rule: { ...demoRule },
    },
  ],
};

export const demoWheel: SettingsWheelState = {
  enabled: true, launcher: 'Mouse Right', launcherStatus: '就绪', launcherMessage: '',
  onlyInPrograms: false, diameter: 420, fontSize: 11, thirdCapacity: 0, timeoutMs: 20000,
  repeatKey: 'F1', repeatDisplay: 'F1', wideOuter: false, hideEmptyOuter: true,
  hideLabelWithIcon: false, centerName: false, clampToScreen: true,
  movePointerToCenter: false, showGuide: true, extendedHint: true, followTheme: true,
  gloss: true, hoverBorder: true, popAnimation: true, isSystemOff: false, problem: '',
  launcherChoices: [
    { value: 'Mouse Right', label: '鼠标右键' },
    { value: 'Mouse Middle', label: '鼠标中键' },
    { value: 'Mouse 5', label: '鼠标前进键' },
    { value: 'Mouse 4', label: '鼠标后退键' },
  ],
  thirdChoices: [
    { value: 0, label: '关闭（两圈）' },
    { value: 8, label: '8 格' },
    { value: 16, label: '16 格' },
    { value: 24, label: '24 格' },
  ],
  scenes: [
    { key: 'global', summary: '全局', isGlobal: true, rule: { ...demoRule, title: '', process: '', className: '' } },
    { key: 'notepad', summary: 'Notepad · 标题包含', isGlobal: false, rule: { ...demoRule } },
  ],
  selectedScene: 'global',
  geometry: demoWheelGeometry,
  slots: demoWheelSlots(),
  preview: demoWheelSlots(),
  macros: [
    { name: '演示宏', enabled: true },
    { name: '备份', enabled: true },
  ],
};

export const demoWindows: WindowEntryState[] = [
  {
    handle: 1, title: 'Untitled - Notepad', process: 'notepad', className: 'Notepad',
    detail: '800×600', summary: 'notepad · Notepad · 800×600',
  },
];

