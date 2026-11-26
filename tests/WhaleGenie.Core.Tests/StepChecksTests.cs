using System;
using System.Collections.Generic;
using System.Linq;

using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// What a step must be given before it runs and what it promises to hand back. The answers come
/// from the catalogue, so an action that is added is checked the moment it is written.
/// </summary>
public class StepChecksTests
{
    private static ExecutableStep Step(string type, params ExecutableParameter[] parameters)
        => new() { Type = type, Parameters = [.. parameters] };

    [Fact]
    public void A_condition_that_says_nothing_is_refused()
    {
        var failure = Assert.Throws<StepFailure>(
            () => StepChecks.RequireCondition(Step("control.waitUntil", Fields.Text("condition", " ")),
                "condition"));

        Assert.Equal(RunKeys.MissingCondition, failure.Key);
    }

    [Fact]
    public void A_condition_that_says_something_passes()
        => StepChecks.RequireCondition(Step("control.waitUntil", Fields.Text("condition", "1 > 0")),
            "condition");

    [Fact]
    public void A_required_field_left_blank_is_refused_by_the_key_the_action_names()
    {
        var definition = ActionSemanticsCatalog.Find("control.waitUntil")!;
        var condition = definition.Parameter("condition")!;

        var failure = Assert.Throws<StepFailure>(
            () => StepChecks.ValidateAnswers(Step(definition.Key, Fields.Text("condition", " "))));

        Assert.Equal(condition.MissingKey, failure.Key);
    }

    [Fact]
    public void A_choice_the_action_does_not_offer_is_refused()
    {
        var definition = ActionSemanticsCatalog.Find("file.readText")!;
        var encoding = definition.Parameter("encoding")!;

        var failure = Assert.Throws<StepFailure>(() => StepChecks.ValidateAnswers(Step(definition.Key,
            Fields.Text("path", "notes.txt"), Fields.Text("encoding", "klingon"))));

        Assert.Equal(encoding.OptionKey, failure.Key);
    }

    [Fact]
    public void A_step_whose_fields_are_filled_is_accepted()
        => StepChecks.ValidateAnswers(Step("control.delay", Fields.Text("ms", "100")));

    [Fact]
    public void A_return_key_the_action_does_not_declare_is_refused()
    {
        var failure = Assert.Throws<StepFailure>(() => StepChecks.ValidateAnswers(
            Step("control.waitUntil",
                Fields.Text("condition", "1 > 0"),
                Fields.Rows("returns", new Dictionary<string, string>
                {
                    ["key"] = "nope",
                    ["name"] = "v",
                }))));

        Assert.Equal(RunKeys.BadResult, failure.Key);
    }

    [Fact]
    public void A_row_column_the_field_does_not_declare_is_refused()
    {
        var failure = Assert.Throws<StepFailure>(() => StepChecks.ValidateAnswers(
            Step("file.readText",
                Fields.Text("path", "notes.txt"),
                Fields.Rows("returns", new Dictionary<string, string> { ["nobody"] = "x" }))));

        Assert.Equal(RunKeys.BadRowColumn, failure.Key);
    }

    [Fact]
    public void A_return_has_to_have_the_shape_the_action_declared()
    {
        var declared = ActionSemanticsCatalog.Find("vision.findImage")!
            .Returns.First(result => result.Key == "match");
        var fields = declared.Fields.Select(field => (field.Name, ValueRules.Empty(field.Type))).ToArray();

        Assert.True(StepChecks.ReturnFits(declared, Value.FromRecord(fields)));
        Assert.True(StepChecks.ReturnFits(declared, Value.Null));
        Assert.True(StepChecks.ReturnFits(declared, Value.FromText(string.Empty)));

        Assert.False(StepChecks.ReturnFits(declared, Value.FromRecord(fields[1..])));
        Assert.False(StepChecks.ReturnFits(declared, Value.FromRecord(
            [.. fields, ("extra", Value.FromText("x"))])));
    }

    [Fact]
    public void A_record_with_a_field_of_the_wrong_kind_does_not_fit()
    {
        var declared = ActionSemanticsCatalog.Find("vision.findImage")!
            .Returns.First(result => result.Key == "match");
        var wrong = declared.Fields.Select(field => (field.Name,
            field.Type == ValueKind.Number ? (Value)Value.FromText("not a number") : Value.FromRecord([])))
            .ToArray();

        Assert.False(StepChecks.ReturnFits(declared, Value.FromRecord(wrong)));
    }

    [Fact]
    public void A_list_of_records_fits_when_every_record_does()
    {
        var declared = ActionSemanticsCatalog.Find("vision.findImage")!
            .Returns.First(result => result.Key == "every");
        var record = Value.FromRecord(
            declared.Fields.Select(field => (field.Name, ValueRules.Empty(field.Type))).ToArray());

        Assert.True(StepChecks.ReturnFits(declared, Value.FromList([record, record])));
        Assert.True(StepChecks.ReturnFits(declared, Value.FromList([])));
        Assert.False(StepChecks.ReturnFits(declared, Value.FromList([record, Value.FromText("x")])));
    }
}
