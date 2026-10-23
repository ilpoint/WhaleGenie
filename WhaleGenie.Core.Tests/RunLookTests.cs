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

/// <summary>
/// What each step that looks at the screen hands over for a debugger to draw: the picture it
/// looked at, where that picture sits, and every mark on it.
/// </summary>
public class RunLookTests
{
    /// <summary>Somewhere to put the looks handed over during a run.</summary>
    private sealed class Watched : IRunLooks
    {
        public List<StepLook> Seen { get; } = [];

        /// <summary>What each step came to, in the order the steps finished.</summary>
        public List<StepRun> Runs { get; } = [];

        /// <summary>Which window each reading said it was going to read, in the order it said so.</summary>
        public List<WindowInfo?> Read { get; } = [];

        public void Look(StepLook look) => Seen.Add(look);

        public void Ran(StepRun run) => Runs.Add(run);

        public void Reading(WindowInfo? window) => Read.Add(window);
    }

    private static ExecutableStep Step(string type, params ExecutableParameter[] parameters)
        => new() { Type = type, Id = "k3f9", Parameters = parameters };

    /// <summary>See <see cref="SampleFiles.Within"/>: the same step, allowed this long.</summary>
    private static ExecutableStep Within(int milliseconds, ExecutableStep step)
        => SampleFiles.Within(milliseconds, step);

    private static ExecutableParameter Param(string name, string text = "")
        => new() { Name = name, Text = text };

    private static ExecutableParameter Body(string name, params ExecutableStep[] steps)
        => new() { Name = name, Steps = steps };

    private static ExecutableParameter When(string name, ExecutableStep step)
        => new() { Name = name, Text = TestConditions.Expression(step) };

    /// <summary>See <see cref="SampleFiles.Returns"/>: the results a step hands over.</summary>
    private static ExecutableParameter Returns(params string[] results)
        => SampleFiles.Returns(results);

    /// <summary>See <see cref="SampleFiles.Hit"/>: a place, a picture or a program and all its parts.</summary>
    private static ExecutableParameter Hit(string key, string name, bool every = false)
        => SampleFiles.Hit(key, name, every);

    /// <summary>
    /// The pictures a step looks for, written the way the dialog writes them: one row each, tried
    /// in the order they are listed.
    /// </summary>
    private static ExecutableParameter Pictures(params string[] written)
        => new()
        {
            Name = "images",
            Rows = [.. written.Select(text => (IReadOnlyDictionary<string, string>)
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["image"] = text,
                })],
        };

    /// <summary>See <see cref="SampleFiles.Capture"/>: a step that copies one part of the screen.</summary>
    private static ExecutableStep Capture(int x, int y, int width, int height,
        params ExecutableParameter[] rest) => SampleFiles.Capture(x, y, width, height, rest);

    /// <summary>See <see cref="SampleFiles.Recognize"/>: a step that reads one part of the screen.</summary>
    private static ExecutableStep Recognize(int x, int y, int width, int height,
        params ExecutableParameter[] rest) => SampleFiles.Recognize(x, y, width, height, rest);

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
        var devices = new FakeDeviceLayer { ReadPageDelayMs = 120 };
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
    private static ExecutableStep Find(string image = @"C:\images\ok.png",
        params (int X, int Y, int Width, int Height)[] regions)
        => Step("vision.findImage", Pictures(image), Param("confidence", "90"),
            new ExecutableParameter
            {
                Name = "region",
                Rows = [.. regions.Select(region => (IReadOnlyDictionary<string, string>)
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["x"] = region.X.ToString(CultureInfo.InvariantCulture),
                        ["y"] = region.Y.ToString(CultureInfo.InvariantCulture),
                        ["width"] = region.Width.ToString(CultureInfo.InvariantCulture),
                        ["height"] = region.Height.ToString(CultureInfo.InvariantCulture),
                    })],
            },
            Hit("match", "match"));

    private static FakeDeviceLayer Screen(int width = 200, int height = 100,
        ImageMatch? match = null)
        => new()
        {
            Desktop = new ScreenLayout(new ScreenArea(0, 0, width, height),
                [new ScreenDisplay(new ScreenArea(0, 0, width, height), true)]),
            Match = match,
            Loaded = new ImageFrame(2, 2, new byte[16]),
            Display = new ImageFrame(width, height, new byte[width * height * 4]),
        };

    private static async Task<RunResult> Run(ExecutableStep step, FakeDeviceLayer devices,
        Watched? looks = null)
        => await RunAll([step], devices, looks);

    private static async Task<RunResult> RunAll(ExecutableStep[] steps, FakeDeviceLayer devices,
        Watched? looks = null)
        => await new MacroRunner(new VariableStore(), new SilentRunHost(), devices, 1, null, looks)
            .RunAsync(steps);

    [Fact]
    public async Task A_picture_that_was_found_is_handed_over_with_where_it_was()
    {
        var devices = Screen(match: new ImageMatch(0.95, new ScreenPoint(30, 40), new ScreenSize(2, 2)));
        var looks = new Watched();

        await Run(Find(), devices, looks);

        var look = Assert.Single(looks.Seen);
        Assert.Equal("vision.findImage", look.StepType);
        Assert.Equal("k3f9", look.StepId);
        Assert.Equal(LookKind.Template, look.Kind);
        Assert.Equal(new ScreenPoint(0, 0), look.Origin);
        Assert.Equal(200, look.Frame.Width);
        Assert.Equal(2, look.Boxes.Count);
        Assert.Equal(LookRole.Area, look.Boxes[0].Role);
        Assert.Equal(LookRole.Hit, look.Boxes[1].Role);
        Assert.Equal(new ScreenPoint(30, 40), look.Boxes[1].Match.Location);
        Assert.Equal(0.95, look.Boxes[1].Match.Score);
        Assert.Equal(2, look.ChosenIndex);
        Assert.NotNull(look.Needle);
        Assert.Equal(0.9, look.Minimum);
    }

    [Fact]
    public async Task A_search_inside_a_region_is_reported_in_screen_pixels()
    {
        var devices = Screen(match: new ImageMatch(0.9, new ScreenPoint(5, 6), new ScreenSize(2, 2)));
        var looks = new Watched();

        await Run(Find(regions: [(10, 20, 50, 40)]), devices, looks);

        var look = Assert.Single(looks.Seen);
        Assert.Equal(new ScreenPoint(10, 20), look.Origin);
        Assert.Equal(50, look.Frame.Width);
        Assert.Equal(40, look.Frame.Height);
        Assert.Equal(new ScreenPoint(10, 20), look.Boxes[0].Match.Location);
        Assert.Equal(50, look.Boxes[0].Match.Size.Width);

        // The hit was found five across and six down inside the region, so on screen it sits at
        // fifteen and twenty-six: the position a mark has to have to be drawn over the right place.
        Assert.Equal(new ScreenPoint(15, 26), look.Boxes[1].Match.Location);
    }

    [Fact]
    public async Task A_step_that_found_nothing_still_hands_over_what_it_looked_at()
    {
        var devices = Screen();
        var looks = new Watched();

        var result = await Run(Find(), devices, looks);

        Assert.Equal(RunStatus.Completed, result.Status);
        var look = Assert.Single(looks.Seen);
        Assert.Equal(0, look.ChosenIndex);
        Assert.Equal(LookRole.Area, Assert.Single(look.Boxes).Role);
    }

    /// <summary>
    /// A step may list several reference pictures — the same button drawn differently from one
    /// screen to the next — and the first one that turns up is the one it goes with.
    /// </summary>
    [Fact]
    public async Task Each_reference_picture_is_tried_in_turn_until_one_turns_up()
    {
        var first = new ImageFrame(3, 3, new byte[36]);
        var second = new ImageFrame(4, 4, new byte[64]);
        var devices = Screen();
        devices.Pictures[@"C:\images\first.png"] = first;
        devices.Pictures[@"C:\images\second.png"] = second;
        devices.PictureAnswers = (needle, _) => needle.Width == second.Width
            ? [new ImageMatch(0.95, new ScreenPoint(30, 40), new ScreenSize(2, 2))]
            : [];

        var looks = new Watched();
        var step = Step("vision.findImage", Pictures(@"C:\images\first.png", @"C:\images\second.png"),
            Param("confidence", "90"), Hit("match", "match"));

        var result = await Run(step, devices, looks);

        Assert.Equal(RunStatus.Completed, result.Status);
        Assert.Equal(2, devices.Queries.Count);
        var look = Assert.Single(looks.Seen);
        Assert.Equal(2, look.PictureNumber);
        Assert.Equal(2, look.PictureCount);
        Assert.Equal(@"C:\images\second.png", look.Looking);

        // The picture that turned up is the one shown beside the answer, so the window says which
        // of the several was the one that matched.
        Assert.Equal(second, look.Needle);
        Assert.Equal(LookRole.Hit, look.Boxes[^1].Role);
        Assert.Equal(new ScreenPoint(30, 40), look.Boxes[^1].Match.Location);
    }

    /// <summary>
    /// "The third one" counts the hits of the picture that turned up: a picture that was not there
    /// contributes nothing to the count, so listing several does not shift which hit a step takes.
    /// </summary>
    [Fact]
    public async Task Which_hit_is_taken_is_counted_inside_the_picture_that_turned_up()
    {
        var first = new ImageFrame(3, 3, new byte[36]);
        var second = new ImageFrame(4, 4, new byte[64]);
        var devices = Screen();
        devices.Pictures[@"C:\images\first.png"] = first;
        devices.Pictures[@"C:\images\second.png"] = second;
        devices.PictureAnswers = (needle, _) => needle.Width == second.Width
            ?
            [
                new ImageMatch(0.95, new ScreenPoint(10, 10), new ScreenSize(2, 2)),
                new ImageMatch(0.95, new ScreenPoint(10, 20), new ScreenSize(2, 2)),
                new ImageMatch(0.95, new ScreenPoint(10, 30), new ScreenSize(2, 2)),
            ]
            : [];

        var looks = new Watched();
        var step = Step("vision.findImage", Pictures(@"C:\images\first.png", @"C:\images\second.png"),
            Param("confidence", "90"), Param("matchIndex", "3"), Hit("match", "match"));

        var result = await Run(step, devices, looks);

        Assert.Equal(RunStatus.Completed, result.Status);
        var look = Assert.Single(looks.Seen);
        Assert.Equal(2, look.PictureNumber);

        // "The third one" counts the hits of the picture that turned up. The picture tried before
        // it had none, so its turn contributes nothing to the count; the mark taken is the third
        // of the second picture's three hits.
        Assert.Equal(3, look.Boxes.Count(box => box.Role is LookRole.Hit or LookRole.Candidate));
        Assert.Equal(LookRole.Hit, look.Boxes[^1].Role);
        Assert.Equal(new ScreenPoint(10, 30), look.Boxes[^1].Match.Location);
    }

    /// <summary>
    /// A picture that is listed but not filled in is one there is nothing to look for, and a step
    /// that lists none at all cannot look for anything — it says so rather than searching for
    /// whatever it happens to have.
    /// </summary>
    [Fact]
    public async Task A_search_with_no_reference_picture_says_what_is_missing()
    {
        var devices = Screen();
        var step = Step("vision.findImage", Pictures("  "), Param("confidence", "90"),
            Hit("match", "match"));

        var result = await Run(step, devices);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal("Run.MissingImage", result.Key);
        Assert.Empty(devices.Queries);
    }

    [Fact]
    public async Task A_wait_that_ran_out_hands_over_the_look_it_gave_up_on()
    {
        var devices = Screen();
        var looks = new Watched();
        var step = Within(60, Step("vision.waitImage", Pictures(@"C:\images\ok.png"),
            Param("confidence", "90"), Param("intervalMs", "10"), Hit("match", "match")));

        var result = await Run(step, devices, looks);

        Assert.Equal(RunStatus.Failed, result.Status);
        var look = Assert.Single(looks.Seen);
        Assert.Equal(0, look.ChosenIndex);
        Assert.True(devices.Searches > 1, $"the picture should be looked for more than once, was {devices.Searches}");
    }

    [Fact]
    public async Task What_was_searched_is_put_together_into_one_picture()
    {
        var quiet = Screen(match: new ImageMatch(0.9, new ScreenPoint(1, 1), new ScreenSize(2, 2)));
        var watched = Screen(match: new ImageMatch(0.9, new ScreenPoint(1, 1), new ScreenSize(2, 2)));
        var looks = new Watched();

        await Run(Find(regions: [(10, 20, 30, 40), (50, 10, 30, 40)]), quiet);
        await Run(Find(regions: [(10, 20, 30, 40), (50, 10, 30, 40)]), watched, looks);

        // The picture the marks are drawn on is laid out of the areas themselves, so a step reads
        // each of them once and once only whether or not anybody is watching.
        Assert.Equal(2, quiet.Calls.Count(call => call.StartsWith("capture ", StringComparison.Ordinal)));
        Assert.Equal(2, watched.Calls.Count(call => call.StartsWith("capture ", StringComparison.Ordinal)));

        var look = Assert.Single(looks.Seen);
        Assert.Equal(new ScreenPoint(10, 10), look.Origin);
        Assert.Equal(70, look.Frame.Width);
        Assert.Equal(50, look.Frame.Height);
        Assert.Equal(2, look.Boxes.Count(box => box.Role == LookRole.Area));
    }

    [Fact]
    public async Task The_same_reference_picture_is_read_from_disk_once_and_a_changed_one_is_read_again()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wg-look-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(path, PngWriter.Encode(new ImageFrame(2, 2, new byte[16])));
        try
        {
            var devices = Screen();
            var runner = new MacroRunner(new VariableStore(), new SilentRunHost(), devices);

            await runner.RunAsync([Find(path)]);
            await runner.RunAsync([Find(path)]);
            Assert.Single(devices.Calls, call => call.StartsWith("load ", StringComparison.Ordinal));

            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(5));
            await runner.RunAsync([Find(path)]);
            Assert.Equal(2, devices.Calls.Count(call => call.StartsWith("load ", StringComparison.Ordinal)));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task A_click_is_handed_over_with_the_place_it_aimed_at()
    {
        var devices = Screen(match: new ImageMatch(0.9, new ScreenPoint(30, 40), new ScreenSize(2, 2)));
        var looks = new Watched();
        var step = Step("vision.clickImage", Pictures(@"C:\images\ok.png"),
            Param("confidence", "90"), Param("offsetX", "3"), Param("offsetY", "-2"),
            Param("button", "left"));

        await Run(step, devices, looks);

        var look = Assert.Single(looks.Seen);
        var aimed = look.Boxes[^1];
        Assert.Equal(LookRole.Target, aimed.Role);

        // The middle of a two by two hit at thirty, forty is thirty-one, forty-one; the step moved
        // three across and two up from there.
        Assert.Equal(new ScreenPoint(34, 39), aimed.Match.Location);
        Assert.Equal(look.Boxes.Count, look.ChosenIndex);
    }

    [Fact]
    public async Task Every_line_that_was_read_is_handed_over_and_not_only_the_one_that_matched()
    {
        var devices = Screen();
        devices.Spans =
        [
            new TextSpan("Save", new ScreenPoint(5, 6), new ScreenSize(20, 8), 30),
            new TextSpan("Cancel", new ScreenPoint(40, 6), new ScreenSize(20, 8), 12),
        ];
        var looks = new Watched();
        var step = Step("ocr.findText", Param("queryText", "Save"), Param("matchMode", "contains"),
            Param("minScore", "0"), Hit("match", "match"));

        await Run(step, devices, looks);

        var look = Assert.Single(looks.Seen);
        Assert.Equal(LookKind.Text, look.Kind);
        Assert.Equal(3, look.Boxes.Count);
        Assert.Equal("Save", look.Boxes[1].Label);
        Assert.Equal(LookRole.Hit, look.Boxes[1].Role);
        Assert.Equal("Cancel", look.Boxes[2].Label);
        Assert.Equal(LookRole.Candidate, look.Boxes[2].Role);
        Assert.Equal(2, look.ChosenIndex);
        Assert.Equal(0, look.Minimum);
    }

    [Fact]
    public async Task Reading_a_region_hands_over_the_writing_with_where_it_was_read()
    {
        var devices = Screen();
        devices.Spans = [new TextSpan("Save", new ScreenPoint(5, 6), new ScreenSize(20, 8), 30)];
        var looks = new Watched();
        var step = Recognize(10, 20, 40, 30, Returns("text=text"));

        await Run(step, devices, looks);

        var look = Assert.Single(looks.Seen);
        Assert.Equal("Save", look.Note);
        Assert.Equal(new ScreenPoint(15, 26), look.Boxes[1].Match.Location);
        Assert.Equal(30, look.Boxes[1].Match.Score);
    }

    [Fact]
    public async Task A_picture_that_was_taken_is_handed_over_as_the_picture_it_took()
    {
        var devices = Screen();
        var looks = new Watched();
        var step = Capture(1, 2, 5, 6, Hit("image", "shot"));

        await Run(step, devices, looks);

        var look = Assert.Single(looks.Seen);
        Assert.Equal(LookKind.Capture, look.Kind);
        Assert.Equal(new ScreenPoint(1, 2), look.Origin);
        Assert.Equal(5, look.Frame.Width);
        Assert.Equal(6, look.Frame.Height);
        Assert.Equal(new ScreenPoint(1, 2), Assert.Single(look.Boxes).Match.Location);
        Assert.Equal("shot", look.Looking);
    }

    [Fact]
    public async Task A_watched_pixel_is_handed_over_with_a_square_of_screen_around_it()
    {
        var devices = Screen();
        var looks = new Watched();
        var step = Step("vision.getPixel", Param("x", "50"), Param("y", "25"),
            Returns("color=color"));

        await Run(step, devices, looks);

        var look = Assert.Single(looks.Seen);
        Assert.Equal(LookKind.Pixel, look.Kind);
        Assert.Equal(2, look.Boxes.Count);
        Assert.Equal(LookRole.Target, look.Boxes[1].Role);
        Assert.Equal(new ScreenPoint(50, 25), look.Boxes[1].Match.Location);
        Assert.Equal(2, look.ChosenIndex);
        Assert.False(look.Frame.IsEmpty);
    }

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
            Param("confidence", "90"), Param("intervalMs", "10"), Hit("match", "match")));

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

    [Fact]
    public async Task Nothing_is_handed_over_for_a_step_that_does_not_look_at_the_screen()
    {
        var devices = Screen();
        var looks = new Watched();
        var step = Step("control.setVariable", Param("name", "n"), Param("scope", "local"),
            Param("value", "1"));

        await Run(step, devices, looks);

        Assert.Empty(looks.Seen);
        Assert.Empty(looks.Read);
    }

    /// <summary>
    /// Which window a step is reading is said before the picture is taken, because it is the one
    /// question a look cannot answer: a picture says what was there, not where the engine looked for
    /// it. That is what puts a frame around the window a run is reading.
    /// </summary>
    [Fact]
    public async Task The_window_a_step_reads_is_said_before_the_picture_is_taken()
    {
        var devices = Screen();
        devices.Windows.Add(new WindowInfo(4242, "Notepad - notes.txt", new ScreenPoint(1000, 500),
            new ScreenSize(800, 600), false, false));
        var looks = new Watched();

        await Run(Step("vision.captureWindow",
            Param("window", new WindowRule(Title: "Notepad").ToJson().ToJsonString()),
            Hit("image", "shot")),
            devices, looks);

        // The window comes with the reading rather than its handle alone, so whoever is drawing the
        // frame has where the window is without asking the desktop a second time.
        var read = Assert.IsType<WindowInfo>(Assert.Single(looks.Read));
        Assert.Equal(4242, read.Handle);
        Assert.Equal(new ScreenPoint(1000, 500), read.Location);
        Assert.Equal(new ScreenSize(800, 600), read.Size);

        // The desktop itself is no window, so a step reading it says so rather than leaving the last
        // window the run happened to read standing as the answer.
        var desktop = new Watched();
        await Run(Capture(1, 2, 5, 6, Hit("image", "shot")), devices, desktop);

        Assert.Null(Assert.Single(desktop.Read));
    }

    [Fact]
    public void Trying_the_looking_of_a_clicking_step_clicks_nothing()
    {
        var devices = Screen(match: new ImageMatch(0.9, new ScreenPoint(30, 40), new ScreenSize(2, 2)));
        var step = Step("vision.clickImage", Pictures(@"C:\images\ok.png"),
            Param("confidence", "90"), Param("offsetX", "3"), Param("offsetY", "-2"),
            Param("button", "left"), Hit("match", "match"));

        var outcome = MacroRunner.LookOnce(step, devices);

        Assert.NotNull(outcome.Look);
        var look = outcome.Look!;
        Assert.Equal(LookKind.Template, look.Kind);
        Assert.Equal(LookRole.Hit, look.Boxes[1].Role);
        Assert.DoesNotContain(devices.Calls, call => call.StartsWith("click ", StringComparison.Ordinal));
        Assert.DoesNotContain(devices.Calls, call => call.StartsWith("keyPress", StringComparison.Ordinal));

        // No aiming mark: nothing was acted on, so there is nowhere the step went.
        Assert.DoesNotContain(look.Boxes, box => box.Role == LookRole.Target);
    }

    [Fact]
    public void A_step_that_does_not_look_at_the_screen_has_nothing_to_try()
    {
        var outcome = MacroRunner.LookOnce(Step("control.setVariable", Param("name", "n")),
            Screen());

        Assert.Null(outcome.Look);
        Assert.Equal("Run.NotALookingStep", outcome.Key);
        Assert.True(MacroRunner.CanLook("vision.findImage"));
        Assert.True(MacroRunner.CanLook("vision.detect"));
        Assert.True(MacroRunner.CanLook("vision.waitDetect"));
        Assert.True(MacroRunner.CanLook("vision.clickDetect"));
        Assert.False(MacroRunner.CanLook("clipboard.writeImage"));
    }

    /// <summary>
    /// 用模型找东西的那几步交出的样子：看的是哪些块、模型认出的每一处（框连同它说是哪一类）、以及
    /// 这一步取的是哪一处。分是按"有多有把握"读数的那一档，与其他找东西的步骤一样。
    /// </summary>
    [Fact]
    public async Task What_a_detection_step_saw_is_handed_over_box_by_box()
    {
        var devices = Screen(width: 400, height: 300);
        devices.Detections.Add(
            new DetectionHit(0.62, new ScreenPoint(20, 30), new ScreenSize(40, 50), 0, "person"));
        devices.Detections.Add(
            new DetectionHit(0.91, new ScreenPoint(120, 60), new ScreenSize(30, 20), 3, "car"));
        var looks = new Watched();

        // 这一步只要第一处（没有勾「全部命中」）：交出来的仍然是模型认出的每一处，窗口里看得见的
        // 也是每一处，只有取走的那一处带着 Hit。
        await Run(Step("vision.detect", Param("model", @"C:\models\yolo.onnx"),
            Param("confidence", "25"), Hit("match", "spot")), devices, looks);

        var look = Assert.Single(looks.Seen);
        Assert.Equal("vision.detect", look.StepType);
        Assert.Equal(LookKind.Detection, look.Kind);
        Assert.Equal(0.25, look.Minimum!.Value, 6);

        // 看的那一块，然后模型认出的两处：都是一样的框，各自带着它说的那一类。
        Assert.Equal(3, look.Boxes.Count);
        Assert.Equal(LookRole.Area, look.Boxes[0].Role);
        Assert.Equal("person", look.Boxes[1].Label);
        Assert.Equal("car", look.Boxes[2].Label);
        Assert.Equal(new ScreenPoint(120, 60), look.Boxes[2].Match.Location);

        // 「阅读顺序」是默认的排序：先上后下，所以取的是上面那一处（第 1 处），另一处是候选。
        Assert.Equal(LookRole.Hit, look.Boxes[1].Role);
        Assert.Equal(LookRole.Candidate, look.Boxes[2].Role);
        Assert.Equal(2, look.ChosenIndex);
    }

    /// <summary>
    /// 等待与点击检测的「试找一下」只找一次：认出东西就交出来，认不出就交一份空的，且绝不点下去。
    /// </summary>
    [Fact]
    public void Trying_the_looking_of_a_detection_click_clicks_nothing()
    {
        var devices = Screen(width: 200, height: 100);
        devices.Detections.Add(
            new DetectionHit(0.7, new ScreenPoint(30, 40), new ScreenSize(10, 10), 0, "person"));
        var step = Step("vision.clickDetect", Param("model", "yolo.onnx"),
            Param("offsetX", "3"), Param("offsetY", "-2"), Param("button", "left"),
            Hit("match", "match"));

        var outcome = MacroRunner.LookOnce(step, devices);

        var look = Assert.IsType<StepLook>(outcome.Look);
        Assert.Equal(LookKind.Detection, look.Kind);
        Assert.Equal(LookRole.Hit, look.Boxes[1].Role);
        Assert.DoesNotContain(devices.Calls,
            call => call.StartsWith("click ", StringComparison.Ordinal));

        // No aiming mark: nothing was acted on, so there is nowhere the step went.
        Assert.DoesNotContain(look.Boxes, box => box.Role == LookRole.Target);
    }

    [Fact]
    public void Trying_the_looking_of_a_wait_does_not_wait()
    {
        var devices = Screen();
        var step = Step("vision.waitImage", Pictures(@"C:\images\ok.png"),
            Param("confidence", "90"), Param("intervalMs", "10"), Hit("match", "match"));

        var outcome = MacroRunner.LookOnce(step, devices);

        Assert.NotNull(outcome.Look);
        var look = outcome.Look!;
        Assert.Equal(0, look.ChosenIndex);
        Assert.Equal(1, devices.Searches);
    }
}
