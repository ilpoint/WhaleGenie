using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Variables;
using WhaleGenie.GraphicsCapture;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// How often a run asks the screen for a picture, and when it reads one it already has. Both answers
/// come from the settings window rather than from a step, so what these cases set is the policy the
/// engine is handed and what they read is what the run did with it.
/// </summary>
public class FramePolicyTests
{
    private static ExecutableStep Step(string type, params ExecutableParameter[] parameters)
        => SampleFiles.Step(type, parameters);

    private static ExecutableParameter Param(string name, string text = "")
        => new() { Name = name, Text = text };

    private static ExecutableParameter Region(int x, int y, int width, int height)
        => SampleFiles.Region((x, y, width, height));

    private static ExecutableParameter Hit(string key, string name)
        => SampleFiles.Hit(key, name);

    /// <summary>The reference pictures a find or wait step is given, in the shape a package holds.</summary>
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

    private static ExecutableStep Find(params ExecutableParameter[] rest)
        => Step("vision.findImage", [Pictures("ok.png"), Param("confidence", "90"), .. rest]);

    private static ExecutableStep Body(params ExecutableStep[] steps)
        => Step("control.repeat", Param("times", "4"), Param("intervalMs", "0"),
            new ExecutableParameter { Name = "body", Steps = steps });

    [Fact]
    public async Task A_run_takes_no_more_pictures_a_second_than_the_cap_allows()
    {
        // Five pictures a second is one every two hundred milliseconds, so four looks cannot come
        // back in less than three of those.
        var devices = new FakeDeviceLayer();
        devices.Frames.MaxFramesPerSecond = 5;
        devices.Vision.Matches.Add(new ImageMatch(0.99, new ScreenPoint(1, 1), new ScreenSize(1, 1)));

        var started = Stopwatch.GetTimestamp();
        var result = await new MacroRunner(new VariableStore(), new QuietHost(), devices)
            .RunAsync([Body(Find())]);
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
        await new MacroRunner(new VariableStore(), new QuietHost(), devices).RunAsync([Body(Find())]);
        var took = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        // Four looks on a 17 ms beat is three gaps, so anything under fifty means the cap of 120 was
        // used instead of the display.
        Assert.True(took >= 50, $"the display's own rate holds the run to 17 ms, took {took:0}");
    }

    [Fact]
    public async Task A_picture_young_enough_to_reuse_is_read_again_rather_than_taken_again()
    {
        var devices = new FakeDeviceLayer();
        devices.Frames.CacheMilliseconds = 500;
        devices.Vision.Matches.Add(new ImageMatch(0.99, new ScreenPoint(1, 1), new ScreenSize(1, 1)));

        await new MacroRunner(new VariableStore(), new QuietHost(), devices)
            .RunAsync([Find(Region(10, 10, 20, 20)), Find(Region(30, 5, 10, 10))]);

        // The whole screen once, and both steps cut what they asked for out of it.
        var asked = Assert.Single(devices.Screen.Captures);
        Assert.Equal((0, 0, 100, 50), (asked.X, asked.Y, asked.Width, asked.Height));
    }

    [Fact]
    public async Task A_picture_step_is_held_to_the_same_cap_as_one_that_looks_for_something()
    {
        // Saving pictures a run asked for is looking at the screen like any other step, so the cap
        // belongs to the run rather than to the actions that recognise things.
        var devices = new FakeDeviceLayer();
        devices.Frames.MaxFramesPerSecond = 5;
        var capture = Step("vision.capture", Region(0, 0, 4, 4), Param("path", "saved.png"));

        var started = Stopwatch.GetTimestamp();
        await new MacroRunner(new VariableStore(), new QuietHost(), devices).RunAsync([Body(capture)]);
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
