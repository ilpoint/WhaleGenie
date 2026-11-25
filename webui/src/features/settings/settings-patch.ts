import type { SettingsPatch } from '../../shared/bridge/protocol.js';
import type { EditableSetting } from './settings-model.js';

type BooleanSetting = Extract<EditableSetting, 'loadLastProjectAtStart' | 'autoSaveProject'
  | 'exportEmbeddedLibrary' | 'importLibraryToProject'
  | 'systemAtStart' | 'enabledFirst' | 'ignoreOwnInput' | 'soundSystem' | 'soundMacro'
  | 'preloadOcr' | 'frameRateFollow' | 'frameCache' | 'attachDriverAtStart' | 'runAsAdmin'>;

/**
 * One settings control's string value as the typed patch the bridge accepts. The page keeps
 * strings where the DOM does; this is the single place they become booleans or numbers.
 */
export function patchFor(field: EditableSetting, value: string): SettingsPatch {
  switch (field) {
    case 'inputRate': return { inputRate: Number(value) };
    case 'frameRate': return { frameRate: Number(value) };
    case 'frameCacheAge': return { frameCacheAge: Number(value) };
    case 'logCount': return { logCount: Number(value) };
    case 'windowWay': return { windowWay: value };
    case 'screenWay': return { screenWay: value };
    case 'language': return { language: value };
    case 'systemHotkey': return { systemHotkey: value };
    case 'recordStopKey': return { recordStopKey: value };
    case 'publicMacroEditorSources': return { publicMacroEditorSources: value };
    default: return booleanPatch(field, value);
  }
}

function booleanPatch(field: BooleanSetting, value: string): SettingsPatch {
  switch (field) {
    case 'loadLastProjectAtStart': return { loadLastProjectAtStart: value === 'true' };
    case 'autoSaveProject': return { autoSaveProject: value === 'true' };
    case 'exportEmbeddedLibrary': return { exportEmbeddedLibrary: value === 'true' };
    case 'importLibraryToProject': return { importLibraryToProject: value === 'true' };
    case 'systemAtStart': return { systemAtStart: value === 'true' };
    case 'enabledFirst': return { enabledFirst: value === 'true' };
    case 'ignoreOwnInput': return { ignoreOwnInput: value === 'true' };
    case 'soundSystem': return { soundSystem: value === 'true' };
    case 'soundMacro': return { soundMacro: value === 'true' };
    case 'preloadOcr': return { preloadOcr: value === 'true' };
    case 'frameRateFollow': return { frameRateFollow: value === 'true' };
    case 'frameCache': return { frameCache: value === 'true' };
    case 'attachDriverAtStart': return { attachDriverAtStart: value === 'true' };
    case 'runAsAdmin': return { runAsAdmin: value === 'true' };
    default: throw new Error(`unhandled boolean setting ${String(field)}`);
  }
}
