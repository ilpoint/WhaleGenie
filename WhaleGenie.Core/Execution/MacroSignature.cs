using System;
using System.Collections.Generic;
using System.Linq;

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
}

/// <summary>
/// What a call writes the values it passes under. The called macro's argument names are its own,
/// so they go under a prefix instead of standing beside the call's own settings: a caller that
/// happens to have a value called <c>macro</c> or <c>returns</c> cannot then write over the name of
/// the macro being called or the list of what it asks to have back.
/// </summary>
public static class MacroCallFields
{
    /// <summary>What the parameter holding the value passed for one argument starts with.</summary>
    public const string ArgumentPrefix = "arg.";

    /// <summary>The call's parameter for one argument of the called macro.</summary>
    public static string Argument(string name) => ArgumentPrefix + name;
}
