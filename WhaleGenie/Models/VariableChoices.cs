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

    /// <summary>
    /// The steps that read this variable, in the order they run. A name on a button says nothing
    /// about who wants it, and "where is this used" is the question that follows "what is this" —
    /// the answer is what makes it safe to rename or drop one.
    /// </summary>
    public IReadOnlyList<VariableUse> Uses { get; init; } = [];

    /// <summary>What a field holds once this one is chosen.</summary>
    public string Token => "$" + Name;

    /// <summary>
    /// How the button names it: the Chinese name with the code name beside it where WhaleGenie gave
    /// it one — 剪贴板 ($sys.clipboard) — and the name a formula writes otherwise. What choosing it
    /// puts in the field is still <see cref="Token"/>; this is only what to read.
    /// </summary>
    public string Display => VariableCatalog.Display(Name, true);

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
                new[] { Display, Type, Strings.Get($"VarWall.Group.{Group}") }
                    .Where(part => part.Length > 0));
            var said = string.Join("\n",
                new[] { Description, Source }.Where(part => part.Length > 0));
            return said.Length > 0 ? line + "\n" + said : line;
        }
    }
}

/// <summary>One step that reads a variable, named the way a step names itself on the list.</summary>
public sealed record VariableUse(MacroStep Step, string Label);

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
        var uses = new Dictionary<string, List<VariableUse>>(StringComparer.OrdinalIgnoreCase);
        Read(steps, uses);
        Walk(steps, rows, uses);

        IReadOnlyList<VariableChoice> shared =
        [
            .. VariableCatalog.Globals.Select(variable => new VariableChoice
            {
                Name = variable.Name,
                Scope = VariableScope.Global,
                Type = variable.Type,
                Description = variable.Description,
                Uses = UsesOf(uses, variable.Name),
            }),
            .. VariableCatalog.SystemVariables.Select(variable => new VariableChoice
            {
                Name = variable.Name,
                Scope = VariableScope.System,
                Type = variable.Type,
                Description = Strings.Get($"Variable.{variable.Name}.desc", variable.Description),
                Uses = UsesOf(uses, variable.Name),
            }),
        ];

        return Merge(rows, shared);
    }

    private static IReadOnlyList<VariableUse> UsesOf(
        Dictionary<string, List<VariableUse>> uses, string name)
        => uses.TryGetValue(name, out var found) ? found : [];

    /// <summary>
    /// Walks the steps again, this time looking for the names they read rather than the ones they
    /// make. A name is read wherever a field may hold one: a whole value that is an expression, or
    /// text with a name written into the middle of it. What a field that names a variable *defines*
    /// is not a read, and neither is a script's own `$` — that one belongs to the other language.
    /// </summary>
    private static void Read(IEnumerable<MacroStep> steps, Dictionary<string, List<VariableUse>> uses)
    {
        foreach (var step in steps)
        {
            var use = new VariableUse(step, step.PickerLabel);
            var reads = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var parameter in step.Parameters)
            {
                switch (parameter.Kind)
                {
                    case ActionParameterKind.Steps:
                        Read(parameter.Steps, uses);
                        continue;
                    case ActionParameterKind.Condition:
                        if (parameter.Condition is { } condition)
                        {
                            Read([condition], uses);
                        }

                        continue;
                }

                if (step.DefinesVariable(parameter)
                    || step.ParameterDefinition(parameter) is { ForeignText: true })
                {
                    continue;
                }

                // The design-time check and the picker answer "which names does this field read"
                // from the same place, so the two cannot drift apart with the read mode.
                foreach (var name in MacroCheck.ReadsOf(step, parameter))
                {
                    reads.Add(name);
                }
            }

            foreach (var name in reads)
            {
                if (!uses.TryGetValue(name, out var found))
                {
                    found = [];
                    uses[name] = found;
                }

                found.Add(use);
            }
        }
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
    private static void Walk(IEnumerable<MacroStep> steps, List<VariableChoice> rows,
        Dictionary<string, List<VariableUse>> uses)
    {
        foreach (var step in steps)
        {
            foreach (var parameter in step.Parameters)
            {
                if (parameter.Kind is ActionParameterKind.Steps)
                {
                    Walk(parameter.Steps, rows, uses);
                    continue;
                }

                // A step that hands results over names each of them itself, so a row is made for
                // every one that was ticked: what it is called, what it holds, and where it came
                // from. Nothing ticked means the step leaves no result behind.
                if (parameter.Kind is ActionParameterKind.Returns)
                {
                    foreach (var (key, name) in step.ReturnedResults(parameter))
                    {
                        var result = step.Definition?.Returns.FirstOrDefault(
                            candidate => string.Equals(candidate.Key, key,
                                StringComparison.OrdinalIgnoreCase));
                        rows.Add(new VariableChoice
                        {
                            Name = name,
                            Scope = VariableScope.Local,
                            Type = result?.TypeName ?? "text",
                            Description = result?.LocalHint ?? string.Empty,
                            Source = step.PickerLabel,
                            Uses = UsesOf(uses, name),
                        });
                    }

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
                    Uses = UsesOf(uses, parameter.Value.Trim()),
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
                    Uses = UsesOf(uses, MacroRunner.OutcomeName(step.Id)),
                });

                // Why it failed sits beside how it ended, under the same step: a macro that treats
                // one failure differently from another asks this one.
                rows.Add(new VariableChoice
                {
                    Name = MacroRunner.ErrorName(step.Id),
                    Scope = VariableScope.Local,
                    Type = "text",
                    Description = Strings.Get("VarWall.Error"),
                    Source = step.PickerLabel,
                    IsStepResult = true,
                    Uses = UsesOf(uses, MacroRunner.ErrorName(step.Id)),
                });
            }
        }
    }
}
