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

/// <summary>What the page draws: the snapshot and the step, condition and action shapes it is made of.</summary>

internal sealed partial class HeadlessEditorSession
{
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
