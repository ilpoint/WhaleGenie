using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.Localization;


namespace WhaleGenie.Models;

// The names a step reads, writes and returns.
public partial class MacroStep : INotifyPropertyChanged
{
    /// <summary>
    /// Collects the variables this step creates, including the ones nested inside it,
    /// so the variable pickers can offer them.
    /// </summary>
    public void CollectVariables(ICollection<string> names)
    {
        foreach (var parameter in Parameters)
        {
            switch (parameter.Kind)
            {
                case ActionParameterKind.Steps:
                    foreach (var child in parameter.Steps)
                    {
                        child.CollectVariables(names);
                    }
                    break;
                // The results a step hands over are named one by one on the returns page, so what
                // this step creates is read from those rows rather than from a single value.
                case ActionParameterKind.Returns:
                    foreach (var (_, name) in ReturnedResults(parameter))
                    {
                        names.Add(name);
                    }
                    break;
                default:
                    if (DefinesVariable(parameter)
                        && !(Type == "control.setVariable"
                             && string.Equals(Text("scope").Trim(), "global",
                                 System.StringComparison.OrdinalIgnoreCase))
                        && !string.IsNullOrWhiteSpace(parameter.Value))
                    {
                        names.Add(parameter.Value.Trim());
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// The results this step was told to hand over, in the order they are written: one per ticked
    /// result, each row of the <c>returns</c> parameter carrying the key of the result and the name
    /// it goes by.
    /// </summary>
    public IEnumerable<(string Key, string Name)> ReturnedResults(StepParameter parameter)
        => ReturnedNameKeys(parameter).Select(result => (result.Key, result.Name.Name));

    /// <summary>
    /// The same rows with the identity each name is bound to. A picker that offers one of these
    /// names has to carry the identity with it, or the reference it writes is bound by spelling and
    /// loses the declaration the next time two names happen to match.
    /// </summary>
    public IEnumerable<(string Key, VariableKey Name)> ReturnedNameKeys(StepParameter parameter)
    {
        foreach (var row in parameter.Rows)
        {
            var key = Formula.NameOf(row.Cell(FieldNames.Name));
            var name = key.Name.Trim();
            if (name.Length > 0)
            {
                yield return (row.Text(FieldNames.Key).Trim(), key with { Name = name });
            }
        }
    }

    /// <summary>
    /// True when the parameter creates a variable rather than referencing one. What a step leaves
    /// behind is now named one result at a time on the returns page, so this is left with the names
    /// that are part of writing the step itself: the loop's own variables, and the name a "set
    /// variable", "calculate" or "create list" step writes into.
    /// </summary>
    public bool DefinesVariable(StepParameter parameter) => DefinesVariable(Type, parameter.Name);

    /// <summary>
    /// The same question asked of a step that is still being written, which is a type and a field
    /// name rather than a step: the dialog has both while the step is on the screen, and asking it
    /// this way keeps one answer to "which fields have a macro make a variable" rather than two.
    /// </summary>
    public static bool DefinesVariable(string? type, string parameterName) => parameterName switch
    {
        "itemVariable" or "indexVariable" => true,
        "name" => type is "control.setVariable"
            or "control.for",
        _ => false,
    };
}
