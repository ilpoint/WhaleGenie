using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;


namespace WhaleGenie.Models;

// The label beside one field, the value it shows, and the definition behind it.
public partial class MacroStep : INotifyPropertyChanged
{
    /// <summary>Localized label of a parameter, falling back to its raw name.</summary>
    public string ParameterLabel(StepParameter parameter)
        => ParameterDefinition(parameter)?.LocalLabel ?? parameter.Name;

    /// <summary>Choice values are shown with their display text, everything else as typed.</summary>
    private string DescribeValue(StepParameter parameter)
    {
        if (parameter.Kind is ActionParameterKind.Choice
            && ParameterDefinition(parameter) is { } definition)
        {
            return Strings.Get($"{definition.OwnerKey}.{definition.Name}.option.{parameter.Value}",
                parameter.Value);
        }

        return parameter.Value;
    }

    /// <summary>The catalogue entry for a parameter of this step, or null when it is unknown.</summary>
    public ActionParameter? ParameterDefinition(StepParameter parameter)
        => Definition?.Parameters.FirstOrDefault(parameterDefinition =>
            parameterDefinition.Name == parameter.Name);

    /// <summary>The text of a named parameter, or nothing when the step has no such parameter.</summary>
    /// <remarks>
    /// Read by the things that look at a step without running it — the design-time check and the
    /// dialog that fills one in — which is the same question the engine asks of the step it runs.
    /// </remarks>
    public string Text(string name)
        => Parameters.FirstOrDefault(parameter => parameter.Name == name)?.Value ?? string.Empty;
}
