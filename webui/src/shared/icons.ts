import { unsafeSVG } from 'lit/directives/unsafe-svg.js';
import appWindow from 'lucide-static/icons/app-window.svg?raw';
import arrowDown from 'lucide-static/icons/arrow-down.svg?raw';
import arrowUp from 'lucide-static/icons/arrow-up.svg?raw';
import bookOpen from 'lucide-static/icons/book-open.svg?raw';
import box from 'lucide-static/icons/box.svg?raw';
import bug from 'lucide-static/icons/bug.svg?raw';
import bugPlay from 'lucide-static/icons/bug-play.svg?raw';
import check from 'lucide-static/icons/check.svg?raw';
import clipboardPaste from 'lucide-static/icons/clipboard-paste.svg?raw';
import group from 'lucide-static/icons/group.svg?raw';
import scissors from 'lucide-static/icons/scissors.svg?raw';
import chevronDown from 'lucide-static/icons/chevron-down.svg?raw';
import chevronRight from 'lucide-static/icons/chevron-right.svg?raw';
import clipboard from 'lucide-static/icons/clipboard.svg?raw';
import code from 'lucide-static/icons/code.svg?raw';
import copy from 'lucide-static/icons/copy.svg?raw';
import cpu from 'lucide-static/icons/cpu.svg?raw';
import eye from 'lucide-static/icons/eye.svg?raw';
import filePen from 'lucide-static/icons/file-pen.svg?raw';
import filePlus from 'lucide-static/icons/file-plus.svg?raw';
import fileText from 'lucide-static/icons/file-text.svg?raw';
import folder from 'lucide-static/icons/folder.svg?raw';
import gamepad from 'lucide-static/icons/gamepad-2.svg?raw';
import globe from 'lucide-static/icons/globe.svg?raw';
import info from 'lucide-static/icons/info.svg?raw';
import keyboard from 'lucide-static/icons/keyboard.svg?raw';
import layoutGrid from 'lucide-static/icons/layout-grid.svg?raw';
import list from 'lucide-static/icons/list.svg?raw';
import listTree from 'lucide-static/icons/list-tree.svg?raw';
import minus from 'lucide-static/icons/minus.svg?raw';
import mouse from 'lucide-static/icons/mouse.svg?raw';
import mousePointerClick from 'lucide-static/icons/mouse-pointer-click.svg?raw';
import panelRightClose from 'lucide-static/icons/panel-right-close.svg?raw';
import panelRightOpen from 'lucide-static/icons/panel-right-open.svg?raw';
import pencil from 'lucide-static/icons/pencil.svg?raw';
import pin from 'lucide-static/icons/pin.svg?raw';
import power from 'lucide-static/icons/power.svg?raw';
import plus from 'lucide-static/icons/plus.svg?raw';
import redo from 'lucide-static/icons/redo-2.svg?raw';
import scanText from 'lucide-static/icons/scan-text.svg?raw';
import saveAll from 'lucide-static/icons/save-all.svg?raw';
import search from 'lucide-static/icons/search.svg?raw';
import settings from 'lucide-static/icons/settings.svg?raw';
import square from 'lucide-static/icons/square.svg?raw';
import trash from 'lucide-static/icons/trash-2.svg?raw';
import triangleAlert from 'lucide-static/icons/triangle-alert.svg?raw';
import undo from 'lucide-static/icons/undo-2.svg?raw';
import upload from 'lucide-static/icons/upload.svg?raw';
import variable from 'lucide-static/icons/variable.svg?raw';
import workflow from 'lucide-static/icons/workflow.svg?raw';
import x from 'lucide-static/icons/x.svg?raw';

/**
 * The shell's icon set, taken from the official Lucide static SVGs. Keeping the SVG strings in one
 * place means the activity bar, title bar, palette and step list draw the same stroke width and cap
 * style instead of each page carrying its own paths.
 */
const icons = {
  macros: list,
  editor: filePen,
  run: bugPlay,
  library: bookOpen,
  settings,
  about: info,
  pin,
  power,
  minimize: minus,
  maximize: square,
  restore: copy,
  close: x,
  edit: pencil,
  export: upload,
  delete: trash,
  undo,
  redo,
  copy,
  check,
  cut: scissors,
  paste: clipboardPaste,
  group,
  search,
  plus,
  'save-all': saveAll,
  'chevron-right': chevronRight,
  'chevron-down': chevronDown,
  'arrow-up': arrowUp,
  'arrow-down': arrowDown,
  'layout-grid': layoutGrid,
  'list-tree': listTree,
  'panel-right-close': panelRightClose,
  'panel-right-open': panelRightOpen,
  bug,
  'bug-play': bugPlay,
  'file-plus': filePlus,
  workflow,
  keyboard,
  mouse,
  gamepad,
  eye,
  'scan-text': scanText,
  'mouse-pointer-click': mousePointerClick,
  'app-window': appWindow,
  globe,
  code,
  cpu,
  variable,
  'file-text': fileText,
  folder,
  clipboard,
  box,
  'triangle-alert': triangleAlert,
} as const;

export type IconName = keyof typeof icons;

export const icon = (name: IconName) => unsafeSVG(icons[name]);

/**
 * The category-to-icon vocabulary shared by the palette rail and the step rows. C# owns the
 * category; the icon is presentation, so it lives here rather than in the snapshot.
 */
const categoryIcons: Record<string, IconName> = {
  Control: 'workflow',
  Input: 'keyboard',
  Vision: 'eye',
  Ocr: 'scan-text',
  Uia: 'mouse-pointer-click',
  Browser: 'globe',
  Script: 'code',
  Process: 'cpu',
  Window: 'app-window',
  Data: 'variable',
  File: 'file-text',
  Clipboard: 'clipboard',
  System: 'settings',
};

export function iconForCategory(category: string): IconName {
  return categoryIcons[category] ?? 'box';
}
