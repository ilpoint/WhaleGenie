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

    public static void RenameGlobal(IEnumerable<MacroItem> macros, string oldName, string newName,
        string id = "")
    {
        if (id.Length == 0)
        {
            id = VariableCatalog.Globals.FirstOrDefault(global =>
                string.Equals(global.Name.Trim(), oldName, StringComparison.OrdinalIgnoreCase))?.Id
                ?? string.Empty;
        }
        foreach (var macro in macros)
        {
            RenameSteps(macro.Steps, oldName, newName, global: true);
            foreach (var parameter in macro.Parameters)
            {
                parameter.Default = RenameValue(parameter.Default, oldName, newName);
            }

            foreach (var local in macro.Locals)
            {
                local.Initial = RenameValue(local.Initial, oldName, newName);
            }

            // The name pass above keeps the old behaviour for references that came in without an
            // identity. The identity pass then puts every bound reference back on the name of the
            // declaration it actually belongs to, which is what a same-named local cannot steal.
            VariableIds.Rebind(macro.Steps, id, newName);
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
                    parameter.Written = RenameDeclared(parameter.Written, oldName, newName);
                }

                if (parameter.IsRowList)
                {
                    foreach (var row in parameter.Rows)
                    {
                        foreach (var column in row.Cells.Keys.ToList())
                        {
                            var cell = row.Cells[column];
                            row.Cells[column] = parameter.Kind is ActionParameterKind.Returns
                                && string.Equals(column, "name", StringComparison.Ordinal)
                                ? RenameDeclared(cell, oldName, newName)
                                : RenameCell(cell, oldName, newName);
                        }
                    }

                    continue;
                }

                // A field that names a variable declares one, so the word itself changes; every
                // other field reads one, and the tree says where. Nothing is spelled out and read
                // back: a name inside a piece of text is a name because the value says so.
                parameter.Written = step.ParameterDefinition(parameter)?.ValueForm
                    is ActionValueForm.Name && (global || !globalTarget)
                        ? RenameDeclared(parameter.Written, oldName, newName)
                        : Expression.Rename(parameter.Written, oldName, newName);

            }
        }
    }

    /// <summary>
    /// A declared value with one name inside it changed. The value is a shape, so the tree says
    /// which pieces are names: nothing has to be spelled out as a line and read back to find them.
    /// </summary>
    public static ExpressionNode RenameValue(ExpressionNode value, string oldName, string newName)
        => Expression.Rename(value, oldName, newName);

    /// <summary>A cell renamed the way any other value is: only its references change.</summary>
    private static ExpressionNode RenameCell(ExpressionNode cell, string oldName, string newName)
        => Expression.Rename(cell, oldName, newName);

    /// <summary>
    /// A cell that declares a name rather than reading one — a result's name, a returned field —
    /// is text, so it is compared as the word it is.
    /// </summary>
    private static ExpressionNode RenameDeclared(ExpressionNode cell, string oldName,
        string newName)
        => cell is TextNode text
            && string.Equals(text.Text.Trim(), oldName, StringComparison.Ordinal)
                ? Formula.Text(newName)
                : cell;

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
