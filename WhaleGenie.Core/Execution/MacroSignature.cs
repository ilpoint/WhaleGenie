using System;
using System.Collections.Generic;
using System.Linq;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// One value a macro asks a call to fill in. The name is what the called macro reads, which is why
/// a call that names something the macro never declared is a mistake rather than a value nobody
/// happens to read.
/// </summary>
public sealed record MacroArgument(string Name, string Default, bool Required);

/// <summary>
/// What a macro asks of a call to it: the values that have to be filled in, and the names it may
/// hand back. A macro that declares neither is called the way every call used to work, so a macro
/// written before this existed still runs.
/// </summary>
public sealed record MacroSignature(
    IReadOnlyList<MacroArgument> Arguments, IReadOnlyList<string> Results)
{
    /// <summary>What a macro that declares nothing asks for.</summary>
    public static MacroSignature Empty { get; } = new([], []);

    /// <summary>The declared argument of this name, or null when there is none.</summary>
    public MacroArgument? Argument(string name)
        => Arguments.FirstOrDefault(argument =>
            string.Equals(argument.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>True when the macro offers to hand back a value of this name.</summary>
    public bool Offers(string name)
        => Results.Any(result =>
            string.Equals(result.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));
}
