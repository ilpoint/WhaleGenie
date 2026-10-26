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
/// library a run was given: a macro that called one failed with <c>Run.MacroNotFound</c> however it
/// was started. The editor could create the call and the patch layer could change the steps; only
/// the run could not find them.
/// </remarks>
public static class CallableMacros
{
    /// <summary>
    /// Every macro and submacro of a project, ready for <see cref="ProjectMacroLibrary"/>, in the
    /// order they are listed and then in the order their parents hold them.
    /// </summary>
    public static IReadOnlyList<(string Name, IReadOnlyList<ExecutableStep> Steps,
        MacroSignature Signature, bool ExclusiveInput)> Entries(IEnumerable<MacroItem> macros)
    {
        var found = new List<(MacroItem Macro, string Bare, string Qualified)>();
        foreach (var macro in macros)
        {
            Gather(macro, string.Empty, found);
        }

        // A bare name is only worth registering when exactly one macro answers to it: two parents
        // may each hold a "收尾", and a call that says "收尾" has to keep meaning the one its own
        // parent holds. The qualified name always works and is what makes the difference visible.
        var twice = found
            .GroupBy(entry => entry.Bare, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var entries = new List<(string, IReadOnlyList<ExecutableStep>, MacroSignature, bool)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (macro, bare, qualified) in found)
        {
            if (bare.Length == 0)
            {
                continue;
            }

            var signature = macro.Callable.Signature;
            if (!twice.Contains(bare) && seen.Add(bare))
            {
                entries.Add((bare, macro.Steps.ToExecutable(), signature, macro.ExclusiveInput));
            }

            if (!string.Equals(qualified, bare, StringComparison.OrdinalIgnoreCase)
                && seen.Add(qualified))
            {
                entries.Add((qualified, macro.Steps.ToExecutable(), signature, macro.ExclusiveInput));
            }
        }

        return entries;
    }

    /// <summary>One macro, then every submacro it holds, with the path that names each one.</summary>
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
