using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;


namespace WhaleGenie.Tests;

// What a step hands over: where it goes, what a choice starts on, and the defaults that matter.
public partial class ActionContractTests
{
    [Fact]
    public void Every_action_that_hands_something_over_says_where_its_returns_are_written()
    {
        foreach (var action in ActionSemanticsCatalog.All)
        {
            if (action.Returns.Count == 0)
            {
                continue;
            }

            var returns = action.Parameter("returns");
            Assert.NotNull(returns);
            Assert.Equal(ActionValueShape.Rows, returns.Shape);
            Assert.Equal(["key", "name"], returns.Columns.Select(column => column.Name));
        }
    }

    /// <summary>
    /// A choice that starts on a word it does not offer is a value the engine will refuse before
    /// the user changes anything, and the form shows a starting answer that is not one of the
    /// answers. It is the same kind of mistake as a missing option label, and just as invisible.
    /// </summary>
    [Fact]
    public void Every_choice_starts_on_one_of_its_own_options()
    {
        foreach (var action in ActionSemanticsCatalog.All)
        {
            foreach (var parameter in action.Parameters.Where(parameter =>
                         parameter.Shape == ActionValueShape.Choice
                         && parameter.DefaultValue.Length > 0))
            {
                Assert.Contains(parameter.DefaultValue, parameter.Options);
            }
        }
    }

    [Fact]
    public void Starting_a_program_hands_over_the_fields_later_steps_use()
    {
        var started = ActionSemanticsCatalog.Find("process.start")!
            .Returns.Single(result => result.Key == "program");

        Assert.Equal(ActionReturnType.Record, started.Type);
        Assert.Equal(["id", "process", "title", "handle", "how"],
            started.Fields.Select(field => field.Name));
        Assert.DoesNotContain(started.Fields, field => field.Name == "memoryMb");
        Assert.DoesNotContain(started.Fields, field => field.Name == "cpuSeconds");
    }

    [Theory]
    [InlineData("process.start", "hidden", "false")]
    [InlineData("file.writeCsv", "writeMode", "overwrite")]
    public void A_default_that_means_the_useful_case_is_the_one_the_action_starts_with(
        string actionKey, string parameterName, string expected)
    {
        var parameter = ActionSemanticsCatalog.Find(actionKey)!.Parameter(parameterName);
        Assert.NotNull(parameter);
        Assert.Equal(expected, parameter.DefaultValue);
    }

    [Fact]
    public void Every_action_has_the_facts_its_contract_needs()
    {
        var problems = new List<string>();

        foreach (var action in ActionSemanticsCatalog.All)
        {
            if (action.Waits && action.TimeoutSource == ActionTimeoutSource.None)
            {
                problems.Add($"{action.Key}: waits without saying where its timeout comes from");
            }

            if (action.StepTimeoutMs > 0 && !action.Waits)
            {
                problems.Add($"{action.Key}: has a step timeout but is not marked as waiting");
            }

            foreach (var parameter in action.Parameters)
            {
                if (parameter.Shape == ActionValueShape.Choice && parameter.Options.Count == 0)
                {
                    problems.Add($"{action.Key}.{parameter.Name}: a choice has no options");
                }

                if (parameter.Picker == ActionPickerKind.Browser
                    && !action.Key.StartsWith("browser.", StringComparison.Ordinal))
                {
                    problems.Add($"{action.Key}.{parameter.Name}: a browser picker outside a browser action");
                }
            }

            if (action.Returns.Any(result => string.IsNullOrWhiteSpace(result.Key)))
            {
                problems.Add($"{action.Key}: a return has no key");
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }
}
