/** settings bridge shapes. The barrel protocol.ts re-exports this file. */

export interface SettingsState {
  loadLastProjectAtStart: boolean;
  systemAtStart: boolean;
  enabledFirst: boolean;
  ignoreOwnInput: boolean;
  soundSystem: boolean;
  soundMacro: boolean;
  preloadOcr: boolean;
  inputRate: number;
  frameRateFollow: boolean;
  frameRate: number;
  frameRateMax: number;
  frameCache: boolean;
  frameCacheAge: number;
  logCount: number;
  language: string;
  systemHotkey: string;
  recordStopKey: string;
  systemHotkeyProblem: string;
  recordStopProblem: string;
  storage: SettingsStorage[];
  driverStatus: string;
  driverReady: boolean;
  attachDriverAtStart: boolean;
  runAsAdmin: boolean;
  adminStatus: string;
  windowWay: string;
  screenWay: string;
  windowWays: SettingsChoice[];
  screenWays: SettingsChoice[];
  menu: SettingsMenuState;
  wheel: SettingsWheelState;
}

export interface WindowRuleState {
  title: string;
  process: string;
  className: string;
  handle: number;
  how: string;
  caseSensitive: boolean;
  trim: boolean;
  multiline: boolean;
}

export interface SettingsMenuRule {
  key: string;
  summary: string;
  pageCountLabel: string;
  isDefault: boolean;
  isBlocked: boolean;
  canAddPage: boolean;
  rule: WindowRuleState;
}

export interface SettingsMenuState {
  launcher: string;
  display: string;
  status: string;
  isMiddle: boolean;
  isCustom: boolean;
  problem: string;
  rules: SettingsMenuRule[];
}

export interface SettingsWheelChoice {
  value: string | number;
  label: string;
}

export interface SettingsWheelScene {
  key: string;
  summary: string;
  isGlobal: boolean;
  rule: WindowRuleState;
}

export interface SettingsWheelIcon {
  kind: string;
  value: string;
}

export interface SettingsWheelSlot {
  ring: string;
  index: number;
  x: number;
  y: number;
  position: string;
  macro: string;
  note: string;
  label: string;
  icon: SettingsWheelIcon | null;
  inherited: boolean;
  overridden: boolean;
  empty: boolean;
  tone: string;
}

export interface SettingsWheelGeometry {
  diameter: number;
  center: number;
  inner: number;
  outer: number;
  third: number;
  edge: number;
  outerCount: number;
  thirdCount: number;
  hasOuter: boolean;
  hasThird: boolean;
}

export interface SettingsMacroChoice {
  name: string;
  enabled: boolean;
}

export interface SettingsWheelState {
  enabled: boolean;
  launcher: string;
  launcherStatus: string;
  launcherMessage: string;
  onlyInPrograms: boolean;
  diameter: number;
  fontSize: number;
  thirdCapacity: number;
  timeoutMs: number;
  repeatKey: string;
  repeatDisplay: string;
  wideOuter: boolean;
  hideEmptyOuter: boolean;
  hideLabelWithIcon: boolean;
  centerName: boolean;
  clampToScreen: boolean;
  movePointerToCenter: boolean;
  showGuide: boolean;
  extendedHint: boolean;
  followTheme: boolean;
  gloss: boolean;
  hoverBorder: boolean;
  popAnimation: boolean;
  isSystemOff: boolean;
  problem: string;
  launcherChoices: SettingsWheelChoice[];
  thirdChoices: SettingsWheelChoice[];
  scenes: SettingsWheelScene[];
  selectedScene: string;
  geometry: SettingsWheelGeometry;
  slots: SettingsWheelSlot[];
  preview: SettingsWheelSlot[];
  macros: SettingsMacroChoice[];
}

export interface WindowEntryState {
  handle: number;
  title: string;
  process: string;
  className: string;
  detail: string;
  summary: string;
}

export interface WindowRulePickerState {
  windows: WindowEntryState[];
  hows: SettingsChoice[];
}

export interface SettingsChoice {
  value: string;
  label: string;
}

export interface SettingsStorage {
  name: string;
  what: string;
  where: string;
  folder: string;
}
