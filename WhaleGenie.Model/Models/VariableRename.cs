using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Models;

/// <summary>
/// Rewrites the variable references the macro model can know exactly. Record field names and
/// script bodies are deliberately outside this operation.
/// </summary>
public static class VariableRename
{
    public static string? GlobalConflict(IEnumerable<MacroItem> macros, string oldName) =>
        macros.SelectMany(macro => macro.LocalVariableNames())
            .FirstOrDefault(name => string.Equals(name, oldName, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<string> ScriptUsers(IEnumerable<MacroItem> macros, string name)
    {
        var found = new List<string>();
        foreach (var macro in macros)
        {
            if (Every(macro.Steps).Any(step => !step.StepLists.Any()
                    && ScriptMentions(step, name)))
            {
                found.Add(macro.Name);
            }
        }

        return found;
    }

    public static void RenameGlobal(IEnumerable<MacroItem> macros, string oldName, string newName)
    {
        foreach (var macro in macros)
        {
            RenameSteps(macro.Steps, oldName, newName, global: true);
            foreach (var parameter in macro.Parameters)
            {
                parameter.Default = RenameValue(parameter.Default, oldName, newName);
            }
        }
    }

    public static void RenameSteps(IEnumerable<MacroStep> steps, string oldName, string newName,
        bool global = false)
    {
        foreach (var step in Every(steps))
        {
            foreach (var parameter in step.Parameters)
            {
                if (parameter.Kind is ActionParameterKind.Steps)
                {
                    continue;
                }

                var globalTarget = step.Type == "control.incrementGlobal"
                    || step.Type == "control.setVariable"
                    && string.Equals(step.Text("scope").Trim(), "global",
                        StringComparison.OrdinalIgnoreCase);
                if (step.DefinesVariable(parameter) && (global || !globalTarget))
                {
                    parameter.Value = RenameName(parameter.Value, oldName, newName);
                }

                if (parameter.IsRowList)
                {
                    foreach (var row in parameter.Rows)
                    {
                        foreach (var column in row.Columns.Keys.ToList())
                        {
                            row.Columns[column] = row.Columns[column] is var value
                                && parameter.Kind is ActionParameterKind.Returns
                                && string.Equals(column, "name", StringComparison.Ordinal)
                                ? RenameName(value, oldName, newName)
                                : RenameValue(value, oldName, newName);
                        }
                    }

                    continue;
                }

                parameter.Value = step.ParameterDefinition(parameter)?.ReadMode switch
                {
                    ActionReadMode.VariableName when global || !globalTarget =>
                        RenameName(parameter.Value, oldName, newName),
                    ActionReadMode.WholeValue => RenameWholeValue(parameter.Value, oldName, newName),
                    _ => VariableText.Rename(parameter.Value, oldName, newName),
                };
            }
        }
    }

    private static string RenameWholeValue(string value, string oldName, string newName)
        => Expression.TryParse(value, out _, out _)
            ? Expression.RenameVariable(value, oldName, newName)
            : VariableText.Rename(value, oldName, newName);

    private static string RenameValue(string value, string oldName, string newName)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith('$'))
        {
            return RenameWholeValue(value, oldName, newName);
        }

        return VariableText.Rename(value, oldName, newName);
    }

    private static string RenameName(string value, string oldName, string newName)
    {
        var written = value.Trim();
        var dollar = written.StartsWith('$');
        var name = dollar ? written[1..].Trim() : written;
        return string.Equals(name, oldName, StringComparison.Ordinal)
            ? (dollar ? "$" : string.Empty) + newName
            : value;
    }

    private static IEnumerable<MacroStep> Every(IEnumerable<MacroStep> steps)
    {
        foreach (var step in steps)
        {
            yield return step;
            foreach (var list in step.StepLists)
            {
                foreach (var nested in Every(list.Steps))
                {
                    yield return nested;
                }
            }
        }
    }

    private static bool ScriptMentions(MacroStep step, string name)
        => step.Type.StartsWith("script.", StringComparison.Ordinal)
           && step.Parameters.Any(parameter =>
               parameter.Name is "script" or "scriptFile"
               && parameter.Value.Contains(name, StringComparison.Ordinal));
}
