using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;

namespace WhaleGenie.Models;

/// <summary>
/// What one macro asks of a call to it, read once. The call form, the MCP signature tool and the
/// design-time check all describe the same declaration, and reading it three different ways is how
/// one of them ends up saying something the others do not — a default that is shown in one place
/// and missing in another, a required argument that only one of them enforces.
/// </summary>
/// <remarks>
/// This cannot live in <c>WhaleGenie.Core</c> beside <see cref="MacroSignature"/>: what a call form
/// needs is an <see cref="ActionReturn"/>, and the engine does not know the interface's types. The
/// engine reads the same declaration through <see cref="MacroSignature"/>, which is the shape a
/// run needs; this is the shape a person writing or asking about a call needs.
/// </remarks>
public static class CallableSignature
{
    /// <summary>
    /// The arguments a call has to think about, in the order the macro declares them. A declaration
    /// row with no name is left out: there is nothing to fill in and nothing to name it by, and the
    /// macro's own declaration problems already say so.
    /// </summary>
    public static IReadOnlyList<CalledArgument> Arguments(MacroCallable call)
        => [.. call.Parameters.Where(parameter => parameter.Name.Trim().Length > 0)
            .Select(CalledArgument.Of)];

    /// <summary>The values the macro offers to hand back, in the order it declares them.</summary>
    public static IReadOnlyList<CalledResult> Results(MacroCallable call)
        => [.. call.Results.Where(result => result.Name.Trim().Length > 0).Select(CalledResult.Of)];

    /// <summary>One declared argument by name, or null when the macro declares none by that name.</summary>
    public static CalledArgument? Argument(MacroCallable call, string name)
        => Arguments(call).FirstOrDefault(argument =>
            string.Equals(argument.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
}

/// <summary>One value a macro asks a call to fill in, as the call site and the tools describe it.</summary>
public sealed record CalledArgument(
    string Name, ActionReturnType Type, bool Required, string Default, string Description)
{
    /// <summary>The same, read off a declaration row.</summary>
    public static CalledArgument Of(MacroParameter parameter)
        => new(parameter.Name.Trim(), parameter.Type, parameter.Required, parameter.Default,
            parameter.Description);

    /// <summary>
    /// What the call form says under the row: the macro's own words about the value, what kind it
    /// asks for, and whether the call has to fill it in or may leave it to the default. The default
    /// is named rather than written into the row — it belongs to the declaration, and copying it into
    /// every call is what made a changed default invisible to calls already written.
    /// </summary>
    public string Hint
    {
        get
        {
            var parts = new List<string>();
            if (Description.Length > 0)
            {
                parts.Add(Description);
            }

            parts.Add(Strings.Format("Add.CallArgumentType",
                Strings.Get("ActionType." + Type, Type.ToString())));
            if (Required)
            {
                parts.Add(Strings.Get("Add.CallArgumentRequired"));
            }
            else if (Default.Trim().Length > 0)
            {
                parts.Add(Strings.Format("Add.CallArgumentDefault", Default));
            }

            return string.Join(" ", parts);
        }
    }
}

/// <summary>One value a macro offers to hand back, as the results page and the tools describe it.</summary>
public sealed record CalledResult(string Name, ActionReturnType Type, string Description)
{
    /// <summary>The same, read off a declaration row.</summary>
    public static CalledResult Of(MacroResult result)
        => new(result.Name.Trim(), result.Type, result.Description);

    /// <summary>
    /// What the results page says under the name: the macro's own words and what kind of value it
    /// hands back, so a record is not mistaken for a line of text at the call site.
    /// </summary>
    public string Hint
    {
        get
        {
            var kind = Strings.Format("Add.CallArgumentType",
                Strings.Get("ActionType." + Type, Type.ToString()));
            return Description.Length == 0 ? kind : Description + " " + kind;
        }
    }
}
