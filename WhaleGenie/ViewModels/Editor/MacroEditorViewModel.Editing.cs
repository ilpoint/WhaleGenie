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

// Changing the step tree: add, edit, group, lift, call and delete.
public partial class MacroEditorViewModel
{
    [RelayCommand]
    private void AddAction(MacroAction? action)
    {
        if (action is null || !action.IsEnabled)
        {
            return;
        }

        switch (action.Key)
        {
            case "edit":
                EditSelectedCommand.Execute(null);
                break;
            case "toggleEnabled":
                ToggleStepEnabledCommand.Execute(null);
                break;
            case "undo":
                UndoCommand.Execute(null);
                break;
            case "redo":
                RedoCommand.Execute(null);
                break;
            case "copy":
                CopyRequested?.Invoke();
                break;
            case "cut":
                CutRequested?.Invoke();
                break;
            case "paste":
                PasteRequested?.Invoke();
                break;
            case "delete":
                DeleteSelectedCommand.Execute(null);
                break;
            case "group":
                GroupSelectedCommand.Execute(null);
                break;
            case "liftToMacro":
                LiftSelectedCommand.Execute(null);
                break;
            case "moveUp":
                MoveSelectedUpCommand.Execute(null);
                break;
            case "moveDown":
                MoveSelectedDownCommand.Execute(null);
                break;
            case "clear":
                ClearStepsCommand.Execute(null);
                break;
            case "adjustDelays":
                AdjustDelaysRequested?.Invoke();
                break;
            case "runMacro":
                RunMacroRequested?.Invoke();
                break;
        }
    }

    /// <summary>
    /// Asks the window to open the "Add Action" dialog. The new step goes below whatever is
    /// picked, inside the block it sits in, which is where a step added while reading a block
    /// is wanted: with nothing picked it goes at the end of the macro.
    /// </summary>
    [RelayCommand]
    private void NewStep()
    {
        _insertAt = InsertionPoint();
        AddStepRequested?.Invoke();
    }

    /// <summary>
    /// What the "Add Action" dialog may offer for the step being built: a switch's branch list
    /// only takes branches, everything else takes anything. Read by the window when it opens the
    /// dialog.
    /// </summary>
    public IReadOnlyList<ActionDefinition>? InsertChoices => _insertAt is
    { Owner: { } owner, Parameter: { } parameter }
        ? ChoicesFor(owner, parameter)
        : null;

    /// <summary>
    /// The same list as a value rather than an option, for the places that hand the catalogue to a
    /// dialog: a step added at the top level may be any action the palette offers, so that is what
    /// an unrestricted place comes to. The dialog is never given "no list" — that is how it came to
    /// offer the whole catalogue beside a form whose action had already been decided.
    /// </summary>
    public IReadOnlyList<ActionDefinition> AddableActions
        => InsertChoices ?? ActionCatalog.RunnableActions;

    private static IReadOnlyList<ActionDefinition>? ChoicesFor(MacroStep owner, StepParameter parameter)
    {
        var definition = owner.Definition?.Parameters
            .FirstOrDefault(candidate => candidate.Name == parameter.Name);

        return definition is { ChildKeys.Count: > 0 } ? ActionCatalog.ForKeys(definition.ChildKeys) : null;
    }

    /// <summary>
    /// Checks a proposed insertion without moving the editor's pending target. Drag feedback uses
    /// this before the pointer is released, so an illegal seam never promises a drop it will refuse.
    /// </summary>
    public string? PlacementRefusalAt(IList<MacroStep> list, int index, IEnumerable<MacroStep> steps)
        => PlacementRefusal(TargetForList(list, index), steps);

    /// <summary>Checks the current selection at a proposed drop seam.</summary>
    public string? MoveSelectionRefusalAt(IList<MacroStep> list, int index)
    {
        var target = TargetForList(list, index);
        var moving = Ordered(_selection);
        return moving.Any(step => IsInside(target.List, step))
            ? Strings.Get("Editor.PlaceIntoItself")
            : PlacementRefusal(target, moving);
    }

    /// <summary>Shows the reason a placement was refused without changing the macro.</summary>
    public void ReportPlacementRefusal(string reason) => Notice = reason;

    /// <summary>
    /// Lets every step in this document have an id of its own, and says so when one had to move.
    /// Two steps with one id is a file that cannot be branched on — the run would keep one answer
    /// under a name two steps share — so the repair is done here and reported rather than left to
    /// be discovered at run time.
    /// </summary>
    public void SettleStepIds() => ShowStepIdsRepaired(StepIds.Settle(Steps));

    /// <summary>
    /// Says which ids a repair moved, for a caller that settled more than one document and knows
    /// which one this is. The macro counts as changed: the file on disk still has the old names.
    /// </summary>
    public void ShowStepIdsRepaired(IReadOnlyList<StepIds.Rename> renamed)
    {
        if (renamed.Count == 0)
        {
            return;
        }

        Notice = Strings.Format("Editor.StepIdsRepaired", renamed.Count,
            string.Join(", ", renamed.Select(rename => $"{rename.Old} → {rename.New}")));
        IsDirty = true;
    }

    /// <summary>
    /// Shows or hides the steps inside a block. The tree keeps the steps either way — folding is
    /// a row that is not drawn, not a row that is gone — so this only has to deal with a selection
    /// that went out of sight with them.
    /// </summary>
    public void SetExpanded(MacroStep step, bool expanded)
    {
        if (step.IsExpanded == expanded)
        {
            return;
        }

        step.IsExpanded = expanded;

        if (!expanded)
        {
            var visible = VisibleSteps().ToHashSet();
            if (_selection.Any(selected => !visible.Contains(selected)))
            {
                var kept = _selection.Where(visible.Contains).ToList();
                RestoreSelection(kept.Count > 0 ? kept : [step]);
            }
        }

        Reshape();
        SelectionRefreshRequested?.Invoke();
    }

    /// <summary>
    /// Opens every block a step sits inside, so a step somebody asked to be shown is really on the
    /// list rather than hidden inside a folded block. Answers false when the step is not part of
    /// this macro, which is the case for one that has since been replaced.
    /// </summary>
    public bool Reveal(MacroStep step)
    {
        if (!Opens(Steps, step))
        {
            return false;
        }

        RebuildRows();
        return true;
    }

    /// <summary>
    /// Opens the blocks on the way down to a step. Whatever a step is nested in has to be unfolded
    /// for it to have a row at all, so the answer is collected on the way back up.
    /// </summary>
    private static bool Opens(IEnumerable<MacroStep> steps, MacroStep wanted)
    {
        foreach (var candidate in steps)
        {
            if (ReferenceEquals(candidate, wanted))
            {
                return true;
            }

            foreach (var list in candidate.StepLists)
            {
                if (Opens(list.Steps, wanted))
                {
                    candidate.IsExpanded = true;
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Asks the window to open the run window for the steps as they are now, which is what the button
    /// beside 「保存」 does, and it loads the steps there rather than running them. It is the only way
    /// into a run from this window: the palette had a second entry for it and F5 a third, and all
    /// three asked for the same window.
    /// </summary>
    [RelayCommand]
    private void Run() => RunRequested?.Invoke();

    /// <summary>Asks the window to open the edit dialog for the selected step.</summary>
    [RelayCommand]
    private void EditSelected()
    {
        if (_selection.Count == 1)
        {
            EditStepRequested?.Invoke(_selection[0]);
        }
    }

    /// <summary>
    /// Turns the selected steps on or off. When any of them is already off they all come
    /// back on, so one press always changes something.
    /// </summary>
    [RelayCommand]
    private void ToggleStepEnabled()
    {
        var selected = Ordered(_selection);
        if (selected.Count == 0)
        {
            return;
        }

        PushUndo();

        var enable = selected.Any(step => !step.Meta.IsEnabled);
        foreach (var step in selected)
        {
            step.Meta = step.Meta.WithEnabled(enable);
        }

        NotifyStepsChanged();
    }

    /// <summary>
    /// Wraps the selected steps in a <c>control.sequence</c> step, which is the macro
    /// editor's way of grouping: a sequence runs its children in order, so grouping and
    /// running in order are the same thing.
    /// </summary>
    [RelayCommand]
    private void GroupSelected()
    {
        var selected = Ordered(_selection);
        if (selected.Count == 0 || ListOf(selected[0]) is not { } list)
        {
            return;
        }

        // Grouping wraps steps that sit side by side. Steps picked out of different blocks have
        // no one place to be wrapped into, so the block the first of them is in is the one that
        // gets the group.
        var grouped = selected.Where(step => ReferenceEquals(ListOf(step), list)).ToList();
        var index = list.IndexOf(grouped[0]);
        var group = new MacroStep
        {
            Type = "control.sequence",
            Meta = StepMeta.New,
            Parameters =
            [
                new StepParameter
                {
                    Name = "steps",
                    Kind = ActionParameterKind.Steps,
                    Steps = [.. grouped],
                },
            ],
        };

        if (PlacementRefusal(TargetForList(list, index), [group]) is { } refusal)
        {
            ReportPlacementRefusal(refusal);
            return;
        }

        PushUndo();
        foreach (var step in grouped)
        {
            list.Remove(step);
        }

        list.Insert(Math.Clamp(index, 0, list.Count), group);
        SettleStepIds();
        RestoreSelection([group]);
        NotifyStepsChanged();
    }

    /// <summary>
    /// Moves the selected steps into a macro of their own and leaves a call to it in their place.
    /// What the stretch reads from this macro becomes the new macro's arguments and what it writes
    /// becomes its results, so the steps around the call go on reading the names they always did.
    /// </summary>
    [RelayCommand]
    private void LiftSelected()
    {
        var selected = Ordered(_selection);
        if (selected.Count == 0 || ListOf(selected[0]) is not { } list)
        {
            return;
        }

        // The stretch is the part of the selection that sits in one list. Steps picked out of
        // several blocks have no one place to come out of, and the call they left behind would
        // have nowhere to go; as with grouping, the block the first of them is in is the one.
        var stretch = selected.Where(step => ReferenceEquals(ListOf(step), list)).ToList();
        if (stretch.Count == 0)
        {
            return;
        }

        var lifted = LiftedMacro(stretch);
        var call = CallTo(lifted);
        if (PlacementRefusal(TargetForList(list, list.IndexOf(stretch[0])), [call]) is { } refusal)
        {
            ReportPlacementRefusal(refusal);
            return;
        }

        PushUndo();
        var index = list.IndexOf(stretch[0]);
        foreach (var step in stretch)
        {
            list.Remove(step);
        }

        list.Insert(Math.Clamp(index, 0, list.Count), call);
        SettleStepIds();

        // The new macro is one of the ones a call may reach from here, so the step just written is
        // checked against it rather than being marked as calling something that is not there.
        _projectMacros = [.. _projectMacros, lifted.Callable];
        RestoreSelection([call]);
        NotifyStepsChanged();
        MacroLifted?.Invoke(lifted);
    }

    /// <summary>
    /// The macro a stretch of steps becomes: named after this one, taking as arguments the values
    /// it read from around itself, and offering back the values it writes.
    /// </summary>
    private MacroItem LiftedMacro(IReadOnlyList<MacroStep> stretch)
    {
        var lifted = new MacroItem { Name = FreeMacroName() };

        // What the stretch makes is what the steps after the call can still be reading, so every
        // name it writes is offered back rather than left behind in the called macro's own table.
        var written = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var step in stretch)
        {
            step.CollectVariables(written);
        }

        // Shared values and the ones WhaleGenie provides are readable on both sides of a call, so
        // they need no argument; everything else the stretch reads has to be handed in.
        var alwaysVisible = new HashSet<string>(
            VariableCatalog.SystemVariables.Select(variable => variable.Name),
            StringComparer.OrdinalIgnoreCase);
        alwaysVisible.UnionWith(Variables.Globals.Select(variable => variable.Name));
        alwaysVisible.Add(MacroRunner.CaughtKeyName);
        alwaysVisible.Add(MacroRunner.CaughtDetailName);

        // What each name holds, read from the steps themselves: a value a call handed back as a
        // number stays a number when it becomes an argument, so the call form says what it wants and
        // the checks either side of the call agree about it.
        var types = MacroCheck.TypesOf(stretch, OwnCallable, _projectMacros);

        // A lifted argument is required because the call this very lift writes always passes it. The
        // alternative — declaring it optional — would let a later edit delete the row and leave the
        // macro reading an empty value with nothing said, which is the failure the declaration rules
        // are for.
        var read = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in MacroCheck.Reads(stretch))
        {
            if (!read.Add(name) || written.Contains(name) || alwaysVisible.Contains(name))
            {
                continue;
            }

            lifted.Parameters.Add(new MacroParameter
            {
                Name = name,
                Type = TypeOf(types, name),
                Required = true,
            });
        }

        foreach (var name in written)
        {
            lifted.Results.Add(new MacroResult { Name = name, Type = TypeOf(types, name) });
        }

        foreach (var step in stretch)
        {
            lifted.Steps.Add(step);
        }

        return lifted;
    }

    /// <summary>
    /// What a name was seen to hold, or text when nothing said. Text is the honest answer for a name
    /// whose value nothing declares: a wrong guess here would be a type the call site enforces.
    /// </summary>
    private static ValueKind TypeOf(IReadOnlyDictionary<string, ValueKind> types,
        string name)
        => types.TryGetValue(name, out var type) ? type : ValueKind.Text;

    /// <summary>The call left where the stretch was: it hands in this macro's values and takes back
    /// every name the stretch writes, each under the name it already had.</summary>
    private MacroStep CallTo(MacroItem lifted)
    {
        var parameters = new List<StepParameter>
        {
            new()
            {
                Name = "macro",
                Kind = ActionParameterKind.Macro,
                Written = Formula.Text(lifted.Name),
            },
        };

        // What the call passes is the same values the stretch read, so each argument row points at
        // the name the value already had: the lifted steps keep working without a rename anywhere.
        parameters.Add(new StepParameter
        {
            Name = FieldNames.Arguments,
            Kind = ActionParameterKind.Arguments,
            Rows =
            [
                .. lifted.Parameters
                    .Where(argument => argument.Name.Trim().Length > 0)
                    .Select(argument => new StepParameterRow
                    {
                        Cells = new Dictionary<string, ExpressionNode>(StringComparer.OrdinalIgnoreCase)
                        {
                            [FieldNames.Name] = Formula.Text(argument.Name.Trim()),
                            [FieldNames.Value] = Formula.Name(argument.Name.Trim()),
                        },
                    }),
            ],
        });

        parameters.Add(new StepParameter
        {
            Name = "returns",
            Kind = ActionParameterKind.Returns,
            Rows =
            [
                .. lifted.Results.Select(result => new StepParameterRow
                {
                    Cells = new Dictionary<string, ExpressionNode>(StringComparer.OrdinalIgnoreCase)
                    {
                        [FieldNames.Key] = Formula.Text(result.Name),
                        [FieldNames.Name] = Formula.Text(result.Name),
                    },
                }),
            ],
        });

        return new MacroStep
        {
            Type = "control.runMacro",
            Id = NewStepId(),
            Meta = StepMeta.New,
            Parameters = parameters,
        };
    }

    /// <summary>A name for the new macro that is not one of the project's own.</summary>
    private string FreeMacroName()
    {
        var stem = Name.Trim();
        var wanted = stem.Length == 0
            ? Strings.Get("Editor.LiftedMacroNameEmpty")
            : Strings.Format("Editor.LiftedMacroName", stem);

        var taken = new HashSet<string>(
            _projectMacros.Select(macro => macro.Name), StringComparer.OrdinalIgnoreCase);
        if (stem.Length > 0)
        {
            taken.Add(stem);
        }

        return VariableNames.Free(wanted, taken);
    }

    [RelayCommand]
    private void DeleteSelected()
    {
        if (_selection.Count == 0)
        {
            return;
        }

        PushUndo();

        // A block that is dropped takes the steps inside it with it, and a step that is already
        // gone has no list to be taken from, so any order but this one would trip over itself.
        foreach (var step in Ordered(_selection))
        {
            ListOf(step)?.Remove(step);
        }

        _selection.Clear();
        NotifyStepsChanged();
    }

    [RelayCommand]
    private void MoveSelectedUp() => MoveSelection(-1);

    [RelayCommand]
    private void MoveSelectedDown() => MoveSelection(1);

    /// <summary>Asks the window to confirm, because clearing cannot be undone.</summary>
    [RelayCommand]
    private void ClearSteps()
    {
        if (Steps.Count == 0)
        {
            return;
        }

        ClearRequested?.Invoke();
    }

    /// <summary>Removes every step once the window has confirmed the request.</summary>
    public void ClearStepsNow()
    {
        PushUndo();
        Steps.Clear();
        _selection.Clear();
        NotifyStepsChanged();
    }
}
