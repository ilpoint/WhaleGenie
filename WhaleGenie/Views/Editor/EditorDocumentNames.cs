using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.Views;

/// <summary>
/// The names the documents of one editor answer to: a sub-macro takes a name no other document or
/// project macro has, and a rename follows every call that pointed at the old one. Both rules are
/// about the whole set of documents rather than about one page, which is why they are here rather
/// than in the window that owns the pages.
/// </summary>
internal static class EditorDocumentNames
{
    /// <summary>
    /// A name for a sub-macro that no document and no project macro already has. A second copy of
    /// "login" is "login (2)", the way a file manager numbers copies: the number says "another one
    /// of these", which is exactly what a copied sub-macro is.
    /// </summary>
    public static string Unique(string basis, IEnumerable<string> taken)
    {
        var names = taken.Select(name => name.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var stem = basis.Trim();
        if (stem.Length == 0)
        {
            stem = Strings.Get("Editor.NewSubMacroName");
        }

        if (names.Add(stem))
        {
            return stem;
        }

        for (var index = 2; ; index++)
        {
            var candidate = $"{stem} ({index})";
            if (names.Add(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>True when this step, or any step inside it, calls the named macro.</summary>
    public static bool Calls(IEnumerable<MacroStep> steps, string macroName)
    {
        foreach (var step in steps)
        {
            if (step.Type is "control.runMacro"
                && string.Equals(step.Text("macro").Trim(), macroName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (step.StepLists.Any(list => Calls(list.Steps, macroName)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Points every call to <paramref name="oldName"/> at <paramref name="newName"/>.</summary>
    public static void RenameCalls(IEnumerable<MacroStep> steps, string oldName, string newName)
    {
        foreach (var step in steps)
        {
            if (step.Type is "control.runMacro"
                && step.Parameters.FirstOrDefault(parameter => parameter.Name == "macro")
                    is { } target
                && string.Equals(target.Value?.Trim(), oldName,
                    StringComparison.OrdinalIgnoreCase))
            {
                target.Written = Formula.Text(newName);
            }

            foreach (var parameter in step.StepLists)
            {
                RenameCalls(parameter.Steps, oldName, newName);
            }
        }
    }
}
