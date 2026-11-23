using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using WhaleGenie.Contracts;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Recording;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;
namespace WhaleGenie.Editing;

/// <summary>The value editor: scalar and condition values a step opens.</summary>

internal sealed partial class HeadlessEditorSession
{
    public EditorValueSnapshot OpenValue(string stepId, string parameterName)
    {
        if (_actionSession is { } action)
        {
            return action.OpenValue(parameterName);
        }

        var step = FindStep(stepId)
            ?? throw new InvalidOperationException("the macro has no step " + stepId);
        var definition = ActionCatalog.Find(step.Type)
            ?? throw new InvalidOperationException("unknown action " + step.Type);
        var parameter = definition.Parameters.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, parameterName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("the step has no parameter " + parameterName);
        var stored = step.Parameters.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, parameterName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("the step has no stored parameter " + parameterName);

        var field = new StepParameterViewModel(parameter, _model.CollectVariables(), [],
            _model.StepChoices());
        field.ApplyValue(stored);
        var picker = new ValuePickerViewModel(field.CurrentShape, field.Variables,
            parameter.ValueForm, field.Label, field.AllowsNewName);

        _valueSession = new HeadlessValueSession(picker, value =>
        {
            field.WriteValue(value);
            var updated = field.ToStepParameter();
            var index = step.Parameters.FindIndex(candidate =>
                string.Equals(candidate.Name, parameterName, StringComparison.Ordinal));
            if (index >= 0)
            {
                step.Parameters[index] = updated;
            }
            else
            {
                step.Parameters.Add(updated);
            }
        }, () => _model.IsDirty = true);

        return _valueSession.Snapshot();
    }

    public EditorValueSnapshot OpenConditionValue(string stepId, string name, int[] path, string field)
    {
        if (_actionSession is { } action)
        {
            return action.OpenConditionValue(name, path, field);
        }

        var parameter = FindParameter(stepId, name);
        var draft = EnsureConditionDraft(parameter);
        var row = ConditionRowAt(draft.Root, path);
        var seed = field switch
        {
            "left" => row.Left,
            "right" => row.Right,
            _ => throw new InvalidOperationException("unknown condition field " + field),
        };
        var part = field == "left"
            ? Strings.Get("Editor.ConditionLeft")
            : Strings.Get("Editor.ConditionRight");
        var picker = new ValuePickerViewModel(seed, _model.CollectVariables(),
            ActionValueForm.Value, parameter.Name + " · " + part);
        _valueSession = new HeadlessValueSession(picker, value =>
        {
            if (field == "left")
            {
                row.Left = value;
            }
            else
            {
                row.Right = value;
            }

            parameter.Written = Condition.ToNode(draft.Root.ToShape()) ?? Formula.Nothing;
            _model.IsDirty = true;
        }, () => _model.IsDirty = true);
        return _valueSession.Snapshot();
    }

    public EditorValueSnapshot OpenRowValue(string stepId, string name, int rowIndex, string column)
    {
        if (_actionSession is { } action)
        {
            return action.OpenRowValue(name, rowIndex, column);
        }

        var parameter = FindParameter(stepId, name);
        if (rowIndex < 0 || rowIndex >= parameter.Rows.Count)
        {
            throw new InvalidOperationException("the row table has no row " + rowIndex);
        }

        var row = parameter.Rows[rowIndex];
        var seed = row.Cells.TryGetValue(column, out var existing) ? existing : Formula.Nothing;
        var picker = new ValuePickerViewModel(seed, _model.CollectVariables(),
            ActionValueForm.Value, parameter.Name + " · " + column);
        _valueSession = new HeadlessValueSession(picker, value =>
        {
            row.Cells[column] = value;
            _model.IsDirty = true;
        }, () => _model.IsDirty = true);
        return _valueSession.Snapshot();
    }

    public EditorValueSnapshot ValueState()
        => _actionSession?.ValueState()
           ?? _valueSession?.Snapshot()
           ?? throw new InvalidOperationException("no value field is open");

    public EditorValueSnapshot ValueCommand(ValueEditorCommand command)
        => _actionSession?.ValueCommand(command)
           ?? _valueSession?.Execute(command)
           ?? throw new InvalidOperationException("no value field is open");

    public EditorValueOutcome ConfirmValue()
    {
        if (_actionSession is { } action)
        {
            return action.ConfirmValue();
        }

        var session = _valueSession
            ?? throw new InvalidOperationException("no value field is open");
        var outcome = session.Confirm();
        if (outcome.Closed)
        {
            _valueSession = null;
        }

        return outcome;
    }

    public EditorValueOutcome CancelValue()
    {
        if (_actionSession is { } action)
        {
            return action.CancelValue();
        }

        var session = _valueSession
            ?? throw new InvalidOperationException("no value field is open");
        var outcome = session.Cancel();
        _valueSession = null;
        return outcome;
    }

    /// <summary>Switching or closing a document must not leave a picker holding the old field.</summary>
    public void CloseValueEditor()
    {
        _valueSession = null;
        _actionSession?.CloseValueEditor();
    }

    /// <summary>Writes the page in front to a package of its own, the way the native export did.</summary>
    public static IReadOnlyList<EditorPublicMacroSnapshot> PublicMacros()
    {
        try
        {
            return [.. PublicMacroLibrary.Load(AppPaths.PublicMacroLibrary).Select(entry =>
                new EditorPublicMacroSnapshot(entry.Id, entry.Version,
                    entry.Macro.Name, entry.Macro.Description))];
        }
        catch (Exception)
        {
            return [];
        }
    }

    public EditorSnapshot AddPublicMacro(string id)
    {
        var entry = PublicMacroLibrary.Load(AppPaths.PublicMacroLibrary)
            .FirstOrDefault(candidate => string.Equals(candidate.Id, id, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("the public library has no macro " + id);
        var macro = MacroItem.FromJson(entry.Macro.ToSubMacroJson());
        var taken = _model.SubMacros.Select(candidate => candidate.Name)
            .Concat(_workspace.Macros.Select(candidate => candidate.Name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var stem = macro.Name.Trim();
        var name = stem;
        for (var index = 2; taken.Contains(name); index++)
        {
            name = stem + " " + index.ToString(CultureInfo.InvariantCulture);
        }

        macro.Name = name;
        _model.SubMacros.Add(macro);

        var call = new MacroStep
        {
            Type = "control.runMacro",
            Meta = StepMeta.New,
            Parameters =
            [
                new StepParameter
                {
                    Name = "macro",
                    Kind = ActionParameterKind.Macro,
                    Written = Formula.Text(name),
                },
            ],
        };

        if (macro.Parameters.Count > 0)
        {
            call.Parameters.Add(new StepParameter
            {
                Name = FieldNames.Arguments,
                Kind = ActionParameterKind.Arguments,
                Rows =
                [
                    .. macro.Parameters.Select(parameter => new StepParameterRow
                    {
                        Cells = new Dictionary<string, ExpressionNode>(StringComparer.OrdinalIgnoreCase)
                        {
                            [FieldNames.Name] = Formula.Text(parameter.Name),
                            [FieldNames.Value] = Formula.Text(string.Empty),
                        },
                    }),
                ],
            });
        }

        if (macro.Results.Count > 0)
        {
            call.Parameters.Add(new StepParameter
            {
                Name = "returns",
                Kind = ActionParameterKind.Returns,
                Rows =
                [
                    .. macro.Results.Select(result => new StepParameterRow
                    {
                        Cells = new Dictionary<string, ExpressionNode>(StringComparer.OrdinalIgnoreCase)
                        {
                            [FieldNames.Key] = Formula.Text(result.Name),
                            [FieldNames.Name] = Formula.Text(result.Name),
                        },
                    }),
                ],
            });
        }

        _model.Steps.Add(call);
        _model.SettleStepIds();
        _model.IsDirty = true;
        return Snapshot();
    }

    public bool Export(string path)
    {
        if (path.Trim().Length == 0)
        {
            throw new InvalidOperationException("an export needs a file name");
        }

        var draft = _model.IsSubMacro
            ? new MacroDraft(_model.BuildMacro(), [], [])
            : _model.Draft();
        var triggers = new ProjectTriggers();
        foreach (var entry in draft.Entries)
        {
            triggers.Entries.Add(entry.Copy());
        }

        foreach (var layout in draft.Layouts)
        {
            triggers.InterfaceLayouts.Add(layout);
        }

        var assets = _workspace.CurrentPath is { Length: > 0 } current
            ? MacroPackage.AssetFolderFor(current)
            : null;
        MacroPackage.Save(path, [draft.Macro],
            MacroBundle.Take(triggers, [draft.Macro]),
            MacroBundle.UsedGlobals([draft.Macro], _workspace.Variables.Globals),
            AppVersion.Text, assets);
        return true;
    }

    public bool Save()
    {
        if (!CanSave)
        {
            throw new InvalidOperationException(SaveProblem is { Length: > 0 } problem
                ? problem
                : Strings.Get("Editor.Problem.CannotSave"));
        }

        var draft = _model.Draft();
        if (_owner is not null)
        {
            _owner.ReplaceSubmacro(_submacroOriginal, draft.Macro);
            _model.IsDirty = false;
            _existing = draft.Macro;
            return true;
        }

        var commit = new ProjectMacroCommit(_workspace, _existing, MacroView.All,
            _ => { }, () => { }, () => { });
        commit.Apply(draft, []);
        CommitGlobals();
        _model.IsDirty = false;
        _existing = _workspace.Macros.FirstOrDefault(candidate => candidate.Name == draft.Macro.Name);
        return _existing is not null;
    }

    /// <summary>What the page will draw: the macro's fields and its step tree as the engine reads it.</summary>
    public EditorSnapshot Snapshot()
    {
        _model.SettleStepIds();
        var macro = _model.Draft().Macro;
        var problems = ProblemByStep();
        return new EditorSnapshot(
            Name,
            Description,
            Group,
            SubMacros,
            CallableParameters,
            CallableResults,
            FollowInputLimit,
            InputLimit,
            ToggleKey,
            IsDirty,
            Count(macro.Steps),
            [.. macro.Steps.Select(step => StepSnapshot(step, problems))],
            Triggers,
            Variables,
            Globals,
            ValueChoices,
            ValueFunctions,
            ConditionCompares,
            VariableTypes,
            TriggerKindOptions,
            CanUndo,
            CanRedo,
            CanSave,
            NameProblem,
            ProblemSummary,
            DeclarationProblemSummary,
            CallImpactSummary,
            Notice,
            TriggerProblemSummary,
            ToggleKeyProblem,
            Recording,
            IsSubMacro,
            Revision: ++_revision);
    }

    /// <summary>The design-time check grouped by the step it belongs to, both messages when one
    /// step has more than one.</summary>
    private Dictionary<MacroStep, string> ProblemByStep()
    {
        var byStep = new Dictionary<MacroStep, string>();
        foreach (var problem in _model.Problems)
        {
            byStep[problem.Step] = byStep.TryGetValue(problem.Step, out var seen)
                ? seen + "\n" + problem.Message
                : problem.Message;
        }

        return byStep;
    }

    /// <summary>The runnable actions, in catalogue order, with the grouping the palette draws.</summary>
    public static IReadOnlyList<EditorActionSnapshot> Actions()
        => [.. ActionCatalog.RunnableActions.Select(ActionSnapshot)];

    /// <summary>The category rail's order and labels, so the page never owns a second copy.</summary>
    public static IReadOnlyList<EditorActionCategorySnapshot> ActionCategories()
        => [.. ActionCategoryPresentation.BasicCategories().Select(choice =>
            new EditorActionCategorySnapshot(choice.Value?.ToString(), choice.Label, choice.Short))];

    private static EditorActionSnapshot ActionSnapshot(ActionDefinition action)
    {
        var category = ActionCategoryPresentation.PickerCategory(action.Category);
        var categoryLabel = Strings.Get($"Add.Category.{category}", category.ToString());
        var categoryShort = Strings.Get($"Add.Category.{category}.short", categoryLabel);
        string group;
        string groupLabel;
        int groupRank;
        if (category == ActionCategory.Control)
        {
            group = ActionCategoryPresentation.FlowGroup(action.Key);
            groupLabel = Strings.Get($"Palette.Flow.{group}", group);
            groupRank = ActionCategoryPresentation.FlowGroupRank(group);
        }
        else
        {
            group = action.Group;
            groupLabel = group.Length == 0
                ? Strings.Get("Palette.Group.Other")
                : Strings.Get($"Palette.Group.{group}", group);
            groupRank = int.MaxValue;
            var order = ActionCategoryPresentation.BasicGroups(category);
            for (var index = 0; index < order.Count; index++)
            {
                if (string.Equals(order[index], group, StringComparison.Ordinal))
                {
                    groupRank = index;
                    break;
                }
            }
        }

        return new EditorActionSnapshot(action.Key, action.LocalName, category.ToString(),
            categoryLabel, categoryShort, group, groupLabel, groupRank, action.LocalDescription,
            [.. action.Parameters
                .Where(parameter => parameter.Kind != ActionParameterKind.Steps)
                .Select(parameter => new EditorActionParameterSnapshot(
                    parameter.Name,
                    parameter.LocalLabel,
                    parameter.Kind.ToString(),
                    parameter.Required,
                    parameter.Placeholder,
                    [.. parameter.OptionChoices.Select(choice => new EditorChoiceSnapshot(
                        choice.Value, choice.Display))]))]);
    }

    private static int Count(IReadOnlyList<MacroStep> steps)
        => steps.Count + steps.Sum(step => step.Parameters
            .Where(parameter => parameter.Kind == ActionParameterKind.Steps)
            .Sum(parameter => Count(parameter.Steps)));

    private EditorConditionSnapshot ConditionSnapshot(StepParameter parameter)
    {
        ConditionDraftNode root;
        if (_conditionDrafts.TryGetValue(parameter, out var draft))
        {
            root = draft.Root;
        }
        else
        {
            if (!Condition.TryRead(parameter.Written, out var shape))
            {
                return new EditorConditionSnapshot("All", [], false);
            }

            root = ConditionDraftNode.Root(shape);
        }

        return new EditorConditionSnapshot(
            root.Join.ToString(),
            [.. root.Children.Select(NodeSnapshot)],
            true);
    }

    private static EditorConditionNodeSnapshot NodeSnapshot(ConditionDraftNode node)
        => node.IsGroup
            ? new EditorConditionNodeSnapshot("group", node.Join.ToString(), string.Empty,
                string.Empty, string.Empty, [.. node.Children.Select(NodeSnapshot)])
            : new EditorConditionNodeSnapshot("row", string.Empty,
                Expression.Render(node.Left), node.Compare.ToString(),
                Expression.Render(node.Right), []);

    private static MacroStep NewStep(string type)
    {
        var definition = ActionCatalog.Definitions.FirstOrDefault(action => action.Key == type)
            ?? throw new InvalidOperationException("unknown action " + type);
        var step = new MacroStep { Type = type };
        foreach (var field in definition.Parameters)
        {
            step.Parameters.Add(new StepParameter
            {
                Name = field.Name,
                Kind = field.Kind,
                Written = field.Kind == ActionParameterKind.Steps
                    ? Formula.Nothing
                    : WrittenFor(field.Kind, field.DefaultValue),
            });
        }

        return step;
    }

    private static ExpressionNode WrittenFor(ActionParameterKind kind, string value)
        => kind switch
        {
            ActionParameterKind.Bool or ActionParameterKind.Choice => Formula.Constant(value),
            ActionParameterKind.Number
                when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture,
                    out var number) => Formula.Number(value, number),
            _ => Formula.Text(value),
        };

    private EditorStepSnapshot StepSnapshot(MacroStep step, IReadOnlyDictionary<MacroStep, string> problems)
    {
        var definition = ActionCatalog.Definitions.FirstOrDefault(action => action.Key == step.Type);
        var parameters = step.Parameters
            .Where(parameter => parameter.Kind != ActionParameterKind.Steps)
            .Select(parameter =>
            {
                var field = definition?.Parameters.FirstOrDefault(candidate =>
                    candidate.Name == parameter.Name);
                var columns = field?.Semantics.Columns ?? [];
                var parts = parameter.Written is ConcatNode run
                    ? run.Parts.Select(part => part switch
                    {
                        TextNode text => new EditorValuePartSnapshot("text", text.Text, string.Empty),
                        VariableNode name => new EditorValuePartSnapshot("name", string.Empty, name.Name),
                        _ => new EditorValuePartSnapshot("text", Expression.Render(part), string.Empty),
                    }).ToList()
                    : parameter.Written switch
                    {
                        TextNode text => [new EditorValuePartSnapshot("text", text.Text, string.Empty)],
                        VariableNode name => [new EditorValuePartSnapshot("name", string.Empty, name.Name)],
                        _ => new List<EditorValuePartSnapshot>(),
                    };
                var isNumber = field?.Kind is ActionParameterKind.Number;
                var isMultiline = field?.Kind is ActionParameterKind.MultilineText
                    or ActionParameterKind.Code;
                return new EditorParameterSnapshot(
                    parameter.Name,
                    field?.LocalLabel ?? parameter.Name,
                    parameter.Kind.ToString(),
                    parameter.Value,
                    [.. field?.OptionChoices.Select(choice => new EditorChoiceSnapshot(
                        choice.Value, choice.Display)) ?? []],
                    [.. columns.Select(column => new EditorColumnSnapshot(column.Name, column.Required))],
                    [.. parameter.Rows.Select(row => new EditorRowSnapshot(
                        [.. columns.Select(column => row.Text(column.Name))]))],
                    parameter.Kind == ActionParameterKind.Condition
                        ? ConditionSnapshot(parameter)
                        : null,
                    parameter.Value,
                    field?.ValueForm.ToString() ?? "Value",
                    field is not null && field.Kind is not (ActionParameterKind.Bool
                        or ActionParameterKind.Choice or ActionParameterKind.Step
                        or ActionParameterKind.Steps)
                        && field.Unit != ActionUnit.Percent,
                    field?.AcceptsFormula == true
                        || field?.Kind is ActionParameterKind.Expression or ActionParameterKind.Number,
                    field?.NamesVariable == true,
                    isNumber,
                    isNumber && parameter.Written is not NumberNode,
                    isMultiline,
                    field?.Picker is ActionPickerKind.Color,
                    field?.PathIntent is not PathIntent.None,
                    field?.Kind is ActionParameterKind.Key or ActionParameterKind.Keys,
                    field?.Placeholder ?? string.Empty,
                    field?.Required == true,
                    parts,
                    field?.Advanced == true,
                    field?.Group ?? string.Empty,
                    field is { Group.Length: > 0 } grouped
                        ? Strings.Get($"Palette.Group.{grouped.Group}")
                        : string.Empty,
                    field?.Kind == ActionParameterKind.Returns);
            })
            .ToList();
        var sections = step.StepLists
            .Select(list => new EditorStepSectionSnapshot(list.Name, step.ParameterLabel(list),
                [.. list.Steps.Select(child => StepSnapshot(child, problems))]))
            .ToList();
        var acceptsSteps = definition?.Parameters.Any(parameter =>
            parameter.Kind == ActionParameterKind.Steps) == true;
        return new EditorStepSnapshot(step.Id, step.Type,
            definition?.LocalName ?? step.Type, step.Meta.IsEnabled, step.Meta.Comment,
            step.Meta.TimeoutMs, step.Meta.RetryCount, step.Meta.RetryDelayMs,
            step.Meta.OnError.ToString(), acceptsSteps, step.Detail,
            problems.GetValueOrDefault(step) ?? string.Empty, step.InputMark, step.MetaSummary,
            parameters, sections);
    }

    private List<MacroStep> FindSteps(IReadOnlyList<string> stepIds)
    {
        var steps = new List<MacroStep>();
        foreach (var id in stepIds)
        {
            if (FindStep(id) is { } step && !steps.Contains(step))
            {
                steps.Add(step);
            }
        }

        return steps;
    }

    private static string? ClipboardText;

    private MacroStep? FindTreeStep(string stepId)
        => Find(stepId) is { } found ? found.List[found.Index] : null;

    private MacroStep? FindStep(string stepId)
        => Find(stepId) is { } found ? found.List[found.Index] : null;

    private (IList<MacroStep> List, int Index)? Find(string stepId)
    {
        foreach (var (list, index) in Walk(_model.Steps))
        {
            if (string.Equals(list[index].Id, stepId, StringComparison.Ordinal))
            {
                return (list, index);
            }
        }

        return null;
    }

    private static IEnumerable<(IList<MacroStep> List, int Index)> Walk(IList<MacroStep> steps)
    {
        for (var index = 0; index < steps.Count; index++)
        {
            yield return (steps, index);
            foreach (var children in steps[index].Parameters
                         .Where(parameter => parameter.Kind == ActionParameterKind.Steps)
                         .SelectMany(parameter => Walk(parameter.Steps)))
            {
                yield return children;
            }
        }
    }
}
