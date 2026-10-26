using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Execution;
using WhaleGenie.Models;

namespace WhaleGenie.Execution;

/// <summary>
/// The macros a run may call, gathered from the project's list — including the submacros nested in
/// each one. The engine only knows names, so a submacro has to arrive here under a name a call can
/// write: its own when that name is unique, and "父宏/子宏" always.
/// </summary>
/// <remarks>
/// A submacro is a separate document inside the parent's package, and until now it was outside every
/// library a run was given and outside every list the editor offered: a macro that called one failed
/// with <c>Run.MacroNotFound</c> however it was started, and the picker beside a call step could not
/// name one at all. One place now decides what a submacro is called, so the picker, the check and
/// the run agree.
/// </remarks>
public static class CallableMacros
{
    /// <summary>
    /// Every macro and submacro of a project, ready for <see cref="ProjectMacroLibrary"/>, in the
    /// order they are listed and then in the order their parents hold them.
    /// </summary>
    public static IReadOnlyList<(string Name, IReadOnlyList<ExecutableStep> Steps,
        MacroSignature Signature, bool ExclusiveInput)> Entries(IEnumerable<MacroItem> macros)
        =>
        [
            .. Pieces(macros).Select(piece => (piece.Name, piece.Macro.Steps.ToExecutable(),
                piece.Macro.Callable.Signature, piece.Macro.ExclusiveInput)),
        ];

    /// <summary>
    /// The same names as <see cref="Entries"/>, as callables: what an editor offers beside a call
    /// step and checks a written name against.
    /// </summary>
    public static IReadOnlyList<MacroCallable> Callables(IEnumerable<MacroItem> macros)
        =>
        [
            .. Pieces(macros).Select(piece =>
                new MacroCallable(piece.Name, piece.Macro.Parameters, piece.Macro.Results)),
        ];

    /// <summary>One macro, then every submacro it holds, with the path that names each one.</summary>
    private static IEnumerable<(string Name, MacroItem Macro)> Pieces(IEnumerable<MacroItem> macros)
    {
        var found = new List<(MacroItem Macro, string Bare, string Qualified)>();
        foreach (var macro in macros)
        {
            Gather(macro, string.Empty, found);
        }

        // A bare name is only handed out when exactly one macro answers to it: two parents may each
        // hold a "收尾", and a call that says "收尾" has to keep meaning the one its own parent
        // holds. The qualified name always works and is what makes the difference visible.
        var twice = found
            .GroupBy(entry => entry.Bare, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (macro, bare, qualified) in found)
        {
            if (bare.Length == 0)
            {
                continue;
            }

            if (!twice.Contains(bare) && seen.Add(bare))
            {
                yield return (bare, macro);
            }

            if (!string.Equals(qualified, bare, StringComparison.OrdinalIgnoreCase)
                && seen.Add(qualified))
            {
                yield return (qualified, macro);
            }
        }
    }

    /// <summary>
    /// Each callable name with the macro it belongs to, for the places that show a list and then
    /// need to write the name the run will look for.
    /// </summary>
    public static IReadOnlyList<(string Name, MacroItem Macro)> Named(IEnumerable<MacroItem> macros)
        => [.. Pieces(macros)];

    /// <summary>One macro and then everything nested in it, each with its bare and its full name.</summary>
    private static void Gather(MacroItem macro, string prefix,
        List<(MacroItem Macro, string Bare, string Qualified)> found)
    {
        var bare = macro.Name.Trim();
        var qualified = prefix.Length == 0 ? bare : $"{prefix}/{bare}";
        found.Add((macro, bare, qualified));

        foreach (var child in macro.SubMacros)
        {
            Gather(child, qualified, found);
        }
    }
}
