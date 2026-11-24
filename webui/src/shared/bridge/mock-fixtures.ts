import type {
  SettingsMenuState,
  SettingsWheelSlot,
  SettingsWheelState,
  SurfaceMenuSnapshot,
  SurfacePanelSnapshot,
  SurfaceWheelSnapshot,
  WindowEntryState,
} from './protocol.js';
import raw from './data/fixtures.json';

/**
 * 设置页与浮层用到的固定快照都在 data/fixtures.json：菜单规则、轮盘设置、窗口列表与三个
 * 浮层。这里只把轮盘格子按环算出来（角度与半径是画法，不是数据）。
 */

interface Fixtures {
  menu: SettingsMenuState;
  wheel: Omit<SettingsWheelState, 'slots' | 'preview'>;
  wheelRings: { name: string; count: number; radius: number }[];
  windows: WindowEntryState[];
  surfaceMenu: SurfaceMenuSnapshot;
  surfaceWheel: SurfaceWheelSnapshot;
  surfacePanel: SurfacePanelSnapshot;
}

const DATA = raw as unknown as Fixtures;

function demoWheelSlots(): SettingsWheelSlot[] {
  const slots: SettingsWheelSlot[] = [];
  for (const ring of DATA.wheelRings) {
    for (let index = 0; index < ring.count; index++) {
      const angle = (index * Math.PI * 2) / ring.count;
      const macro = ring.name === 'inner' && index === 0 ? '演示宏' : '';
      const angleLabel = ring.name === 'inner' ? '内圈' : ring.name === 'outer' ? '外圈' : '划动圈';
      slots.push({
        ring: ring.name,
        index,
        x: ring.radius * Math.sin(angle),
        y: -ring.radius * Math.cos(angle),
        position: angleLabel + '第 ' + String(index + 1) + ' 格',
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

export const demoMenu: SettingsMenuState = DATA.menu;

export const demoWheel: SettingsWheelState = {
  ...DATA.wheel,
  slots: demoWheelSlots(),
  preview: demoWheelSlots(),
};

export const demoWindows: WindowEntryState[] = DATA.windows;

/** 菜单浮层：两页常见宏与一页默认上下文，够开发与 e2e 看见分页和空格子。 */
export const demoSurfaceMenu: SurfaceMenuSnapshot = DATA.surfaceMenu;

/** 轮盘浮层：内外两圈、每格 8 个位置，空白格也要看得见。 */
export const demoSurfaceWheel: SurfaceWheelSnapshot = DATA.surfaceWheel;

/** 界面宏浮层：一个组的两三个成员，够开发与 e2e 看排版。 */
export const demoSurfacePanel: SurfacePanelSnapshot = DATA.surfacePanel;
