using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.GraphicsCapture;


namespace WhaleGenie.Core.Tests;

// Retries, returns and catches: what one attempt leaves behind and what it hands over.
public partial class RunLookTests
{
    /// <summary>
    /// A retry spends the same clock as the first try: two tries that each take most of the budget
    /// cannot add up to twice the time the step was allowed.
    /// </summary>
    [Fact]
    public async Task Retries_share_one_timeout_budget_and_each_try_is_kept_for_the_debugger()
    {
        var block = new ExecutableStep
        {
            Type = "control.sequence",
            Parameters =
            [
                Body("steps",
                    Step("control.delay", Param("ms", "250")),
                    Step("not.an.action")),
            ],
            Meta = new StepMeta
            {
                TimeoutMs = 1000,
                RetryCount = 5,
                RetryDelayMs = 0,
                OnError = StepErrorAction.Continue,
            },
        };

        var watched = new Watched();
        var result = await new MacroRunner(new VariableStore(), devices: NullDeviceLayer.Instance,
            looks: watched).RunAsync([block]);

        // Six tries at 250 ms each would be 1.5 s with a clock of their own. One 1000 ms budget
        // still ends before the sixth try; a loaded machine gets at least the second one made.
        Assert.True(result.Succeeded, result.Key);

        var run = Assert.Single(watched.Runs, run => run.StepType == "control.sequence");
        Assert.Equal(1000, run.TimeoutMs);
        Assert.True(run.Attempts.Count >= 2, $"only {run.Attempts.Count} try made");
        Assert.Equal("failed", run.Attempts[0].Outcome);
        Assert.NotNull(run.Attempts[0].RemainingMs);
        Assert.True(run.Attempts[^1].Overran);
    }

    [Fact]
    public async Task A_return_is_reported_as_done_and_the_steps_after_it_are_left_skipped()
    {
        var store = new VariableStore();
        var watched = new Watched();
        var steps = new[]
        {
            new ExecutableStep { Type = "control.return", Id = "aaaa" },
            new ExecutableStep
            {
                Type = "control.setVariable",
                Id = "bbbb",
                Parameters =
                [
                    Param("name", "after"), Param("scope", "local"), Param("value", "1"),
                ],
            },
        };

        var result = await new MacroRunner(store, devices: NullDeviceLayer.Instance, looks: watched)
            .RunAsync(steps);

        Assert.True(result.Succeeded, result.Key);
        var returned = Assert.Single(watched.Runs, run => run.StepId == "aaaa");
        Assert.Equal("ok", returned.Outcome);

        // The run debugger reads this name to draw a step it never reached, so a return has to
        // leave the same mark behind as any other early end.
        Assert.Equal("skipped", store.Local.Values["step.bbbb.outcome"].AsText());
        Assert.False(store.TryGet("after", out _));
    }

    [Fact]
    public async Task A_catch_that_declines_a_failure_leaves_the_try_failed_with_the_original_key()
    {
        var store = new VariableStore();
        var watched = new Watched();
        var steps = new[]
        {
            new ExecutableStep
            {
                Type = "control.try",
                Id = "cccc",
                Parameters =
                [
                    Body("body", new ExecutableStep
                    {
                        Type = "control.fail",
                        Id = "dddd",
                        Parameters = [Param("reason", "boom")],
                    }),
                    When("catchWhen", Step("condition.compare",
                        Param("variable", "caught.key"),
                        Param("compareOperator", "equals"),
                        Param("compareValue", "Run.NoDevice"))),
                    Body("catch", new ExecutableStep
                    {
                        Type = "control.setVariable",
                        Id = "eeee",
                        Parameters =
                        [
                            Param("name", "handled"), Param("scope", "local"), Param("value", "1"),
                        ],
                    }),
                    Body("finally", new ExecutableStep
                    {
                        Type = "control.setVariable",
                        Id = "ffff",
                        Parameters =
                        [
                            Param("name", "tidied"), Param("scope", "local"), Param("value", "1"),
                        ],
                    }),
                ],
            },
        };

        var result = await new MacroRunner(store, devices: NullDeviceLayer.Instance, looks: watched)
            .RunAsync(steps);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal(RunKeys.StepFailedByMacro, result.Key);
        Assert.Equal("boom", result.Detail);

        var tried = Assert.Single(watched.Runs, run => run.StepId == "cccc");
        Assert.Equal("failed", tried.Outcome);
        Assert.Equal(RunKeys.StepFailedByMacro, tried.FailureKey);
        Assert.Equal("skipped", store.Local.Values["step.eeee.outcome"].AsText());
        Assert.Equal("ok", store.Local.Values["step.ffff.outcome"].AsText());
    }

    /// <summary>
    /// A device call that has already started cannot be torn down, so it is judged when it comes
    /// back: the step fails with the generic timeout rather than pretending the call was stopped.
    /// </summary>
    [Fact]
    public async Task A_call_that_returns_after_the_budget_is_judged_when_it_returns()
    {
        var devices = new FakeDeviceLayer();
        devices.Browser.PageReadDelay = 120;
        var page = Within(40, Step("browser.readPage"));
        var watched = new Watched();

        var watch = Stopwatch.StartNew();
        var result = await new MacroRunner(new VariableStore(), devices: devices, looks: watched)
            .RunAsync([page]);
        watch.Stop();

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal(RunKeys.Timeout, result.Key);
        Assert.True(watch.ElapsedMilliseconds >= 100,
            $"the call was supposed to finish before being judged, took {watch.ElapsedMilliseconds} ms");

        var run = Assert.Single(watched.Runs);
        var attempt = Assert.Single(run.Attempts);
        Assert.True(attempt.Overran);
        Assert.True(attempt.ElapsedMs >= 100, $"the call took {attempt.ElapsedMs:0} ms");
    }

    /// <summary>
    /// A step that looks for a picture, with the places to look given the way the dialog gives
    /// them: one row per rectangle.
    /// </summary>
    /// <summary>One place to look, written the way a row of the field is written.</summary>
    private static IReadOnlyDictionary<string, ExpressionNode> Region(
        (int X, int Y, int Width, int Height) region)
        => Cells.Of(
            ("x", region.X.ToString(CultureInfo.InvariantCulture)),
            ("y", region.Y.ToString(CultureInfo.InvariantCulture)),
            ("width", region.Width.ToString(CultureInfo.InvariantCulture)),
            ("height", region.Height.ToString(CultureInfo.InvariantCulture)));

    private static ExecutableStep Find(string image = @"C:\images\ok.png",
            params (int X, int Y, int Width, int Height)[] regions)
            => Step("vision.findImage", Pictures(image), Param("confidence", "90"),
                Fields.Rows("region", [.. regions.Select(Region)]),
                Hit("match", "match"));

    private static FakeDeviceLayer Screen(int width = 200, int height = 100,
        ImageMatch? match = null)
    {
        var devices = new FakeDeviceLayer();
        devices.Screen.Desktop = new ScreenLayout(new ScreenArea(0, 0, width, height),
            [new ScreenDisplay(new ScreenArea(0, 0, width, height), true)]);
        devices.Vision.Match = match;
        devices.Vision.Loaded = new ImageFrame(2, 2, new byte[16]);
        devices.Screen.Display = new ImageFrame(width, height, new byte[width * height * 4]);
        return devices;
    }
}
