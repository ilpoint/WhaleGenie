import type {
  EditorAction,
  EditorActionCategory,
  EditorCatalogue,
  EditorChoice,
  EditorColumn,
  EditorPublicMacro,
} from './protocol.js';
import raw from './data/editor-catalogue.json';
import {
  defaultConditionCompares,
  defaultTriggerKinds,
  defaultValueFunctions,
  defaultVariableTypes,
  systemValueChoices,
} from './mock-editor-fixtures.js';

/**
 * 编辑器 mock 的动作目录：类别、分组、次序与动作清单都是固定数据，放在
 * data/editor-catalogue.json；这里只把它们变成页面读的形状，并按语言取标签。
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

interface CatalogueData {
  categories: Record<string, { label: Label; short: Label }>;
  groups: Record<string, Label>;
  categoryOrder: string[];
  groupOrder: Record<string, string[]>;
  flowOrder: string[];
  sections: Record<string, MockActionSection[]>;
  publicMacros: Record<string, EditorPublicMacro[]>;
  actions: (Omit<MockAction, 'sections'> & { sections?: string })[];
}

const DATA = raw as unknown as CatalogueData;

const ACTIONS: readonly MockAction[] = DATA.actions.map((action) => ({
  key: action.key,
  name: action.name,
  category: action.category,
  group: action.group,
  description: action.description,
  parameters: action.parameters,
  ...(action.sections === undefined ? {} : { sections: DATA.sections[action.sections] ?? [] }),
}));

function text(label: Label, english: boolean): string {
  return english ? label.en : label.zh;
}

function categoryLabel(category: string, english: boolean): string {
  const entry = DATA.categories[category];
  return entry ? text(entry.label, english) : category;
}

function categoryShort(category: string, english: boolean): string {
  const entry = DATA.categories[category];
  return entry ? text(entry.short, english) : category;
}

function groupLabel(group: string, english: boolean): string {
  const entry = DATA.groups[group];
  return entry ? text(entry, english) : group;
}

function groupRank(category: string, group: string): number {
  const order = category === 'Control' ? DATA.flowOrder : DATA.groupOrder[category] ?? [];
  const index = order.indexOf(group);
  return index < 0 ? order.length : index;
}

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
    ...DATA.categoryOrder.map((value) => ({
      value,
      label: categoryLabel(value, english),
      short: categoryShort(value, english),
    })),
  ];
  return {
    count: actions.length,
    actions,
    categories,
    publicMacros: DATA.publicMacros[english ? 'en' : 'zh'] ?? [],
    valueFunctions: defaultValueFunctions(),
    variableTypes: defaultVariableTypes(),
    conditionCompares: defaultConditionCompares(),
    triggerKinds: defaultTriggerKinds(),
    systemValueChoices: systemValueChoices(),
  };
}

export function findAction(key: string): MockAction | undefined {
  return ACTIONS.find((action) => action.key === key);
}
