using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WindowRuleModel = WhaleGenie.Core.Devices.WindowRule;

namespace WhaleGenie.ViewModels;

// The rows the list shows: rebuilding, flattening, problem marks and branches.
public partial class MacroEditorViewModel
{
    /// <summary>
    /// Rebuilds the rows the list shows and puts the selection back on them, in that order: the
    /// view can only highlight a row that is there.
    /// </summary>
    internal void NotifyStepsChanged()
    {
        Notice = string.Empty;
        RebuildRows();
        OnPropertyChanged(nameof(HasSteps));
        RefreshPaletteState();
        RefreshVariableCenter();
        RefreshRowDetails();
        SelectionRefreshRequested?.Invoke();
    }

    /// <summary>
    /// Lays the steps out as a tree. Every node is rebuilt rather than patched, so what the tree
    /// shows can never drift from the steps; folding is left alone, because a folded block is a
    /// hidden child rather than a missing one.
    /// </summary>
    private void RebuildRows()
    {
        RefreshProblems();

        IsRebuildingRows = true;
        try
        {
            Rows.Clear();
            Roots.Clear();
            AddNodes(Steps, null, 0, [], Roots);
            Flatten(Roots);
            RefreshRowDetails();
        }
        finally
        {
            _valuesToEdit = null;
            IsRebuildingRows = false;
        }
    }

    /// <summary>Numbers the nodes that are on screen, in document order.</summary>
    private void Flatten(IEnumerable<StepRow> nodes)
    {
        foreach (var node in nodes)
        {
            node.RowIndex = Rows.Count;
            Rows.Add(node);

            if (node.IsExpanded)
            {
                Flatten(node.Children);
            }
        }
    }

    /// <summary>Reads the rows on screen off the tree again, after something opened or closed.</summary>
    private void Reshape()
    {
        IsRebuildingRows = true;
        try
        {
            Rows.Clear();
            Flatten(Roots);
            RefreshRowDetails();
        }
        finally
        {
            IsRebuildingRows = false;
        }
    }

    /// <summary>
    /// Runs the design-time check over the steps as they stand and puts the answers where the
    /// rows and the line above the list can read them.
    /// </summary>
    private void RefreshProblems()
    {
        Problems = MacroCheck.Inspect(Steps, OwnCallable, _projectMacros, _browserShared);
        DeclarationProblems = MacroCheck.DeclarationProblems(OwnCallable);
        CallImpactSummary = MacroCheck.CallImpact(OwnCallable, _projectMacros);
        _problemByStep = Problems
            .GroupBy(problem => problem.Step)
            .ToDictionary(group => group.Key,
                group => string.Join("\n", group.Select(problem => problem.Message).Distinct()));

        OnPropertyChanged(nameof(Problems));
        OnPropertyChanged(nameof(HasProblems));
        OnPropertyChanged(nameof(HasStepProblems));
        OnPropertyChanged(nameof(DeclarationProblems));
        OnPropertyChanged(nameof(HasDeclarationProblems));
        OnPropertyChanged(nameof(HasBlockingDeclarationProblems));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CallImpactSummary));
        OnPropertyChanged(nameof(HasCallImpact));
        OnPropertyChanged(nameof(ProblemSummary));
        OnPropertyChanged(nameof(DeclarationProblemSummary));
    }

    /// <summary>Checks the declarations again after the macro interface changed a name or a kind.</summary>
    internal void RefreshDeclarations()
    {
        // MarkDirty raises Changed even when the macro was already unsaved. Without that, a second
        // declaration edit would never reach the recovery writer: it listens for changes that arrive
        // while the macro is already dirty, and the dirty flag itself does not move again.
        MarkDirty();
        RefreshProblems();
    }
    private void AddNodes(IList<MacroStep> steps, StepParameter? owner, int depth,
        IReadOnlyList<bool> continues, IList<StepRow> into)
    {
        for (var index = 0; index < steps.Count; index++)
        {
            var step = steps[index];
            step.EnsureDeclaredLists();
            var lists = step.StepLists.ToList();

            var hasNext = index < steps.Count - 1;
            var row = new StepRow
            {
                Kind = StepRowKind.Step,
                Step = step,
                List = owner,
                Index = index,
                Depth = depth,
                Problem = _problemByStep.GetValueOrDefault(step) ?? string.Empty,
                Folded = OnRowFolded,
            };
            row.Guide(depth, continues, hasNext, hasBox: lists.Count > 0);
            into.Add(row);

            if (lists.Count == 0)
            {
                continue;
            }

            // Every list a block runs gets a row of its own, one list or several: it is the body a
            // person drags into, and without it there is nothing on screen that says where the
            // block's steps begin and end.
            var deeper = continues.Append(hasNext).ToList();
            for (var seat = 0; seat < lists.Count; seat++)
            {
                var list = lists[seat];
                var sectionNext = seat < lists.Count - 1;

                // A switch is a table of values, so it reads as one: every branch is a row named by
                // the values it catches, with its steps underneath. A section called "cases" holding
                // a node per branch spends two levels of indentation on one line of content, and the
                // values — the only thing that tells one branch from another — end up buried in the
                // node's summary.
                if (IsBranches(step, list))
                {
                    AddBranches(list, row, depth, deeper, sectionNext);
                    continue;
                }
                var section = new StepRow
                {
                    Kind = StepRowKind.Section,
                    Step = step,
                    List = list,
                    Depth = depth + 1,
                    Folded = OnRowFolded,
                };
                section.Guide(depth + 1, deeper, sectionNext, hasBox: false);
                row.Children.Add(section);

                var bodyContinues = deeper.Append(sectionNext).ToList();
                AddNodes(list.Steps, list, depth + 2, bodyContinues, section.Children);
            }
        }
    }

    /// <summary>
    /// True on the list a switch keeps its branches in. It is read as a table whether or not it
    /// holds a branch yet: an empty switch still needs the line that adds the first one, and a
    /// section titled "branches" with nothing under it says neither what a branch is nor how to
    /// make one.
    /// </summary>
    private static bool IsBranches(MacroStep step, StepParameter list)
        => step.Type == "control.switch"
            && list.Name == "cases"
            && list.Steps.All(branch => branch.Type == "control.case");

    /// <summary>
    /// Lays a switch's branches out as one row each: the row is named by the branch's values, and
    /// the branch's own steps hang under it, so the whole switch reads as a table of values. The
    /// table ends with the line that adds the next branch.
    /// </summary>
    private void AddBranches(StepParameter list, StepRow block, int depth,
        IReadOnlyList<bool> continues, bool lastList)
    {
        for (var index = 0; index < list.Steps.Count; index++)
        {
            var choice = list.Steps[index];
            choice.EnsureDeclaredLists();
            var body = choice.StepLists.FirstOrDefault();
            var hasNext = index < list.Steps.Count - 1 || lastList;

            var section = new StepRow
            {
                Kind = StepRowKind.Section,
                Step = choice,
                List = body,
                Depth = depth + 1,
                Folded = OnRowFolded,
                ValuesEdited = OnRowValuesEdited,
            };

            // A branch that was just added opens its values for typing: a branch is one line of
            // values and the steps that run for them, and until the values are written it matches
            // nothing. This is the one moment the form knows what the person came here to type.
            if (ReferenceEquals(choice, _valuesToEdit))
            {
                section.BeginEditingValues();
            }

            section.Guide(depth + 1, continues, hasNext, hasBox: false);
            block.Children.Add(section);

            if (body is not null)
            {
                AddNodes(body.Steps, body, depth + 2, continues.Append(hasNext).ToList(), section.Children);
            }
        }

        _valuesToEdit = null;

        // The foot of the table. A branch cannot be dragged in from the library the way every other
        // step can — there is no branch card — so the block offers it here, where the next branch
        // would be written, rather than relying on "pick the switch, then ask for a step".
        var foot = new StepRow
        {
            Kind = StepRowKind.Add,
            Step = block.Step,
            List = list,
            Depth = depth + 1,
            Folded = OnRowFolded,
        };
        foot.Guide(depth + 1, continues, lastList, hasBox: false);
        block.Children.Add(foot);
    }

    /// <summary>The branch a freshly added row is about to open for its values, if any.</summary>
    private MacroStep? _valuesToEdit;

    /// <summary>
    /// Adds a branch under a switch and opens its values for typing. The branch starts with no
    /// values, which is what "no branch matches this yet" honestly is: guessing one would put a
    /// value the user never wrote into the macro.
    /// </summary>
    public MacroStep? AddBranch(MacroStep block)
    {
        if (block.StepLists.FirstOrDefault(list => list.Name == "cases") is not { } cases)
        {
            return null;
        }

        PushUndo();
        block.IsExpanded = true;

        var branch = new MacroStep
        {
            Type = "control.case",
            Id = NewStepId(),
            Meta = StepMeta.New,
            Parameters =
            [
                new StepParameter { Name = "values", Kind = ActionParameterKind.Text },
                new StepParameter { Name = "body", Kind = ActionParameterKind.Steps },
            ],
        };

        cases.Steps.Add(branch);
        _valuesToEdit = branch;
        SettleStepIds();
        RestoreSelection([branch]);
        NotifyStepsChanged();
        return branch;
    }

    /// <summary>
    /// A branch's values were typed in and kept. They are written into the step as they are typed,
    /// so there is nothing to copy here — only a macro that has been changed, and a check that may
    /// have something new to say about the switch.
    /// </summary>
    private void OnRowValuesEdited(StepRow row)
    {
        MarkDirty();
        RefreshProblems();
    }

    /// <summary>
    /// A run of steps went out of sight, so the selection follows: a step nobody can see must not
    /// be the one a delete or a cut acts on.
    /// </summary>
    private void OnRowFolded(StepRow row, bool expanded)
    {
        if (!expanded && _selection.Count > 0)
        {
            var visible = VisibleSteps().ToHashSet();
            if (_selection.Any(selected => !visible.Contains(selected)))
            {
                var kept = _selection.Where(visible.Contains).ToList();
                RestoreSelection(kept.Count > 0 ? kept : [row.Step]);
            }
        }

        Reshape();
        SelectionRefreshRequested?.Invoke();
    }

    /// <summary>The steps on screen: those whose block, and whose section, are open.</summary>
    private IEnumerable<MacroStep> VisibleSteps()
        => Visible(Roots);

    private static IEnumerable<MacroStep> Visible(IEnumerable<StepRow> rows)
    {
        foreach (var row in rows)
        {
            if (row.IsStep)
            {
                yield return row.Step;
            }

            if (!row.IsExpanded)
            {
                continue;
            }

            foreach (var step in Visible(row.Children))
            {
                yield return step;
            }
        }
    }

    /// <summary>True while the step list can be rolled back.</summary>
    public bool CanUndo => _undo.Count > 0;
}
