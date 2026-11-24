import type {
  EditorParameter,
  EditorSnapshot,
  EditorValueNode,
  EditorValueOutcome,
  EditorValueSnapshot,
  EditorValueVariable,
  EditorValuePickerFunction,
} from './protocol.js';
import raw from './data/value-panel.json';

/**
 * A small mock of the value window. It is enough for the preview and the end-to-end tests to
 * exercise the same token band and panels as the host: one value token, the signs, the wall and
 * the function cards. The panel's fixed wording, not operators and starting snapshot live in
 * data/value-panel.json; the real shapes and readings stay in C#.
 */

interface ValuePanelData {
  modes: Record<string, Partial<EditorValueSnapshot>>;
  base: EditorValueSnapshot;
  node: EditorValueNode;
  operators: EditorValueSnapshot['operators'];
  values: EditorValueSnapshot['values'];
  wallFilters: EditorValueSnapshot['wall']['filters'];
}

const DATA = raw as unknown as ValuePanelData;

export class MockValueEditor {
  private text: string;
  private textBody: string;
  private typingText = '';
  private typingNumber: boolean;
  private looking = '';
  private wallSearch = '';
  private wallFilter = 'All';
  private functionQuery = '';
  private functionGroup = '';
  private history: string[] = [];
  private future: string[] = [];

  private readonly mode: 'Text' | 'Run' | 'Value' | 'Name';

  constructor(
    private readonly document: EditorSnapshot,
    private readonly parameter: EditorParameter,
    private readonly write: (value: string) => void,
  ) {
    this.text = parameter.value;
    this.textBody = parameter.value;
    this.typingNumber = parameter.isNumber;
    this.mode = parameter.valueForm === 'Text'
      ? 'Text'
      : parameter.valueForm === 'Name' ? 'Name' : parameter.valueForm === 'Run' ? 'Run' : 'Value';
  }

  snapshot(): EditorValueSnapshot {
    const variables = this.variables();
    const shown = variables.filter((variable) =>
      (this.wallFilter === 'All' || variable.group === this.wallFilter)
      && (this.wallSearch.trim().length === 0
        || variable.name.toLowerCase().includes(this.wallSearch.trim().toLowerCase())
        || variable.display.toLowerCase().includes(this.wallSearch.trim().toLowerCase())));
    const functionGroups = [
      { group: '', label: '全部', selected: this.functionGroup.length === 0 },
      ...[...new Set(this.document.valueFunctions.map((fn) => fn.group))].map((group) => ({
        group,
        label: group,
        selected: this.functionGroup === group,
      })),
    ];
    const functions: EditorValuePickerFunction[] = this.document.valueFunctions
      .filter((fn) => this.functionGroup.length === 0 || fn.group === this.functionGroup)
      .filter((fn) => this.functionQuery.trim().length === 0
        || fn.name.toLowerCase().includes(this.functionQuery.trim().toLowerCase()))
      .map((fn) => ({
        name: fn.name,
        tag: fn.name,
        heading: fn.name + '(' + fn.signature + ')',
        writtenAs: fn.signature,
        arityNote: '',
        hint: fn.description,
        group: fn.group,
        insert: fn.name + '(',
        arguments: [],
      }));

    const valueNode: EditorValueNode = this.text.length > 0
      ? this.node('value', [0, 0], this.text, { isName: this.mode === 'Name' })
      : this.node('empty', [0, 0], '插槽', { isGhost: false, isRequired: false });
    const sequence = this.node('sequence', [0], '', { children: [valueNode] });
    const recipe = this.node('root', [], '', { children: [sequence] });
    const name = this.wallSearch.trim();
    const canCreateName = this.mode === 'Name'
      && name.length > 0
      && !variables.some((variable) => variable.name.toLowerCase() === name.toLowerCase());

    return {
      ...structuredClone(DATA.base),
      ...structuredClone(DATA.modes[this.mode] ?? {}),
      mode: this.mode,
      targetLine: this.parameter.label,
      recipe,
      caret: this.text.length,
      spanEnd: this.text.length,
      caretPath: [0, 0],
      isValueEmpty: this.text.length === 0,
      text: this.text,
      note: this.text.length === 0 ? '还没有填值。' : '读得通。',
      reads: this.text.length > 0,
      canConfirm: this.mode === 'Text' || this.mode === 'Name'
        ? this.textBody.length > 0 || this.text.length > 0 || canCreateName
        : this.text.length > 0,
      canUndo: this.history.length > 0,
      canRedo: this.future.length > 0,
      canClear: this.text.length > 0,
      textBody: this.textBody,
      isTypingNumber: this.typingNumber,
      typingText: this.typingText,
      typingPlaceholder: this.typingNumber ? '数字' : '文字',
      canInsertTyped: this.typingText.trim().length > 0,
      functionQuery: this.functionQuery,
      selectedFunctionGroup: this.functionGroup.length > 0 ? this.functionGroup : null,
      isAllFunctions: this.functionGroup.length === 0,
      hasChosenVariable: this.looking.length > 0,
      allowNewName: this.mode === 'Name',
      newName: name,
      canCreateName,
      newNameLine: canCreateName ? '新建 ' + name : '',
      operators: structuredClone(DATA.operators),
      values: structuredClone(DATA.values),
      functionGroups,
      functions,
      wall: {
        filters: DATA.wallFilters.map((filter) => ({
          ...filter,
          selected: this.wallFilter === filter.group,
        })),
        selectedFilter: this.wallFilter,
        search: this.wallSearch,
        looking: this.looking,
        details: shown.length === 0 ? '没有可用的名字。' : '在按钮上停一下查看变量。',
        isEmpty: shown.length === 0,
        shown,
      },
    };
  }

  command(action: string, params: Record<string, unknown>): EditorValueSnapshot {
    switch (action) {
      case 'place': {
        const token = this.textValue(params, 'token').trim();
        this.remember();
        this.text = this.text.length === 0 ? token : this.text + (token.match(/^[+\-*/=<>]/) ? ' ' : ' ') + token;
        break;
      }
      case 'textBody':
        this.textBody = this.textValue(params, 'text');
        break;
      case 'typingText':
        this.typingText = this.textValue(params, 'text');
        break;
      case 'insertTyped':
        if (this.typingText.trim().length > 0) {
          this.remember();
          this.text = this.text.length === 0 ? this.typingText.trim() : this.text + ' ' + this.typingText.trim();
          this.typingText = '';
        }
        break;
      case 'clear':
        this.remember();
        this.text = '';
        this.textBody = '';
        break;
      case 'wrapGroup':
        this.remember();
        this.text = '(' + this.text + ')';
        break;
      case 'wrapList':
        this.remember();
        this.text = '[' + this.text + ']';
        break;
      case 'variable': {
        const name = this.textValue(params, 'name');
        if (this.mode === 'Name') {
          this.looking = name;
        } else {
          this.remember();
          this.text = this.text.length === 0 ? name : this.text + ' ' + name;
        }
        break;
      }
      case 'function': {
        const name = this.textValue(params, 'name');
        this.remember();
        this.text = this.text.length === 0 ? name + '(' : this.text + ' ' + name + '(';
        break;
      }
      case 'wallSearch':
        this.wallSearch = this.textValue(params, 'text');
        break;
      case 'wallFilter':
        this.wallFilter = this.textValue(params, 'group') || 'All';
        break;
      case 'functionSearch':
        this.functionQuery = this.textValue(params, 'text');
        break;
      case 'functionGroup':
        this.functionGroup = this.textValue(params, 'group');
        break;
      case 'typingMode':
        this.typingNumber = params['number'] === true;
        break;
      case 'remove':
        this.remember();
        this.text = '';
        break;
      case 'cutBefore':
        this.remember();
        this.text = this.text.slice(0, -1).trimEnd();
        break;
      case 'undo': {
        const previous = this.history.pop();
        if (previous !== undefined) {
          this.future.push(this.text);
          this.text = previous;
        }
        break;
      }
      case 'redo': {
        const next = this.future.pop();
        if (next !== undefined) {
          this.history.push(this.text);
          this.text = next;
        }
        break;
      }
      default:
        break;
    }

    return this.snapshot();
  }

  confirm(): EditorValueOutcome {
    const value = this.mode === 'Name'
      ? (this.looking || this.wallSearch.trim())
      : this.mode === 'Text' ? this.textBody : this.text;
    if (value.length === 0 && this.mode !== 'Text') {
      return { closed: false, applied: false, snapshot: this.snapshot() };
    }

    this.write(value);
    return { closed: true, applied: true, snapshot: this.snapshot() };
  }

  cancel(): EditorValueOutcome {
    return { closed: true, applied: false, snapshot: this.snapshot() };
  }

  private variables(): EditorValueVariable[] {
    return this.document.valueChoices.map((choice) => ({
      name: choice.name,
      display: choice.display,
      type: choice.type,
      group: choice.group,
      origin: choice.origin,
      source: choice.source,
      description: choice.description,
      persistent: choice.persistent,
      tone: choice.group.toLowerCase(),
      details: [choice.description, choice.source].filter((part) => part.length > 0).join(' · '),
    }));
  }

  private node(
    kind: string,
    path: number[],
    text: string,
    extra: Partial<EditorValueNode> = {},
  ): EditorValueNode {
    return {
      ...structuredClone(DATA.node),
      kind,
      path,
      length: text.length,
      text,
      tone: kind === 'value' ? 'text' : '',
      isEmpty: kind === 'empty',
      canRemove: kind === 'value',
      ...extra,
    };
  }

  private remember(): void {
    this.history.push(this.text);
    if (this.history.length > 30) {
      this.history.shift();
    }
    this.future = [];
  }

  private textValue(params: Record<string, unknown>, name: string): string {
    const value = params[name];
    return value === undefined || value === null ? '' : String(value);
  }
}
