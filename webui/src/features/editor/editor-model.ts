import type {
  EditorAction,
  EditorStep,
  EditorValueChoice,
} from '../../shared/bridge/protocol.js';

/** The step's own place in the tree, with the section it lives in. */
export function flattenSteps(steps: readonly EditorStep[]): EditorStep[] {
  const all: EditorStep[] = [];
  for (const step of steps) {
    all.push(step);
    for (const section of step.sections) {
      all.push(...flattenSteps(section.steps));
    }
  }

  return all;
}

export function findStep(steps: readonly EditorStep[], id: string): EditorStep | null {
  if (id.length === 0) {
    return null;
  }

  for (const step of steps) {
    if (step.id === id) {
      return step;
    }

    for (const section of step.sections) {
      const found = findStep(section.steps, id);
      if (found) {
        return found;
      }
    }
  }

  return null;
}

/** True when the action card answers the search, including the native two-character fallback. */
export function actionAnswers(action: EditorAction, search: string): boolean {
  const needle = search.trim().toLocaleLowerCase();
  if (needle.length === 0) {
    return true;
  }

  const says = (text: string): boolean => text.toLocaleLowerCase().includes(needle);
  if (says(action.key) || says(action.name) || says(action.description)
      || says(action.categoryLabel)) {
    return true;
  }

  // A long Chinese phrase no longer appears whole in a renamed action; two-character pieces are
  // the shortest split that still means something (one character matches almost everything).
  if (needle.length < 4) {
    return false;
  }

  const fields = [action.key, action.name, action.description, action.categoryLabel]
    .map((text) => text.toLocaleLowerCase());
  for (let at = 0; at < needle.length; at += 2) {
    const piece = needle.slice(at, at + 2);
    if (!fields.some((text) => text.includes(piece))) {
      return false;
    }
  }

  return true;
}

export type PaletteSource = 'basic' | 'flow' | 'sub' | 'public';

/** The actions a palette dock and category show; a search opens both action docks into one library. */
export function paletteActions(
  actions: readonly EditorAction[],
  source: PaletteSource,
  category: string | null,
  search: string,
): EditorAction[] {
  if (source === 'sub' || source === 'public') {
    return [];
  }

  const everywhere = search.trim().length > 0;
  return actions
    .filter((action) => everywhere
      ? category === null || action.category === category
      : source === 'flow'
        ? action.category === 'Control'
        : action.category !== 'Control'
          && (category === null || action.category === category))
    .filter((action) => actionAnswers(action, search))
    .sort((left, right) => left.category.localeCompare(right.category)
      || left.groupRank - right.groupRank
      || left.name.localeCompare(right.name));
}

export interface ActionSection {
  label: string;
  rank: number;
  actions: EditorAction[];
}

/** One section per small group, in the order the catalogue ranks them. */
export function actionSections(actions: readonly EditorAction[]): ActionSection[] {
  const byLabel = new Map<string, ActionSection>();
  for (const action of actions) {
    const found = byLabel.get(action.groupLabel);
    if (found) {
      found.actions.push(action);
    } else {
      byLabel.set(action.groupLabel, {
        label: action.groupLabel,
        rank: action.groupRank,
        actions: [action],
      });
    }
  }

  return [...byLabel.values()].sort((left, right) => left.rank - right.rank
    || left.label.localeCompare(right.label));
}

export type VariableGroupFilter = 'All' | 'Local' | 'Global' | 'System' | 'StepResult';

/** The wall's filter, by the group the C# snapshot already assigned to each entry. */
export function wallVariables(
  choices: readonly EditorValueChoice[],
  group: VariableGroupFilter,
  search: string,
): EditorValueChoice[] {
  const needle = search.trim().toLocaleLowerCase();
  return choices
    .filter((choice) => group === 'All' || choice.group === group)
    .filter((choice) => needle.length === 0
      || choice.name.toLocaleLowerCase().includes(needle)
      || choice.display.toLocaleLowerCase().includes(needle)
      || choice.source.toLocaleLowerCase().includes(needle)
      || choice.description.toLocaleLowerCase().includes(needle)
      || choice.type.toLocaleLowerCase().includes(needle))
    .sort((left, right) => left.display.localeCompare(right.display));
}
