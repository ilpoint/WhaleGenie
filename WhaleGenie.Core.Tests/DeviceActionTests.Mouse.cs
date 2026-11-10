using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Devices.Platform;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Recording;
using WhaleGenie.Core.Variables;
using WhaleGenie.GraphicsCapture;

namespace WhaleGenie.Core.Tests;

// Clicks, runs of key presses, scrolling and dragging as the engine sends them.
public partial class DeviceActionTests
{
    [Fact]
    public async Task A_click_goes_to_the_point_it_names()
    {
        var (_, devices, _) = await RunAsync(
        [
            Step("input.mouseClick",
                Param("button", "right"), Param("x", "100"), Param("y", "200"),
                Param("repeat", "2"), Param("intervalMs", "40")),
        ]);

        Assert.Equal(["click right 100 200 2 40"], devices.Calls);
    }

    [Fact]
    public async Task A_click_with_no_point_uses_where_the_pointer_already_is()
    {
        var devices = new FakeDeviceLayer();
        devices.Screen.Cursor = new ScreenPoint(7, 9);
        var (_, _, _) = await RunAsync(
            [Step("input.mouseClick", Param("button", "left"), Param("x", ""), Param("y", ""))],
            devices);

        Assert.Equal(["click left 7 9 1 0"], devices.Calls);
    }

    /// <summary>
    /// A step told to work at the pointer works where the pointer is, whatever numbers it carries:
    /// the numbers it holds are of the pointer as it stood while the macro was being written, and
    /// reading them would click the corner of the screen instead.
    /// </summary>
    [Fact]
    public async Task A_step_told_to_work_at_the_pointer_uses_where_the_pointer_is()
    {
        var devices = new FakeDeviceLayer();
        devices.Screen.Cursor = new ScreenPoint(7, 9);
        var (result, _, _) = await RunAsync(
        [
            Step("input.mouseClick", Param("button", "left"), Param("useCursor", "true"),
                Param("x", "100"), Param("y", "200")),
        ], devices);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal(["click left 7 9 1 0"], devices.Calls);
    }

    /// <summary>
    /// The other mouse steps answer the same question the same way: pressing, letting go, scrolling
    /// and double clicking can each be told to do it wherever the pointer is.
    /// </summary>
    [Fact]
    public async Task Every_mouse_step_can_be_told_to_work_at_the_pointer()
    {
        var devices = new FakeDeviceLayer();
        devices.Screen.Cursor = new ScreenPoint(7, 9);
        var (result, _, _) = await RunAsync(
        [
            Step("input.mouseDoubleClick", Param("button", "left"), Param("useCursor", "true"),
                Param("x", "100"), Param("y", "200")),
            Step("input.mouseDown", Param("button", "left"), Param("useCursor", "true"),
                Param("x", "100"), Param("y", "200")),
            Step("input.mouseUp", Param("button", "left"), Param("useCursor", "true"),
                Param("x", "100"), Param("y", "200")),
            Step("input.mouseScroll", Param("direction", "down"), Param("amount", "3"),
                Param("useCursor", "true"), Param("x", "100"), Param("y", "200")),
        ], devices);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal(["click left 7 9 2 0", "down left 7 9", "up left 7 9", "scroll down 360 7 9"],
            devices.Calls);
    }

    /// <summary>
    /// A step that works at the pointer does not need the window its numbers would have been
    /// counted from, so a window that has since been closed does not fail it — the pointer is
    /// somewhere on the screen whether that window is open or not.
    /// </summary>
    [Fact]
    public async Task A_step_at_the_pointer_does_not_need_the_window_its_numbers_are_counted_from()
    {
        var devices = WithAWindow();
        devices.Screen.Cursor = new ScreenPoint(7, 9);
        var (result, _, _) = await RunAsync(
        [
            Step("input.mouseClick", Param("button", "left"), Param("useCursor", "true"),
                Param("x", "10"), Param("y", "20")),
        ], devices);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal(["click left 7 9 1 0"], devices.Calls);
    }

    [Fact]
    public async Task A_click_can_hold_the_button_down()
    {
        var (result, devices, _) = await RunAsync(
        [
            Step("input.mouseClick", Param("button", "left"), Param("x", "4"), Param("y", "5"),
                Param("repeat", "2"), Param("intervalMs", "0"), Param("holdMs", "1")),
        ]);

        // A hold has to be a press and a release of its own, because the device's click is a tap.
        Assert.True(result.Succeeded);
        Assert.Equal(["down left 4 5", "up left 4 5", "down left 4 5", "up left 4 5"], devices.Calls);
    }

    [Fact]
    public async Task A_double_click_clicks_twice()
    {
        var (_, devices, _) = await RunAsync(
        [
            Step("input.mouseDoubleClick",
                Param("button", "left"), Param("x", "10"), Param("y", "20")),
        ]);

        Assert.Equal(["click left 10 20 2 0"], devices.Calls);
    }

    /// <summary>
    /// A click that is allowed to drift lands inside the spread it asked for rather than on the one
    /// pixel, which is how a button stops being pressed in exactly the same place every time.
    /// </summary>
    [Fact]
    public async Task A_click_that_may_drift_lands_inside_its_spread()
    {
        var (_, devices, _) = await RunAsync(
        [
            Step("input.mouseClick", Param("button", "left"), Param("x", "100"), Param("y", "200"),
                Param("variance", "6")),
        ]);

        var call = Assert.Single(devices.Calls);
        var parts = call.Split(' ');
        var x = int.Parse(parts[2], CultureInfo.InvariantCulture);
        var y = int.Parse(parts[3], CultureInfo.InvariantCulture);
        Assert.InRange(x, 94, 106);
        Assert.InRange(y, 194, 206);
    }

    /// <summary>
    /// A step that asks for it leaves the pointer where it found it: the click happened, and the
    /// mouse is back where the user left it rather than sitting over the game.
    /// </summary>
    [Fact]
    public async Task A_click_can_put_the_pointer_back_where_it_was()
    {
        var devices = new FakeDeviceLayer();
        devices.Screen.Cursor = new ScreenPoint(300, 400);

        var (result, _, _) = await RunAsync(
        [
            Step("input.mouseClick", Param("button", "left"), Param("x", "10"), Param("y", "20"),
                Param("moveBack", "true")),
        ], devices);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal(["click left 10 20 1 0", "move 300 400 0"], devices.Calls);
    }

    /// <summary>Nothing is moved back unless the step asked, so the usual click is one device call.</summary>
    [Fact]
    public async Task A_click_that_was_not_asked_to_put_the_pointer_back_does_not()
    {
        var devices = new FakeDeviceLayer();
        devices.Screen.Cursor = new ScreenPoint(300, 400);

        await RunAsync(
            [Step("input.mouseClick", Param("button", "left"), Param("x", "10"), Param("y", "20"))],
            devices);

        Assert.Equal(["click left 10 20 1 0"], devices.Calls);
    }

    /// <summary>
    /// A run of combinations goes out in the order it is written, which is what makes a combo a
    /// combo rather than a single press of several keys.
    /// </summary>
    [Fact]
    public async Task A_key_run_sends_its_combinations_in_the_order_they_are_written()
    {
        var (result, devices, _) = await RunAsync(
        [
            Step("input.keySequence",
                Rows("keySequence", Press("Ctrl+A"), Press("B"), Press("Shift+Del")),
                Param("holdMs", "10"), Param("gapMs", "0")),
        ]);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal(["hotkey Ctrl|A 10", "hotkey B 10", "hotkey Shift|Del 10"], devices.Calls);
    }

    /// <summary>
    /// The beat of a run is the gap between one press and the next, and a row may have a beat of
    /// its own. There is nothing to wait for after the last press.
    /// </summary>
    [Fact]
    public async Task A_key_run_waits_between_presses_and_not_after_the_last()
    {
        // How many beats were taken is read off the quickest of a few rounds, and the beat itself is
        // a second long: how long a round took is otherwise at the mercy of whatever else the
        // machine is doing, and two beats have to be a number a busy round cannot be mistaken for.
        var quickest = double.MaxValue;
        for (var round = 0; round < 3; round++)
        {
            var started = Stopwatch.GetTimestamp();

            var (result, devices, _) = await RunAsync(
            [
                Step("input.keySequence",
                    Rows("keySequence", Press("A"), Press("B", holdMs: 30)),
                    Param("holdMs", "5"), Param("gapMs", "1000")),
            ]);

            var waited = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

            Assert.True(result.Succeeded, result.Key);
            Assert.Equal(["hotkey A 5", "hotkey B 30"], devices.Calls);
            Assert.True(waited >= 900, $"one beat of 1000ms took {waited:0}ms, under the beat");
            quickest = Math.Min(quickest, waited);
        }

        // One beat of 1000ms: two of them would be past 2000.
        Assert.True(quickest < 1900, $"the quickest round took {quickest:0}ms, which is two beats");
    }

    /// <summary>A row that says its own gap is the one place where the beat is not the run's.</summary>
    [Fact]
    public async Task A_row_of_a_key_run_can_have_a_beat_of_its_own()
    {
        var quickest = double.MaxValue;
        for (var round = 0; round < 3; round++)
        {
            var started = Stopwatch.GetTimestamp();

            var (result, devices, _) = await RunAsync(
            [
                Step("input.keySequence",
                    Rows("keySequence", Press("A", gapMs: 0), Press("B"), Press("C")),
                    Param("holdMs", "5"), Param("gapMs", "1000")),
            ]);

            var waited = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

            Assert.True(result.Succeeded, result.Key);
            Assert.Equal(["hotkey A 5", "hotkey B 5", "hotkey C 5"], devices.Calls);
            Assert.True(waited >= 900, $"one beat of 1000ms took {waited:0}ms, under the beat");
            quickest = Math.Min(quickest, waited);
        }

        // The first row asked for no beat after it, so the run waited the run's own gap only once:
        // taking it after every row would be past 2000.
        Assert.True(quickest < 1900, $"the quickest round took {quickest:0}ms, which is two beats");
    }

    [Fact]
    public async Task A_key_run_can_be_played_more_than_once()
    {
        var (result, devices, _) = await RunAsync(
        [
            Step("input.keySequence", Rows("keySequence", Press("A"), Press("B")),
                Param("holdMs", "1"), Param("gapMs", "0"), Param("repeat", "2")),
        ]);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal(["hotkey A 1", "hotkey B 1", "hotkey A 1", "hotkey B 1"], devices.Calls);
    }

    /// <summary>A run with nothing in it is the macro's own mistake, so it is said in a sentence.</summary>
    [Fact]
    public async Task A_key_run_with_no_presses_says_so()
    {
        var (result, _, _) = await RunAsync(
            [Step("input.keySequence", Rows("keySequence"), Param("gapMs", "0"))]);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.NoKeys", result.Key);
    }

    [Fact]
    public async Task A_double_click_can_be_sent_more_than_once()
    {
        var (_, devices, _) = await RunAsync(
        [
            Step("input.mouseDoubleClick", Param("button", "left"), Param("x", "3"), Param("y", "4"),
                Param("repeat", "2"), Param("intervalMs", "0")),
        ]);

        Assert.Equal(["click left 3 4 2 0", "click left 3 4 2 0"], devices.Calls);
    }
}
