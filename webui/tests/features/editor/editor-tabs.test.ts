import { beforeEach, describe, expect, it } from 'vitest';
import { editor } from '../../../src/features/editor/editor-store.js';
import { WgEditorTabs } from '../../../src/features/editor/editor-tabs.js';

describe('editor tabs navigation', () => {
  beforeEach(() => {
    window.location.hash = 'editor';
    document.body.replaceChildren();
    editor.applyHostSnapshot(null);
  });

  it('offers a way back to the macro list without the side bar', async () => {
    const tabs = new WgEditorTabs();
    document.body.appendChild(tabs);
    await tabs.updateComplete;

    const back = tabs.renderRoot.querySelector<HTMLButtonElement>('.back');

    expect(back).not.toBeNull();
    back!.click();
    expect(window.location.hash).toBe('#macros');
  });
});
