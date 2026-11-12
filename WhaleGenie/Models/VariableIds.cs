using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Models;

/// <summary>
/// Gives declarations a stable identity and keeps the references that name them pointing at the
/// same declaration. Identity lives on the shape: a name is a <see cref="VariableNode"/>, and a
/// reference is that node wherever it stands. Nothing here is stored beside the value, which is
/// what lets undo, copy and paste carry identity without a second table to keep in step.
/// </summary>
public static class VariableIds
{
    /// <summary>One id that had to move because another declaration was already using it.</summary>
    public sealed record Rename(string Old, string New);

    /// <summary>An id no declaration in this document is using yet.</summary>
    public static string Next(ISet<string> taken) => VariableId.Next(taken);

    /// <summary>
    /// Gives every declaration an id and makes a name field a shape. A step declaration that
    /// repeats an id moves inside its own step: that is the block the copy travelled with, so the
    /// block already in place keeps the references it owns.
    /// </summary>
    public static IReadOnlyList<Rename> Settle(MacroItem document)
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectIds(document, taken);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var renamed = new List<Rename>();

        foreach (var parameter in document.Parameters)
        {
            if (parameter.Id.Length == 0)
            {
                parameter.Id = VariableId.Next(taken);
            }

            taken.Add(parameter.Id);
            seen.Add(parameter.Id);
        }

        foreach (var result in document.Results)
        {
            // A result may share an argument's name: the call passes a value in and takes the same
            // one back out. That is one variable, so it is one identity.
            var inOut = document.Parameters.FirstOrDefault(parameter =>
                string.Equals(parameter.Name.Trim(), result.Name.Trim(),
                    StringComparison.OrdinalIgnoreCase));
            if (inOut is not null && inOut.Id.Length > 0)
            {
                if (result.Id.Length > 0
                    && !string.Equals(result.Id, inOut.Id, StringComparison.Ordinal))
                {
                    Remap(document, result.Id, inOut.Id);
                }

                result.Id = inOut.Id;
                continue;
            }

            if (result.Id.Length == 0)
            {
                result.Id = VariableId.Next(taken);
            }

            taken.Add(result.Id);
            seen.Add(result.Id);
        }

        foreach (var local in document.Locals)
        {
            if (local.Id.Length == 0)
            {
                local.Id = VariableId.Next(taken);
            }

            taken.Add(local.Id);
            seen.Add(local.Id);
        }

        foreach (var step in Walk(document.Steps))
        {
            SettleStep(step, taken, seen, renamed);
        }

        // Bind the references this document can answer on its own. A reference to a global is
        // left alone here: the package has the globals and binds it once they are in hand.
        Bind(document, []);
        return renamed;
    }

    /// <summary>Gives every global declaration an id, and settles a repeated one.</summary>
    public static void SettleGlobals(IReadOnlyList<VariableDefinition> globals)
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var global in globals.Where(global => global.Id.Length > 0))
        {
            taken.Add(global.Id);
        }

        foreach (var global in globals)
        {
            if (global.Id.Length == 0)
            {
                global.Id = VariableId.Next(taken);
            }

            taken.Add(global.Id);
        }
    }

    /// <summary>
    /// Binds a reference that came in without an identity to the one declaration that carries its
    /// name. A name with no declaration, or with more than one, is left as the name it is: only a
    /// unique answer is a binding, and anything else keeps the behaviour a name alone had.
    /// </summary>
    public static void Bind(MacroItem document, IReadOnlyList<VariableDefinition> globals)
    {
        var byName = Declarations(document, globals);

        foreach (var step in Walk(document.Steps))
        {
            BindStep(step, byName);
        }
    }

    /// <summary>
    /// Makes every reference read the current name of the declaration it is bound to. A field of a
    /// record keeps its suffix: the identity belongs to the record, not to the field read off it.
    /// </summary>
    public static void RefreshNames(MacroItem document, IReadOnlyList<VariableDefinition> globals)
    {
        var names = Declarations(document, globals)
            .Where(pair => pair.Value.Count == 1)
            .ToDictionary(pair => pair.Value[0], pair => pair.Key, StringComparer.Ordinal);

        foreach (var step in Walk(document.Steps))
        {
            MapStep(step, variable =>
            {
                if (variable.Id.Length == 0 || !names.TryGetValue(variable.Id, out var name))
                {
                    return variable;
                }

                var dot = variable.Name.IndexOf('.');
                var suffix = dot >= 0 ? variable.Name[dot..] : string.Empty;
                return variable with { Name = name + suffix };
            });
        }
    }

    /// <summary>Points every reference with one identity at another, for an import or a paste.</summary>
    public static void Remap(MacroItem document, string fromId, string toId)
    {
        if (fromId.Length == 0 || toId.Length == 0 || string.Equals(fromId, toId, StringComparison.Ordinal))
        {
            return;
        }

        foreach (var step in Walk(document.Steps))
        {
            RemapStep(step, fromId, toId);
        }
    }

    /// <summary>
    /// Makes every reference bound to one identity read a new name. This is the rename path: the
    /// declaration changes its label, and the references follow because they carry the identity,
    /// not because they happen to spell the old label.
    /// </summary>
    public static void Rebind(IEnumerable<MacroStep> steps, string id, string newName)
    {
        if (id.Length == 0)
        {
            return;
        }

        foreach (var step in Walk(steps))
        {
            MapStep(step, variable =>
            {
                if (!string.Equals(variable.Id, id, StringComparison.Ordinal))
                {
                    return variable;
                }

                var dot = variable.Name.IndexOf('.');
                var suffix = dot >= 0 ? variable.Name[dot..] : string.Empty;
                return variable with { Name = newName + suffix };
            });
        }
    }

    private static void SettleStep(MacroStep step, HashSet<string> taken, HashSet<string> seen,
        List<Rename> renamed)
    {
        foreach (var parameter in step.Parameters)
        {
            if (parameter.Kind is ActionParameterKind.Steps)
            {
                continue;
            }

            if (parameter.Kind is ActionParameterKind.Returns)
            {
                foreach (var row in parameter.Rows)
                {
                    var written = row.Cell(FieldNames.Name);
                    if (Formula.NameOf(written).Name.Length == 0)
                    {
                        continue;
                    }

                    row.Cells[FieldNames.Name] = SettleName(step, written, taken, seen, renamed);
                }

                continue;
            }

            if (!MacroStep.DefinesVariable(step.Type, parameter.Name)
                || Formula.NameOf(parameter.Written).Name.Length == 0)
            {
                continue;
            }

            parameter.Written = SettleName(step, parameter.Written, taken, seen, renamed);
        }
    }

    private static ExpressionNode SettleName(MacroStep step, ExpressionNode written,
        HashSet<string> taken, HashSet<string> seen, List<Rename> renamed)
    {
        var key = Formula.NameOf(written);
        var id = key.Id;
        if (id.Length > 0 && seen.Contains(id))
        {
            var next = VariableId.Next(taken);
            renamed.Add(new Rename(id, next));
            RemapStep(step, id, next);
            id = next;
        }

        if (id.Length == 0)
        {
            id = VariableId.Next(taken);
        }

        taken.Add(id);
        seen.Add(id);
        return new VariableNode(key.Name, id);
    }

    private static void BindStep(MacroStep step, Dictionary<string, List<string>> byName)
    {
        foreach (var parameter in step.Parameters)
        {
            if (parameter.Kind is ActionParameterKind.Steps)
            {
                continue;
            }

            var declaresName = MacroStep.DefinesVariable(step.Type, parameter.Name)
                || parameter.Kind is ActionParameterKind.Returns;
            foreach (var row in parameter.Rows)
            {
                foreach (var column in row.Cells.Keys.ToList())
                {
                    if (declaresName && string.Equals(column, FieldNames.Name, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    row.Cells[column] = BindTree(row.Cells[column], byName);
                }
            }

            if (!declaresName)
            {
                parameter.Written = BindTree(parameter.Written, byName);
            }
        }
    }

    private static ExpressionNode BindTree(ExpressionNode tree, Dictionary<string, List<string>> byName)
        => Map(tree, variable =>
        {
            // A reference that already has an identity keeps it. The identity may belong to a
            // global this document cannot see; a stale one is reported by the check, not guessed
            // away here.
            if (variable.Id.Length > 0)
            {
                return variable;
            }

            var name = variable.Name.Trim();
            if (!byName.TryGetValue(name, out var ids))
            {
                var dot = name.IndexOf('.');
                if (dot <= 0 || !byName.TryGetValue(name[..dot], out ids))
                {
                    return variable;
                }
            }

            return ids.Count == 1 ? variable with { Id = ids[0] } : variable;
        });

    private static void MapStep(MacroStep step, Func<VariableNode, VariableNode> change)
    {
        foreach (var parameter in step.Parameters)
        {
            parameter.Written = Map(parameter.Written, change);
            foreach (var row in parameter.Rows)
            {
                foreach (var column in row.Cells.Keys.ToList())
                {
                    row.Cells[column] = Map(row.Cells[column], change);
                }
            }

            foreach (var child in parameter.Steps)
            {
                MapStep(child, change);
            }
        }
    }

    private static void RemapStep(MacroStep step, string fromId, string toId)
        => MapStep(step, variable =>
            string.Equals(variable.Id, fromId, StringComparison.Ordinal)
                ? variable with { Id = toId }
                : variable);

    private static ExpressionNode Map(ExpressionNode node, Func<VariableNode, VariableNode> change)
        => node switch
        {
            VariableNode variable => change(variable),
            ConcatNode run => run with { Parts = [.. run.Parts.Select(part => Map(part, change))] },
            ListNode list => list with { Items = [.. list.Items.Select(item => Map(item, change))] },
            CallNode call => call with
            {
                Arguments = [.. call.Arguments.Select(argument => Map(argument, change))],
            },
            UnaryNode unary => unary with { Operand = Map(unary.Operand, change) },
            BinaryNode binary => binary with
            {
                Left = Map(binary.Left, change),
                Right = Map(binary.Right, change),
            },
            _ => node,
        };

    private static IEnumerable<MacroStep> Walk(IEnumerable<MacroStep> steps)
    {
        foreach (var step in steps)
        {
            yield return step;
            foreach (var parameter in step.Parameters)
            {
                foreach (var child in Walk(parameter.Steps))
                {
                    yield return child;
                }
            }
        }
    }

    private static void CollectIds(MacroItem document, HashSet<string> ids)
    {
        foreach (var parameter in document.Parameters.Where(parameter => parameter.Id.Length > 0))
        {
            ids.Add(parameter.Id);
        }

        foreach (var result in document.Results.Where(result => result.Id.Length > 0))
        {
            ids.Add(result.Id);
        }

        foreach (var local in document.Locals.Where(local => local.Id.Length > 0))
        {
            ids.Add(local.Id);
        }

        foreach (var step in Walk(document.Steps))
        {
            foreach (var parameter in step.Parameters)
            {
                Collect(parameter.Written, ids);
                foreach (var row in parameter.Rows)
                {
                    foreach (var cell in row.Cells.Values)
                    {
                        Collect(cell, ids);
                    }
                }
            }
        }
    }

    private static void Collect(ExpressionNode node, HashSet<string> ids)
    {
        switch (node)
        {
            case VariableNode variable when variable.Id.Length > 0:
                ids.Add(variable.Id);
                break;
            case ConcatNode run:
                foreach (var part in run.Parts)
                {
                    Collect(part, ids);
                }

                break;
            case ListNode list:
                foreach (var item in list.Items)
                {
                    Collect(item, ids);
                }

                break;
            case CallNode call:
                foreach (var argument in call.Arguments)
                {
                    Collect(argument, ids);
                }

                break;
            case UnaryNode unary:
                Collect(unary.Operand, ids);
                break;
            case BinaryNode binary:
                Collect(binary.Left, ids);
                Collect(binary.Right, ids);
                break;
        }
    }

    private static Dictionary<string, List<string>> Declarations(MacroItem document,
        IReadOnlyList<VariableDefinition> globals)
    {
        var byName = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        void Add(string name, string id)
        {
            var written = name.Trim();
            if (written.Length == 0 || id.Length == 0)
            {
                return;
            }

            if (!byName.TryGetValue(written, out var ids))
            {
                byName[written] = ids = [];
            }

            ids.Add(id);
        }

        foreach (var parameter in document.Parameters)
        {
            Add(parameter.Name, parameter.Id);
        }

        foreach (var result in document.Results)
        {
            Add(result.Name, result.Id);
        }

        foreach (var local in document.Locals)
        {
            Add(local.Name, local.Id);
        }

        foreach (var step in Walk(document.Steps))
        {
            foreach (var parameter in step.Parameters)
            {
                if (parameter.Kind is ActionParameterKind.Returns)
                {
                    foreach (var row in parameter.Rows)
                    {
                        var key = Formula.NameOf(row.Cell(FieldNames.Name));
                        Add(key.Name, key.Id);
                    }
                }
                else if (MacroStep.DefinesVariable(step.Type, parameter.Name))
                {
                    var key = Formula.NameOf(parameter.Written);
                    Add(key.Name, key.Id);
                }
            }
        }

        foreach (var global in globals)
        {
            Add(global.Name, global.Id);
        }

        return byName;
    }
}
