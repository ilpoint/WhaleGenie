import { afterEach, describe, expect, it } from 'vitest';
import type { EditorActionField } from '../../../src/shared/bridge/protocol.js';
import '../../../src/features/editor/value-field.js';

function field(overrides: Partial<EditorActionField> = {}): EditorActionField {
  return {
    name: 'value', label: 'Value', hint: '', kind: 'Text', valueForm: 'Value',
    value: '', display: '', parts: [], options: [], columns: [], condition: null,
    isApplicable: true, isDefault: false, isMissing: false, required: false, advanced: false,
    group: '', groupLabel: '', offersValue: true, takesFormula: true, showsNameMark: false,
    allowsNewName: false, isNumber: false, isNumberFormula: false, isDuration: false,
    unit: '', unitLabel: '', durationUnits: [], isPercent: false, percentValue: 0,
    channelNote: '', jitterRange: '', isMultiline: false, isCode: false, isColor: false,
    colour: '', isPath: false, pathIntent: '', pathFilter: '', isKey: false, isKeys: false,
    isSelector: false, isBrowserTarget: false, isImage: false, isImageList: false,
    isRegion: false, isRegionList: false, isColourPoints: false, isColourRows: false,
    isKeySequence: false, isNarrow: false, isWindow: false, isWindowRule: false,
    isMacro: false, isStepChoice: false, isRecord: false, isPoint: false, isRect: false,
    placeholder: '', hasToBeAnswered: false, isFollowingWindow: false,
    windowRuleSummary: '', regions: [], images: [], colours: [], colourPoints: [], keys: [],
    narrowRows: [], rows: [], macros: [], steps: [], candidates: [],
    windowPreview: null, imagePreview: null,
    minimum: null, maximum: null, increment: 1, jitterPercent: null, emptyMeans: 'Zero',
    ...overrides,
  };
}

afterEach(() => {
  document.body.replaceChildren();
});

describe('value field', () => {
  it('offers the value window for a bare name', async () => {
    document.body.innerHTML = '<wg-value-field></wg-value-field>';
    const element = document.querySelector('wg-value-field') as HTMLElement & {
      parameter: EditorActionField;
      updateComplete: Promise<boolean>;
    };
    element.parameter = field({
      name: 'name', valueForm: 'Name', kind: 'Variable', offersValue: true,
      display: '「count」', parts: [{ kind: 'name', text: '', name: 'count' }],
    });
    await element.updateComplete;

    const chip = element.shadowRoot?.querySelector('.chip');
    expect(chip?.textContent).toContain('Add.Value');
    expect(element.shadowRoot?.querySelector('.name-part')?.textContent).toBe('count');
  });

  it('does not offer the value window for a percentage number', async () => {
    document.body.innerHTML = '<wg-value-field></wg-value-field>';
    const element = document.querySelector('wg-value-field') as HTMLElement & {
      parameter: EditorActionField;
      updateComplete: Promise<boolean>;
    };
    element.parameter = field({
      name: 'tolerance', isNumber: true, isPercent: true, offersValue: false,
      display: '10', value: '10',
    });
    await element.updateComplete;

    expect(element.shadowRoot?.querySelector('.chip')).toBeNull();
    expect(element.shadowRoot?.querySelector('input.slider')).not.toBeNull();
  });
});
