using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Variables;
using WhaleGenie.GraphicsCapture;


namespace WhaleGenie.Core.Tests;

// What a step worked out: records, failures, timings and skipped steps.
public partial class RunLookTests
{
    /// <summary>
    /// Everything a step worked out is handed over, whether or not the macro keeps it. Ticking a
    /// result is what makes it a variable of its own; it was never the condition for being able to
    /// read what the step had to offer, which is the question a debugger is asked.
    /// </summary>
    [Fact]
    public async Task What_a_step_worked_out_is_handed_over_even_where_nothing_was_ticked()
    {
        var devices = Screen(match: new ImageMatch(0.95, new ScreenPoint(30, 40), new ScreenSize(2, 2)));
        var looks = new Watched();

        // The step is asked to keep the hit and nothing else.
        await Run(Find(), devices, looks);

        var run = Assert.Single(looks.Runs);
        Assert.Equal("k3f9", run.StepId);
        Assert.Equal("vision.findImage", run.StepType);
        Assert.Equal("ok", run.Outcome);
        Assert.Equal(string.Empty, run.FailureKey);

        // The hit comes with the name the macro keeps it under...
        var match = Assert.Single(run.Results, result => result.Key == "match");
        Assert.Equal("match", match.Name);
        Assert.Contains("\"x\"", match.Text, StringComparison.Ordinal);

        // ...and the parts nobody asked for still come, under no name of their own.
        var method = Assert.Single(run.Results, result => result.Key == "method");
        Assert.Equal(string.Empty, method.Name);
        Assert.NotEmpty(method.Text);
    }

    /// <summary>
    /// A result that is a record travels as its shape as well as its text, because the text of a
    /// record is JSON but the text of a list of them is the records one after another, which cannot
    /// be read back into fields. A result that is one piece of data has no shape: its text is the
    /// whole of it, and a debugger showing it as a one-field record would be inventing a field.
    /// </summary>
    [Fact]
    public async Task A_result_that_is_a_record_is_handed_over_with_its_fields()
    {
        var devices = Screen(match: new ImageMatch(0.95, new ScreenPoint(30, 40), new ScreenSize(2, 2)));
        var looks = new Watched();

        await Run(Find(), devices, looks);

        var run = Assert.Single(looks.Runs);
        var hit = Assert.IsType<System.Text.Json.Nodes.JsonObject>(
            Assert.Single(run.Results, result => result.Key == "match").Data);

        // Numbers travel as numbers, the way every other record's do.
        Assert.Equal(31d, hit["x"]!.GetValue<double>());
        Assert.Equal(41d, hit["y"]!.GetValue<double>());
        Assert.Equal("0.95", hit["score"]!.GetValue<double>().ToString(CultureInfo.InvariantCulture));

        // The method is one word, so there is nothing to unfold.
        Assert.Null(Assert.Single(run.Results, result => result.Key == "method").Data);
    }

    /// <summary>A step that failed says why and how long it took, which is what its row could not say.</summary>
    [Fact]
    public async Task A_step_that_failed_is_handed_over_with_its_reason_and_its_time()
    {
        var devices = Screen();
        var looks = new Watched();
        var step = Within(60, Step("vision.waitImage", Pictures(@"C:\images\ok.png"),
            Param("confidence", "90"), Hit("match", "match")));

        var result = await Run(step, devices, looks);

        Assert.Equal(RunStatus.Failed, result.Status);
        var run = Assert.Single(looks.Runs);
        Assert.Equal("failed", run.Outcome);
        Assert.Equal("Run.ImageNotFound", run.FailureKey);
        Assert.NotEmpty(run.FailureDetail);
    }

    /// <summary>
    /// How long a step took is the step's own time, counted around its work rather than around the
    /// pauses the macro wrote beside it. A length of time is the one thing a row cannot say by
    /// looking at it, which is why "this step waited three seconds" is worth handing over.
    /// </summary>
    [Fact]
    public async Task How_long_a_step_took_is_handed_over()
    {
        var looks = new Watched();

        await Run(Step("control.delay", Param("ms", "60")), Screen(), looks);

        var run = Assert.Single(looks.Runs);
        Assert.Equal("ok", run.Outcome);
        Assert.True(run.ElapsedMs >= 50, $"a 60 ms pause cannot have taken {run.ElapsedMs} ms");
    }

    [Fact]
    public async Task A_step_that_was_switched_off_is_handed_over_as_skipped()
    {
        var devices = Screen();
        var looks = new Watched();
        var off = new ExecutableStep
        {
            Type = "control.setVariable",
            Id = "k3f9",
            Parameters = [Param("name", "n"), Param("scope", "local"), Param("value", "1")],
            Meta = StepMeta.Empty.WithEnabled(false),
        };

        await Run(off, devices, looks);

        var run = Assert.Single(looks.Runs);
        Assert.Equal("skipped", run.Outcome);
        Assert.Equal(0, run.ElapsedMs);
        Assert.Empty(run.Results);
    }

    /// <summary>
    /// What a step inside a block worked out belongs to that step rather than to the block around
    /// it: the row a value is read on has to be the row of the step that worked it out.
    /// </summary>
    [Fact]
    public async Task What_a_step_inside_a_block_worked_out_is_filed_under_that_step()
    {
        var devices = Screen(match: new ImageMatch(0.95, new ScreenPoint(30, 40), new ScreenSize(2, 2)));
        var looks = new Watched();
        var find = new ExecutableStep
        {
            Type = "vision.findImage",
            Id = "bbbb",
            Parameters = Find().Parameters,
        };
        var block = new ExecutableStep
        {
            Type = "control.sequence",
            Id = "aaaa",
            Parameters = [Body("steps", find)],
        };

        await RunAll([block], devices, looks);

        // The step inside finishes first, so it is reported first.
        Assert.Equal(["bbbb", "aaaa"], looks.Runs.Select(run => run.StepId));
        Assert.Equal("match",
            Assert.Single(looks.Runs[0].Results, result => result.Key == "match").Name);
        Assert.Empty(looks.Runs[1].Results);
    }
}
