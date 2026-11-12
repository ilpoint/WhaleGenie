using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
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

        return Formula.Speak(parameter.Written);
    }

    /// <summary>
    /// What a named parameter says, in the words a person reads it in: the prose as written, a
    /// name between corner marks so it is told apart from the words around it, a formula with its
    /// spacing. This is the form every row and every sentence about a step uses — never the form a
    /// value is read back out of, which is why a name in it needs no sign in front of it.
    /// </summary>
    public string Said(string name)
        => Parameters.FirstOrDefault(parameter => parameter.Name == name) is { } parameter
            ? Formula.Speak(parameter.Written)
            : string.Empty;

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
    {
        var parameter = Parameters.FirstOrDefault(parameter => parameter.Name == name);
        if (parameter is null)
        {
            return string.Empty;
        }

        // A field that writes a variable name shows the name, not the dollar the formula writer
        // puts in front of a reference. The shape holds the name and its identity; this is the
        // reader for the places that ask a step what it says.
        return ParameterDefinition(parameter) is { NamesVariable: true }
            ? Formula.NameOf(parameter.Written).Name
            : parameter.Value;
    }
}
