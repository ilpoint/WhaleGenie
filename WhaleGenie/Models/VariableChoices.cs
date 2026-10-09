using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;

namespace WhaleGenie.Models;

/// <summary>
/// One variable a picker can offer. A name on its own does not say which variable a person means —
/// two steps both want to call their result "rows" — so a row carries what choosing one takes: what
/// it is called, where it comes from, what it holds, and which step left it behind.
/// </summary>
public sealed class VariableChoice
{
    /// <summary>The name as it is written down, without the dollar sign that reads it.</summary>
    public required string Name { get; init; }

    public required VariableScope Scope { get; init; }

    /// <summary>What it holds: text, number, flag, colour, date or list.</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>One line saying what it is for.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// The step that makes it, named the way a step is named (remark, action, name), or nothing for
    /// the ones that do not come from a step.
    /// </summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>True for a step's own ending, which a run leaves behind rather than a step naming it.</summary>
    public bool IsStepResult { get; init; }

    /// <summary>What a field holds once this one is chosen.</summary>
    public string Token => "$" + Name;

    /// <summary>Which of the picker's buttons shows it.</summary>
    public VariableGroup Group => IsStepResult
        ? VariableGroup.StepResult
        : Scope switch
        {
            VariableScope.System => VariableGroup.System,
            VariableScope.Global => VariableGroup.Global,
            _ => VariableGroup.Local,
        };

    /// <summary>Everything there is to know about it, for the line under the buttons.</summary>
    public string Details
    {
        get
        {
            var line = string.Join(" · ",
                new[] { Token, Type, Strings.Get($"VarWall.Group.{Group}") }
                    .Where(part => part.Length > 0));
            var said = string.Join("\n",
                new[] { Description, Source }.Where(part => part.Length > 0));
            return said.Length > 0 ? line + "\n" + said : line;
        }
    }
}

/// <summary>Which kind of variable a row is, which is also what the picker's buttons filter by.</summary>
public enum VariableGroup
{
    All,

    /// <summary>Made by this macro, visible only inside it.</summary>
    Local,

    /// <summary>Shared by every macro, created in the Variable Center.</summary>
    Global,

    /// <summary>Provided by WhaleGenie itself.</summary>
    System,

    /// <summary>What a step's run came to: ok, failed or skipped.</summary>
    StepResult,
}

/// <summary>
/// The rows a variable picker offers, built from what the editor knows: what WhaleGenie provides,
/// what the Variable Center shares, what this macro makes for itself, and the ending each step
/// leaves behind for a condition to ask about.
/// </summary>
public static class VariableChoices
{
    public static IReadOnlyList<VariableChoice> For(IEnumerable<MacroStep> steps)
    {
        var rows = new List<VariableChoice>();
        Walk(steps, rows);

        IReadOnlyList<VariableChoice> shared =
        [
            .. VariableCatalog.Globals.Select(variable => new VariableChoice
            {
                Name = variable.Name,
                Scope = VariableScope.Global,
                Type = variable.Type,
                Description = variable.Description,
            }),
            .. VariableCatalog.SystemVariables.Select(variable => new VariableChoice
            {
                Name = variable.Name,
                Scope = VariableScope.System,
                Type = variable.Type,
                Description = Strings.Get($"Variable.{variable.Name}.desc", variable.Description),
            }),
        ];

        return Merge(rows, shared);
    }

    /// <summary>
    /// The rows of several lists as one, with a name appearing once and the order the picker reads
    /// them in. The list that comes first wins a name it shares: what a dialog was handed is the
    /// macro's own variables, and a local that goes by a shared name is the one the macro reads.
    /// </summary>
    public static IReadOnlyList<VariableChoice> Merge(params IReadOnlyList<VariableChoice>[] lists)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return
        [
            .. lists
                .SelectMany(list => list)
                .Where(row => seen.Add(row.Name))
                .OrderBy(row => row.Group)
                .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase),
        ];
    }

    /// <summary>
    /// Walks the steps the way the macro is laid out: what a block holds is that block's variables,
    /// and every step that runs leaves an ending behind whether or not anybody asked for one.
    /// </summary>
    private static void Walk(IEnumerable<MacroStep> steps, List<VariableChoice> rows)
    {
        foreach (var step in steps)
        {
            foreach (var parameter in step.Parameters)
            {
                if (parameter.Kind is ActionParameterKind.Steps)
                {
                    Walk(parameter.Steps, rows);
                    continue;
                }

                // A condition reads variables and makes none, so there is nothing to collect there.
                if (parameter.Kind is ActionParameterKind.Condition
                    || !step.DefinesVariable(parameter)
                    || parameter.Value.Trim().Length == 0)
                {
                    continue;
                }

                rows.Add(new VariableChoice
                {
                    Name = parameter.Value.Trim(),
                    Scope = VariableScope.Local,
                    Description = step.Definition?.Parameters
                        .FirstOrDefault(candidate => candidate.Name == parameter.Name)
                        ?.LocalHint ?? string.Empty,
                    Source = step.PickerLabel,
                });
            }

            // A condition is asked by the step it belongs to rather than run on its own, so its
            // ending is nothing anybody can ask about.
            if (!step.IsCondition && step.Id.Length > 0)
            {
                rows.Add(new VariableChoice
                {
                    Name = MacroRunner.OutcomeName(step.Id),
                    Scope = VariableScope.Local,
                    Type = "text",
                    Description = Strings.Get("VarWall.Outcome"),
                    Source = step.PickerLabel,
                    IsStepResult = true,
                });
            }
        }
    }
}
