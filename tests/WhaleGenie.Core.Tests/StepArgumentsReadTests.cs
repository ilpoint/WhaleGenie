using System;

using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// Reading one field of a step: a number held to the box its unit declares, an empty interval that
/// means one screen refresh, a wait bent by the run's dice, and a field that names a value in the
/// table rather than holding one.
/// </summary>
public class StepArgumentsReadTests
{
    private static StepArguments Args(string type, VariableStore? variables = null, int refresh = 16,
        EvaluationContext? context = null, params ExecutableParameter[] parameters)
        => new(
            new ExecutableStep { Type = type, Parameters = [.. parameters] },
            variables ?? new VariableStore(),
            refresh,
            context ?? EvaluationContext.System);

    [Fact]
    public void A_number_at_the_top_of_its_box_is_taken_and_one_past_it_is_refused()
    {
        Assert.Equal(100,
            Args("vision.findImage", parameters: [Fields.Text("tolerance", "100")]).Number("tolerance"));

        var past = Assert.Throws<StepFailure>(() => Args("vision.findImage",
            parameters: [Fields.Text("tolerance", "101")]).Number("tolerance"));
        Assert.Equal(RunKeys.OutOfRange, past.Key);

        Assert.Throws<StepFailure>(() => Args("vision.findImage",
            parameters: [Fields.Text("tolerance", "-1")]).Number("tolerance"));
    }

    [Fact]
    public void A_pixel_at_the_edges_of_its_box_is_taken_and_one_past_them_is_refused()
    {
        Assert.Equal(100000,
            Args("input.mouseMove", parameters: [Fields.Text("x", "100000")]).Number("x"));
        Assert.Equal(-100000,
            Args("input.mouseMove", parameters: [Fields.Text("x", "-100000")]).Number("x"));

        Assert.Throws<StepFailure>(() => Args("input.mouseMove",
            parameters: [Fields.Text("x", "100001")]).Number("x"));
        Assert.Throws<StepFailure>(() => Args("input.mouseMove",
            parameters: [Fields.Text("x", "-100001")]).Number("x"));
    }

    [Fact]
    public void An_empty_interval_is_one_screen_refresh_or_a_millisecond()
    {
        Assert.Equal(16, Args("control.waitUntil").Number("intervalMs"));
        Assert.Equal(1, Args("control.waitUntil", refresh: 0).Number("intervalMs"));
    }

    [Fact]
    public void A_wait_is_bent_by_the_runs_own_dice_and_by_nothing_else()
    {
        var expected = (int)Math.Round(500 * (1 + (((new Random(7).NextDouble() * 2) - 1) * 0.2)));
        var bent = Args("input.keyPress",
            context: new EvaluationContext(TimeProvider.System, new Random(7)),
            parameters: [Fields.Text("holdMs", "500", jitter: 0.2m)]).Number("holdMs");

        Assert.Equal(expected, bent);
        Assert.Equal(500,
            Args("input.keyPress", parameters: [Fields.Text("holdMs", "500")]).Number("holdMs"));
        Assert.Equal(0, Args("input.keyPress",
            parameters: [Fields.Text("holdMs", "0", jitter: 1m)]).Number("holdMs"));
    }

    [Fact]
    public void A_name_field_reads_the_value_it_names_and_keeps_one_it_does_not()
    {
        var variables = new VariableStore();
        variables.Local.Set("where", Value.FromText("left"));

        Assert.Equal("left", Args("control.setVariable", variables,
            parameters: [Fields.Reference("name", "where")]).ValueOf("name").AsText());
        Assert.Equal("nobody", Args("control.setVariable",
            parameters: [Fields.Reference("name", "nobody")]).ValueOf("name").AsText());
        Assert.True(Args("control.setVariable").ValueOf("name").IsNull);

        // A name shape that is empty but is not "nothing was written": the field was answered
        // with a group around empty text, so the name it holds is empty rather than absent.
        Assert.True(Args("control.setVariable",
            parameters: [Fields.Computed("name", new GroupNode(Formula.Text(string.Empty)))])
            .ValueOf("name").IsNull);
    }

    [Fact]
    public void A_combination_is_split_at_pluses_with_spaces_and_empty_pieces_dropped()
    {
        Assert.Equal(["Ctrl", "Shift", "S"], StepArguments.Chords("Ctrl + Shift + S"));
        Assert.Equal(["Ctrl", "S"], StepArguments.Chords("Ctrl++S"));
        Assert.Empty(StepArguments.Chords(" + "));
        Assert.Equal(["F8"], StepArguments.Chords(" F8 "));
    }
}
