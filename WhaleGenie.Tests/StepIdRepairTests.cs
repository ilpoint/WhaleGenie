using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using WhaleGenie.Models;
using Xunit;

namespace WhaleGenie.Tests;

/// <summary>
/// What happens when two steps arrive carrying one id. The name is what a condition and an
/// expression point a step by, so a second step under a name already in use is renamed — and the
/// references that came with it have to move too, or the repair turns a file that branched on the
/// wrong step into one that fails at run time.
/// </summary>
public class StepIdRepairTests
{
    [Fact]
    public void A_step_that_moves_takes_its_own_condition_with_it()
    {
        var macro = Macro(
            Step("abcd", "control.delay", """{"ms":"1"}"""),
            Step("abcd", "control.if", """{"condition":"$step.abcd.outcome == \"ok\""}"""));

        var renamed = StepIds.Settle(macro.Steps);

        var moved = Assert.Single(renamed);
        Assert.Equal("abcd", moved.Old);
        Assert.Equal("abcd", macro.Steps[0].Id);
        Assert.Equal(moved.New, macro.Steps[1].Id);
        Assert.Equal($"$step.{moved.New}.outcome == \"ok\"", Value(macro.Steps[1], "condition"));
    }

    /// <summary>
    /// Pasting a block copies ids and the conditions that name them together. Both copies arrive
    /// under the same names, so the second block moves — and its own condition goes with it, while
    /// the block that was already there keeps the condition it had.
    /// </summary>
    [Fact]
    public void The_block_that_was_already_there_keeps_its_own_condition()
    {
        var macro = Macro(
            Step("abcd", "control.if", """{"condition":"$step.abcd.outcome == \"ok\""}"""),
            Step("abcd", "control.if", """{"condition":"$step.abcd.outcome == \"ok\""}"""));

        var renamed = Assert.Single(StepIds.Settle(macro.Steps));

        Assert.Equal("$step.abcd.outcome == \"ok\"", Value(macro.Steps[0], "condition"));
        Assert.Equal($"$step.{renamed.New}.outcome == \"ok\"", Value(macro.Steps[1], "condition"));
    }

    /// <summary>
    /// A field the engine reads as words is not a reference: a macro that types the text
    /// <c>step.abcd.outcome</c> into a window means those words.
    /// </summary>
    [Fact]
    public void A_field_read_as_words_is_left_alone()
    {
        var macro = Macro(
            Step("abcd", "input.typeText", """{"typedText":"step.abcd.outcome"}"""),
            Step("abcd", "input.typeText", """{"typedText":"step.abcd.outcome"}"""));

        StepIds.Settle(macro.Steps);

        Assert.Equal("step.abcd.outcome", Value(macro.Steps[0], "typedText"));
        Assert.Equal("step.abcd.outcome", Value(macro.Steps[1], "typedText"));
    }

    [Fact]
    public void Nothing_moves_when_every_id_is_its_own()
    {
        var macro = Macro(
            Step("abcd", "control.delay", """{"ms":"1"}"""),
            Step("efgh", "control.if", """{"condition":"$step.abcd.outcome == \"ok\""}"""));

        Assert.Empty(StepIds.Settle(macro.Steps));
        Assert.Equal("$step.abcd.outcome == \"ok\"", Value(macro.Steps[1], "condition"));
    }

    private static MacroItem Macro(params JsonObject[] steps)
        => MacroItem.FromJson(new JsonObject
        {
            ["name"] = "换名",
            ["steps"] = new JsonArray(steps),
        });

    private static JsonObject Step(string id, string type, string parameters)
        => new()
        {
            ["id"] = id,
            ["type"] = type,
            ["params"] = JsonNode.Parse(parameters),
        };

    private static string Value(MacroStep step, string parameter)
        => step.Parameters.Single(candidate => candidate.Name == parameter).Value;
}
