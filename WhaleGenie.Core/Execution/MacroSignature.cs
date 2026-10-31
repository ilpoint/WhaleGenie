using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// One value a macro asks a call to fill in. The name is what the called macro reads, which is why
/// a call that names something the macro never declared is a mistake rather than a value nobody
/// happens to read.
/// </summary>
public sealed record MacroArgument(string Name, string Default, bool Required,
    ActionReturnType Type = ActionReturnType.Text);

/// <summary>One value a macro offers to hand back: the name and what kind of value it is.</summary>
public sealed record MacroReturn(string Name, ActionReturnType Type = ActionReturnType.Text);

/// <summary>
/// One value supplied to a direct run, the way a call supplies an argument. <paramref name="Given"/>
/// is the explicit-empty marker: a field left empty with it false means "use the declaration".
/// </summary>
public sealed record MacroStartArgument(string Name, string Text, bool Given);

/// <summary>
/// What a macro asks of a call to it: the values that have to be filled in, and the names it may
/// hand back. A macro that declares neither is called the way every call used to work, so a macro
/// written before this existed still runs.
/// </summary>
public sealed record MacroSignature(
    IReadOnlyList<MacroArgument> Arguments, IReadOnlyList<MacroReturn> Results)
{
    /// <summary>What a macro that declares nothing asks for.</summary>
    public static MacroSignature Empty { get; } = new([], []);

    /// <summary>The declared argument of this name, or null when there is none.</summary>
    public MacroArgument? Argument(string name)
        => Arguments.FirstOrDefault(argument =>
            string.Equals(argument.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>True when the macro offers to hand back a value of this name.</summary>
    public bool Offers(string name) => Result(name) is not null;

    /// <summary>The declared result of this name, or null when the macro offers none by it.</summary>
    public MacroReturn? Result(string name)
        => Results.FirstOrDefault(result =>
            string.Equals(result.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Every name that is empty, unreadable as a variable, repeated, or one the run keeps for
    /// itself. A call that passed a value under a duplicate name would have one of the two
    /// silently overwritten, and a parameter named like a built-in would read as the built-in
    /// inside the called macro. A name the macro language cannot read is just as unusable: a
    /// caller has no way to write it, so <c>$name</c> never works inside the called macro.
    /// </summary>
    public IReadOnlyList<(MacroSignatureFault Fault, string Name)> Problems()
    {
        var problems = new List<(MacroSignatureFault, string)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Collect(Arguments.Select(argument => argument.Name), seen, problems);
        seen.Clear();
        Collect(Results.Select(result => result.Name), seen, problems);

        // A required argument with a default has two answers to "what if the call leaves it
        // out": the declaration says the call must fill it in, and the default says it need
        // not. One of the two would silently win, so the declaration is refused instead.
        foreach (var argument in Arguments)
        {
            var name = argument.Name.Trim();
            if (name.Length > 0 && argument.Required && argument.Default.Trim().Length > 0)
            {
                problems.Add((MacroSignatureFault.RequiredWithDefault, name));
            }
        }

        return problems;
    }

    /// <summary>The first of <see cref="Problems"/>, which is what a run reports before it starts.</summary>
    public (MacroSignatureFault Fault, string Name)? Problem()
    {
        var problems = Problems();
        return problems.Count == 0 ? null : problems[0];
    }

    private static void Collect(IEnumerable<string> names, HashSet<string> seen,
        List<(MacroSignatureFault Fault, string Name)> problems)
    {
        foreach (var written in names)
        {
            var name = written.Trim();
            if (name.Length == 0)
            {
                problems.Add((MacroSignatureFault.EmptyName, written));
                continue;
            }

            if (!VariableText.IsName(name))
            {
                problems.Add((MacroSignatureFault.InvalidName, name));
                continue;
            }

            if (!seen.Add(name))
            {
                problems.Add((MacroSignatureFault.DuplicateName, name));
                continue;
            }

            if (Reserved(name))
            {
                problems.Add((MacroSignatureFault.ReservedName, name));
            }
        }
    }

    private static bool Reserved(string name)
        => name.StartsWith("sys.", StringComparison.OrdinalIgnoreCase)
           || MacroRunner.IsRunStepName(name);
}

/// <summary>What is wrong with a macro's declared names.</summary>
public enum MacroSignatureFault
{
    EmptyName,

    /// <summary>The name cannot be read as a variable: a space, a leading digit, or similar.</summary>
    InvalidName,

    DuplicateName,
    ReservedName,

    /// <summary>A required argument also has a default, so omitting it has two answers.</summary>
    RequiredWithDefault,
}
