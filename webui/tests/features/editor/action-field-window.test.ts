import { render } from 'lit';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { EditorActionField } from '../../../src/shared/bridge/protocol.js';
import { renderEditor, type ActionFieldHost } from '../../../src/features/editor/action-fields/render.js';

function windowTitleField(): EditorActionField {
  return {
    name: 'window', label: 'Window', hint: '', kind: 'Text', valueForm: 'Value',
    value: '', display: '', parts: [], options: [], columns: [], condition: null,
    isApplicable: true, isDefault: false, isMissing: false, required: true, advanced: false,
    group: '', groupLabel: '', offersValue: false, takesFormula: false, showsNameMark: false,
    allowsNewName: false, isNumber: false, isNumberFormula: false, isDuration: false,
    unit: '', unitLabel: '', durationUnits: [], isPercent: false, percentValue: 0,
    channelNote: '', jitterRange: '', isMultiline: false, isCode: false, isColor: false,
    colour: '', isPath: false, pathIntent: '', pathFilter: '', isKey: false, isKeys: false,
    isSelector: false, isBrowserTarget: false, isImage: false, isImageList: false,
    isRegion: false, isRegionList: false, isColourPoints: false, isColourRows: false,
    isKeySequence: false, isNarrow: false, isWindow: true, isWindowRule: false,
    isMacro: false, isStepChoice: false, isRecord: false, isPoint: false, isRect: false,
    placeholder: '', hasToBeAnswered: false, isFollowingWindow: false,
    windowRuleSummary: '', regions: [], images: [], colours: [], colourPoints: [], keys: [],
    narrowRows: [], rows: [], macros: [], steps: [], candidates: [],
    windowPreview: null, imagePreview: null,
    minimum: null, maximum: null, increment: 1, jitterPercent: null, emptyMeans: 'Zero',
  };
}

afterEach(() => {
  document.body.replaceChildren();
});

describe('window title field', () => {
  it('opens the page window list instead of a desktop picker', () => {
    const host = {
      openWindowList: vi.fn(),
      pick: vi.fn(),
    } as unknown as ActionFieldHost;
    render(renderEditor(host, windowTitleField()), document.body);

    const button = document.querySelector('button.chip');
    expect(button?.textContent).toContain('Add.PickWindow');
    button?.dispatchEvent(new MouseEvent('click', { bubbles: true }));

    expect(host.openWindowList).toHaveBeenCalledTimes(1);
    expect(host.pick).not.toHaveBeenCalled();
  });
});
