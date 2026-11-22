import type {
  EditorAction,
  EditorActionCategory,
  EditorCatalogue,
  EditorChoice,
  EditorColumn,
} from './protocol.js';

/**
 * The mock editor's action catalogue and step tree. The host owns both for real; here they are
 * fixtures with the same shapes, kept apart from the document commands so neither file has to
 * know how the other stores its state.
 */

export interface MockActionParameter {
  name: string;
  label: string;
  kind: string;
  required?: boolean;
  placeholder?: string;
  options?: EditorChoice[];
  columns?: EditorColumn[];
  initial?: string;
}

export interface MockActionSection {
  name: string;
  label: string;
}

export interface MockAction {
  key: string;
  name: string;
  category: string;
  group: string;
  description: string;
  parameters: MockActionParameter[];
  sections?: MockActionSection[];
}

interface Label {
  zh: string;
  en: string;
}

const CATEGORY_LABELS: Record<string, { label: Label; short: Label }> = {
  Control: { label: { zh: '流程控制', en: 'Control flow' }, short: { zh: '流程', en: 'Flow' } },
  Input: {
    label: { zh: '键盘、鼠标与手柄', en: 'Keyboard, mouse and controller' },
    short: { zh: '键鼠', en: 'Input' },
  },
  Vision: {
    label: { zh: '视觉（图像、OCR、YOLO）', en: 'Vision (pictures, OCR, YOLO)' },
    short: { zh: '视觉', en: 'Vision' },
  },
  Uia: { label: { zh: '界面元素', en: 'Interface elements' }, short: { zh: '界面', en: 'UIA' } },
  Browser: { label: { zh: '浏览器', en: 'Browser' }, short: { zh: '浏览器', en: 'Web' } },
  Script: { label: { zh: '脚本', en: 'Scripts' }, short: { zh: '脚本', en: 'Script' } },
  Process: {
    label: { zh: '程序与窗口', en: 'Programs and windows' },
    short: { zh: '程序', en: 'App' },
  },
  Data: {
    label: { zh: '数据与变量', en: 'Data and variables' },
    short: { zh: '数据', en: 'Data' },
  },
  File: { label: { zh: '文件', en: 'Files' }, short: { zh: '文件', en: 'File' } },
  Clipboard: {
    label: { zh: '剪贴板', en: 'Clipboard' },
    short: { zh: '剪贴', en: 'Clip' },
  },
  System: { label: { zh: '系统', en: 'System' }, short: { zh: '系统', en: 'Sys' } },
};

const GROUP_LABELS: Record<string, Label> = {
  Branch: { zh: '重复与分支', en: 'Repeats and branches' },
  Jump: { zh: '跳转', en: 'Jump' },
  Waiting: { zh: '等待与后台运行', en: 'Timing and background runs' },
  Data: { zh: '变量与输入', en: 'Variables and input' },
  Notes: { zh: '说明与日志', en: 'Notes and logs' },
  Keyboard: { zh: '键盘', en: 'Keyboard' },
  Mouse: { zh: '鼠标', en: 'Mouse' },
  Screen: { zh: '画面', en: 'Screen' },
  FindImage: { zh: '找图', en: 'Finding pictures' },
  Colour: { zh: '找色', en: 'Finding colours' },
  Element: { zh: '元素', en: 'Elements' },
  Navigate: { zh: '打开与页签', en: 'Opening and tabs' },
  Run: { zh: '运行', en: 'Running' },
  Program: { zh: '程序', en: 'Programs' },
  Window: { zh: '窗口', en: 'Windows' },
  Variables: { zh: '变量', en: 'Variables' },
  Text: { zh: '文本', en: 'Text' },
  Paths: { zh: '路径与目录', en: 'Paths and folders' },
  Info: { zh: '系统信息', en: 'System information' },
  Other: { zh: '其他', en: 'Other' },
};

/** The category rail's fixed order; Control lives in its own flow dock, not on the rail. */
const CATEGORY_ORDER = [
  'Input', 'Vision', 'Uia', 'Browser', 'Script',
  'Process', 'Data', 'File', 'Clipboard', 'System',
];

const GROUP_ORDER: Record<string, string[]> = {
  Input: ['Keyboard', 'Mouse'],
  Vision: ['Screen', 'FindImage', 'Colour'],
  Uia: ['Element'],
  Browser: ['Navigate'],
  Script: ['Run'],
  Process: ['Program', 'Window'],
  Data: ['Variables'],
  File: ['Text', 'Paths'],
  Clipboard: ['Text'],
  System: ['Info'],
};

const FLOW_ORDER = ['Branch', 'Jump', 'Waiting', 'Data', 'Notes', 'Other'];

function text(label: Label, english: boolean): string {
  return english ? label.en : label.zh;
}

function categoryLabel(category: string, english: boolean): string {
  const entry = CATEGORY_LABELS[category];
  return entry ? text(entry.label, english) : category;
}

function categoryShort(category: string, english: boolean): string {
  const entry = CATEGORY_LABELS[category];
  return entry ? text(entry.short, english) : category;
}

function groupLabel(group: string, english: boolean): string {
  const entry = GROUP_LABELS[group];
  return entry ? text(entry, english) : group;
}

function groupRank(category: string, group: string): number {
  const order = category === 'Control' ? FLOW_ORDER : GROUP_ORDER[category] ?? [];
  const index = order.indexOf(group);
  return index < 0 ? order.length : index;
}

const KEY_MODE_OPTIONS: EditorChoice[] = [
  { value: 'foreground', label: '前台' },
  { value: 'background', label: '后台消息' },
  { value: 'driver', label: '驱动级' },
];

const THEN_ELSE: MockActionSection[] = [
  { name: 'then', label: '条件成立' },
  { name: 'else', label: '否则' },
];
const BODY: MockActionSection[] = [{ name: 'body', label: '循环体' }];
const TRY_SECTIONS: MockActionSection[] = [
  { name: 'body', label: '尝试' },
  { name: 'catch', label: '捕获' },
  { name: 'finally', label: '收尾' },
];
const SWITCH_SECTIONS: MockActionSection[] = [
  { name: 'cases', label: '分支' },
  { name: 'otherwise', label: '其他情况' },
];
const CASE_SECTIONS: MockActionSection[] = [{ name: 'body', label: '分支内容' }];

function parameter(
  name: string,
  label: string,
  kind: string,
  extra: Partial<MockActionParameter> = {},
): MockActionParameter {
  return { name, label, kind, ...extra };
}

const ACTIONS: MockAction[] = [
  {
    key: 'control.if', name: '如果', category: 'Control', group: 'Branch', description: '',
    parameters: [parameter('condition', '条件', 'Condition')],
    sections: THEN_ELSE,
  },
  {
    key: 'control.repeat', name: '重复', category: 'Control', group: 'Branch', description: '',
    parameters: [
      parameter('times', '重复几次', 'Number', { initial: '3' }),
      parameter('returns', '返回值', 'Returns', {
        columns: [{ name: 'key', required: true }, { name: 'name', required: true }],
      }),
    ],
    sections: BODY,
  },
  {
    key: 'control.forEach', name: '遍历', category: 'Control', group: 'Branch', description: '',
    parameters: [parameter('items', '过什么', 'Text'), parameter('each', '每轮叫什么', 'Text')],
    sections: BODY,
  },
  {
    key: 'control.for', name: '计数循环', category: 'Control', group: 'Branch', description: '',
    parameters: [parameter('from', '从', 'Number', { initial: '1' }),
      parameter('to', '到', 'Number', { initial: '10' })],
    sections: BODY,
  },
  {
    key: 'control.while', name: '当条件', category: 'Control', group: 'Branch', description: '',
    parameters: [parameter('condition', '条件', 'Condition')],
    sections: BODY,
  },
  {
    key: 'control.try', name: '尝试', category: 'Control', group: 'Branch', description: '',
    parameters: [], sections: TRY_SECTIONS,
  },
  {
    key: 'control.switch', name: '多分支', category: 'Control', group: 'Branch', description: '',
    parameters: [parameter('value', '比对什么', 'Text')],
    sections: SWITCH_SECTIONS,
  },
  {
    key: 'control.case', name: '分支', category: 'Control', group: 'Branch', description: '',
    parameters: [parameter('values', '值', 'Text', { placeholder: '例如 1; 2; 3' })],
    sections: CASE_SECTIONS,
  },
  {
    key: 'control.delay', name: '延迟', category: 'Control', group: 'Waiting', description: '',
    parameters: [parameter('ms', '等待时长', 'Number')],
  },
  {
    key: 'control.waitUntil', name: '等待条件', category: 'Control', group: 'Waiting', description: '',
    parameters: [parameter('condition', '条件', 'Condition')],
  },
  {
    key: 'control.runMacro', name: '调用宏', category: 'Control', group: 'Waiting', description: '',
    parameters: [parameter('macro', '调用哪个宏', 'Macro')],
  },
  {
    key: 'control.break', name: '跳出循环', category: 'Control', group: 'Jump', description: '',
    parameters: [],
  },
  {
    key: 'control.continue', name: '下一轮', category: 'Control', group: 'Jump', description: '',
    parameters: [],
  },
  {
    key: 'control.note', name: '备注', category: 'Control', group: 'Notes', description: '',
    parameters: [parameter('text', '备注', 'Text')],
  },
  {
    key: 'control.log', name: '写日志', category: 'Control', group: 'Notes', description: '',
    parameters: [parameter('text', '内容', 'Text')],
  },
  {
    key: 'input.keyPress', name: '按键', category: 'Input', group: 'Keyboard', description: '',
    parameters: [parameter('key', '按键', 'Key'),
      parameter('mode', '发给谁', 'Select', { options: KEY_MODE_OPTIONS, initial: 'foreground' })],
  },
  {
    key: 'input.mouseClick', name: '点击', category: 'Input', group: 'Mouse', description: '',
    parameters: [parameter('button', '按键', 'Select', {
      options: [{ value: 'Left', label: '左键' }, { value: 'Right', label: '右键' }],
      initial: 'Left',
    }), parameter('mode', '发给谁', 'Select', { options: KEY_MODE_OPTIONS, initial: 'foreground' })],
  },
  {
    key: 'input.text', name: '输入文字', category: 'Input', group: 'Keyboard', description: '',
    parameters: [parameter('text', '文字', 'Text'),
      parameter('mode', '发给谁', 'Select', { options: KEY_MODE_OPTIONS, initial: 'foreground' })],
  },
  {
    key: 'screen.color', name: '屏幕取色', category: 'Vision', group: 'Colour', description: '',
    parameters: [parameter('hex', '颜色', 'Color', { initial: '#ff0000' })],
  },
  {
    key: 'image.find', name: '找图', category: 'Vision', group: 'FindImage', description: '',
    parameters: [parameter('path', '图片', 'Image')],
  },
  {
    key: 'uia.find', name: '查找元素', category: 'Uia', group: 'Element', description: '',
    parameters: [parameter('selector', '选择器', 'Text')],
  },
  {
    key: 'browser.open', name: '打开网页', category: 'Browser', group: 'Navigate', description: '',
    parameters: [parameter('url', '网址', 'Text')],
  },
  {
    key: 'script.run', name: '运行脚本', category: 'Script', group: 'Run', description: '',
    parameters: [parameter('code', '脚本', 'Text')],
  },
  {
    key: 'process.start', name: '启动程序', category: 'Process', group: 'Program', description: '',
    parameters: [parameter('path', '程序', 'Text')],
  },
  {
    key: 'window.activate', name: '切换窗口', category: 'Process', group: 'Window', description: '',
    parameters: [parameter('title', '标题', 'Text')],
  },
  {
    key: 'data.set', name: '设置变量', category: 'Data', group: 'Variables', description: '',
    parameters: [parameter('name', '变量', 'Text'), parameter('value', '值', 'Text')],
  },
  {
    key: 'file.read', name: '读取文件', category: 'File', group: 'Text', description: '',
    parameters: [parameter('path', '文件', 'Text')],
  },
  {
    key: 'clipboard.copy', name: '写入剪贴板', category: 'Clipboard', group: 'Text', description: '',
    parameters: [parameter('text', '内容', 'Text')],
  },
  {
    key: 'system.info', name: '系统信息', category: 'System', group: 'Info', description: '',
    parameters: [],
  },
];

/** The catalogue in the language the mock host currently draws. */
export function actionCatalogue(language: string): EditorCatalogue {
  const english = language === 'en';
  const actions: EditorAction[] = ACTIONS.map((action) => ({
    key: action.key,
    name: action.name,
    category: action.category,
    categoryLabel: categoryLabel(action.category, english),
    categoryShort: categoryShort(action.category, english),
    group: action.group,
    groupLabel: groupLabel(action.group, english),
    groupRank: groupRank(action.category, action.group),
    description: '',
    parameters: action.parameters.map((entry) => ({
      name: entry.name,
      label: entry.label,
      kind: entry.kind,
      required: entry.required === true,
      placeholder: entry.placeholder ?? '',
      options: entry.options ?? [],
    })),
  }));
  const categories: EditorActionCategory[] = [
    { value: null, label: english ? 'All actions' : '全部', short: english ? 'All' : '全部' },
    ...CATEGORY_ORDER.map((value) => ({
      value,
      label: categoryLabel(value, english),
      short: categoryShort(value, english),
    })),
  ];
  return {
    count: actions.length,
    actions,
    categories,
    publicMacros: english
      ? [{ id: 'public.send-mail', version: 3, name: 'Send mail', description: 'Fill a message and send it through the default mail program.' }]
      : [{ id: 'public.send-mail', version: 3, name: '发送邮件', description: '填好一封邮件，用系统默认邮件程序发出去。' }],
  };
}

export function findAction(key: string): MockAction | undefined {
  return ACTIONS.find((action) => action.key === key);
}
