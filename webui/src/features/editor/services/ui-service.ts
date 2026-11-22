import type { EditorCore } from './editor-core.js';
import type { PaletteSource } from '../editor-model.js';
import type { InspectorTab } from '../state/types.js';

/** The ui command family. All state lives in EditorCore; C# remains the source of truth. */
export class EditorUiService {
  constructor(private readonly core: EditorCore) {}

  selectStep(id: string, additive = false, range: string[] | null = null): void {
    if (range !== null) {
      this.core.state.selectedStepIds = [...new Set(range)];
      this.core.state.selectedStepId = id;
      this.core.notify();
      return;
    }

    if (!additive) {
      this.core.state.selectedStepIds = id.length > 0 ? [id] : [];
      this.core.state.selectedStepId = id;
      this.core.notify();
      return;
    }

    if (this.core.state.selectedStepIds.includes(id)) {
      this.core.state.selectedStepIds = this.core.state.selectedStepIds.filter((candidate) => candidate !== id);
      this.core.state.selectedStepId = this.core.state.selectedStepIds.at(-1) ?? '';
    } else if (id.length > 0) {
      this.core.state.selectedStepIds = [...this.core.state.selectedStepIds, id];
      this.core.state.selectedStepId = id;
    }

    this.core.notify();
  }

  selectedSteps(): string[] {
    return [...this.core.state.selectedStepIds];
  }

  hasStepSelection(): boolean {
    return this.core.state.selectedStepIds.length > 0;
  }

  canEditStep(): boolean {
    return this.core.state.selectedStepIds.length === 1;
  }

  selectAction(key: string): void {
    this.core.state.selectedActionKey = key;
    this.core.notify();
  }

  toggleStep(id: string): void {
    if (this.core.state.collapsedSteps.has(id)) {
      this.core.state.collapsedSteps.delete(id);
    } else {
      this.core.state.collapsedSteps.add(id);
    }

    this.core.notify();
  }

  setPaletteSource(source: PaletteSource): void {
    this.core.state.paletteSource = source;
    this.core.state.category = null;
    this.core.notify();
  }

  setPaletteCategory(category: string | null): void {
    this.core.state.category = category;
    this.core.notify();
  }

  setPaletteSearch(search: string): void {
    this.core.state.search = search;
    this.core.notify();
  }

  setInspectorTab(tab: InspectorTab): void {
    this.core.state.inspectorTab = tab;
    this.core.notify();
  }

  setInspectorVisible(visible: boolean): void {
    this.core.state.inspectorVisible = visible;
    this.core.notify();
  }

  setInspectorWidth(width: number): void {
    this.core.state.inspectorWidth = Math.max(280, Math.min(480, width));
    this.core.notify();
  }

}
