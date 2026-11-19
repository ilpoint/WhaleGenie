using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Models;

namespace WhaleGenie.Tests;

/// <summary>
/// Variables to hand a dialog in a check. A check is about what the dialog does with them rather
/// than about where they came from, so they are the macro's own, known by name alone.
/// </summary>
internal static class VariableChoicesForChecks
{
    public static IReadOnlyList<VariableChoice> Named(params string[] names)
        => [.. names.Select(name => new VariableChoice { Name = name, Scope = VariableScope.Local })];
}
