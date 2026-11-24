using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.Editing;

/// <summary>
/// The rules a macro's callable children are made by: the name one gets when another already
/// answers to it, what a copy of one is, the call step that reaches it, and whether a step still
/// calls it. They are here rather than in the session so that making a child and removing one
/// cannot drift apart in how they read a name or a call.
/// </summary>
internal static class SubMacroEditor
{
    private static readonly char[] Quotes = ['"', '\'', '\u201c', '\u201d', '\u2018', '\u2019'];

    /// <summary>
    /// The name a child is given: the one asked for, or the table's name for a new child when none
    /// was asked for, then the same with a count after it while the document or the project
    /// already has it. A macro is reached by its name, so two children that answer to one name
    /// leave the second unreachable.
    /// </summary>
    public static string UniqueName(IReadOnlyCollection<string> taken, string? wanted)
    {
        var stem = (wanted ?? string.Empty).Trim();
        if (stem.Length == 0)
        {
            stem = Strings.Get("Editor.NewSubMacroName");
        }

        var name = stem;
        for (var index = 2; taken.Contains(name, StringComparer.OrdinalIgnoreCase); index++)
        {
            name = stem + " " + index.ToString(CultureInfo.InvariantCulture);
        }

        return name;
    }

    /// <summary>An empty child of the macro, named so that no other macro already answers to it.</summary>
    public static MacroItem NewSubMacro(IReadOnlyCollection<string> taken, string? wanted)
        => new() { Name = UniqueName(taken, wanted) };

    /// <summary>
    /// A copy of a child or project macro. It goes through the JSON shape the child is written in,
    /// so the copy owns its steps and declarations and editing one cannot reach the other.
    /// </summary>
    public static MacroItem Copy(MacroItem source, IReadOnlyCollection<string> taken, string? wanted)
    {
        var copy = MacroItem.FromJson(source.ToSubMacroJson());
        copy.Name = UniqueName(taken, string.IsNullOrWhiteSpace(wanted) ? source.Name : wanted);
        return copy;
    }

    /// <summary>
    /// The step that reaches a child: the call, and the argument and result rows the child's own
    /// declarations ask for. A child with neither is called with its name alone.
    /// </summary>
    public static MacroStep CallStep(MacroItem submacro)
    {
        var call = new MacroStep
        {
            Type = "control.runMacro",
            Meta = StepMeta.New,
            Parameters =
            [
                new StepParameter
                {
                    Name = "macro",
                    Kind = ActionParameterKind.Macro,
                    Written = Formula.Text(submacro.Name),
                },
            ],
        };

        if (submacro.Parameters.Count > 0)
        {
            call.Parameters.Add(new StepParameter
            {
                Name = FieldNames.Arguments,
                Kind = ActionParameterKind.Arguments,
                Rows =
                [
                    .. submacro.Parameters.Select(parameter => new StepParameterRow
                    {
                        Cells = new Dictionary<string, ExpressionNode>(StringComparer.OrdinalIgnoreCase)
                        {
                            [FieldNames.Name] = Formula.Text(parameter.Name),
                            [FieldNames.Value] = Formula.Text(string.Empty),
                        },
                    }),
                ],
            });
        }

        if (submacro.Results.Count > 0)
        {
            call.Parameters.Add(new StepParameter
            {
                Name = "returns",
                Kind = ActionParameterKind.Returns,
                Rows =
                [
                    .. submacro.Results.Select(result => new StepParameterRow
                    {
                        Cells = new Dictionary<string, ExpressionNode>(StringComparer.OrdinalIgnoreCase)
                        {
                            [FieldNames.Key] = Formula.Text(result.Name),
                            [FieldNames.Name] = Formula.Text(result.Name),
                        },
                    }),
                ],
            });
        }

        return call;
    }

    /// <summary>
    /// Whether a step anywhere inside the macro calls the named child. The name is read the way a
    /// row reads its call and compared without case, so a call typed in another case is still the
    /// same call rather than an unused child and a call into nothing.
    /// </summary>
    public static bool Referenced(MacroItem owner, string name)
    {
        var wanted = name.Trim();
        return wanted.Length > 0
            && Walk(owner.Steps).Any(step =>
                string.Equals(CalledMacro(step), wanted, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Removes a child the macro no longer calls, and answers false while a call is still there:
    /// a call into a name that has just gone would be a mistake nobody asked to make.
    /// </summary>
    public static bool Delete(MacroItem owner, string name)
    {
        var index = owner.SubMacros.FindIndex(macro => Named(macro, name));
        if (index < 0 || Referenced(owner, name))
        {
            return false;
        }

        owner.SubMacros.RemoveAt(index);
        return true;
    }

    /// <summary>What a call step names, with the quotes a typed name may carry taken off.</summary>
    private static string CalledMacro(MacroStep step)
        => string.Equals(step.Type, "control.runMacro", StringComparison.Ordinal)
            ? step.Said("macro").Trim().Trim(Quotes).Trim()
            : string.Empty;

    /// <summary>Whether a child answers to a name, ignoring the case and the spaces around it.</summary>
    public static bool Named(MacroItem macro, string name)
        => string.Equals(macro.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<MacroStep> Walk(IEnumerable<MacroStep> steps)
    {
        foreach (var step in steps)
        {
            yield return step;
            foreach (var child in Walk(step.StepLists.SelectMany(list => list.Steps)))
            {
                yield return child;
            }
        }
    }
}
