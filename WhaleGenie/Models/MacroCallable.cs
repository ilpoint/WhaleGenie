using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Execution;

namespace WhaleGenie.Models;

/// <summary>
/// One value a macro asks whoever calls it to fill in. A macro that expects a count and a name says
/// so here, and every call to it is then a form of exactly those fields rather than a list of
/// <c>NAME=value</c> lines nobody checks.
/// </summary>
public sealed class MacroParameter
{
    /// <summary>The name the called macro reads this value by.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>What is used when the call leaves this value out.</summary>
    public string Default { get; set; } = string.Empty;

    /// <summary>True when a call has to fill this value in.</summary>
    public bool Required { get; set; } = true;

    /// <summary>What the value is for, shown under the field at the call site.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>What kind of value the call is asked for, so a mismatch can be seen before the run.</summary>
    public ActionReturnType Type { get; set; } = ActionReturnType.Text;
}

/// <summary>
/// One name a macro offers to hand back to whoever called it. A call may ask for any of these; a
/// name the macro does not offer is a mistake at the call site rather than a value that quietly
/// arrives empty, which is what a free list of names could not tell apart.
/// </summary>
public sealed class MacroResult
{
    /// <summary>The name the value goes by inside the called macro.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>What the value is, shown beside the box at the call site.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>What kind of value it is, so the variable pickers can say what a name is good for.</summary>
    public ActionReturnType Type { get; set; } = ActionReturnType.Text;
}

/// <summary>
/// What one macro in the project asks of a call to it, in the shape the call site and the
/// design-time check work with.
/// </summary>
public sealed record MacroCallable(
    string Name, IReadOnlyList<MacroParameter> Parameters, IReadOnlyList<MacroResult> Results)
{
    /// <summary>The same, in the shape the engine calls macros by.</summary>
    public MacroSignature Signature => new(
        [.. Parameters
            .Where(parameter => parameter.Name.Trim().Length > 0)
            .Select(parameter => new MacroArgument(parameter.Name.Trim(), parameter.Default,
                parameter.Required, parameter.Type))],
        [.. Results
            .Where(result => result.Name.Trim().Length > 0)
            .Select(result => new MacroReturn(result.Name.Trim(), result.Type))]);

    /// <summary>The declared parameter of this name, or null when the macro declares none.</summary>
    public MacroParameter? Parameter(string name)
        => Parameters.FirstOrDefault(parameter =>
            string.Equals(parameter.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>True when the macro offers to hand back a value of this name.</summary>
    public bool Offers(string name)
        => Results.Any(result =>
            string.Equals(result.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));
}
