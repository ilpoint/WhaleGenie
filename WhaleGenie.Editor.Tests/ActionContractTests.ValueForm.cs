using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace WhaleGenie.Tests;

/// <summary>
/// ValueForm is a catalogue fact. This check reads the catalogue as a document rather than as
/// typed semantics, so a new parameter that forgets to say what it accepts cannot be hidden by
/// the default value of a deserialized property. It also refuses the two facts ValueForm replaced.
/// </summary>
public partial class ActionContractTests
{
    [Fact]
    public void Every_parameter_declares_the_shape_it_accepts()
    {
        var path = Path.Combine(Repository(), "WhaleGenie.Core", "Execution", "ActionSemantics.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        string[] forms = ["Text", "Run", "Value", "Name"];
        var missing = new List<string>();

        foreach (var action in document.RootElement.EnumerateArray())
        {
            var key = action.GetProperty("Key").GetString() ?? string.Empty;
            if (!action.TryGetProperty("Parameters", out var parameters))
            {
                continue;
            }

            foreach (var field in parameters.EnumerateArray())
            {
                var name = field.GetProperty("Name").GetString() ?? string.Empty;
                if (!field.TryGetProperty("ValueForm", out var form)
                    || !forms.Contains(form.GetString(), StringComparer.Ordinal))
                {
                    missing.Add($"{key}.{name} (no value form)");
                }

                if (field.TryGetProperty("ReadMode", out _)
                    || field.TryGetProperty("AcceptsVariables", out _))
                {
                    missing.Add($"{key}.{name} (old read-mode fact)");
                }
            }
        }

        Assert.True(missing.Count == 0,
            "parameters without a single declared value form: " + string.Join(", ", missing));
    }
}
