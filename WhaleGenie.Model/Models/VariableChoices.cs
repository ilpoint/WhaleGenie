using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
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

    /// <summary>The fields a record holds, in catalogue order; empty when the shape is unknown.</summary>
    public IReadOnlyList<ActionReturnField> Fields { get; init; } = [];

    /// <summary>One line saying what it is for.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// The step that makes it, named the way a step is named (remark, action, name), or nothing for
    /// the ones that do not come from a step.
    /// </summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>True for a step's own ending, which a run leaves behind rather than a step naming it.</summary>
    public bool IsStepResult { get; init; }

    /// <summary>Whether this global's current value is kept between program runs.</summary>
    public bool IsPersistent { get; init; }

    /// <summary>Where this name comes from, which decides which editor owns it.</summary>
    public VariableOrigin Origin { get; init; }

    /// <summary>
    /// The steps that read this variable, in the order they run. A name on a button says nothing
    /// about who wants it, and "where is this used" is the question that follows "what is this" —
    /// the answer is what makes it safe to rename or drop one.
    /// </summary>
    public IReadOnlyList<VariableUse> Uses { get; init; } = [];

    /// <summary>What a field holds once this one is chosen.</summary>
    public string Token => "$" + Name;

    internal Func<string?>? ContentReader { get; init; }

    /// <summary>The current value when one can be read, or the reason there is none yet.</summary>
    public string ContentPreview
    {
        get
        {
            var value = ContentReader?.Invoke();
            return string.IsNullOrWhiteSpace(value)
                ? Strings.Get("VarWall.NoRuntimeValue")
                : value;
        }
    }

    /// <summary>Record fields, one line each, read without opening another window.</summary>
    public IReadOnlyList<string> FieldLines =>
    [
        .. Fields.Select(part =>
            $"{part.Name}: {Strings.Get($"Variable.Type.{ActionReturn.TypeNameOf(part.Type)}",
                ActionReturn.TypeNameOf(part.Type))}"),
    ];

    public bool HasFields => FieldLines.Count > 0;

    public string HoverText
    {
        get
        {
            var lines = new List<string>
            {
                Strings.Format("VarWall.ContentLine", ContentPreview),
            };
            if (HasFields)
            {
                lines.Add(Strings.Get("VarWall.Fields"));
                lines.AddRange(FieldLines.Select(part => "• " + part));
            }

            lines.Add(Details);
            return string.Join("\n", lines);
        }
    }

    /// <summary>
    /// How the button names it: the Chinese name with the code name beside it where WhaleGenie gave
    /// it one — 剪贴板 ($sys.clipboard) — and the name a formula writes otherwise. What choosing it
    /// puts in the field is still <see cref="Token"/>; this is only what to read.
    /// </summary>
    public string Display => VariableCatalog.Display(Name, true);

    /// <summary>A short mark on the card when a global value survives a restart.</summary>
    public string PersistenceBadge => IsPersistent ? Strings.Get("VarWall.Persistent") : string.Empty;

    public void RefreshLanguage() => PropertyChanged?.Invoke(this,
        new System.ComponentModel.PropertyChangedEventArgs(string.Empty));

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

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
                new[]
                    {
                        Display,
                        Strings.Get($"Variable.Type.{Type}", Type),
                        Strings.Get($"VarWall.Group.{Group}"),
                    }
                    .Where(part => part.Length > 0));
            var said = string.Join("\n",
                new[] { Description, Source }.Where(part => part.Length > 0));
            return said.Length > 0 ? line + "\n" + said : line;
        }
    }

    internal VariableChoice WithContent(Func<string, VariableScope, string?> content) => new()
    {
        Name = Name,
        Scope = Scope,
        Type = Type,
        Fields = Fields,
        Description = Description,
        Source = Source,
        IsStepResult = IsStepResult,
        IsPersistent = IsPersistent,
        Origin = Origin,
        Uses = Uses,
        ContentReader = () => content(Name, Scope),
    };
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

/// <summary>The declaration or generated source that owns a variable name.</summary>
public enum VariableOrigin
{
    Local,
    Global,
    System,
    Parameter,
    Result,
    StepOutput,
}

/// <summary>
/// The rows a variable picker offers, built from what the editor knows: what WhaleGenie provides,
/// what the Variable Center shares, what this macro makes for itself, and the ending each step
/// leaves behind for a condition to ask about.
/// </summary>
public static class VariableChoices
{
    public static IReadOnlyList<VariableChoice> For(IEnumerable<MacroStep> steps,
        IEnumerable<MacroParameter>? parameters = null, IEnumerable<MacroResult>? results = null,
        Func<string, VariableScope, string?>? content = null)
    {
        var rows = new List<VariableChoice>();
        var uses = new Dictionary<string, List<VariableUse>>(StringComparer.OrdinalIgnoreCase);
        Read(steps, uses);
        AddDeclarations(rows, uses, parameters, results);
        Walk(steps, rows, uses);

        IReadOnlyList<VariableChoice> shared =
        [
            .. VariableCatalog.Globals.Select(variable => new VariableChoice
            {
                Name = variable.Name,
                Scope = VariableScope.Global,
                Type = variable.Type,
                Description = variable.Description,
                IsPersistent = variable.PersistValue,
                Origin = VariableOrigin.Global,
                Uses = UsesOf(uses, variable.Name),
            }),
            .. VariableCatalog.SystemVariables.Select(variable => new VariableChoice
            {
                Name = variable.Name,
                Scope = VariableScope.System,
                Type = variable.Type,
                Description = Strings.Get($"Variable.{variable.Name}.desc", variable.Description),
                Origin = VariableOrigin.System,
                Uses = UsesOf(uses, variable.Name),
            }),
        ];

        var merged = Merge(rows, shared);
        return content is null
            ? merged
            : [.. merged.Select(choice => choice.WithContent(content))];
    }

    /// <summary>
    /// The values a macro's interface declares, added before the rows the steps make so a declared
    /// result wins over a step that happens to write the same name. A value is only offered when
    /// its name is one the macro language can read back; an invalid declaration is the declaration
    /// editor's problem, not something the variable wall should put in front of the user.
    /// </summary>
    private static void AddDeclarations(List<VariableChoice> rows,
        Dictionary<string, List<VariableUse>> uses,
        IEnumerable<MacroParameter>? parameters, IEnumerable<MacroResult>? results)
    {
        foreach (var parameter in parameters ?? [])
        {
            var name = parameter.Name.Trim();
            if (!VariableText.IsName(name))
            {
                continue;
            }

            rows.Add(new VariableChoice
            {
                Name = name,
                Scope = VariableScope.Local,
                Type = ActionReturn.TypeNameOf(parameter.Type),
                Fields = FieldsOf(parameter.Fields),
                Description = parameter.Description,
                Source = Strings.Get("VarWall.Parameter"),
                Origin = VariableOrigin.Parameter,
                Uses = UsesOf(uses, name),
            });

        }

        foreach (var result in results ?? [])
        {
            var name = result.Name.Trim();
            if (!VariableText.IsName(name))
            {
                continue;
            }

            rows.Add(new VariableChoice
            {
                Name = name,
                Scope = VariableScope.Local,
                Type = ActionReturn.TypeNameOf(result.Type),
                Fields = FieldsOf(result.Fields),
                Description = result.Description,
                Source = Strings.Get("VarWall.Result"),
                Origin = VariableOrigin.Result,
                Uses = UsesOf(uses, name),
            });

        }
    }

    /// <summary>
    /// The fields a declared record names, in the shape the wall and the picker draw them from.
    /// </summary>
    private static IReadOnlyList<ActionReturnField> FieldsOf(IReadOnlyList<MacroField> fields)
        => [.. fields.Where(field => field.Name.Trim().Length > 0)
            .Select(field => new ActionReturnField
            {
                Name = field.Name.Trim(),
                Type = field.Type,
            })];

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
                            Fields = result?.Fields ?? [],
                            Description = result?.LocalHint ?? string.Empty,
                            Source = step.PickerLabel,
                            Origin = VariableOrigin.Local,
                            Uses = UsesOf(uses, name),
                        });
                    }

                    continue;
                }

                if (!step.DefinesVariable(parameter)
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
                    Origin = VariableOrigin.Local,
                    Uses = UsesOf(uses, parameter.Value.Trim()),
                });
            }

            if (step.Id.Length > 0)
            {
                rows.Add(new VariableChoice
                {
                    Name = MacroRunner.OutcomeName(step.Id),
                    Scope = VariableScope.Local,
                    Type = "text",
                    Description = Strings.Get("VarWall.Outcome"),
                    Source = step.PickerLabel,
                    IsStepResult = true,
                    Origin = VariableOrigin.StepOutput,
                    Uses = UsesOf(uses, MacroRunner.OutcomeName(step.Id)),
                });
            }
        }
    }
}
