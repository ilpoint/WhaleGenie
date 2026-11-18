using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Recording;
using WhaleGenie.Core.Variables;
using WhaleGenie.GraphicsCapture;
using WhaleGenie.Platform.Windows;

namespace WhaleGenie.Platform.Tests;

// Records of what was found, and the pointing moves that aim at them.
public partial class DeviceActionTests
{
    [Fact]
    public async Task A_found_image_reports_its_parts_by_name()
    {
        var devices = new FakeDeviceLayer();
        devices.Vision.Match = new ImageMatch(0.98, new ScreenPoint(3, 4), new ScreenSize(10, 10));

        var (result, _, store) = await RunAsync(
        [
            Step("vision.findImage", Pictures("a.png"), Param("confidence", "90"),
                Param("region", ""), Hit("match", "where")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Equal("8,9", Centre(store, "where"));
        Assert.Equal(8, Field(store, "where", "x").AsNumber());
        Assert.Equal(9, Field(store, "where", "y").AsNumber());
        Assert.Equal(10, Field(store, "where", "width").AsNumber());
        Assert.Equal(10, Field(store, "where", "height").AsNumber());
        Assert.Equal(0.98, Field(store, "where", "score").AsNumber(), 3);
    }

    [Fact]
    public async Task An_image_that_is_not_there_clears_the_parts()
    {
        var devices = new FakeDeviceLayer();
        devices.Vision.Match = null;
        devices.Vision.Loaded = new ImageFrame(2, 2, new byte[16]);

        var (result, _, store) = await RunAsync(
        [
            Step("control.setVariable", Param("name", "where.x"), Param("value", "999")),
            Step("vision.findImage", Pictures("a.png"), Param("confidence", "90"),
                Param("region", ""), Hit("match", "where")),
        ], devices);

        Assert.True(result.Succeeded);

        // Finding nothing hands over an empty hit, so the record left over from the pass before —
        // here a variable of the old shape, given by name — is not what a later step reads.
        Assert.Equal(string.Empty, store.Local.Values["where"].AsText());
        Assert.False(store.Local.Values["where"].IsRecord);
    }

    [Fact]
    public async Task A_captured_image_is_found_through_its_variable()
    {
        var devices = new FakeDeviceLayer();
        devices.Vision.Match = new ImageMatch(0.95, new ScreenPoint(0, 0), new ScreenSize(10, 10));

        var (result, _, _) = await RunAsync(
        [
            Capture(0, 0, 10, 10, Parts("image", "shot")),
            Step("vision.findImage", Pictures(Formula.Name("shot")), Param("confidence", "90"),
                Param("region", ""), Hit("match", "where")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Contains("findAll 10x10 90 Normed", devices.Calls);
    }

    [Fact]
    public async Task Text_keeps_its_commas_instead_of_becoming_a_number()
    {
        var (result, devices, _) = await RunAsync(
            [Step("input.typeText", Param("typedText", "1,000"), Param("intervalMs", "0"))]);

        Assert.True(result.Succeeded, result.Key);
        Assert.Contains("type 1,000 0", devices.Calls);
    }

    [Fact]
    public async Task A_search_region_can_come_from_a_variable()
    {
        var devices = new FakeDeviceLayer();
        devices.Vision.Loaded = new ImageFrame(2, 2, new byte[16]);
        devices.Vision.Match = new ImageMatch(0.99, new ScreenPoint(0, 0), new ScreenSize(2, 2));

        var (result, _, _) = await RunAsync(
        [
            Step("control.setVariable", Param("name", "box"), Param("value", "10,20,30,40")),
            Step("vision.findImage", Pictures("a.png"), Param("confidence", "90"),
                RegionFrom(Formula.Name("box")), Hit("match", "where")),
        ], devices);

        Assert.True(result.Succeeded, result.Key);
        Assert.Contains("capture 10 20 30 40", devices.Calls);
    }

    /// <summary>
    /// A picture taken earlier can be the place a step looks in, so the screen is grabbed once and
    /// looked at as often as the macro likes rather than again for every search.
    /// </summary>
    [Fact]
    public async Task A_picture_taken_earlier_can_be_searched_inside()
    {
        var devices = new FakeDeviceLayer();
        devices.Screen.Display = Picture("#000000,#FF0000");
        devices.Vision.Match = new ImageMatch(0.99, new ScreenPoint(1, 0), new ScreenSize(1, 1));

        var (result, _, store) = await RunAsync(
        [
            Capture(0, 0, 2, 1, Parts("image", "shot")),
            Step("vision.findImage", Pictures("a.png"), Param("confidence", "90"),
                RegionFrom(Formula.Name("shot")), Hit("match", "where")),
        ], devices);

        Assert.True(result.Succeeded, result.Key);

        // The screen was grabbed for the capture and not again for the search: what was searched is
        // the picture the capture left behind.
        Assert.Single(devices.Calls, call => call.StartsWith("capture", StringComparison.Ordinal));
        Assert.Equal("1,0", Centre(store, "where"));
    }

    /// <summary>A place to look that cannot be read is said in a sentence rather than guessed at.</summary>
    [Fact]
    public async Task A_region_that_makes_no_sense_fails_the_step_with_a_sentence()
    {
        var devices = new FakeDeviceLayer();
        devices.Vision.Loaded = new ImageFrame(2, 2, new byte[16]);
        devices.Vision.Match = new ImageMatch(0.99, new ScreenPoint(0, 0), new ScreenSize(2, 2));

        var (result, _, _) = await RunAsync(
        [
            Step("vision.findImage", Pictures("a.png"), Param("confidence", "90"),
                Region((10, 20, 0, 40)), Hit("match", "where")),
        ], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.BadRegion", result.Key);
    }

    [Fact]
    public async Task A_found_text_reports_its_parts_by_name()
    {
        var devices = new FakeDeviceLayer();
        devices.Ocr.Spans = [new TextSpan("Save", new ScreenPoint(10, 20), new ScreenSize(40, 12), 0.9)];

        var (result, _, store) = await RunAsync(
        [
            Step("ocr.findText", Param("queryText", "Save"), Param("region", ""),
                Param("matchMode", "contains"), Hit("match", "found")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Equal("30,26", Centre(store, "found"));
        Assert.Equal("Save", Field(store, "found", "text").AsText());
        Assert.Equal(30, Field(store, "found", "x").AsNumber());
        Assert.Equal(0.9, Field(store, "found", "score").AsNumber(), 3);
    }

    [Fact]
    public async Task A_number_may_be_written_as_an_expression()
    {
        var (result, devices, store) = await RunAsync(
        [
            Step("control.setVariable", Param("name", "spot"), Param("value", "5")),
            Step("input.mouseMove",
                Fields.Computed("x", Formula.Binary("+", Formula.Name("spot"), Formula.Number(1))),
                Fields.Computed("y", Formula.Binary("*", Formula.Name("spot"), Formula.Number(2))),
                Param("durationMs", "0")),
        ]);

        Assert.True(result.Succeeded);
        Assert.Contains("move 6 10 0", devices.Calls);
        Assert.Equal(5, store.Local.Values["spot"].AsNumber());
    }

    [Fact]
    public async Task A_step_moves_to_the_match_a_find_reported()
    {
        var devices = new FakeDeviceLayer();
        devices.Vision.Match = new ImageMatch(0.97, new ScreenPoint(100, 200), new ScreenSize(20, 20));

        var (result, _, _) = await RunAsync(
        [
            Step("vision.findImage", Pictures("a.png"), Param("confidence", "90"),
                Param("region", ""), Hit("match", "where")),
            Step("input.mouseMove", Param("x", Formula.Binary("+", Formula.Call("field", Formula.Name("where"), Formula.Text("x")), Formula.Number(3))),
                Param("y", Formula.Call("field", Formula.Name("where"), Formula.Text("y"))),
                Param("durationMs", "0")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Contains("move 113 210 0", devices.Calls);
    }

    // ------------------------------------------------------------- wait until

    [Fact]
    public async Task A_wait_until_holds_on_the_first_look_and_keeps_how_long_it_waited()
    {
        var (result, _, store) = await RunAsync(
        [
            Within(2000, Step("control.waitUntil",
                Param("condition", "true"), Param("intervalMs", "10"),
                Returns("elapsed=waited"))),
        ]);

        Assert.True(result.Succeeded);
        Assert.True(store.Local.Values["waited"].AsNumber() >= 0);

    }

    [Fact]
    public async Task A_wait_until_that_never_holds_stops_the_macro()
    {
        var (result, _, store) = await RunAsync(
        [
            Within(300, Step("control.waitUntil",
                Param("condition", "false"), Param("intervalMs", "10"),
                Returns("elapsed=waited"))),
        ]);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal("Run.WaitTimeout", result.Key);

        // How long the wait lasted is worth keeping even when it was cut short: that number is what
        // makes a macro too slow to catch something diagnosable. It reads a little under the step's
        // 300 ms because the step's clock starts before the waiting does.
        var waited = store.Local.Values["waited"].AsNumber();
        Assert.True(waited >= 250, $"the wait reported {waited} ms of an allowed 300");
    }

    /// <summary>
    /// Running out of time is a failure like any other, and what happens next is the step's own
    /// failure rule that decides it: a wait that may carry on is told so in the step's settings
    /// rather than in a field of the action, which is where every other step says it too.
    /// </summary>
    [Fact]
    public async Task A_wait_until_that_runs_out_fails_the_step_and_hands_it_to_the_failure_rule()
    {
        var host = new SilentRunHost();
        var store = new VariableStore();

        var result = await new MacroRunner(store, host).RunAsync(
        [
            Within(80, Step("control.waitUntil",
                Param("condition", "false"), Param("intervalMs", "10")),
                StepErrorAction.Continue, id: "wait"),
            Step("control.setVariable", Param("name", "after"), Param("scope", "local"),
                Param("value", "1")),
        ]);

        Assert.True(result.Succeeded);
        Assert.Contains(host.Entries, entry => entry.Key == "Run.WaitTimeout");
        Assert.Equal("failed", store.Local.Values["step.wait.outcome"].AsText());
        Assert.Equal(1, store.Local.Values["after"].AsNumber());
    }

    [Fact]
    public async Task A_wait_until_says_which_condition_it_waited_for()
    {
        var host = new SilentRunHost();

        await new MacroRunner(new VariableStore(), host).RunAsync(
        [
            Within(2000, Step("control.waitUntil",
                Param("condition", "true"), Param("intervalMs", "10"))),
        ]);

        var line = Assert.Single(host.Entries, entry => entry.Key == "Log.WaitedFor");
        Assert.Equal("true", line.Arguments[1]);
    }

    [Fact]
    public async Task A_wait_until_with_nothing_to_wait_for_is_refused()
    {
        var (result, _, _) = await RunAsync([Within(10, Step("control.waitUntil"))]);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal("Run.MissingCondition", result.Key);
    }

    /// <summary>
    /// The random chance is a roll of the dice, so the two ends are what can be said about it: a
    /// hundred per cent always holds and never waits, and none always runs out of time.
    /// </summary>
    [Fact]
    public async Task A_random_chance_of_a_hundred_always_holds_and_of_none_never_does()
    {
        var (certain, _, certainStore) = await RunAsync(
        [
            Within(200, Step("control.waitUntil",
                Param("condition", "true"), Param("intervalMs", "10"),
                Returns("elapsed=certainly"))),
        ]);

        Assert.True(certain.Succeeded);
        Assert.True(certainStore.Local.Values["certainly"].AsNumber() < 50,
            "a certainty holds the first time it is asked, so there is nothing to wait for");

        var (never, _, store) = await RunAsync(
        [
            Within(300, Step("control.waitUntil",
                Param("condition", "false"), Param("intervalMs", "10"),
                Returns("elapsed=never")), StepErrorAction.Continue),
        ]);

        // A chance of none never comes up, so the whole clock is waited out; that the run carried
        // on regardless is the step's own failure rule saying so, not a field of the action.
        Assert.True(never.Succeeded, never.Key);
        var waited = store.Local.Values["never"].AsNumber();
        Assert.True(waited >= 250,
            $"a chance of none never comes up, so the whole time is waited out, waited {waited}");
    }

    // ------------------------------------------------------------ mouse routes

    [Fact]
    public async Task A_straight_move_is_the_move_it_has_always_been()
    {
        var (result, devices, _) = await RunAsync(
            [Step("input.mouseMove", Param("x", "400"), Param("y", "0"), Param("durationMs", "300"))]);

        Assert.True(result.Succeeded);
        Assert.Equal(["move 400 0 300"], devices.Calls);
        Assert.Empty(devices.Input.Paths);
    }

    [Fact]
    public async Task A_bent_move_walks_a_path_instead_of_jumping()
    {
        var devices = new FakeDeviceLayer();
        devices.Screen.Cursor = new ScreenPoint(0, 0);

        var (result, _, _) = await RunAsync(
        [
            Step("input.mouseMove", Param("x", "400"), Param("y", "0"),
                Param("durationMs", "300"), Param("style", "smooth")),
        ], devices);

        Assert.True(result.Succeeded);
        var path = Assert.Single(devices.Input.Paths);
        Assert.Equal(new ScreenPoint(0, 0), path[0]);
        Assert.Equal(new ScreenPoint(400, 0), path[^1]);
        Assert.True(path.Any(point => Math.Abs(point.Y) >= 20), "the path should bow out of the line");
        Assert.DoesNotContain(devices.Calls, call => call.StartsWith("move ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_bent_move_with_no_duration_borrows_one_so_that_it_can_bend()
    {
        var devices = new FakeDeviceLayer();
        devices.Screen.Cursor = new ScreenPoint(0, 0);

        var (result, _, _) = await RunAsync(
            [Step("input.mouseMove", Param("x", "400"), Param("y", "0"), Param("style", "human"))],
            devices);

        Assert.True(result.Succeeded);
        var call = Assert.Single(devices.Calls);
        Assert.StartsWith("along ", call, StringComparison.Ordinal);
        Assert.EndsWith(" 200", call, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_relative_move_bends_from_wherever_the_pointer_is()
    {
        var devices = new FakeDeviceLayer();
        devices.Screen.Cursor = new ScreenPoint(20, 30);

        var (result, _, _) = await RunAsync(
        [
            Step("input.mouseMoveRelative", Param("dx", "200"), Param("dy", "0"),
                Param("durationMs", "300"), Param("style", "smooth")),
        ], devices);

        Assert.True(result.Succeeded);
        var path = Assert.Single(devices.Input.Paths);
        Assert.Equal(new ScreenPoint(20, 30), path[0]);
        Assert.Equal(new ScreenPoint(220, 30), path[^1]);
    }

    [Fact]
    public async Task A_straight_drag_is_still_one_call_with_its_steps()
    {
        var (result, devices, _) = await RunAsync(
        [
            Step("input.mouseDrag",
                Param("startX", "0"), Param("startY", "0"), Param("endX", "300"), Param("endY", "0"),
                Param("durationMs", "300"), Param("dragSteps", "30")),
        ]);

        Assert.True(result.Succeeded);
        Assert.Equal(["drag left 0 0 300 0 300 30"], devices.Calls);
    }

    /// <summary>
    /// A drag that is told to hold at the end stays down over where it arrived for a moment before
    /// letting go, which is what a drop into a slot needs: letting go the instant the pointer gets
    /// there is how a drag onto something turns into a drag past it.
    /// </summary>
    [Fact]
    public async Task A_drag_can_hold_at_the_end_before_letting_go()
    {
        var started = Stopwatch.GetTimestamp();
        var (result, devices, _) = await RunAsync(
        [
            Step("input.mouseDrag",
                Param("startX", "0"), Param("startY", "0"), Param("endX", "300"), Param("endY", "0"),
                Param("durationMs", "0"), Param("dragSteps", "3"), Param("settleMs", "60")),
        ]);

        var took = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        Assert.True(result.Succeeded, result.Key);

        // Being held means the press and the release are the step's own two calls with the travel
        // between them, rather than the one call the device offers, which lets go on arrival.
        Assert.Equal(3, devices.Calls.Count);
        Assert.Equal("down left 0 0", devices.Calls[0]);
        Assert.StartsWith("along ", devices.Calls[1]);
        Assert.Equal("up left 300 0", devices.Calls[2]);
        Assert.True(took >= 50, $"the button should stay down the 60 ms it was told to, took {took:0}");
    }

    [Fact]
    public async Task A_hand_like_drag_is_the_same_path_with_the_button_held()
    {
        var (result, devices, _) = await RunAsync(
        [
            Step("input.mouseDrag",
                Param("startX", "0"), Param("startY", "0"), Param("endX", "300"), Param("endY", "0"),
                Param("durationMs", "300"), Param("dragSteps", "30"), Param("style", "human")),
        ]);

        Assert.True(result.Succeeded);
        var path = Assert.Single(devices.Input.Paths);
        Assert.Equal(31, path.Count);
        Assert.Equal(new ScreenPoint(0, 0), path[0]);
        Assert.Equal(new ScreenPoint(300, 0), path[^1]);
        Assert.Contains("dragAlong left 31 300", devices.Calls);
    }
}
