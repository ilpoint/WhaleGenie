using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Execution;
using WhaleGenie.Models;
using WhaleGenie.Runtime;

namespace WhaleGenie.Storage;

/// <summary>
/// What a chosen handful of macros needs in order to work somewhere else: the macros themselves
/// (with the submacros they hold), the pictures they look for, the trigger entries that start them
/// and the global variables they read.
/// </summary>
/// <remarks>
/// The file that carries them is an ordinary package. Nothing is added to the format for this:
/// a package already has a macro list, a variable list and a trigger table, and an export is those
/// three lists cut down to what was ticked. That is also why a file holding one macro needs no
/// marking of its own: what a file is meant to be is decided where it is imported, not written
/// into it.
/// </remarks>
public static class MacroBundle
{
    /// <summary>
    /// Copies of the chosen macros, because saving rewrites the trigger entries and the interface
    /// layouts of the objects it is handed. Handing it the project's own macros would leave the
    /// project holding the export's trimmed table.
    /// </summary>
    public static IReadOnlyList<MacroItem> Copies(IEnumerable<MacroItem> macros)
    {
        var copies = new List<MacroItem>();
        foreach (var macro in macros)
        {
            var copy = MacroItem.FromJson(macro.ToJson());
            copy.IsEnabled = macro.IsEnabled;
            copy.Origin = macro.Origin;
            copies.Add(copy);
        }

        return copies;
    }

    /// <summary>
    /// The trigger table of a chosen few: their entries, the interface layouts those entries sit in,
    /// and the menu pages their menu entries refer to. A menu entry without its page would arrive
    /// pointing at a page nobody made, and a floating group without its position would move the
    /// buttons when the file is opened elsewhere.
    /// </summary>
    public static ProjectTriggers Take(ProjectTriggers source, IReadOnlyList<MacroItem> macros)
    {
        var names = macros.Select(macro => macro.Name.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var subset = new ProjectTriggers();
        foreach (var entry in source.Entries.Where(entry => names.Contains(entry.MacroName.Trim())))
        {
            subset.Entries.Add(entry.Copy());
        }

        var groups = subset.Entries
            .Select(entry => ProjectTriggers.GroupKey(entry.WindowTarget))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var layout in source.InterfaceLayouts.Where(layout => groups.Contains(layout.GroupKey)))
        {
            subset.InterfaceLayouts.Add(layout);
        }

        var pages = subset.Entries
            .Where(entry => entry.Kind == TriggerKind.Menu)
            .Select(entry => entry.MenuPageId)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var page in source.MenuPages.Where(page => pages.Contains(page.Id)))
        {
            subset.MenuPages.Add(page);
        }

        return subset;
    }

    /// <summary>
    /// The global variables of a given table that the chosen macros actually read.
    /// </summary>
    /// <remarks>
    /// The names come from <see cref="MacroCheck.ReadsOf"/>, which is the same question the
    /// design-time check and the variable center ask of a field, so a variable that shows a
    /// reference there is one that travels here. The table is passed in rather than read from the
    /// open project because a file's variables are not in it yet — that is exactly what an import
    /// is bringing.
    ///
    /// A script body is not read this way, because it is another language: a macro that only
    /// mentions a variable inside a script travels without it. That is the one case where a file
    /// can arrive with a name the project already has under a different meaning, and it is why the
    /// import reports the variables it brought.
    /// </remarks>
    public static IReadOnlyList<VariableDefinition> UsedGlobals(IEnumerable<MacroItem> macros)
        => UsedGlobals(macros, VariableCatalog.Globals);

    /// <summary>
    /// The same, out of a given table rather than the open project's: a file has its own variables,
    /// and what it will bring is the ones its macros read from that list.
    /// </summary>
    public static IReadOnlyList<VariableDefinition> UsedGlobals(IEnumerable<MacroItem> macros,
        IReadOnlyList<VariableDefinition> pool)
    {
        var used = ReadNames(macros.SelectMany(AllSteps));
        return [.. pool.Where(variable => used.Contains(variable.Name.Trim()))];
    }

    /// <summary>
    /// Calls the chosen macros make to macros that are not coming with them. The file is still
    /// written; saying so first is the difference between a run that stops at
    /// <c>Run.MacroNotFound</c> later and a person who ticks the missing one now.
    /// </summary>
    public static IReadOnlyList<string> DanglingCalls(IReadOnlyList<MacroItem> macros)
    {
        var available = CallableMacros.Callables(macros)
            .Select(callable => callable.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var step in macros.SelectMany(AllSteps))
        {
            if (step.Type != "control.runMacro")
            {
                continue;
            }

            var name = step.Text("macro").Trim();
            if (name.Length > 0 && !available.Contains(name))
            {
                missing.Add(name);
            }
        }

        return [.. missing];
    }

    /// <summary>The names the steps read, taken field by field the way the engine reads one.</summary>
    private static HashSet<string> ReadNames(IEnumerable<MacroStep> steps)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var step in steps)
        {
            foreach (var parameter in step.Parameters)
            {
                if (parameter.Kind == ActionParameterKind.Steps || step.DefinesVariable(parameter))
                {
                    continue;
                }

                // A script body is another language: a name inside it is the script's business, and
                // the text is not one of this macro's variable references.
                if (step.ParameterDefinition(parameter) is { ForeignText: true })
                {
                    continue;
                }

                foreach (var name in MacroCheck.ReadsOf(step, parameter))
                {
                    names.Add(name);
                }
            }
        }

        return names;
    }

    /// <summary>Every step of one macro, including the ones written inside a block or a submacro.</summary>
    private static IEnumerable<MacroStep> AllSteps(MacroItem macro)
    {
        foreach (var step in Walk(macro.Steps))
        {
            yield return step;
        }

        foreach (var sub in macro.SubMacros)
        {
            foreach (var step in AllSteps(sub))
            {
                yield return step;
            }
        }
    }

    /// <summary>The steps themselves and the ones inside every block they hold.</summary>
    private static IEnumerable<MacroStep> Walk(IEnumerable<MacroStep> steps)
    {
        foreach (var step in steps)
        {
            yield return step;
            foreach (var nested in step.StepLists.SelectMany(list => Walk(list.Steps)))
            {
                yield return nested;
            }
        }
    }
}
