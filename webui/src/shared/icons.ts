import { unsafeSVG } from 'lit/directives/unsafe-svg.js';
import copy from 'lucide-static/icons/copy.svg?raw';
import filePen from 'lucide-static/icons/file-pen.svg?raw';
import house from 'lucide-static/icons/house.svg?raw';
import list from 'lucide-static/icons/list.svg?raw';
import minus from 'lucide-static/icons/minus.svg?raw';
import pencil from 'lucide-static/icons/pencil.svg?raw';
import pin from 'lucide-static/icons/pin.svg?raw';
import play from 'lucide-static/icons/play.svg?raw';
import settings from 'lucide-static/icons/settings.svg?raw';
import square from 'lucide-static/icons/square.svg?raw';
import trash from 'lucide-static/icons/trash-2.svg?raw';
import upload from 'lucide-static/icons/upload.svg?raw';
import x from 'lucide-static/icons/x.svg?raw';

/**
 * The shell's icon set, taken from the official Lucide static SVGs. Keeping the SVG strings in one
 * place means the activity bar, title bar and macro cards draw the same stroke width and cap style
 * instead of each page carrying its own paths.
 */
const icons = {
  home: house,
  macros: list,
  editor: filePen,
  run: play,
  settings,
  pin,
  minimize: minus,
  maximize: square,
  restore: copy,
  close: x,
  edit: pencil,
  export: upload,
  delete: trash,
} as const;

export const icon = (name: keyof typeof icons) => unsafeSVG(icons[name]);
