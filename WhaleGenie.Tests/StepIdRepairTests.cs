using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using WhaleGenie.Core.Expressions;
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
            Step("efgh", "control.if", "condition", Formula.Binary("==", Formula.Name("step.abcd.outcome"), Formula.Text("ok"))));

        Assert.Empty(StepIds.Settle(macro.Steps));
        Assert.Equal("step.abcd.outcome == ok", Value(macro.Steps[1], "condition"));
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

    /// <summary>
    /// A step whose one parameter holds the value it is written as. A parameter written as a bare
    /// string in a file is text and nothing else, so a condition — which is a shape — is written the
    /// way the editor writes one.
    /// </summary>
    private static JsonObject Step(string id, string type, string name, ExpressionNode written)
        => new()
        {
            ["id"] = id,
            ["type"] = type,
            ["params"] = new JsonObject { [name] = FormulaJson.Write(written) },
        };

    private static string Value(MacroStep step, string parameter)
        => step.Parameters.Single(candidate => candidate.Name == parameter).Value;
}
