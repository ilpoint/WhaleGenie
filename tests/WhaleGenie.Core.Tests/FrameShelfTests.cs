using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// How often this machine hands over a picture, and when a look is served the one already in hand.
/// Both belong to the machine rather than to a step or to a run: there is one screen, so several
/// macros recognising pictures at the same moment share one ceiling and one picture, and what these
/// cases set is the machine's answers and what they read is what a look did with them.
/// </summary>
public class FrameShelfTests
{
    private static ExecutableStep Step(string type, params ExecutableParameter[] parameters)
        => SampleFiles.Step(type, parameters);

    private static ExecutableParameter Param(string name, string text = "")
        => Fields.Text(name, text);

    /// <summary>One field whose value is already a shape rather than a line.</summary>
    private static ExecutableParameter Param(string name, ExpressionNode value)
    => Fields.Computed(name, value);

    private static ExecutableParameter Region(int x, int y, int width, int height)
        => SampleFiles.Region((x, y, width, height));

    private static ExecutableParameter Hit(string key, string name)
        => SampleFiles.Hit(key, name);

    /// <summary>The reference pictures a find or wait step is given, in the shape a package holds.</summary>
    private static ExecutableParameter Pictures(params string[] written)
        => new()
        {
            Name = "images",
            Rows = [.. written.Select(text =>
                Cells.Of(("image", text)))],
        };

    private static ExecutableStep Find(params ExecutableParameter[] rest)
        => Step("vision.findImage", [Pictures("ok.png"), Param("confidence", "90"), .. rest]);

    private static ExecutableStep Body(params ExecutableStep[] steps)
        => Step("control.repeat", Param("times", "4"), Param("intervalMs", "0"),
            Fields.Steps("body", steps));

    /// <summary>One run of a macro, on the machine these cases are looking at.</summary>
    private static Task<RunResult> Run(FakeDeviceLayer devices, params ExecutableStep[] steps)
        => new MacroRunner(new VariableStore(), new QuietHost(), devices).RunAsync(steps);

    [Fact]
    public async Task A_run_takes_no_more_pictures_a_second_than_the_cap_allows()
    {
        // Five pictures a second is one every two hundred milliseconds, so four looks cannot come
        // back in less than three of those.
        var devices = new FakeDeviceLayer();
        devices.Frames.MaxFramesPerSecond = 5;
        devices.Vision.Matches.Add(new ImageMatch(0.99, new ScreenPoint(1, 1), new ScreenSize(1, 1)));

        var started = Stopwatch.GetTimestamp();
        var result = await Run(devices, Body(Find()));
        var took = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        Assert.True(result.Succeeded);
        Assert.Equal(4, devices.Screen.Captures.Count);
        Assert.True(took >= 590, $"four looks 200 ms apart take at least 600 ms, took {took:0}");
    }

    [Fact]
    public async Task A_cap_above_the_display_rate_is_the_display_rate()
    {
        // A cap nobody can use: the display draws every seventeen milliseconds, so a run asking for
        // a hundred and twenty pictures a second still cannot see more than the display shows.
        var devices = new FakeDeviceLayer();
        devices.Screen.RefreshMilliseconds = 17;
        devices.Frames.MaxFramesPerSecond = 120;
        devices.Vision.Matches.Add(new ImageMatch(0.99, new ScreenPoint(1, 1), new ScreenSize(1, 1)));

        var started = Stopwatch.GetTimestamp();
        await Run(devices, Body(Find()));
        var took = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        // Four looks on a 17 ms beat is three gaps, so anything under fifty means the cap of 120 was
        // used instead of the display.
        Assert.True(took >= 50, $"the display's own rate holds the run to 17 ms, took {took:0}");
    }

    /// <summary>
    /// The beat belongs to the machine: a run that looks straight after another takes the turn after
    /// it rather than a clock of its own. Two macros fired together would otherwise each get the
    /// whole ceiling, which is twice the pictures a second the person asked for.
    /// </summary>
    [Fact]
    public async Task A_look_waits_for_the_turn_the_look_before_it_took()
    {
        var devices = new FakeDeviceLayer();
        devices.Frames.MaxFramesPerSecond = 10;
        devices.Vision.Matches.Add(new ImageMatch(0.99, new ScreenPoint(1, 1), new ScreenSize(1, 1)));

        var started = Stopwatch.GetTimestamp();
        await Run(devices, Find());
        await Run(devices, Find());
        var took = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        Assert.Equal(2, devices.Screen.Captures.Count);
        Assert.True(took >= 90, $"the second look waits out the machine's beat, took {took:0}");
    }

    /// <summary>
    /// The same beat for runs that really are going at once: eight looks between them cannot come
    /// back in less than the seven turns those looks are.
    /// </summary>
    [Fact]
    public async Task Two_runs_looking_at_once_share_the_machines_beat()
    {
        var devices = new FakeDeviceLayer();
        devices.Frames.MaxFramesPerSecond = 10;
        devices.Vision.Matches.Add(new ImageMatch(0.99, new ScreenPoint(1, 1), new ScreenSize(1, 1)));

        var started = Stopwatch.GetTimestamp();
        await Task.WhenAll(Run(devices, Body(Find())), Run(devices, Body(Find())));
        var took = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        Assert.Equal(8, devices.Screen.Captures.Count);
        Assert.True(took >= 690, $"eight looks on one beat take at least 700 ms, took {took:0}");
    }

    [Fact]
    public async Task A_picture_young_enough_to_reuse_is_read_again_rather_than_taken_again()
    {
        var devices = new FakeDeviceLayer();
        devices.Frames.CacheMilliseconds = 500;
        devices.Vision.Matches.Add(new ImageMatch(0.99, new ScreenPoint(1, 1), new ScreenSize(1, 1)));

        await Run(devices, Find(Region(10, 10, 20, 20)), Find(Region(30, 5, 10, 10)));

        // The whole screen once, and both steps cut what they asked for out of it.
        var asked = Assert.Single(devices.Screen.Captures);
        Assert.Equal((0, 0, 100, 50), (asked.X, asked.Y, asked.Width, asked.Height));
    }

    /// <summary>
    /// A picture one run took is worth reading by the next: what a macro sees is what the screen
    /// shows, not what that macro happened to take itself, so a second macro looking at the same
    /// window within the keep is served instead of asking the screen again.
    /// </summary>
    [Fact]
    public async Task A_picture_one_run_took_is_read_again_by_the_next()
    {
        var devices = new FakeDeviceLayer();
        devices.Frames.CacheMilliseconds = 1000;
        devices.Vision.Matches.Add(new ImageMatch(0.99, new ScreenPoint(1, 1), new ScreenSize(1, 1)));

        await Run(devices, Find(Region(10, 10, 20, 20)));
        await Run(devices, Find(Region(30, 5, 10, 10)));

        Assert.Single(devices.Screen.Captures);
    }

    /// <summary>
    /// A keep shorter than one beat cannot hold a picture until the next look, so reading more than
    /// the rectangle asked for would cost a whole screen to save nothing: at five looks a second the
    /// look after this one is two hundred milliseconds away, and nothing kept for a hundred is still
    /// there.
    /// </summary>
    [Fact]
    public async Task A_picture_kept_for_less_than_a_beat_reads_only_the_rectangle_asked_for()
    {
        var devices = new FakeDeviceLayer();
        devices.Frames.MaxFramesPerSecond = 5;
        devices.Frames.CacheMilliseconds = 100;
        devices.Vision.Matches.Add(new ImageMatch(0.99, new ScreenPoint(1, 1), new ScreenSize(1, 1)));

        await Run(devices, Find(Region(10, 10, 20, 20)));

        var asked = Assert.Single(devices.Screen.Captures);
        Assert.Equal((10, 10, 20, 20), (asked.X, asked.Y, asked.Width, asked.Height));
    }

    /// <summary>
    /// A picture step is held to the same cap as one that looks for something: saving a picture a
    /// run asked for is looking at the screen like any other step, so the cap belongs to the machine
    /// rather than to the actions that recognise things.
    /// </summary>
    [Fact]
    public async Task A_picture_step_is_held_to_the_same_cap_as_one_that_looks_for_something()
    {
        var devices = new FakeDeviceLayer();
        devices.Frames.MaxFramesPerSecond = 5;
        var capture = Step("vision.capture", Region(0, 0, 4, 4), Param("path", "saved.png"));

        var started = Stopwatch.GetTimestamp();
        await Run(devices, Body(capture));
        var took = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        Assert.True(took >= 590, $"four pictures 200 ms apart take at least 600 ms, took {took:0}");
    }

    /// <summary>A run with nobody watching it: nothing is written down and nothing is agreed to.</summary>
    private sealed class QuietHost : IRunHost
    {
        public void Log(LogEntry entry)
        {
        }

        public string Text(LogEntry entry) => entry.Key;

        public Task<bool> BeforeStep(ExecutableStep step, int depth, CancellationToken token)
            => Task.FromResult(true);

        public Task<StepErrorChoice> Ask(string step, string reason, string detail, string comment,
            CancellationToken token) => Task.FromResult(StepErrorChoice.Stop);

        public Task<AskAnswer> AskUser(AskQuestion question, CancellationToken token)
            => Task.FromResult(AskAnswer.Cancel);

        public Task<bool> ConfirmIrreversible(ExecutableStep step, CancellationToken token)
            => Task.FromResult(true);
    }
}
