using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The actions that drive the machine. They run against a stand-in for the device layer, so
/// the checks are about what the engine asked for, not about what happened to the desktop.
/// </summary>
public class DeviceActionTests
{
    private static ExecutableStep Step(string type, params ExecutableParameter[] parameters)
        => new() { Type = type, Parameters = parameters };

    private static ExecutableParameter Param(string name, string text = "")
        => new() { Name = name, Text = text };

    private static ExecutableParameter Body(string name, params ExecutableStep[] steps)
        => new() { Name = name, Steps = steps };

    private static ExecutableParameter When(string name, ExecutableStep step)
        => new() { Name = name, Condition = step };

    private static async Task<(RunResult Result, FakeDeviceLayer Devices, VariableStore Store)> RunAsync(
        ExecutableStep[] steps, FakeDeviceLayer? devices = null)
    {
        var layer = devices ?? new FakeDeviceLayer();
        var store = new VariableStore();
        var result = await new MacroRunner(store, new SilentRunHost(), layer).RunAsync(steps);
        return (result, layer, store);
    }

    [Fact]
    public async Task A_run_that_stops_on_a_failure_leaves_a_picture_of_the_screen()
    {
        var devices = new FakeDeviceLayer { Display = Picture("#FF0000,#00FF00") };
        var host = new SilentRunHost();

        var result = await new MacroRunner(new VariableStore(), host, devices)
        {
            FailureScreenshot = true,
        }.RunAsync([Step("nope.unknown")]);

        Assert.Equal(RunStatus.Failed, result.Status);

        // The picture is the whole primary screen, written as a PNG inside the log folder.
        var picture = Assert.Single(devices.Blobs);
        Assert.StartsWith("logs", picture.Key);
        Assert.Contains("failure", picture.Key);
        Assert.EndsWith(".png", picture.Key);
        Assert.Equal(100, BinaryPrimitives.ReadInt32BigEndian(picture.Value.AsSpan(16)));
        Assert.Equal(50, BinaryPrimitives.ReadInt32BigEndian(picture.Value.AsSpan(20)));

        // The log says where it went, which is how the user finds it.
        Assert.Contains(host.Entries, entry => entry.Key == "Run.FailurePicture");
    }

    [Fact]
    public async Task A_picture_goes_to_the_folder_the_program_asks_for()
    {
        // The application hands in a folder of its own, so the pictures land beside the program
        // instead of inside a project that somebody may well be sharing.
        var devices = new FakeDeviceLayer { Display = Picture("#FF0000,#00FF00") };
        var folder = Path.Combine("C:", "whalegenie", "logs");

        var result = await new MacroRunner(new VariableStore(), new SilentRunHost(), devices)
        {
            FailureScreenshot = true,
            FailureFolder = folder,
        }.RunAsync([Step("nope.unknown")]);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.StartsWith(folder, Assert.Single(devices.Blobs).Key, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_run_that_finishes_leaves_no_picture()
    {
        var devices = new FakeDeviceLayer { Display = Picture("#FF0000") };

        var result = await new MacroRunner(new VariableStore(), new SilentRunHost(), devices)
        {
            FailureScreenshot = true,
        }.RunAsync([Step("control.delay", Param("ms", "0"))]);

        Assert.True(result.Succeeded);
        Assert.Empty(devices.Blobs);
    }

    [Fact]
    public async Task A_picture_that_cannot_be_taken_does_not_bring_the_run_down()
    {
        // Everything the fake is asked for is refused, so taking the picture fails too. The run
        // has already failed, and recording that must not be able to make it worse.
        var devices = new FakeDeviceLayer { Unavailable = true };
        var host = new SilentRunHost();

        var result = await new MacroRunner(new VariableStore(), host, devices)
        {
            FailureScreenshot = true,
        }.RunAsync([Step("nope.unknown")]);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Empty(devices.Blobs);
        Assert.Contains(host.Entries, entry => entry.Key == "Run.FailurePictureFailed");
    }

    [Fact]
    public async Task A_key_press_reaches_the_keyboard()
    {
        var (result, devices, _) = await RunAsync(
            [Step("input.keyPress", Param("key", "F5"), Param("holdMs", "50"))]);

        Assert.True(result.Succeeded);
        Assert.Equal(["keyPress F5 50"], devices.Calls);
    }

    [Fact]
    public async Task Holding_and_releasing_a_key_reaches_the_keyboard()
    {
        var (result, devices, _) = await RunAsync(
        [
            Step("input.keyDown", Param("key", "Shift")),
            Step("input.keyUp", Param("key", "Shift")),
        ]);

        Assert.True(result.Succeeded);
        Assert.Equal(["keyDown Shift", "keyUp Shift"], devices.Calls);
    }

    [Fact]
    public async Task A_key_the_macro_leaves_down_is_let_go_when_the_run_ends()
    {
        var host = new SilentRunHost();
        var devices = new FakeDeviceLayer();

        var result = await new MacroRunner(new VariableStore(), host, devices)
            .RunAsync([Step("input.keyDown", Param("key", "Shift"))]);

        Assert.True(result.Succeeded);

        // A key still down when the macro ends would keep shifting everything the user types
        // afterwards, so the run hands the keyboard back the way it found it.
        Assert.Equal(["keyDown Shift", "keyUp Shift"], devices.Calls);
        Assert.Contains(host.Entries, entry => entry.Key == "Run.LetGo");
    }

    [Fact]
    public async Task A_button_the_macro_leaves_down_is_let_go_when_the_run_ends()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
            [Step("input.mouseDown", Param("button", "left"), Param("x", "5"), Param("y", "6"))],
            devices);

        Assert.True(result.Succeeded);

        // The button is let go where it was pressed: the macro may well have moved on since, and
        // a release somewhere else would drop whatever the pointer is over now.
        Assert.Equal(["down left 5 6", "up left 5 6"], devices.Calls);
    }

    [Fact]
    public async Task A_failed_run_lets_go_of_what_it_left_held_too()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
        [
            Step("input.keyDown", Param("key", "Ctrl")),
            Step("input.mouseDown", Param("button", "right"), Param("x", "3"), Param("y", "4")),
            Step("nope.unknown"),
        ], devices);

        Assert.Equal(RunStatus.Failed, result.Status);

        // A run that failed is exactly the one that must not leave the keyboard and the mouse
        // in a state the user cannot see or undo.
        Assert.Equal(
            ["keyDown Ctrl", "down right 3 4", "keyUp Ctrl", "up right 3 4"],
            devices.Calls);
    }

    [Fact]
    public async Task A_hotkey_reaches_the_keyboard_as_one_chord()
    {
        var (_, devices, _) = await RunAsync(
            [Step("input.hotkey", Param("keys", "Ctrl+Shift+S"), Param("holdMs", "80"))]);

        Assert.Equal(["hotkey Ctrl|Shift|S 80"], devices.Calls);
    }

    [Fact]
    public async Task Typing_reaches_the_keyboard_with_its_interval()
    {
        var (_, devices, _) = await RunAsync(
            [Step("input.typeText", Param("text", "hello"), Param("intervalMs", "25"))]);

        Assert.Equal(["type hello 25"], devices.Calls);
    }

    [Fact]
    public async Task A_key_press_can_be_repeated()
    {
        var (result, devices, _) = await RunAsync(
            [Step("input.keyPress", Param("key", "F5"), Param("holdMs", "20"),
                Param("repeat", "3"), Param("intervalMs", "0"))]);

        Assert.True(result.Succeeded);
        Assert.Equal(["keyPress F5 20", "keyPress F5 20", "keyPress F5 20"], devices.Calls);
    }

    [Fact]
    public async Task A_hotkey_can_be_repeated()
    {
        var (_, devices, _) = await RunAsync(
            [Step("input.hotkey", Param("keys", "Ctrl+S"), Param("holdMs", "10"),
                Param("repeat", "2"), Param("intervalMs", "0"))]);

        Assert.Equal(["hotkey Ctrl|S 10", "hotkey Ctrl|S 10"], devices.Calls);
    }

    [Fact]
    public async Task Input_with_no_mode_goes_to_the_front_window()
    {
        var (_, devices, _) = await RunAsync(
            [Step("input.keyPress", Param("key", "F5"), Param("holdMs", "10"))]);

        Assert.Equal([InputRoute.Front], devices.Routes);
    }

    [Fact]
    public async Task Driver_input_goes_through_the_virtual_device_route()
    {
        var (result, devices, _) = await RunAsync(
        [
            Step("input.keyPress",
                Param("key", "F5"), Param("holdMs", "10"), Param("inputMode", "driver")),
        ]);

        Assert.True(result.Succeeded);

        var route = Assert.Single(devices.Routes);
        Assert.Equal(InputDelivery.Driver, route.Delivery);
        Assert.Equal(["keyPress F5 10"], devices.Calls);
    }

    [Fact]
    public async Task Background_input_is_posted_at_the_window_it_names()
    {
        var devices = new FakeDeviceLayer();
        devices.Windows.Add(new WindowInfo(4242, "Notepad - notes.txt",
            new ScreenPoint(0, 0), new ScreenSize(100, 100), false, false));

        var (result, _, _) = await RunAsync(
        [
            Step("input.keyPress",
                Param("key", "F5"), Param("holdMs", "10"),
                Param("inputMode", "background"), Param("targetWindow", "Notepad")),
        ], devices);

        Assert.True(result.Succeeded);

        var route = Assert.Single(devices.Routes);
        Assert.Equal(InputDelivery.Background, route.Delivery);
        Assert.Equal(4242, route.WindowHandle);
    }

    [Fact]
    public async Task Background_input_without_a_window_fails_instead_of_typing_into_the_wrong_one()
    {
        var (result, devices, _) = await RunAsync(
            [Step("input.keyPress", Param("key", "F5"), Param("inputMode", "background"))]);

        Assert.False(result.Succeeded);
        Assert.Empty(devices.Calls);
    }

    [Fact]
    public async Task A_click_goes_to_the_point_it_names()
    {
        var (_, devices, _) = await RunAsync(
        [
            Step("input.mouseClick",
                Param("button", "right"), Param("x", "100"), Param("y", "200"),
                Param("clicks", "2"), Param("intervalMs", "40")),
        ]);

        Assert.Equal(["click right 100 200 2 40"], devices.Calls);
    }

    [Fact]
    public async Task A_click_with_no_point_uses_where_the_pointer_already_is()
    {
        var devices = new FakeDeviceLayer { Cursor = new ScreenPoint(7, 9) };
        var (_, _, _) = await RunAsync(
            [Step("input.mouseClick", Param("button", "left"), Param("x", ""), Param("y", ""))],
            devices);

        Assert.Equal(["click left 7 9 1 0"], devices.Calls);
    }

    [Fact]
    public async Task A_click_can_hold_the_button_down()
    {
        var (result, devices, _) = await RunAsync(
        [
            Step("input.mouseClick", Param("button", "left"), Param("x", "4"), Param("y", "5"),
                Param("clicks", "2"), Param("intervalMs", "0"), Param("holdMs", "1")),
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

    [Fact]
    public async Task Moving_and_dragging_reach_the_mouse()
    {
        var (_, devices, _) = await RunAsync(
        [
            Step("input.mouseMove", Param("x", "11"), Param("y", "22"), Param("durationMs", "30")),
            Step("input.mouseMoveRelative", Param("dx", "3"), Param("dy", "-4"), Param("durationMs", "0")),
            Step("input.mouseDrag",
                Param("startX", "1"), Param("startY", "2"), Param("endX", "3"), Param("endY", "4"),
                Param("button", "left"), Param("durationMs", "200"), Param("steps", "10")),
            Step("input.mouseScroll", Param("direction", "up"), Param("amount", "3"),
                Param("x", "5"), Param("y", "6")),
        ]);

        Assert.Equal(
        [
            "move 11 22 30",
            "moveBy 3 -4 0",
            "drag left 1 2 3 4 200 10",
            // Three notches reach the device as wheel units, 120 to a notch.
            "scroll up 360 5 6",
        ], devices.Calls);
    }

    [Fact]
    public async Task Scrolling_can_be_measured_in_pixels()
    {
        var (_, devices, _) = await RunAsync(
        [
            Step("input.mouseScroll", Param("direction", "down"), Param("unit", "pixels"),
                Param("amount", "200"), Param("x", "1"), Param("y", "2")),
        ]);

        Assert.Equal(["scroll down 200 1 2"], devices.Calls);
    }

    [Fact]
    public async Task A_smooth_scroll_goes_out_in_even_events()
    {
        var (_, devices, _) = await RunAsync(
        [
            Step("input.mouseScroll", Param("direction", "down"), Param("unit", "pixels"),
                Param("amount", "300"), Param("smoothMs", "30"), Param("x", "0"), Param("y", "0")),
        ]);

        // 30 ms is two 15 ms pauses, so the same 300 units leave as 150 at a time.
        Assert.Equal(["scroll down 150 0 0", "scroll down 150 0 0"], devices.Calls);
    }

    /// <summary>A device layer with one window open at 1000,500, the place the anchoring tests move.</summary>
    private static FakeDeviceLayer WithAWindow()
    {
        var devices = new FakeDeviceLayer();
        devices.Windows.Add(new WindowInfo(1, "Notepad - notes.txt",
            new ScreenPoint(1000, 500), new ScreenSize(800, 600), false, false));
        return devices;
    }

    [Fact]
    public async Task A_point_is_measured_from_the_screen_unless_the_step_says_otherwise()
    {
        var devices = WithAWindow();
        var (result, _, _) = await RunAsync(
        [
            Step("input.mouseMove", Param("x", "10"), Param("y", "20"),
                Param("anchorMode", "screen"), Param("anchorWindow", "Notepad")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Equal(["move 10 20 0"], devices.Calls);
    }

    [Fact]
    public async Task A_point_can_be_measured_from_a_window_corner()
    {
        var devices = WithAWindow();
        var (result, _, _) = await RunAsync(
        [
            Step("input.mouseClick", Param("button", "left"), Param("x", "10"), Param("y", "20"),
                Param("anchorMode", "window"), Param("anchorWindow", "Notepad")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Equal(["findWindow Notepad", "click left 1010 520 1 0"], devices.Calls);
    }

    [Fact]
    public async Task A_point_can_be_measured_from_inside_the_window_border()
    {
        var devices = WithAWindow();
        devices.ClientOrigins[1] = new ScreenPoint(1008, 531);

        var (_, _, _) = await RunAsync(
        [
            Step("input.mouseDown", Param("button", "left"), Param("x", "5"), Param("y", "6"),
                Param("anchorMode", "client"), Param("anchorWindow", "Notepad")),
        ], devices);

        // The button is also let go where it was pressed once the run ends, from the place the
        // press was read at rather than a fresh reading of where the window is now.
        Assert.Equal(["findWindow Notepad", "clientOrigin 1", "down left 1013 537", "up left 1013 537"],
            devices.Calls);
    }

    [Fact]
    public async Task A_drag_measures_both_ends_from_the_same_window_corner()
    {
        var devices = WithAWindow();
        var (_, _, _) = await RunAsync(
        [
            Step("input.mouseDrag",
                Param("startX", "10"), Param("startY", "20"),
                Param("endX", "30"), Param("endY", "40"),
                Param("button", "left"), Param("durationMs", "0"), Param("steps", "2"),
                Param("anchorMode", "window"), Param("anchorWindow", "Notepad")),
        ], devices);

        Assert.Equal(["findWindow Notepad", "drag left 1010 520 1030 540 0 2"], devices.Calls);
    }

    /// <summary>
    /// The whole point of anchoring: the window is dragged somewhere else between two runs and
    /// the same macro still aims at the same place inside it.
    /// </summary>
    [Fact]
    public async Task An_anchored_point_follows_a_window_that_has_been_moved()
    {
        var devices = WithAWindow();
        var step = Step("input.mouseMove", Param("x", "10"), Param("y", "20"),
            Param("anchorMode", "window"), Param("anchorWindow", "Notepad"));

        await RunAsync([step], devices);
        devices.Windows[0] = devices.Windows[0] with { Location = new ScreenPoint(300, 400) };
        await RunAsync([step], devices);

        Assert.Equal(
            ["findWindow Notepad", "move 1010 520 0", "findWindow Notepad", "move 310 420 0"],
            devices.Calls);
    }

    [Fact]
    public async Task Anchoring_to_a_window_that_is_not_open_fails_the_step()
    {
        var (result, devices, _) = await RunAsync(
            [Step("input.mouseMove", Param("x", "1"), Param("y", "2"),
                Param("anchorMode", "window"), Param("anchorWindow", "Gone"))]);

        Assert.False(result.Succeeded);

        // The window was looked for and nothing else was touched.
        Assert.Equal(["findWindow Gone"], devices.Calls);
    }

    [Fact]
    public async Task A_pixel_can_be_read_from_a_window_corner()
    {
        var devices = WithAWindow();
        devices.ClientOrigins[1] = new ScreenPoint(1000, 520);

        var (_, _, _) = await RunAsync(
        [
            Step("vision.getPixel", Param("x", "7"), Param("y", "8"),
                Param("anchorMode", "client"), Param("anchorWindow", "Notepad"),
                Param("resultVariable", "tone")),
        ], devices);

        Assert.Equal(["findWindow Notepad", "clientOrigin 1", "pixel 1007 528"], devices.Calls);
    }

    [Fact]
    public async Task A_search_region_can_be_measured_from_a_window_corner()
    {
        var devices = WithAWindow();
        devices.Match = new ImageMatch(0.99, new ScreenPoint(5, 6), new ScreenSize(4, 4));

        var (_, _, store) = await RunAsync(
        [
            Step("vision.findImage", Param("image", "ok.png"), Param("region", "10,20,30,40"),
                Param("anchorMode", "window"), Param("anchorWindow", "Notepad"),
                Param("resultVariable", "where")),
        ], devices);

        Assert.Contains("capture 1010 520 30 40", devices.Calls);

        // The match is reported in screen pixels, so the step that clicks it needs no arithmetic.
        Assert.Equal("1017", store.Local.Values["where.x"].AsText());
        Assert.Equal("528", store.Local.Values["where.y"].AsText());
    }

    [Fact]
    public async Task Reading_a_pixel_stores_its_colour()
    {
        var devices = new FakeDeviceLayer { Pixel = new PixelColor(0xAB, 0xCD, 0xEF) };
        var (_, _, store) = await RunAsync(
        [
            Step("vision.getPixel", Param("x", "5"), Param("y", "6"),
                Param("resultVariable", "tone"), Param("asHex", "true")),
        ], devices);

        Assert.Equal("#ABCDEF", store.Local.Values["tone"].AsText());
    }

    /// <summary>A picture from rows of #RRGGBB colours, one string per row.</summary>
    private static ImageFrame Picture(params string[] rows)
    {
        var width = rows[0].Split(',').Length;
        var bytes = new byte[width * rows.Length * 4];
        for (var y = 0; y < rows.Length; y++)
        {
            var cells = rows[y].Split(',');
            for (var x = 0; x < width; x++)
            {
                var colour = PixelColor.Parse(cells[x]);
                var at = ((y * width) + x) * 4;
                bytes[at] = colour.B;
                bytes[at + 1] = colour.G;
                bytes[at + 2] = colour.R;
                bytes[at + 3] = 255;
            }
        }

        return new ImageFrame(width, rows.Length, bytes);
    }

    [Fact]
    public async Task A_colour_is_found_where_the_region_shows_it()
    {
        var devices = new FakeDeviceLayer { Display = Picture("#000000,#FF0000", "#000000,#000000") };

        var (result, _, store) = await RunAsync(
        [
            Step("vision.findColor", Param("color", "#FF0000"), Param("tolerance", "1"),
                Param("region", "0,0,2,2"), Param("resultVariable", "spot")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Equal("1,0", store.Local.Values["spot"].AsText());
        Assert.Equal(1, store.Local.Values["spot.x"].AsNumber());
    }

    [Fact]
    public async Task A_colour_that_is_not_there_leaves_the_result_empty()
    {
        var devices = new FakeDeviceLayer { Display = Picture("#000000") };

        var (result, _, store) = await RunAsync(
            [Step("vision.findColor", Param("color", "#FF0000"), Param("resultVariable", "spot"))],
            devices);

        Assert.True(result.Succeeded);
        Assert.Equal(string.Empty, store.Local.Values["spot"].AsText());
        Assert.Equal(string.Empty, store.Local.Values["spot.count"].AsText());
    }

    [Fact]
    public async Task Waiting_for_a_colour_that_never_shows_fails_the_step()
    {
        var devices = new FakeDeviceLayer { Display = Picture("#000000") };

        var (result, _, _) = await RunAsync(
        [
            Step("vision.findColor", Param("color", "#FF0000"),
                Param("timeoutMs", "20"), Param("intervalMs", "5")),
        ], devices);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Watching_one_pixel_goes_on_when_it_shows_the_colour()
    {
        var devices = new FakeDeviceLayer { Pixel = new PixelColor(0xFF, 0x00, 0x00) };
        var host = new SilentRunHost();

        var result = await new MacroRunner(new VariableStore(), host, devices).RunAsync(
            [Step("vision.waitColor", Param("x", "5"), Param("y", "6"), Param("color", "#FF0000"),
                Param("tolerance", "5"), Param("timeoutMs", "200"))]);

        Assert.True(result.Succeeded);
        Assert.Contains(host.Entries, entry => entry.Key == "Run.SawColor");
    }

    [Fact]
    public async Task Watching_one_pixel_that_stays_the_wrong_colour_gives_up()
    {
        var devices = new FakeDeviceLayer { Pixel = new PixelColor(0x00, 0x00, 0x00) };

        var (result, _, _) = await RunAsync(
            [Step("vision.waitColor", Param("x", "5"), Param("y", "6"), Param("color", "#FFFFFF"),
                Param("tolerance", "1"), Param("timeoutMs", "20"))], devices);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task The_second_place_a_colour_shows_is_the_one_a_step_can_take()
    {
        var devices = new FakeDeviceLayer { Display = Picture("#FF0000,#000000,#FF0000") };

        var (_, _, store) = await RunAsync(
        [
            Step("vision.findColor", Param("color", "#FF0000"), Param("tolerance", "1"),
                Param("matchIndex", "2"), Param("region", "0,0,3,1"),
                Param("resultVariable", "spot")),
        ], devices);

        Assert.Equal("2,0", store.Local.Values["spot"].AsText());
    }

    [Fact]
    public async Task Every_place_a_colour_shows_can_be_recorded()
    {
        var devices = new FakeDeviceLayer { Display = Picture("#FF0000,#000000,#FF0000") };

        var (_, _, store) = await RunAsync(
        [
            Step("vision.findColor", Param("color", "#FF0000"), Param("tolerance", "1"),
                Param("allMatches", "true"), Param("region", "0,0,3,1"),
                Param("resultVariable", "spot")),
        ], devices);

        Assert.Equal(2, store.Local.Values["spot.count"].AsNumber());
        Assert.Equal("0,0, 2,0", store.Local.Values["spot.list"].AsText());
    }

    [Fact]
    public async Task Waiting_for_a_still_picture_goes_on_once_the_screen_stops_moving()
    {
        var devices = new FakeDeviceLayer { Display = Picture("#000000") };
        var frames = new Queue<ImageFrame>([Picture("#FFFFFF"), Picture("#00FF00")]);
        devices.Next = () => frames.Count > 1 ? frames.Dequeue() : frames.Peek();

        var host = new SilentRunHost();
        var result = await new MacroRunner(new VariableStore(), host, devices).RunAsync(
            [Step("vision.waitStable", Param("quietMs", "0"), Param("intervalMs", "1"), Param("timeoutMs", "5000"))]);

        Assert.True(result.Succeeded);
        Assert.Contains(host.Entries, entry => entry.Key == "Run.FrameStill");

        // Four looks: the picture the wait opened on, the two the screen moved through, and the one
        // that shows it has stopped. The quiet stretch was nothing, so there was nothing left to
        // wait for rather than a fixed pause.
        Assert.Equal(4, devices.Calls.Count(call => call.StartsWith("capture", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_screen_that_never_holds_still_fails_the_step()
    {
        var devices = new FakeDeviceLayer { Display = Picture("#000000") };
        var moving = true;
        devices.Next = () =>
        {
            moving = !moving;
            return Picture(moving ? "#FF0000" : "#0000FF");
        };

        var (result, _, _) = await RunAsync(
        [
            Step("vision.waitStable", Param("quietMs", "50"), Param("intervalMs", "5"),
                Param("timeoutMs", "60")),
        ], devices);

        Assert.False(result.Succeeded);
    }

    /// <summary>
    /// A picture with a little noise on it, or one that fades slowly, is still a picture that is
    /// not being drawn any more: what counts as a change is the caller's tolerance, not any
    /// difference at all.
    /// </summary>
    [Fact]
    public async Task A_picture_that_only_drifts_a_little_is_still_a_picture()
    {
        var devices = new FakeDeviceLayer { Display = Picture("#808080") };
        var frames = new Queue<ImageFrame>([Picture("#838383")]);
        devices.Next = () => frames.Count > 0 ? frames.Dequeue() : Picture("#838383");

        var (result, _, _) = await RunAsync(
        [
            Step("vision.waitStable", Param("quietMs", "0"), Param("intervalMs", "1"),
                Param("tolerance", "5"), Param("timeoutMs", "100")),
        ], devices);

        Assert.True(result.Succeeded);
    }

    /// <summary>Hits are counted the way a person counts them on screen, not the order they came back in.</summary>
    [Fact]
    public async Task A_picture_can_be_taken_by_its_place_on_the_screen()
    {
        var devices = new FakeDeviceLayer();
        devices.Matches.Add(new ImageMatch(0.99, new ScreenPoint(30, 40), new ScreenSize(1, 1)));
        devices.Matches.Add(new ImageMatch(0.99, new ScreenPoint(10, 10), new ScreenSize(1, 1)));
        devices.Matches.Add(new ImageMatch(0.99, new ScreenPoint(50, 5), new ScreenSize(1, 1)));

        var (_, _, store) = await RunAsync(
        [
            Step("vision.findImage", Param("image", "ok.png"), Param("matchIndex", "2"),
                Param("allMatches", "true"), Param("resultVariable", "where")),
        ], devices);

        Assert.Equal("10,10", store.Local.Values["where"].AsText());
        Assert.Equal(3, store.Local.Values["where.count"].AsNumber());
        Assert.Equal("50,5, 10,10, 30,40", store.Local.Values["where.list"].AsText());
    }

    [Fact]
    public async Task A_search_can_watch_two_regions_at_once()
    {
        var devices = new FakeDeviceLayer();
        devices.Matches.Add(new ImageMatch(0.99, new ScreenPoint(2, 3), new ScreenSize(1, 1)));

        var (_, _, store) = await RunAsync(
        [
            Step("vision.findImage", Param("image", "ok.png"),
                Param("region", "0,0,10,10; 100,100,10,10"),
                Param("matchIndex", "2"), Param("resultVariable", "where")),
        ], devices);

        Assert.Contains("capture 0 0 10 10", devices.Calls);
        Assert.Contains("capture 100 100 10 10", devices.Calls);
        Assert.Equal("102,103", store.Local.Values["where"].AsText());
    }

    /// <summary>The negative conditions are their own actions, so "wait until it is gone" can be written.</summary>
    [Fact]
    public async Task The_negative_conditions_say_the_opposite_of_the_plain_ones()
    {
        var gone = new FakeDeviceLayer { Match = null, ElementExists = false, Spans = [] };
        var there = new FakeDeviceLayer
        {
            Match = new ImageMatch(0.99, new ScreenPoint(1, 1), new ScreenSize(1, 1)),
            ElementExists = true,
            Spans = [new TextSpan("Ready", new ScreenPoint(1, 1), new ScreenSize(1, 1), 0.9)],
        };

        Assert.True(await Holds(gone, Step("condition.imageNotExists", Param("image", "ok.png"))));
        Assert.False(await Holds(there, Step("condition.imageNotExists", Param("image", "ok.png"))));
        Assert.True(await Holds(gone, Step("condition.textNotExists", Param("text", "Ready"))));
        Assert.False(await Holds(there, Step("condition.textNotExists", Param("text", "Ready"))));
        Assert.True(await Holds(gone, Step("condition.uiaNotExists", Param("selector", "Button"))));
        Assert.False(await Holds(there, Step("condition.uiaNotExists", Param("selector", "Button"))));
    }

    /// <summary>A condition, asked the way the engine asks one inside an if.</summary>
    private static async Task<bool> Holds(FakeDeviceLayer devices, ExecutableStep condition)
    {
        var (_, _, store) = await RunAsync(
        [
            Step("control.if", When("condition", condition),
                Body("then", Step("control.setVariable", Param("name", "held"), Param("value", "yes"))),
                Body("else", Step("control.setVariable", Param("name", "held"), Param("value", "no")))),
        ], devices);

        return store.Local.Values["held"].AsText() == "yes";
    }

    [Fact]
    public async Task A_condition_can_be_written_as_an_expression()
    {
        var devices = new FakeDeviceLayer();
        var (_, _, store) = await RunAsync(
        [
            Step("control.setVariable", Param("name", "count"), Param("value", "5")),
            Step("control.setVariable", Param("name", "name"), Param("value", "ok")),
            Step("control.if",
                When("condition", Step("condition.expression",
                    Param("expression", "$count > 3 and contains($name, \"o\")"))),
                Body("then", Step("control.setVariable", Param("name", "hit"), Param("value", "yes"))),
                Body("else", Step("control.setVariable", Param("name", "hit"), Param("value", "no")))),
        ], devices);

        Assert.Equal("yes", store.Local.Values["hit"].AsText());
    }

    [Fact]
    public async Task An_expression_condition_is_false_when_it_does_not_hold()
    {
        var devices = new FakeDeviceLayer();
        var (_, _, store) = await RunAsync(
        [
            Step("control.setVariable", Param("name", "count"), Param("value", "1")),
            Step("control.if",
                When("condition", Step("condition.expression",
                    Param("expression", "$count > 3 or $count == 0"))),
                Body("then", Step("control.setVariable", Param("name", "hit"), Param("value", "yes"))),
                Body("else", Step("control.setVariable", Param("name", "hit"), Param("value", "no")))),
        ], devices);

        Assert.Equal("no", store.Local.Values["hit"].AsText());
    }

    [Fact]
    public async Task An_expression_condition_that_cannot_be_read_fails_the_step()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
        [
            Step("control.if",
                When("condition", Step("condition.expression", Param("expression", "$count +"))),
                Body("then", Step("control.setVariable", Param("name", "hit"), Param("value", "yes")))),
        ], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.BadExpression", result.Key);
    }

    [Fact]
    public async Task Every_point_of_a_colour_comparison_has_to_match()
    {
        var devices = new FakeDeviceLayer { Display = Picture("#FF0000,#00FF00", "#0000FF,#FFFFFF") };

        var (_, _, store) = await RunAsync(
        [
            Step("control.if",
                When("condition", Step("condition.colorsMatch",
                    Param("points", "0,0,#FF0000; 1,0,#00FF00"), Param("tolerance", "1"))),
                Body("then", Step("control.setVariable", Param("name", "hit"), Param("value", "yes"))),
                Body("else", Step("control.setVariable", Param("name", "hit"), Param("value", "no")))),
        ], devices);

        Assert.Equal("yes", store.Local.Values["hit"].AsText());
    }

    [Fact]
    public async Task One_point_that_does_not_match_makes_the_comparison_false()
    {
        var devices = new FakeDeviceLayer { Display = Picture("#FF0000,#00FF00") };

        var (_, _, store) = await RunAsync(
        [
            Step("control.if",
                When("condition", Step("condition.colorsMatch",
                    Param("points", "0,0,#FF0000; 1,0,#123456"), Param("tolerance", "1"))),
                Body("then", Step("control.setVariable", Param("name", "hit"), Param("value", "yes"))),
                Body("else", Step("control.setVariable", Param("name", "hit"), Param("value", "no")))),
        ], devices);

        Assert.Equal("no", store.Local.Values["hit"].AsText());
    }

    [Fact]
    public async Task A_comparison_can_settle_for_one_point_matching()
    {
        var devices = new FakeDeviceLayer { Display = Picture("#FF0000,#00FF00") };

        var (_, _, store) = await RunAsync(
        [
            Step("control.if",
                When("condition", Step("condition.colorsMatch",
                    Param("points", "0,0,#FF0000; 1,0,#123456"),
                    Param("tolerance", "1"), Param("mode", "any"))),
                Body("then", Step("control.setVariable", Param("name", "hit"), Param("value", "yes"))),
                Body("else", Step("control.setVariable", Param("name", "hit"), Param("value", "no")))),
        ], devices);

        Assert.Equal("yes", store.Local.Values["hit"].AsText());
    }

    [Fact]
    public async Task A_point_a_colour_comparison_cannot_read_fails_the_step()
    {
        var devices = new FakeDeviceLayer { Display = Picture("#FF0000") };

        var (result, _, _) = await RunAsync(
        [
            Step("control.if",
                When("condition", Step("condition.colorsMatch", Param("points", "0,0"))),
                Body("then", Step("control.setVariable", Param("name", "hit"), Param("value", "yes")))),
        ], devices);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task A_colour_condition_compares_the_pixel_to_the_colour()
    {
        var devices = new FakeDeviceLayer { Pixel = new PixelColor(0x10, 0x20, 0x30) };

        var (_, _, store) = await RunAsync(
        [
            Step("control.if",
                When("condition", Step("condition.colorEquals",
                    Param("x", "0"), Param("y", "0"), Param("color", "#102030"), Param("tolerance", "5"))),
                Body("then", Step("control.setVariable", Param("name", "hit"), Param("value", "yes"))),
                Body("else", Step("control.setVariable", Param("name", "hit"), Param("value", "no")))),
        ], devices);

        Assert.Equal("yes", store.Local.Values["hit"].AsText());
    }

    [Fact]
    public async Task A_colour_condition_is_false_for_a_different_colour()
    {
        var devices = new FakeDeviceLayer { Pixel = new PixelColor(0xFF, 0xFF, 0xFF) };

        var (_, _, store) = await RunAsync(
        [
            Step("control.if",
                When("condition", Step("condition.colorEquals",
                    Param("x", "0"), Param("y", "0"), Param("color", "#102030"), Param("tolerance", "5"))),
                Body("then", Step("control.setVariable", Param("name", "hit"), Param("value", "yes"))),
                Body("else", Step("control.setVariable", Param("name", "hit"), Param("value", "no")))),
        ], devices);

        Assert.Equal("no", store.Local.Values["hit"].AsText());
    }

    [Fact]
    public async Task A_capture_can_be_searched_for_again()
    {
        var devices = new FakeDeviceLayer
        {
            Match = new ImageMatch(0.98, new ScreenPoint(3, 4), new ScreenSize(10, 10)),
        };

        var (result, _, store) = await RunAsync(
        [
            Step("vision.capture", Param("x", "0"), Param("y", "0"), Param("width", "10"),
                Param("height", "10"), Param("saveTo", "shot")),
            Step("vision.findImage", Param("image", "shot"), Param("confidence", "90"),
                Param("region", ""), Param("resultVariable", "where")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Equal("<image 10x10>", store.Local.Values["shot"].AsText());
        Assert.Equal("8,9", store.Local.Values["where"].AsText());
        Assert.Contains("capture 0 0 10 10", devices.Calls);
        Assert.Contains("findAll 10x10 90", devices.Calls);
    }

    [Fact]
    public async Task Waiting_for_an_image_keeps_looking_until_it_appears()
    {
        var devices = new FakeDeviceLayer
        {
            Loaded = new ImageFrame(2, 2, new byte[16]),
            Match = new ImageMatch(0.95, new ScreenPoint(1, 1), new ScreenSize(2, 2)),
            MatchAfter = 3,
        };

        var (result, _, store) = await RunAsync(
        [
            Step("vision.waitImage", Param("image", "anything.png"), Param("confidence", "90"),
                Param("timeoutMs", "2000"), Param("intervalMs", "1"), Param("resultVariable", "spot")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Equal("2,2", store.Local.Values["spot"].AsText());
        Assert.Equal(3, devices.Searches);
    }

    [Fact]
    public async Task Waiting_for_an_image_that_never_appears_fails()
    {
        var devices = new FakeDeviceLayer
        {
            Loaded = new ImageFrame(2, 2, new byte[16]),
            Match = null,
        };

        var (result, _, _) = await RunAsync(
        [
            Step("vision.waitImage", Param("image", "anything.png"), Param("confidence", "90"),
                Param("timeoutMs", "20"), Param("intervalMs", "1"), Param("resultVariable", "spot")),
        ], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.ImageNotFound", result.Key);
    }

    [Fact]
    public async Task An_image_condition_uses_the_found_match()
    {
        var devices = new FakeDeviceLayer
        {
            Loaded = new ImageFrame(2, 2, new byte[16]),
            Match = new ImageMatch(0.99, new ScreenPoint(0, 0), new ScreenSize(2, 2)),
        };

        var (_, _, store) = await RunAsync(
        [
            Step("control.if",
                When("condition", Step("condition.imageExists",
                    Param("image", "a.png"), Param("confidence", "90"), Param("region", ""))),
                Body("then", Step("control.setVariable", Param("name", "seen"), Param("value", "yes"))),
                Body("else", Step("control.setVariable", Param("name", "seen"), Param("value", "no")))),
        ], devices);

        Assert.Equal("yes", store.Local.Values["seen"].AsText());
    }

    [Fact]
    public async Task A_search_region_is_copied_before_it_is_searched()
    {
        var devices = new FakeDeviceLayer
        {
            Loaded = new ImageFrame(2, 2, new byte[16]),
            Match = new ImageMatch(0.99, new ScreenPoint(0, 0), new ScreenSize(2, 2)),
        };

        var (_, _, store) = await RunAsync(
        [
            Step("vision.findImage", Param("image", "a.png"), Param("confidence", "90"),
                Param("region", "10,20,30,40"), Param("resultVariable", "where")),
        ], devices);

        // The match is reported in screen coordinates, not in region coordinates.
        Assert.Contains("capture 10 20 30 40", devices.Calls);
        Assert.Equal("11,21", store.Local.Values["where"].AsText());
    }

    [Fact]
    public async Task A_missing_device_is_reported_as_such()
    {
        var devices = new FakeDeviceLayer { Unavailable = true };
        var (result, _, _) = await RunAsync(
            [Step("input.keyPress", Param("key", "A"))], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.NoDevice", result.Key);
    }

    [Fact]
    public async Task A_refused_action_keeps_its_reason()
    {
        var devices = new FakeDeviceLayer { Refusal = new DeviceActionException("Run.UnknownKey", "QQQ") };
        var (result, _, _) = await RunAsync(
            [Step("input.keyPress", Param("key", "QQQ"))], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.UnknownKey", result.Key);
        Assert.Equal("QQQ", result.Detail);
    }

    [Fact]
    public async Task The_engine_still_runs_without_any_devices()
    {
        // The stand-in for "no devices at all" is the engine's own default.
        var store = new VariableStore();
        var result = await new MacroRunner(store).RunAsync(
            [Step("control.setVariable", Param("name", "a"), Param("value", "1"))]);

        Assert.True(result.Succeeded);
        Assert.Equal(1, store.Local.Values["a"].AsNumber());
    }

    // ------------------------------------------------------------------ text on screen

    [Fact]
    public async Task Recognising_text_puts_the_words_in_a_variable()
    {
        var devices = new FakeDeviceLayer
        {
            Spans =
            [
                new TextSpan("Ready", new ScreenPoint(1, 2), new ScreenSize(10, 4), 0.9),
                new TextSpan("Go", new ScreenPoint(20, 2), new ScreenSize(6, 4), 0.8),
            ],
        };

        var (_, _, store) = await RunAsync(
        [
            Step("ocr.recognize", Param("x", "0"), Param("y", "0"), Param("width", "100"),
                Param("height", "20"), Param("language", "en"), Param("resultVariable", "words")),
        ], devices);

        Assert.Equal("Ready Go", store.Local.Values["words"].AsText());
        Assert.Contains("ocr en", devices.Calls);
    }

    [Fact]
    public async Task Finding_text_reports_where_it_is()
    {
        var devices = new FakeDeviceLayer
        {
            Spans = [new TextSpan("Save as", new ScreenPoint(10, 10), new ScreenSize(20, 8), 0.9)],
        };

        var (_, _, store) = await RunAsync(
        [
            Step("ocr.findText", Param("text", "Save"), Param("region", ""),
                Param("matchMode", "contains"), Param("resultVariable", "spot")),
        ], devices);

        Assert.Equal("20,14", store.Local.Values["spot"].AsText());
    }

    [Fact]
    public async Task A_text_search_can_leave_out_a_reading_the_model_was_unsure_of()
    {
        var devices = new FakeDeviceLayer
        {
            Spans =
            [
                new TextSpan("Save as", new ScreenPoint(10, 10), new ScreenSize(20, 8), 12),
                new TextSpan("Save now", new ScreenPoint(10, 40), new ScreenSize(20, 8), 38),
            ],
        };

        var (_, _, store) = await RunAsync(
        [
            Step("ocr.findText", Param("text", "Save"), Param("matchMode", "contains"),
                Param("minScore", "30"), Param("allMatches", "true"), Param("resultVariable", "sure")),
            Step("ocr.findText", Param("text", "Save"), Param("matchMode", "contains"),
                Param("resultVariable", "any")),
        ], devices);

        // The shaky reading is dropped, so the only one left is the one the model was sure of —
        // including in the list of every hit.
        Assert.Equal("20,44", store.Local.Values["sure"].AsText());
        Assert.Equal(1, store.Local.Values["sure.count"].AsNumber());

        // Left alone, the field takes everything, shaky reading first, which is what macros that
        // never set it have always seen.
        Assert.Equal("20,14", store.Local.Values["any"].AsText());
    }

    [Fact]
    public async Task A_text_search_can_record_every_place_the_text_was_read()
    {
        var devices = new FakeDeviceLayer
        {
            Spans =
            [
                new TextSpan("Total 12", new ScreenPoint(0, 0), new ScreenSize(40, 8), 0.9),
                new TextSpan("Other", new ScreenPoint(0, 20), new ScreenSize(20, 8), 0.9),
                new TextSpan("Total 99", new ScreenPoint(0, 40), new ScreenSize(40, 8), 0.9),
            ],
        };

        var (_, _, store) = await RunAsync(
        [
            Step("ocr.findText", Param("text", "Total"), Param("matchMode", "contains"),
                Param("allMatches", "true"), Param("resultVariable", "totals")),
            Step("ocr.findText", Param("text", "Other"), Param("matchMode", "contains"),
                Param("resultVariable", "one")),
        ], devices);

        Assert.Equal("20,4", store.Local.Values["totals"].AsText());
        Assert.Equal(2, store.Local.Values["totals.count"].AsNumber());
        Assert.Equal("20,4", store.Local.Values["totals.list"].AsList()[0].AsText());
        Assert.Equal("20,44", store.Local.Values["totals.list"].AsList()[1].AsText());

        // A step that did not ask for the whole set leaves no list behind for an older macro to
        // trip over.
        Assert.False(store.Local.Values.ContainsKey("one.count"));
    }

    [Fact]
    public async Task Reading_and_finding_can_ask_for_the_numbers_only()
    {
        var devices = new FakeDeviceLayer
        {
            Spans =
            [
                new TextSpan("合计 ¥1,234.50", new ScreenPoint(0, 0), new ScreenSize(80, 12), 0.9),
                new TextSpan("已完成", new ScreenPoint(0, 20), new ScreenSize(30, 12), 0.9),
            ],
        };

        var (_, _, store) = await RunAsync(
        [
            Step("ocr.recognize", Param("x", "0"), Param("y", "0"), Param("width", "100"),
                Param("height", "40"), Param("content", "digits"), Param("resultVariable", "amounts")),
            Step("ocr.findText", Param("text", "1234.50"), Param("content", "digits"),
                Param("resultVariable", "where")),
        ], devices);

        Assert.Equal("1234.50", store.Local.Values["amounts"].AsText());
        Assert.Equal("40,6", store.Local.Values["where"].AsText());
    }

    [Fact]
    public async Task Recognising_text_can_keep_the_lines_and_columns_it_was_read_in()
    {
        var devices = new FakeDeviceLayer
        {
            Spans =
            [
                new TextSpan("名称", new ScreenPoint(0, 0), new ScreenSize(30, 14), 0.9),
                new TextSpan("数量", new ScreenPoint(120, 0), new ScreenSize(30, 14), 0.9),
                new TextSpan("苹果", new ScreenPoint(0, 30), new ScreenSize(30, 14), 0.9),
                new TextSpan("3", new ScreenPoint(120, 30), new ScreenSize(8, 14), 0.9),
            ],
        };

        var (_, _, store) = await RunAsync(
        [
            Step("ocr.recognize", Param("x", "0"), Param("y", "0"), Param("width", "200"),
                Param("height", "60"), Param("table", "true"), Param("resultVariable", "sheet")),
        ], devices);

        var rows = store.Local.Values["sheet"].AsList();
        Assert.Equal(2, rows.Count);
        Assert.Equal(["名称", "数量"], rows[0].AsList().Select(cell => cell.AsText()));
        Assert.Equal(["苹果", "3"], rows[1].AsList().Select(cell => cell.AsText()));

        // The writing is still there as text, one line per row, which is what a macro shows or
        // writes to a file.
        Assert.Equal("名称\t数量\n苹果\t3", store.Local.Values["sheet.text"].AsText());
    }

    [Fact]
    public async Task Cleaning_the_picture_up_before_reading_it_keeps_the_positions_on_screen()
    {
        var devices = new FakeDeviceLayer
        {
            Spans = [new TextSpan("Go", new ScreenPoint(20, 10), new ScreenSize(40, 8), 0.9)],
        };

        var (_, _, store) = await RunAsync(
        [
            Step("ocr.findText", Param("text", "Go"), Param("preprocess", "upscale"),
                Param("resultVariable", "spot")),
        ], devices);

        // The picture the OCR was handed was the searched region at twice its size...
        Assert.Equal([(200, 100)], devices.OcrFrames);

        // ...so what it read comes back halved, and the place still means screen pixels: the
        // writing sat at 20,10 and was 40 by 8, so its centre is 40,14 and comes back as 20,7.
        Assert.Equal("20,7", store.Local.Values["spot"].AsText());
        Assert.Equal(20, store.Local.Values["spot.width"].AsNumber());
        Assert.Equal(4, store.Local.Values["spot.height"].AsNumber());
    }

    [Fact]
    public async Task A_text_search_can_be_exact_or_a_pattern()
    {
        var devices = new FakeDeviceLayer
        {
            Spans = [new TextSpan("Running", new ScreenPoint(0, 0), new ScreenSize(10, 4), 0.9)],
        };

        var (_, _, store) = await RunAsync(
        [
            Step("ocr.findText", Param("text", "Run"), Param("matchMode", "exact"),
                Param("resultVariable", "exactHit")),
            Step("ocr.findText", Param("text", "^Run"), Param("matchMode", "regex"),
                Param("resultVariable", "patternHit")),
            Step("ocr.findText", Param("text", "nope"), Param("matchMode", "contains"),
                Param("resultVariable", "miss")),
        ], devices);

        Assert.Equal(string.Empty, store.Local.Values["exactHit"].AsText());
        Assert.Equal("5,2", store.Local.Values["patternHit"].AsText());
        Assert.Equal(string.Empty, store.Local.Values["miss"].AsText());
    }

    [Fact]
    public async Task Clicking_text_clicks_where_the_words_are()
    {
        var devices = new FakeDeviceLayer
        {
            Spans = [new TextSpan("OK", new ScreenPoint(10, 20), new ScreenSize(20, 10), 0.9)],
        };

        var (result, _, _) = await RunAsync(
        [
            Step("ocr.clickText", Param("text", "OK"), Param("matchMode", "contains"),
                Param("offsetX", "1"), Param("offsetY", "2"), Param("timeoutMs", "1000"),
                Param("button", "left")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Contains("click left 21 27 1 0", devices.Calls);
    }

    [Fact]
    public async Task A_text_condition_is_true_when_the_words_are_on_screen()
    {
        var devices = new FakeDeviceLayer
        {
            Spans = [new TextSpan("Ready", new ScreenPoint(0, 0), new ScreenSize(10, 4), 0.9)],
        };

        var (_, _, store) = await RunAsync(
        [
            Step("control.if",
                When("condition", Step("condition.textExists", Param("text", "ready"),
                    Param("region", ""), Param("matchMode", "contains"))),
                Body("then", Step("control.setVariable", Param("name", "seen"), Param("value", "yes"))),
                Body("else", Step("control.setVariable", Param("name", "seen"), Param("value", "no")))),
        ], devices);

        Assert.Equal("yes", store.Local.Values["seen"].AsText());
    }

    // ------------------------------------------------------------------ window controls

    [Fact]
    public async Task Looking_for_an_element_reports_true_or_false()
    {
        var found = new FakeDeviceLayer { ElementExists = true };
        var missing = new FakeDeviceLayer { ElementExists = false };

        ExecutableStep Exists() => Step("uia.exists",
            Param("window", "Notepad"), Param("selector", "Button[name='Save']"),
            Param("timeoutMs", "0"), Param("resultVariable", "there"));

        var (_, _, yes) = await RunAsync([Exists()], found);
        var (_, _, no) = await RunAsync([Exists()], missing);

        Assert.Equal("true", yes.Local.Values["there"].AsText());
        Assert.Equal("false", no.Local.Values["there"].AsText());
        Assert.Contains("ui Button/Save/", found.Calls[0]);
    }

    [Fact]
    public async Task Reading_an_element_puts_its_text_in_a_variable()
    {
        var devices = new FakeDeviceLayer { ElementText = "12 items" };
        var (_, _, store) = await RunAsync(
        [
            Step("uia.getText", Param("window", ""), Param("selector", "Text[name='Count']"),
                Param("resultVariable", "countText")),
        ], devices);

        Assert.Equal("12 items", store.Local.Values["countText"].AsText());
    }

    [Fact]
    public async Task Finding_an_element_records_where_it_sits()
    {
        var devices = new FakeDeviceLayer();
        devices.Elements.Add(new UiElementInfo("Save", "saveButton", "Button", "ButtonClass",
            new ScreenPoint(10, 20), new ScreenSize(80, 24), "Untitled - Notepad"));

        var (_, _, store) = await RunAsync(
        [
            Step("uia.find", Param("window", "Notepad"), Param("selector", "Button[name='Save']"),
                Param("resultVariable", "save")),
        ], devices);

        // The centre is what a click aims at, and the parts are what a later step measures from.
        Assert.Equal("50,32", store.Local.Values["save"].AsText());
        Assert.Equal("50", store.Local.Values["save.x"].AsText());
        Assert.Equal("32", store.Local.Values["save.y"].AsText());
        Assert.Equal("80", store.Local.Values["save.width"].AsText());
        Assert.Equal("24", store.Local.Values["save.height"].AsText());
        Assert.Equal("Save", store.Local.Values["save.text"].AsText());
    }

    [Fact]
    public async Task A_find_that_hits_nothing_empties_the_variable()
    {
        var devices = new FakeDeviceLayer();
        var (_, _, store) = await RunAsync(
        [
            Step("uia.find", Param("selector", "Button[name='Save']"),
                Param("resultVariable", "save")),
        ], devices);

        Assert.Equal(string.Empty, store.Local.Values["save"].AsText());
        Assert.Equal(string.Empty, store.Local.Values["save.x"].AsText());
    }

    [Fact]
    public async Task Recording_every_element_notes_how_many_and_where()
    {
        var devices = new FakeDeviceLayer();
        devices.Elements.Add(new UiElementInfo("Row 1", "row1", "ListItem", "ListBoxItem",
            new ScreenPoint(0, 100), new ScreenSize(200, 20), "Tasks"));
        devices.Elements.Add(new UiElementInfo("Row 2", "row2", "ListItem", "ListBoxItem",
            new ScreenPoint(0, 120), new ScreenSize(200, 20), "Tasks"));

        var (_, _, store) = await RunAsync(
        [
            Step("uia.find", Param("selector", "ListItem[automationId='row1']"),
                Param("allMatches", "true"), Param("resultVariable", "rows")),
        ], devices);

        Assert.Equal("2", store.Local.Values["rows.count"].AsText());
        Assert.Equal("100,110", store.Local.Values["rows.list"].Items[0].AsText());
        Assert.Equal("100,130", store.Local.Values["rows.list"].Items[1].AsText());
    }

    [Fact]
    public async Task The_match_number_asks_the_search_for_that_one()
    {
        var devices = new FakeDeviceLayer();
        await RunAsync(
        [
            Step("uia.click", Param("selector", "ListItem[automationId='row1']"),
                Param("matchIndex", "3"), Param("timeoutMs", "0")),
        ], devices);

        Assert.Contains("clickElement  #3 left", devices.Calls);
    }

    [Fact]
    public async Task Picking_an_entry_says_which_one_and_how()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
        [
            Step("uia.select", Param("selector", "ComboBox[automationId='rate']"),
                Param("item", "Fast"), Param("itemIndex", "0")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Contains("selectItem ComboBox//rate#1 \"Fast\" 0", devices.Calls);
    }

    [Fact]
    public async Task Picking_an_entry_by_number_counts_from_one()
    {
        var devices = new FakeDeviceLayer();
        await RunAsync(
        [
            Step("control.setVariable", Param("name", "wanted"), Param("value", "Fast")),
            Step("uia.select", Param("selector", "ComboBox[automationId='rate']"),
                Param("item", "$wanted"), Param("itemIndex", "3")),
        ], devices);

        // The text field is kept as written even when the number is what decides, so a step can
        // be switched from one to the other without losing what was typed; a variable in it is
        // filled in the way every other field reads one.
        Assert.Contains("selectItem ComboBox//rate#1 \"Fast\" 3", devices.Calls);
    }

    [Fact]
    public async Task A_pick_with_nothing_to_pick_fails_the_step()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
        [Step("uia.select", Param("selector", "ComboBox[automationId='rate']"))], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.MissingItem", result.Key);
    }

    [Fact]
    public async Task A_pick_that_finds_no_such_entry_fails_the_step()
    {
        var devices = new FakeDeviceLayer { ItemSelectable = false };
        var (result, _, _) = await RunAsync(
        [
            Step("uia.select", Param("selector", "ComboBox[automationId='rate']"),
                Param("item", "Missing")),
        ], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.ItemNotFound", result.Key);
    }

    [Fact]
    public async Task A_check_box_can_be_switched_by_name_or_flipped()
    {
        var devices = new FakeDeviceLayer();
        await RunAsync(
        [
            Step("uia.check", Param("selector", "CheckBox[name='Remember']"), Param("state", "off")),
            Step("uia.check", Param("selector", "CheckBox[name='Remember']"), Param("state", "toggle")),
        ], devices);

        Assert.Contains("setChecked Remember False", devices.Calls);
        Assert.Contains("setChecked Remember flip", devices.Calls);
    }

    [Fact]
    public async Task Opening_and_closing_a_node_says_which_way()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
        [
            Step("uia.expand", Param("selector", "TreeItem[name='Tools']"),
                Param("state", "collapse")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Contains("setExpanded Tools collapse", devices.Calls);
    }

    [Fact]
    public async Task Scrolling_into_view_a_step_of_a_list_that_will_not_scroll_fails()
    {
        var devices = new FakeDeviceLayer { Scrollable = false };
        var (result, _, _) = await RunAsync(
        [Step("uia.scrollIntoView", Param("selector", "ListItem[name='Row 9']"))], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.ElementNotScrollable", result.Key);
    }

    [Fact]
    public async Task Reading_a_table_puts_the_rows_in_a_variable()
    {
        var devices = new FakeDeviceLayer();
        devices.Table.Add(["Name", "Age"]);
        devices.Table.Add(["Ann", "31"]);

        var (result, _, store) = await RunAsync(
        [
            Step("uia.readTable", Param("selector", "Table[automationId='people']"),
                Param("resultVariable", "people")),
        ], devices);

        Assert.True(result.Succeeded);

        // The shape is the same as the CSV reader's: a row per entry, a cell per entry inside it.
        var table = store.Local.Values["people"];
        Assert.True(table.IsList);
        Assert.Equal(2, table.Items.Count);
        Assert.Equal("Name", table.Items[0].Items[0].AsText());
        Assert.Equal("31", table.Items[1].Items[1].AsText());
    }

    [Fact]
    public async Task Reading_a_table_stops_at_the_row_count_it_was_given()
    {
        var devices = new FakeDeviceLayer();
        devices.Table.Add(["one"]);
        devices.Table.Add(["two"]);
        devices.Table.Add(["three"]);

        var (_, _, store) = await RunAsync(
        [
            Step("uia.readTable", Param("selector", "Table[automationId='people']"),
                Param("maxRows", "2"), Param("resultVariable", "people")),
        ], devices);

        Assert.Equal(2, store.Local.Values["people"].Items.Count);
    }

    /// <summary>
    /// A table keeps the names of its columns in a strip of its own, which is not a row of data.
    /// They are the only place that says which column is which, so they are handed over separately
    /// rather than left inside the rows or dropped along with the empty ones.
    /// </summary>
    [Fact]
    public async Task Reading_a_table_can_hand_back_the_names_of_its_columns()
    {
        var devices = new FakeDeviceLayer();
        devices.Columns.AddRange(["Name", "Age"]);
        devices.Table.Add(["Ann", "31"]);

        var (result, _, store) = await RunAsync(
        [
            Step("uia.readTable", Param("selector", "Table[automationId='people']"),
                Param("columnsVariable", "titles"), Param("resultVariable", "people")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Equal(["Name", "Age"],
            store.Local.Values["titles"].Items.Select(cell => cell.AsText()));

        // And the row that was read is the data, with no title strip left over in it.
        var table = store.Local.Values["people"];
        Assert.Equal("Ann", Assert.Single(table.Items).Items[0].AsText());
    }

    [Fact]
    public async Task Coordinates_can_be_measured_from_a_control()
    {
        var devices = new FakeDeviceLayer();
        devices.Elements.Add(new UiElementInfo("Panel", "panel", "Pane", "PaneClass",
            new ScreenPoint(200, 100), new ScreenSize(400, 300), "Tasks"));

        var (result, _, _) = await RunAsync(
        [
            Step("input.mouseMove", Param("x", "10"), Param("y", "20"),
                Param("anchorMode", "element"), Param("anchorSelector", "Pane[automationId='panel']")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Contains("findElements Pane//panel #1 take 1", devices.Calls);
        Assert.Contains("move 210 120 0", devices.Calls);
    }

    [Fact]
    public async Task Measuring_from_a_control_that_is_not_there_fails_the_step()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
        [
            Step("input.mouseMove", Param("x", "10"), Param("y", "20"),
                Param("anchorMode", "element"), Param("anchorSelector", "Pane[automationId='panel']")),
        ], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.ElementNotFound", result.Key);
        Assert.DoesNotContain("move 10 20 0", devices.Calls);
    }

    [Fact]
    public async Task Writing_into_an_element_carries_the_text_and_the_clear_flag()
    {
        var devices = new FakeDeviceLayer { ElementWritable = true };
        var (result, _, _) = await RunAsync(
        [
            Step("uia.setText", Param("window", ""), Param("selector", "Edit[automationId='input']"),
                Param("text", "hello"), Param("clearFirst", "false")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Contains("fill Edit/input hello False", devices.Calls);
    }

    [Fact]
    public async Task An_element_that_cannot_be_written_fails_the_step()
    {
        var devices = new FakeDeviceLayer { ElementWritable = false };
        var (result, _, _) = await RunAsync(
        [
            Step("uia.setText", Param("selector", "Edit[name='Query']"), Param("text", "hello")),
        ], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.ElementNotWritable", result.Key);
    }

    [Fact]
    public async Task Focusing_a_window_that_is_not_there_fails()
    {
        var devices = new FakeDeviceLayer { WindowFocused = false };
        var (result, _, _) = await RunAsync(
            [Step("uia.focusWindow", Param("window", "Nothing"))], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.WindowNotFound", result.Key);
    }

    [Fact]
    public async Task An_element_condition_uses_ui_automation()
    {
        var devices = new FakeDeviceLayer { ElementExists = true };

        var (_, _, store) = await RunAsync(
        [
            Step("control.if",
                When("condition", Step("condition.uiaExists", Param("selector", "Button[name='Send']"))),
                Body("then", Step("control.setVariable", Param("name", "seen"), Param("value", "yes"))),
                Body("else", Step("control.setVariable", Param("name", "seen"), Param("value", "no")))),
        ], devices);

        Assert.Equal("yes", store.Local.Values["seen"].AsText());
    }

    [Fact]
    public async Task A_selector_reads_as_a_control_and_its_properties()
    {
        var devices = new FakeDeviceLayer { ElementExists = true };
        await RunAsync(
        [
            Step("uia.exists", Param("window", "Notepad"),
                Param("selector", "Edit[automationId='input', controlType='Edit', class='Edit']"),
                Param("timeoutMs", "0"), Param("resultVariable", "there")),
        ], devices);

        Assert.Contains("ui Edit//input", devices.Calls[0]);
    }

    [Fact]
    public async Task Reading_a_text_file_fills_a_variable()
    {
        var devices = new FakeDeviceLayer();
        devices.Files["notes.txt"] = "hello world";
        var (result, _, store) = await RunAsync(
            [Step("file.readText", Param("path", "notes.txt"), Param("resultVariable", "text"))],
            devices);

        Assert.True(result.Succeeded);
        Assert.Equal("hello world", store.Local.Values["text"].AsText());
        Assert.Contains("readFile notes.txt utf8", devices.Calls);
    }

    [Fact]
    public async Task Reading_a_text_file_without_a_name_uses_a_default()
    {
        var devices = new FakeDeviceLayer();
        devices.Files["notes.txt"] = "kept";
        var (_, _, store) = await RunAsync(
            [Step("file.readText", Param("path", "notes.txt"))], devices);

        Assert.Equal("kept", store.Local.Values["text"].AsText());
    }

    [Fact]
    public async Task Text_files_can_name_the_encoding_they_are_written_in()
    {
        var devices = new FakeDeviceLayer();
        devices.Files["old.csv"] = "a,b";
        var (result, _, _) = await RunAsync(
        [
            Step("file.writeText", Param("path", "notes.txt"), Param("text", "你好"),
                Param("encoding", "gbk")),
            Step("file.readText", Param("path", "notes.txt"), Param("encoding", "gbk")),
            Step("file.readCsv", Param("path", "old.csv"), Param("encoding", "gbk")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Contains("writeFile notes.txt 你好 False gbk", devices.Calls);
        Assert.Contains("readFile notes.txt gbk", devices.Calls);
        Assert.Contains("readFile old.csv gbk", devices.Calls);
    }

    [Fact]
    public async Task Writing_a_text_file_replaces_it_or_adds_to_the_end()
    {
        var devices = new FakeDeviceLayer();
        devices.Files["log.txt"] = "first\n";
        await RunAsync(
        [
            Step("file.writeText", Param("path", "log.txt"), Param("text", "second"),
                Param("mode", "append")),
            Step("file.writeText", Param("path", "other.txt"), Param("text", "fresh"),
                Param("mode", "overwrite")),
        ], devices);

        Assert.Equal("first\nsecond", devices.Files["log.txt"]);
        Assert.Equal("fresh", devices.Files["other.txt"]);
    }

    [Fact]
    public async Task A_log_line_is_added_to_the_end_of_the_file()
    {
        var devices = new FakeDeviceLayer();
        devices.Files["log.txt"] = "first\n";
        await RunAsync(
        [
            Step("file.appendLog", Param("path", "log.txt"), Param("text", "second"),
                Param("timestamp", "false")),
        ], devices);

        // The line carries its own line break, which is what keeps the next one on its own line.
        Assert.Equal("first\nsecond" + Environment.NewLine, devices.Files["log.txt"]);
    }

    [Fact]
    public async Task A_log_line_can_carry_the_time_it_was_written()
    {
        var devices = new FakeDeviceLayer();
        await RunAsync(
        [
            Step("file.appendLog", Param("path", "log.txt"), Param("text", "done"),
                Param("timestamp", "true")),
        ], devices);

        var text = devices.Files["log.txt"];
        Assert.EndsWith(" done" + Environment.NewLine, text);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2} done", text);
    }

    [Fact]
    public async Task Checking_for_a_file_leaves_true_or_false()
    {
        var devices = new FakeDeviceLayer { PathExists = false };
        devices.Files["there.txt"] = "";
        var (_, _, store) = await RunAsync(
        [
            Step("file.exists", Param("path", "there.txt"), Param("resultVariable", "there")),
            Step("file.exists", Param("path", "gone.txt"), Param("resultVariable", "gone")),
        ], devices);

        Assert.True(store.Local.Values["there"].Flag);
        Assert.False(store.Local.Values["gone"].Flag);
    }

    [Fact]
    public async Task Deleting_a_file_asks_the_file_device_to_remove_it()
    {
        var devices = new FakeDeviceLayer();
        devices.Files["old.txt"] = "x";
        await RunAsync([Step("file.delete", Param("path", "old.txt"))], devices);

        Assert.DoesNotContain("old.txt", devices.Files.Keys);
        Assert.Contains("deleteFile old.txt", devices.Calls);
    }

    [Fact]
    public async Task Copying_a_file_passes_both_paths_and_the_overwrite_flag()
    {
        var devices = new FakeDeviceLayer();
        devices.Files["a.txt"] = "data";
        await RunAsync(
        [
            Step("file.copy", Param("from", "a.txt"), Param("to", "b.txt"),
                Param("overwrite", "false")),
        ], devices);

        Assert.Equal("data", devices.Files["b.txt"]);
        Assert.Contains("copyFile a.txt b.txt False", devices.Calls);
    }

    [Fact]
    public async Task Moving_a_file_takes_it_away_from_where_it_was()
    {
        var devices = new FakeDeviceLayer();
        devices.Files["report.csv"] = "rows";
        var (result, _, _) = await RunAsync(
        [
            Step("file.move", Param("from", "report.csv"), Param("to", @"archive\old.csv"),
                Param("overwrite", "true")),
        ], devices);

        // A move is a rename as much as it is a change of folder: the old name is gone.
        Assert.True(result.Succeeded);
        Assert.Equal("rows", devices.Files[@"archive\old.csv"]);
        Assert.False(devices.Files.ContainsKey("report.csv"));
        Assert.Contains(@"moveFile report.csv archive\old.csv True", devices.Calls);
    }

    [Fact]
    public async Task Moving_onto_a_file_can_be_refused()
    {
        var devices = new FakeDeviceLayer { TargetExists = true };
        devices.Files["report.csv"] = "rows";
        var (result, _, _) = await RunAsync(
        [
            Step("file.move", Param("from", "report.csv"), Param("to", "taken.csv"),
                Param("overwrite", "false")),
        ], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.FileExists", result.Key);
        Assert.Equal("rows", devices.Files["report.csv"]);
    }

    [Fact]
    public async Task A_folder_can_be_made()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
            [Step("file.createFolder", Param("path", @"output\reports"))], devices);

        Assert.True(result.Succeeded);
        Assert.Contains(@"createFolder output\reports", devices.Calls);
        Assert.Contains(@"output\reports", devices.Folders);
    }

    [Fact]
    public async Task A_folder_that_still_holds_something_is_left_alone_unless_told_otherwise()
    {
        var devices = new FakeDeviceLayer { FolderHasFiles = true };
        var (refused, _, _) = await RunAsync(
            [Step("file.deleteFolder", Param("path", "old"))], devices);

        Assert.False(refused.Succeeded);
        Assert.Equal("Run.FileFailed", refused.Key);

        var (agreed, _, _) = await RunAsync(
            [Step("file.deleteFolder", Param("path", "old"), Param("recurse", "true"))], devices);

        Assert.True(agreed.Succeeded);
        Assert.Contains("deleteFolder old True", devices.Calls);
    }

    [Fact]
    public async Task A_path_can_be_taken_apart_and_put_back_together()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, store) = await RunAsync(
        [
            Step("file.path", Param("operation", "combine"), Param("path", @"reports\"),
                Param("name", "day.csv"), Param("resultVariable", "joined")),
            Step("file.path", Param("operation", "folder"), Param("path", @"reports\day.csv"),
                Param("resultVariable", "where")),
            Step("file.path", Param("operation", "name"), Param("path", @"reports\day.csv"),
                Param("resultVariable", "what")),
            Step("file.path", Param("operation", "baseName"), Param("path", @"reports\day.csv"),
                Param("resultVariable", "stem")),
            Step("file.path", Param("operation", "extension"), Param("path", @"reports\day.csv"),
                Param("resultVariable", "tail")),
            Step("file.path", Param("operation", "full"), Param("path", @"reports\day.csv"),
                Param("resultVariable", "whole")),
            Step("file.path", Param("operation", "macros"), Param("resultVariable", "home")),
            Step("file.path", Param("operation", "temp"), Param("resultVariable", "scratch")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Equal(@"reports\day.csv", store.Local.Values["joined"].AsText());
        Assert.Equal("reports", store.Local.Values["where"].AsText());
        Assert.Equal("day.csv", store.Local.Values["what"].AsText());
        Assert.Equal("day", store.Local.Values["stem"].AsText());
        Assert.Equal(".csv", store.Local.Values["tail"].AsText());

        // A relative path points inside the macros folder, which is where the file actions look too.
        Assert.Equal(Path.Combine(@"G:\fake", @"reports\day.csv"), store.Local.Values["whole"].AsText());
        Assert.Equal(@"G:\fake", store.Local.Values["home"].AsText());
        Assert.True(Path.IsPathRooted(store.Local.Values["scratch"].AsText()));
    }

    [Fact]
    public async Task A_zip_file_can_be_unpacked()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
        [
            Step("file.unzip", Param("from", "download.zip"), Param("folder", "unpacked"),
                Param("overwrite", "true")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Contains(@"unzip download.zip unpacked True", devices.Calls);
        Assert.Equal("unpacked", devices.Files[Path.Combine("unpacked", "readme.txt")]);
    }

    [Fact]
    public async Task A_folder_can_be_packed_into_a_zip_file()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
            [Step("file.zip", Param("folder", "reports"), Param("to", "reports.zip"))],
            devices);

        Assert.True(result.Succeeded);
        Assert.Contains(@"zip reports reports.zip", devices.Calls);
    }

    [Fact]
    public async Task A_zip_file_that_is_not_one_reports_what_the_device_said()
    {
        var devices = new FakeDeviceLayer { UnzipWorks = false };
        var (result, _, _) = await RunAsync(
            [Step("file.unzip", Param("from", "notes.txt"), Param("folder", "unpacked"))], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.FileFailed", result.Key);
    }

    [Fact]
    public async Task Listing_a_folder_gives_a_list_of_full_paths()
    {
        var devices = new FakeDeviceLayer();
        devices.FolderEntries.Add(@"C:\fake\a.txt");
        devices.FolderEntries.Add(@"C:\fake\b.txt");
        var (_, _, store) = await RunAsync(
        [
            Step("file.listFiles", Param("folder", "."), Param("pattern", "*.txt"),
                Param("recurse", "true"), Param("resultVariable", "files")),
        ], devices);

        var files = store.Local.Values["files"];
        Assert.True(files.IsList);
        Assert.Equal(2, files.Items.Count);
        Assert.Equal(@"C:\fake\b.txt", files.Items[1].AsText());
        Assert.Contains(@"listFiles . *.txt True", devices.Calls);
    }

    [Fact]
    public async Task Reading_json_walks_a_dotted_path()
    {
        var devices = new FakeDeviceLayer();
        devices.Files["config.json"] = """{"server":{"name":"alpha","port":8080}}""";
        var (_, _, store) = await RunAsync(
        [
            Step("file.readJson", Param("path", "config.json"), Param("query", "server.name"),
                Param("resultVariable", "host")),
            Step("file.readJson", Param("path", "config.json"), Param("query", "server.port"),
                Param("resultVariable", "port")),
        ], devices);

        Assert.Equal("alpha", store.Local.Values["host"].AsText());
        Assert.Equal(8080d, store.Local.Values["port"].Number);
    }

    [Fact]
    public async Task Writing_json_keeps_the_rest_and_makes_missing_objects()
    {
        var devices = new FakeDeviceLayer();
        devices.Files["config.json"] = """{"server":{"name":"alpha"}}""";
        await RunAsync(
        [
            Step("file.writeJson", Param("path", "config.json"), Param("query", "server.port"),
                Param("value", "9000")),
        ], devices);

        var written = devices.Files["config.json"];
        Assert.Contains("\"name\": \"alpha\"", written);
        Assert.Contains("\"port\": 9000", written);
    }

    [Fact]
    public async Task Reading_csv_splits_rows_and_skips_a_header()
    {
        var devices = new FakeDeviceLayer();
        devices.Files["rows.csv"] = "name,age\r\nalice,30\r\nbob,41";
        var (_, _, store) = await RunAsync(
        [
            Step("file.readCsv", Param("path", "rows.csv"), Param("separator", "comma"),
                Param("hasHeader", "true"), Param("resultVariable", "rows")),
        ], devices);

        var rows = store.Local.Values["rows"];
        Assert.Equal(2, rows.Items.Count);
        Assert.Equal("alice", rows.Items[0].Items[0].AsText());
        Assert.Equal("41", rows.Items[1].Items[1].AsText());
    }

    [Fact]
    public async Task Writing_csv_quotes_the_cells_that_need_it()
    {
        var devices = new FakeDeviceLayer();
        devices.Files["in.csv"] = "a,\"b,c\"";
        await RunAsync(
        [
            Step("file.readCsv", Param("path", "in.csv"), Param("separator", "comma"),
                Param("hasHeader", "false"), Param("resultVariable", "rows")),
            Step("file.writeCsv", Param("path", "out.csv"), Param("rows", "$rows"),
                Param("separator", "comma")),
        ], devices);

        // The cell holding the separator comes back out quoted, and the file ends its last line
        // the way every writer ends one: with a line terminator, rather than a line left hanging.
        Assert.Equal("a,\"b,c\"\r\n", devices.Files["out.csv"]);
    }

    [Fact]
    public async Task Reading_csv_can_hand_back_the_names_in_the_header()
    {
        var devices = new FakeDeviceLayer();
        devices.Files["rows.csv"] = "name,age\r\nalice,30";

        var (_, _, store) = await RunAsync(
        [
            Step("file.readCsv", Param("path", "rows.csv"), Param("separator", "comma"),
                Param("hasHeader", "true"), Param("headerVariable", "columns"),
                Param("resultVariable", "rows")),
        ], devices);

        var columns = store.Local.Values["columns"].Items;
        Assert.Equal(2, columns.Count);
        Assert.Equal("name", columns[0].AsText());
        Assert.Equal("age", columns[1].AsText());

        // The names are handed back and the row they were written on is still left out of the
        // data, which is what the header setting promises.
        Assert.Single(store.Local.Values["rows"].Items);
    }

    [Fact]
    public async Task Reading_csv_can_start_further_down_the_file_and_stop_early()
    {
        var devices = new FakeDeviceLayer();
        devices.Files["big.csv"] = "a,1\r\nb,2\r\nc,3\r\nd,4";

        var (_, _, store) = await RunAsync(
        [
            Step("file.readCsv", Param("path", "big.csv"), Param("separator", "comma"),
                Param("hasHeader", "false"), Param("startRow", "2"), Param("maxRows", "2"),
                Param("resultVariable", "rows")),
        ], devices);

        var rows = store.Local.Values["rows"].Items;
        Assert.Equal(2, rows.Count);
        Assert.Equal("b", rows[0].Items[0].AsText());
        Assert.Equal("c", rows[1].Items[0].AsText());
    }

    [Fact]
    public async Task Reading_csv_leaves_out_the_blank_lines_a_file_ends_with()
    {
        var devices = new FakeDeviceLayer();
        devices.Files["rows.csv"] = "a,b\r\n\r\nc,d\r\n\r\n";

        var (_, _, store) = await RunAsync(
        [
            Step("file.readCsv", Param("path", "rows.csv"), Param("separator", "comma"),
                Param("hasHeader", "false"), Param("resultVariable", "kept")),
            Step("file.readCsv", Param("path", "rows.csv"), Param("separator", "comma"),
                Param("hasHeader", "false"), Param("skipBlankLines", "false"),
                Param("resultVariable", "every")),
        ], devices);

        Assert.Equal(2, store.Local.Values["kept"].Items.Count);
        Assert.Equal(4, store.Local.Values["every"].Items.Count);
    }

    [Fact]
    public async Task Reading_csv_can_use_a_character_the_picker_does_not_offer()
    {
        var devices = new FakeDeviceLayer();
        devices.Files["odd.csv"] = "a#b#c";

        var (_, _, store) = await RunAsync(
        [
            Step("file.readCsv", Param("path", "odd.csv"), Param("separator", "comma"),
                Param("separatorText", "#"), Param("hasHeader", "false"),
                Param("resultVariable", "rows")),
        ], devices);

        var row = Assert.Single(store.Local.Values["rows"].Items);
        Assert.Equal(3, row.Items.Count);
        Assert.Equal("c", row.Items[2].AsText());
    }

    [Fact]
    public async Task Reading_csv_can_trim_the_spaces_around_a_cell()
    {
        var devices = new FakeDeviceLayer();
        devices.Files["padded.csv"] = "a , b";

        var (_, _, store) = await RunAsync(
        [
            Step("file.readCsv", Param("path", "padded.csv"), Param("separator", "comma"),
                Param("hasHeader", "false"), Param("trim", "true"), Param("resultVariable", "rows")),
        ], devices);

        var row = Assert.Single(store.Local.Values["rows"].Items);
        Assert.Equal("a", row.Items[0].AsText());
        Assert.Equal("b", row.Items[1].AsText());
    }

    /// <summary>
    /// A log is the reason adding to a CSV matters: the first run makes the file and its header,
    /// and every run after it adds rows under the same header.
    /// </summary>
    [Fact]
    public async Task Adding_to_a_csv_writes_the_header_once_and_the_rows_after_it()
    {
        var devices = new FakeDeviceLayer();
        devices.Files["first.csv"] = "alice,30";
        devices.Files["second.csv"] = "bob,41";

        await RunAsync(
        [
            Step("file.readCsv", Param("path", "first.csv"), Param("separator", "comma"),
                Param("hasHeader", "false"), Param("resultVariable", "first")),
            Step("file.readCsv", Param("path", "second.csv"), Param("separator", "comma"),
                Param("hasHeader", "false"), Param("resultVariable", "second")),
            Step("file.writeCsv", Param("path", "log.csv"), Param("rows", "$first"),
                Param("header", "name,age"), Param("separator", "comma"),
                Param("mode", "append")),
            Step("file.writeCsv", Param("path", "log.csv"), Param("rows", "$second"),
                Param("header", "name,age"), Param("separator", "comma"),
                Param("mode", "append")),
        ], devices);

        Assert.Equal("name,age\r\nalice,30\r\nbob,41\r\n", devices.Files["log.csv"]);
    }

    /// <summary>
    /// A cell is a cell: text that begins with a character a spreadsheet would read as a formula
    /// is written exactly as the macro held it. Escaping those changes the bytes of a file the step
    /// was asked to write, and the file may well be going to something that is not a spreadsheet.
    /// </summary>
    [Fact]
    public async Task A_csv_cell_that_looks_like_a_formula_is_written_as_it_stands()
    {
        var devices = new FakeDeviceLayer();
        devices.Files["in.csv"] = "=SUM(A1),@mine,-1";

        await RunAsync(
        [
            Step("file.readCsv", Param("path", "in.csv"), Param("separator", "comma"),
                Param("hasHeader", "false"), Param("resultVariable", "rows")),
            Step("file.writeCsv", Param("path", "out.csv"), Param("rows", "$rows"),
                Param("separator", "comma")),
        ], devices);

        Assert.Equal("=SUM(A1),@mine,-1\r\n", devices.Files["out.csv"]);
    }

    [Fact]
    public async Task Writing_csv_can_end_its_lines_and_quote_every_cell_the_way_it_was_told()
    {
        var devices = new FakeDeviceLayer();
        devices.Files["in.csv"] = "a,b\r\nc,d";

        await RunAsync(
        [
            Step("file.readCsv", Param("path", "in.csv"), Param("separator", "comma"),
                Param("hasHeader", "false"), Param("resultVariable", "rows")),
            Step("file.writeCsv", Param("path", "unix.csv"), Param("rows", "$rows"),
                Param("separator", "comma"), Param("lineEnding", "unix"),
                Param("quoteAll", "true")),
        ], devices);

        Assert.Equal("\"a\",\"b\"\n\"c\",\"d\"\n", devices.Files["unix.csv"]);
    }

    [Fact]
    public async Task An_empty_csv_cell_is_left_empty_unless_it_is_asked_to_be_shown()
    {
        var devices = new FakeDeviceLayer();
        devices.Files["in.csv"] = "a,,c";

        await RunAsync(
        [
            Step("file.readCsv", Param("path", "in.csv"), Param("separator", "comma"),
                Param("hasHeader", "false"), Param("resultVariable", "rows")),
            Step("file.writeCsv", Param("path", "bare.csv"), Param("rows", "$rows"),
                Param("separator", "comma")),
            Step("file.writeCsv", Param("path", "shown.csv"), Param("rows", "$rows"),
                Param("separator", "comma"), Param("emptyCells", "quoted")),
            Step("file.readCsv", Param("path", "shown.csv"), Param("separator", "comma"),
                Param("hasHeader", "false"), Param("resultVariable", "back")),
        ], devices);

        // Both are a cell holding nothing; the second one is for the reader that wants to see a
        // cell there, and it reads back as the same empty cell.
        Assert.Equal("a,,c\r\n", devices.Files["bare.csv"]);
        Assert.Equal("a,\"\",c\r\n", devices.Files["shown.csv"]);
    }

    /// <summary>
    /// A CSV somebody else's program keeps gets columns of its own, and adding to it has to follow
    /// the names in its first line rather than counts of cells.
    /// </summary>
    [Fact]
    public async Task Adding_to_a_csv_by_name_puts_each_cell_under_the_column_that_has_its_name()
    {
        var devices = new FakeDeviceLayer();
        devices.Files["log.csv"] = "日期,订单号,金额\r\n2026-01-05,A123,120\r\n";
        devices.Files["in.csv"] = "B456,80";

        await RunAsync(
        [
            Step("file.readCsv", Param("path", "in.csv"), Param("separator", "comma"),
                Param("hasHeader", "false"), Param("resultVariable", "rows")),
            Step("file.writeCsv", Param("path", "log.csv"), Param("rows", "$rows"),
                Param("header", "订单号,金额"), Param("separator", "comma"),
                Param("mode", "append"), Param("align", "true")),
        ], devices);

        // The date column is the file's own and this step knows nothing about it: it stays empty
        // in the new line rather than taking the order number.
        Assert.Equal("日期,订单号,金额\r\n2026-01-05,A123,120\r\n,B456,80\r\n",
            devices.Files["log.csv"]);
    }

    [Fact]
    public async Task A_csv_column_name_the_file_has_not_got_is_reported()
    {
        var devices = new FakeDeviceLayer();
        devices.Files["log.csv"] = "日期,订单号\r\n2026-01-05,A123\r\n";
        devices.Files["in.csv"] = "1";

        var (result, _, _) = await RunAsync(
        [
            Step("file.readCsv", Param("path", "in.csv"), Param("separator", "comma"),
                Param("hasHeader", "false"), Param("resultVariable", "rows")),
            Step("file.writeCsv", Param("path", "log.csv"), Param("rows", "$rows"),
                Param("header", "总价"), Param("separator", "comma"),
                Param("mode", "append"), Param("align", "true")),
        ], devices);

        Assert.Equal("Run.NoSuchColumn", result.Key);
    }

    [Fact]
    public async Task A_csv_cell_added_by_name_needs_a_name_of_its_own()
    {
        var devices = new FakeDeviceLayer();
        devices.Files["log.csv"] = "日期,订单号\r\n2026-01-05,A123\r\n";
        devices.Files["in.csv"] = "A1,2";

        var (result, _, _) = await RunAsync(
        [
            Step("file.readCsv", Param("path", "in.csv"), Param("separator", "comma"),
                Param("hasHeader", "false"), Param("resultVariable", "rows")),
            Step("file.writeCsv", Param("path", "log.csv"), Param("rows", "$rows"),
                Param("header", "订单号"), Param("separator", "comma"),
                Param("mode", "append"), Param("align", "true")),
        ], devices);

        Assert.Equal("Run.AlignNeedsNames", result.Key);
    }

    // ------------------------------------------------------------ spreadsheets

    [Fact]
    public async Task A_sheet_written_from_rows_reads_back_as_the_same_rows()
    {
        var devices = new FakeDeviceLayer();
        devices.Files["in.csv"] = "name,age\nalice,30\nbob,41";

        var (_, _, store) = await RunAsync(
        [
            Step("file.readCsv", Param("path", "in.csv"), Param("separator", "comma"),
                Param("hasHeader", "false"), Param("resultVariable", "rows")),
            Step("excel.writeSheet", Param("path", "book.xlsx"), Param("sheet", "Sheet1"),
                Param("rows", "$rows"), Param("mode", "replace")),
            Step("excel.readSheet", Param("path", "book.xlsx"), Param("sheet", "Sheet1"),
                Param("hasHeader", "false"), Param("resultVariable", "back")),
        ], devices);

        // There was no file to begin with, so the step has to have made one.
        Assert.True(devices.Blobs.ContainsKey("book.xlsx"));

        var back = store.Local.Values["back"];
        Assert.Equal(3, back.Items.Count);
        Assert.Equal("name", back.Items[0].Items[0].AsText());
        Assert.Equal("41", back.Items[2].Items[1].AsText());
    }

    [Fact]
    public async Task Reading_a_sheet_leaves_a_header_out_when_it_is_told_to()
    {
        var devices = new FakeDeviceLayer();
        devices.Blobs["book.xlsx"] = Book(Tab("Data", ["name", "age"], ["alice", 30]));

        var (_, _, store) = await RunAsync(
        [
            Step("excel.readSheet", Param("path", "book.xlsx"), Param("sheet", "Data"),
                Param("resultVariable", "rows")),
        ], devices);

        var rows = store.Local.Values["rows"];
        Assert.Single(rows.Items);
        Assert.Equal("alice", rows.Items[0].Items[0].AsText());

        // A number in the sheet is a number here too, not text that looks like one.
        Assert.Equal(30, rows.Items[0].Items[1].Number);
    }

    /// <summary>
    /// Whatever kind of thing a cell holds has to come back as something a macro can read. The
    /// workbook is built by hand, with the format's own library, so what the reader makes of a
    /// date or a boolean is not the writer's doing in disguise.
    /// </summary>
    [Fact]
    public async Task Reading_a_sheet_gives_back_what_each_cell_holds()
    {
        var devices = new FakeDeviceLayer();
        devices.Blobs["report.xlsx"] = Book(Tab("Data",
            ["name", "score", "passed", "when", "day"],
            ["alice", 42.5, true, new DateTime(2024, 3, 5, 9, 30, 0), new DateTime(2024, 3, 5)]));

        var (_, _, store) = await RunAsync(
        [
            Step("excel.readSheet", Param("path", "report.xlsx"), Param("sheet", "Data"),
                Param("hasHeader", "false"), Param("resultVariable", "rows")),
        ], devices);

        var row = store.Local.Values["rows"].Items[1];
        Assert.Equal("alice", row.Items[0].AsText());
        Assert.Equal(42.5, row.Items[1].Number);
        Assert.True(row.Items[2].Flag);
        Assert.Equal("2024-03-05 09:30:00", row.Items[3].AsText());
        Assert.Equal("2024-03-05", row.Items[4].AsText());
    }

    [Fact]
    public async Task A_sheet_is_read_by_the_name_on_its_tab()
    {
        var devices = new FakeDeviceLayer();
        devices.Blobs["two.xlsx"] = Book(Tab("First", ["one"]), Tab("Second", ["two"]));

        var (_, _, store) = await RunAsync(
        [
            Step("excel.readSheet", Param("path", "two.xlsx"), Param("sheet", "Second"),
                Param("hasHeader", "false"), Param("resultVariable", "rows")),
        ], devices);

        Assert.Equal("two", store.Local.Values["rows"].Items[0].Items[0].AsText());
    }

    [Fact]
    public async Task A_sheet_name_left_empty_reads_the_first_sheet()
    {
        var devices = new FakeDeviceLayer();
        devices.Blobs["two.xlsx"] = Book(Tab("First", ["one"]), Tab("Second", ["two"]));

        var (_, _, store) = await RunAsync(
        [
            Step("excel.readSheet", Param("path", "two.xlsx"), Param("sheet", ""),
                Param("hasHeader", "false"), Param("resultVariable", "rows")),
        ], devices);

        Assert.Equal("one", store.Local.Values["rows"].Items[0].Items[0].AsText());
    }

    /// <summary>
    /// A sheet that is not there is the mistake a macro makes when a file was renamed or a typo
    /// slipped in, so the failure has to name the sheets the workbook does have.
    /// </summary>
    [Fact]
    public async Task A_sheet_that_is_not_there_stops_the_step_and_names_the_ones_that_are()
    {
        var devices = new FakeDeviceLayer();
        devices.Blobs["two.xlsx"] = Book(Tab("First", ["one"]), Tab("Second", ["two"]));

        var (result, _, _) = await RunAsync(
        [
            Step("excel.readSheet", Param("path", "two.xlsx"), Param("sheet", "Third"),
                Param("resultVariable", "rows")),
        ], devices);

        Assert.Equal("Run.NoSuchSheet", result.Key);
        Assert.Contains("Third", result.Detail);
        Assert.Contains("First", result.Detail);
        Assert.Contains("Second", result.Detail);
    }

    [Fact]
    public async Task A_file_that_is_not_a_workbook_stops_the_step_with_a_reason()
    {
        var devices = new FakeDeviceLayer();
        devices.Blobs["notes.xlsx"] = [80, 75, 3, 4, 5, 6, 7, 8, 9, 10];

        var (result, _, _) = await RunAsync(
        [
            Step("excel.readSheet", Param("path", "notes.xlsx"), Param("resultVariable", "rows")),
        ], devices);

        Assert.Equal("Run.NotAWorkbook", result.Key);
    }

    /// <summary>
    /// A workbook usually has more in it than the one table a macro writes, and a step that meant
    /// to fill in one sheet must not quietly drop the rest of the file.
    /// </summary>
    [Fact]
    public async Task Writing_a_sheet_leaves_the_other_sheets_as_they_were()
    {
        var devices = new FakeDeviceLayer();
        devices.Blobs["book.xlsx"] = Book(Tab("Data", ["old"]), Tab("Notes", ["keep me"]));
        devices.Files["in.csv"] = "new";

        var (_, _, store) = await RunAsync(
        [
            Step("file.readCsv", Param("path", "in.csv"), Param("separator", "comma"),
                Param("hasHeader", "false"), Param("resultVariable", "rows")),
            Step("excel.writeSheet", Param("path", "book.xlsx"), Param("sheet", "Data"),
                Param("rows", "$rows"), Param("mode", "replace")),
            Step("excel.readSheet", Param("path", "book.xlsx"), Param("sheet", "Data"),
                Param("hasHeader", "false"), Param("resultVariable", "data")),
            Step("excel.readSheet", Param("path", "book.xlsx"), Param("sheet", "Notes"),
                Param("hasHeader", "false"), Param("resultVariable", "notes")),
        ], devices);

        var data = store.Local.Values["data"];
        Assert.Equal("new", Assert.Single(data.Items).Items[0].AsText());
        Assert.Equal("keep me", store.Local.Values["notes"].Items[0].Items[0].AsText());
    }

    [Fact]
    public async Task Writing_a_sheet_again_replaces_it_unless_it_is_told_to_add()
    {
        var devices = new FakeDeviceLayer();
        devices.Files["one.csv"] = "a";
        devices.Files["two.csv"] = "b";

        var (_, _, store) = await RunAsync(
        [
            Step("file.readCsv", Param("path", "one.csv"), Param("separator", "comma"),
                Param("hasHeader", "false"), Param("resultVariable", "first")),
            Step("file.readCsv", Param("path", "two.csv"), Param("separator", "comma"),
                Param("hasHeader", "false"), Param("resultVariable", "second")),
            Step("excel.writeSheet", Param("path", "book.xlsx"), Param("sheet", "Sheet1"),
                Param("rows", "$first"), Param("mode", "replace")),
            Step("excel.writeSheet", Param("path", "book.xlsx"), Param("sheet", "Sheet1"),
                Param("rows", "$second"), Param("mode", "replace")),
            Step("excel.readSheet", Param("path", "book.xlsx"), Param("sheet", "Sheet1"),
                Param("hasHeader", "false"), Param("resultVariable", "replaced")),
            Step("excel.writeSheet", Param("path", "book.xlsx"), Param("sheet", "Sheet1"),
                Param("rows", "$first"), Param("mode", "append")),
            Step("excel.readSheet", Param("path", "book.xlsx"), Param("sheet", "Sheet1"),
                Param("hasHeader", "false"), Param("resultVariable", "added")),
        ], devices);

        Assert.Equal("b", Assert.Single(store.Local.Values["replaced"].Items).Items[0].AsText());

        var added = store.Local.Values["added"].Items;
        Assert.Equal(2, added.Count);
        Assert.Equal("b", added[0].Items[0].AsText());
        Assert.Equal("a", added[1].Items[0].AsText());
    }

    [Fact]
    public async Task Writing_makes_the_sheet_it_names_on_a_file_it_has_to_make()
    {
        var devices = new FakeDeviceLayer();
        devices.Files["in.csv"] = "hello";

        var (_, _, store) = await RunAsync(
        [
            Step("file.readCsv", Param("path", "in.csv"), Param("separator", "comma"),
                Param("hasHeader", "false"), Param("resultVariable", "rows")),
            Step("excel.writeSheet", Param("path", "fresh.xlsx"), Param("sheet", "数据"),
                Param("rows", "$rows"), Param("mode", "replace")),
            Step("excel.readSheet", Param("path", "fresh.xlsx"), Param("sheet", "数据"),
                Param("hasHeader", "false"), Param("resultVariable", "back")),
        ], devices);

        Assert.Equal("hello", store.Local.Values["back"].Items[0].Items[0].AsText());
    }

    [Fact]
    public async Task A_row_from_a_file_reaches_past_column_z()
    {
        var devices = new FakeDeviceLayer();
        devices.Blobs["wide.xlsx"] = Book(Tab("Data",
            [.. Enumerable.Range(1, 30).Select(number => (object)$"c{number}")]));

        var (_, _, store) = await RunAsync(
        [
            Step("excel.readSheet", Param("path", "wide.xlsx"), Param("sheet", "Data"),
                Param("hasHeader", "false"), Param("resultVariable", "rows")),
        ], devices);

        var row = Assert.Single(store.Local.Values["rows"].Items);
        Assert.Equal(30, row.Items.Count);
        Assert.Equal("c30", row.Items[29].AsText());
    }

    [Fact]
    public async Task A_row_written_past_column_z_keeps_its_columns()
    {
        var devices = new FakeDeviceLayer();
        devices.Files["wide.csv"] = string.Join(",", Enumerable.Range(1, 30).Select(number => $"c{number}"));

        var (_, _, store) = await RunAsync(
        [
            Step("file.readCsv", Param("path", "wide.csv"), Param("separator", "comma"),
                Param("hasHeader", "false"), Param("resultVariable", "rows")),
            Step("excel.writeSheet", Param("path", "wide.xlsx"), Param("sheet", "Sheet1"),
                Param("rows", "$rows"), Param("mode", "replace")),
            Step("excel.readSheet", Param("path", "wide.xlsx"), Param("sheet", "Sheet1"),
                Param("hasHeader", "false"), Param("resultVariable", "back")),
        ], devices);

        var row = Assert.Single(store.Local.Values["back"].Items);
        Assert.Equal(30, row.Items.Count);
        Assert.Equal("c30", row.Items[29].AsText());
    }

    /// <summary>
    /// A file that is not one Excel will open is worse than no file at all, and reading it back
    /// cannot show that: our own reader hands back whatever our own writer put there. The format's
    /// own validator is the stand-in for Excel, and opening the file by hand is still the real
    /// check — this only catches a file that is wrong on its face.
    /// </summary>
    [Fact]
    public async Task The_workbook_a_step_writes_is_one_the_format_accepts()
    {
        var devices = new FakeDeviceLayer();
        devices.Files["in.csv"] = "name,age\nalice,30";

        await RunAsync(
        [
            Step("file.readCsv", Param("path", "in.csv"), Param("separator", "comma"),
                Param("hasHeader", "false"), Param("resultVariable", "rows")),
            Step("excel.writeSheet", Param("path", "book.xlsx"), Param("sheet", "Sheet1"),
                Param("rows", "$rows"), Param("mode", "replace")),
        ], devices);

        using var stream = new MemoryStream(devices.Blobs["book.xlsx"]);
        using var document = SpreadsheetDocument.Open(stream, false);
        var complaints = new OpenXmlValidator(FileFormatVersions.Office2019)
            .Validate(document)
            .Select(bad => $"{bad.Path?.XPath}: {bad.Description}")
            .ToList();

        Assert.Empty(complaints);
    }

    /// <summary>
    /// One sheet of a workbook built by hand: the name on its tab and the rows in it.
    /// </summary>
    private static (string Name, object?[][] Rows) Tab(string name, params object?[][] rows)
        => (name, rows);

    /// <summary>
    /// A real workbook, written with the format's own library rather than with the code being
    /// checked. A file written and read by the same piece of code cannot tell a reader that agrees
    /// with its writer from one that reads what the format says, so the files a reader is tried
    /// against are made somewhere else. Text goes in the shared table, the way Excel writes it;
    /// a date goes in as the serial number it is, with a date format on the cell.
    /// </summary>
    private static byte[] Book(params (string Name, object?[][] Rows)[] sheets)
    {
        var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbook = document.AddWorkbookPart();
            workbook.Workbook = new Workbook();
            var tabs = workbook.Workbook.AppendChild(new Sheets());

            var styles = workbook.AddNewPart<WorkbookStylesPart>();
            styles.Stylesheet = new Stylesheet
            {
                CellFormats = new CellFormats(
                    new CellFormat(),
                    new CellFormat { NumberFormatId = 14, ApplyNumberFormat = true }),
            };

            var shared = new SharedStringTable();
            var id = 1u;
            foreach (var (name, rows) in sheets)
            {
                var part = workbook.AddNewPart<WorksheetPart>();
                var data = new SheetData();
                var line = 0u;
                foreach (var row in rows)
                {
                    line++;
                    var entry = new Row { RowIndex = line };
                    for (var at = 0; at < row.Length; at++)
                    {
                        if (row[at] is { } value)
                        {
                            entry.AppendChild(CellAt(at, line, value, shared));
                        }
                    }

                    data.AppendChild(entry);
                }

                part.Worksheet = new Worksheet(data);
                tabs.Append(new Sheet { Id = workbook.GetIdOfPart(part), SheetId = id, Name = name });
                id++;
            }

            if (shared.HasChildren)
            {
                workbook.AddNewPart<SharedStringTablePart>().SharedStringTable = shared;
            }

            workbook.Workbook.Save();
        }

        return stream.ToArray();
    }

    /// <summary>One cell of a hand-built workbook, of whatever kind the value is.</summary>
    private static Cell CellAt(int at, uint line, object value, SharedStringTable shared)
    {
        var cell = new Cell { CellReference = $"{Letter(at)}{line}" };
        switch (value)
        {
            case bool flag:
                cell.DataType = CellValues.Boolean;
                cell.CellValue = new CellValue(flag ? "1" : "0");
                break;

            case int whole:
                cell.DataType = CellValues.Number;
                cell.CellValue = new CellValue(whole.ToString(CultureInfo.InvariantCulture));
                break;

            case double number:
                cell.DataType = CellValues.Number;
                cell.CellValue = new CellValue(number.ToString(CultureInfo.InvariantCulture));
                break;

            case DateTime moment:
                cell.StyleIndex = 1;
                cell.DataType = CellValues.Number;
                cell.CellValue = new CellValue(moment.ToOADate().ToString(CultureInfo.InvariantCulture));
                break;

            default:
                var text = value.ToString() ?? string.Empty;
                var index = shared.Elements<SharedStringItem>().Count();
                shared.AppendChild(new SharedStringItem(new Text(text)));
                cell.DataType = CellValues.SharedString;
                cell.CellValue = new CellValue(index.ToString(CultureInfo.InvariantCulture));
                break;
        }

        return cell;
    }

    /// <summary>The Excel name of a column, counted the way the format counts them.</summary>
    private static string Letter(int index)
    {
        var name = string.Empty;
        for (var at = index; at >= 0; at = (at / 26) - 1)
        {
            name = (char)('A' + (at % 26)) + name;
        }

        return name;
    }

    /// <summary>
    /// A sheet written the way a real library writes one: a blank row is left out of the file
    /// altogether rather than written as an empty one, and a formula comes back as the answer it
    /// worked out. A file built cell by cell by hand never shows either.
    /// </summary>
    private static byte[] WrittenBook(Action<IXLWorksheet> fill)
    {
        using var stream = new MemoryStream();
        using (var book = new XLWorkbook())
        {
            fill(book.AddWorksheet("Data"));
            book.SaveAs(stream);
        }

        return stream.ToArray();
    }

    /// <summary>
    /// A gap in a sheet is nothing at all in the file — the rows above and below it are simply not
    /// next to each other — so what has to be checked is that everything below stays below.
    /// </summary>
    [Fact]
    public async Task A_gap_between_rows_keeps_the_rows_below_it_where_they_were()
    {
        var devices = new FakeDeviceLayer();
        devices.Blobs["gaps.xlsx"] = WrittenBook(sheet =>
        {
            sheet.Cell(1, 1).Value = "top";
            sheet.Cell(4, 1).Value = "fourth";
        });

        var (_, _, store) = await RunAsync(
        [
            Step("excel.readSheet", Param("path", "gaps.xlsx"), Param("sheet", "Data"),
                Param("hasHeader", "false"), Param("resultVariable", "rows")),
        ], devices);

        var rows = store.Local.Values["rows"].Items;
        Assert.Equal(4, rows.Count);
        Assert.Equal("top", rows[0].Items[0].AsText());
        Assert.Empty(rows[1].Items);
        Assert.Empty(rows[2].Items);
        Assert.Equal("fourth", rows[3].Items[0].AsText());
    }

    [Fact]
    public async Task A_formula_reads_as_the_answer_it_worked_out()
    {
        var devices = new FakeDeviceLayer();
        devices.Blobs["sums.xlsx"] = WrittenBook(sheet =>
        {
            sheet.Cell(1, 1).FormulaA1 = "1+2";
            sheet.Cell(1, 2).FormulaA1 = "A1*2";
        });

        var (_, _, store) = await RunAsync(
        [
            Step("excel.readSheet", Param("path", "sums.xlsx"), Param("sheet", "Data"),
                Param("hasHeader", "false"), Param("resultVariable", "rows")),
        ], devices);

        var row = store.Local.Values["rows"].Items[0];
        Assert.Equal(3, row.Items[0].Number);
        Assert.Equal(6, row.Items[1].Number);
    }

    [Fact]
    public async Task A_time_of_day_reads_as_a_time()
    {
        var devices = new FakeDeviceLayer();
        devices.Blobs["times.xlsx"] = WrittenBook(
            sheet => sheet.Cell(1, 1).Value = new TimeSpan(9, 30, 0));

        var (_, _, store) = await RunAsync(
        [
            Step("excel.readSheet", Param("path", "times.xlsx"), Param("sheet", "Data"),
                Param("hasHeader", "false"), Param("resultVariable", "rows")),
        ], devices);

        Assert.Equal("09:30:00", store.Local.Values["rows"].Items[0].Items[0].AsText());
    }

    /// <summary>
    /// What is written has to keep the kind of thing it was: a sheet of numbers that only looks
    /// like numbers is the failure this feature exists to avoid.
    /// </summary>
    [Fact]
    public async Task Writing_keeps_the_kind_of_thing_each_cell_holds()
    {
        var devices = new FakeDeviceLayer();
        devices.Blobs["in.xlsx"] = WrittenBook(sheet =>
        {
            sheet.Cell(1, 1).Value = "alice";
            sheet.Cell(1, 2).Value = 42.5;
            sheet.Cell(1, 3).Value = true;
            sheet.Cell(1, 4).Value = "007";
        });

        var (_, _, store) = await RunAsync(
        [
            Step("excel.readSheet", Param("path", "in.xlsx"), Param("sheet", "Data"),
                Param("hasHeader", "false"), Param("resultVariable", "rows")),
            Step("excel.writeSheet", Param("path", "out.xlsx"), Param("sheet", "Data"),
                Param("rows", "$rows"), Param("mode", "replace")),
            Step("excel.readSheet", Param("path", "out.xlsx"), Param("sheet", "Data"),
                Param("hasHeader", "false"), Param("resultVariable", "back")),
        ], devices);

        var row = store.Local.Values["back"].Items[0];
        Assert.Equal("alice", row.Items[0].AsText());
        Assert.Equal(42.5, row.Items[1].Number);
        Assert.True(row.Items[2].Flag);

        // "007" was text in the file and has to still be text here, or it comes back as 7.
        Assert.Equal("007", row.Items[3].AsText());

        // And the file itself, read by the library that wrote the one we read from: a number in it
        // has to be a number in the file and not text that ends up with a green corner in Excel.
        using var check = new XLWorkbook(new MemoryStream(devices.Blobs["out.xlsx"]));
        var written = check.Worksheet("Data").Row(1);
        Assert.Equal(XLDataType.Text, written.Cell(1).Value.Type);
        Assert.Equal(XLDataType.Number, written.Cell(2).Value.Type);
        Assert.Equal(XLDataType.Boolean, written.Cell(3).Value.Type);
        Assert.Equal(XLDataType.Text, written.Cell(4).Value.Type);
    }

    [Fact]
    public async Task A_sheet_name_excel_would_not_take_stops_the_step_with_a_reason()
    {
        var devices = new FakeDeviceLayer();
        devices.Files["in.csv"] = "a";

        var (result, _, _) = await RunAsync(
        [
            Step("file.readCsv", Param("path", "in.csv"), Param("separator", "comma"),
                Param("hasHeader", "false"), Param("resultVariable", "rows")),
            Step("excel.writeSheet", Param("path", "bad.xlsx"), Param("sheet", "a/b"),
                Param("rows", "$rows"), Param("mode", "replace")),
        ], devices);

        Assert.Equal("Run.BadSheetName", result.Key);
        Assert.Contains("a/b", result.Detail);
    }

    /// <summary>Three columns by three rows, which is what the range checks measure.</summary>
    private static byte[] Table() => WrittenBook(sheet =>
    {
        sheet.Cell(1, 1).Value = "name";
        sheet.Cell(1, 2).Value = "age";
        sheet.Cell(1, 3).Value = "city";
        sheet.Cell(2, 1).Value = "alice";
        sheet.Cell(2, 2).Value = 30;
        sheet.Cell(2, 3).Value = "rome";
        sheet.Cell(3, 1).Value = "bob";
        sheet.Cell(3, 2).Value = 41;
        sheet.Cell(3, 3).Value = "oslo";
    });

    [Fact]
    public async Task Reading_a_range_reads_exactly_that_rectangle()
    {
        var devices = new FakeDeviceLayer();
        devices.Blobs["table.xlsx"] = Table();

        var (_, _, store) = await RunAsync(
        [
            Step("excel.readSheet", Param("path", "table.xlsx"), Param("sheet", "Data"),
                Param("range", "B2:C3"), Param("hasHeader", "false"),
                Param("resultVariable", "rows")),
        ], devices);

        var rows = store.Local.Values["rows"].Items;
        Assert.Equal(2, rows.Count);
        Assert.Equal("30", rows[0].Items[0].AsText());
        Assert.Equal("rome", rows[0].Items[1].AsText());
        Assert.Equal("oslo", rows[1].Items[1].AsText());
    }

    /// <summary>
    /// A range with no end to it is what a person means by "from this cell on", and the sheet's own
    /// used cells say where that stops.
    /// </summary>
    [Fact]
    public async Task Reading_from_a_cell_on_takes_the_rest_of_the_sheet()
    {
        var devices = new FakeDeviceLayer();
        devices.Blobs["table.xlsx"] = Table();

        var (_, _, store) = await RunAsync(
        [
            Step("excel.readSheet", Param("path", "table.xlsx"), Param("sheet", "Data"),
                Param("range", "B2"), Param("hasHeader", "false"), Param("maxRows", "1"),
                Param("resultVariable", "rows")),
        ], devices);

        var row = Assert.Single(store.Local.Values["rows"].Items);
        Assert.Equal(2, row.Items.Count);
        Assert.Equal("30", row.Items[0].AsText());
        Assert.Equal("rome", row.Items[1].AsText());
    }

    [Fact]
    public async Task Reading_a_range_hands_back_the_names_it_was_told_the_header_holds()
    {
        var devices = new FakeDeviceLayer();
        devices.Blobs["table.xlsx"] = Table();

        var (_, _, store) = await RunAsync(
        [
            Step("excel.readSheet", Param("path", "table.xlsx"), Param("sheet", "Data"),
                Param("range", "A1:C3"), Param("hasHeader", "true"),
                Param("headerVariable", "columns"), Param("resultVariable", "rows")),
        ], devices);

        var columns = store.Local.Values["columns"].Items;
        Assert.Equal(["name", "age", "city"], columns.Select(cell => cell.AsText()));
        Assert.Equal(2, store.Local.Values["rows"].Items.Count);
    }

    [Fact]
    public async Task A_cell_can_be_read_as_the_text_it_shows_instead_of_what_it_holds()
    {
        var devices = new FakeDeviceLayer();
        devices.Blobs["shown.xlsx"] = WrittenBook(sheet =>
        {
            sheet.Cell(1, 1).Value = 42;
            sheet.Cell(1, 1).Style.NumberFormat.Format = "0000";
        });

        var (_, _, store) = await RunAsync(
        [
            Step("excel.readSheet", Param("path", "shown.xlsx"), Param("sheet", "Data"),
                Param("hasHeader", "false"), Param("resultVariable", "held")),
            Step("excel.readSheet", Param("path", "shown.xlsx"), Param("sheet", "Data"),
                Param("hasHeader", "false"), Param("asText", "true"),
                Param("resultVariable", "shown")),
        ], devices);

        Assert.Equal(42, store.Local.Values["held"].Items[0].Items[0].Number);
        Assert.Equal("0042", store.Local.Values["shown"].Items[0].Items[0].AsText());
    }

    /// <summary>
    /// Writing from a named cell is how a macro fills in a sheet that is already laid out: the
    /// rows go where it was told, and the rest of the sheet is not touched.
    /// </summary>
    [Fact]
    public async Task Writing_from_a_cell_leaves_the_rest_of_the_sheet_alone()
    {
        var devices = new FakeDeviceLayer();
        devices.Blobs["report.xlsx"] = WrittenBook(sheet =>
        {
            sheet.Cell(1, 1).Value = "keep me";
            sheet.Cell(5, 5).Value = "and me";
        });
        devices.Files["in.csv"] = "filled";

        var (_, _, store) = await RunAsync(
        [
            Step("file.readCsv", Param("path", "in.csv"), Param("separator", "comma"),
                Param("hasHeader", "false"), Param("resultVariable", "rows")),
            Step("excel.writeSheet", Param("path", "report.xlsx"), Param("sheet", "Data"),
                Param("rows", "$rows"), Param("mode", "insert"), Param("startCell", "B2")),
            Step("excel.readSheet", Param("path", "report.xlsx"), Param("sheet", "Data"),
                Param("range", "A1:B2"), Param("hasHeader", "false"), Param("resultVariable", "back")),
        ], devices);

        var rows = store.Local.Values["back"].Items;
        Assert.Equal("keep me", rows[0].Items[0].AsText());
        Assert.Equal("filled", rows[1].Items[1].AsText());

        using var check = new XLWorkbook(new MemoryStream(devices.Blobs["report.xlsx"]));
        Assert.Equal("and me", check.Worksheet("Data").Cell(5, 5).GetString());
    }

    [Fact]
    public async Task Text_that_starts_with_an_equals_sign_can_be_written_as_a_formula()
    {
        var devices = new FakeDeviceLayer();
        devices.Files["in.csv"] = "=SUM(A1:B1)";

        await RunAsync(
        [
            Step("file.readCsv", Param("path", "in.csv"), Param("separator", "comma"),
                Param("hasHeader", "false"), Param("resultVariable", "rows")),
            Step("excel.writeSheet", Param("path", "worked.xlsx"), Param("sheet", "Data"),
                Param("rows", "$rows"), Param("mode", "replace")),
            Step("excel.writeSheet", Param("path", "plain.xlsx"), Param("sheet", "Data"),
                Param("rows", "$rows"), Param("mode", "replace"), Param("formula", "false")),
        ], devices);

        using var worked = new XLWorkbook(new MemoryStream(devices.Blobs["worked.xlsx"]));
        Assert.Equal("SUM(A1:B1)", worked.Worksheet("Data").Cell(1, 1).FormulaA1);

        // Told not to, the same text is written as the text it is: a macro that read a sheet and
        // writes it back has not turned one of its cells into a live formula.
        using var plain = new XLWorkbook(new MemoryStream(devices.Blobs["plain.xlsx"]));
        var cell = plain.Worksheet("Data").Cell(1, 1);
        Assert.Equal(string.Empty, cell.FormulaA1);
        Assert.Equal("=SUM(A1:B1)", cell.GetString());
    }

    [Fact]
    public async Task The_columns_can_be_widened_to_show_what_was_written()
    {
        var devices = new FakeDeviceLayer();
        devices.Files["in.csv"] = "a long piece of text that no default column shows";

        await RunAsync(
        [
            Step("file.readCsv", Param("path", "in.csv"), Param("separator", "comma"),
                Param("hasHeader", "false"), Param("resultVariable", "rows")),
            Step("excel.writeSheet", Param("path", "wide.xlsx"), Param("sheet", "Data"),
                Param("rows", "$rows"), Param("mode", "replace"), Param("autoFit", "true")),
        ], devices);

        using var check = new XLWorkbook(new MemoryStream(devices.Blobs["wide.xlsx"]));
        Assert.True(check.Worksheet("Data").Column(1).Width > 20,
            $"the column should have been widened, it is {check.Worksheet("Data").Column(1).Width}");
    }

    [Fact]
    public async Task The_sheets_of_a_workbook_are_listed_in_the_order_they_sit_in()
    {
        var devices = new FakeDeviceLayer();
        devices.Blobs["book.xlsx"] = Book(Tab("Data", ["a"]), Tab("Notes", ["b"]));

        var (_, _, store) = await RunAsync(
            [Step("excel.listSheets", Param("path", "book.xlsx"), Param("resultVariable", "sheets"))],
            devices);

        Assert.Equal(["Data", "Notes"],
            store.Local.Values["sheets"].Items.Select(name => name.AsText()));
    }

    /// <summary>
    /// A macro that opens "today" runs every day, and some days somebody runs it twice: the second
    /// run must find the sheet already there rather than fail or make a second one.
    /// </summary>
    [Fact]
    public async Task Adding_a_sheet_makes_it_once_and_the_second_go_leaves_it_alone()
    {
        var devices = new FakeDeviceLayer();
        var host = new SilentRunHost();

        var result = await new MacroRunner(new VariableStore(), host, devices).RunAsync(
        [
            Step("excel.addSheet", Param("path", "log.xlsx"), Param("sheet", "Today")),
            Step("excel.addSheet", Param("path", "log.xlsx"), Param("sheet", "Today")),
            Step("excel.listSheets", Param("path", "log.xlsx"), Param("resultVariable", "sheets")),
        ]);

        Assert.True(result.Succeeded);
        Assert.Contains(host.Entries, entry => entry.Key == "Run.AddedSheet");
        Assert.Contains(host.Entries, entry => entry.Key == "Run.SheetAlreadyThere");
    }

    [Fact]
    public async Task A_sheet_can_be_renamed_but_not_to_a_name_that_is_taken()
    {
        var devices = new FakeDeviceLayer();
        devices.Blobs["book.xlsx"] = Book(Tab("Data", ["a"]), Tab("Notes", ["b"]));

        var (result, _, _) = await RunAsync(
            [Step("excel.renameSheet", Param("path", "book.xlsx"), Param("sheet", "Data"),
                Param("newName", "Notes"))], devices);

        Assert.Equal("Run.SheetNameTaken", result.Key);

        var (renamed, _, store) = await RunAsync(
        [
            Step("excel.renameSheet", Param("path", "book.xlsx"), Param("sheet", "Data"),
                Param("newName", "March")),
            Step("excel.listSheets", Param("path", "book.xlsx"), Param("resultVariable", "sheets")),
        ], devices);

        Assert.True(renamed.Succeeded);
        Assert.Equal(["March", "Notes"],
            store.Local.Values["sheets"].Items.Select(name => name.AsText()));
    }

    [Fact]
    public async Task A_sheet_can_be_deleted_but_never_the_last_one()
    {
        var devices = new FakeDeviceLayer();
        devices.Blobs["book.xlsx"] = Book(Tab("Data", ["a"]), Tab("Notes", ["b"]));

        var (dropped, _, store) = await RunAsync(
        [
            Step("excel.deleteSheet", Param("path", "book.xlsx"), Param("sheet", "Notes")),
            Step("excel.listSheets", Param("path", "book.xlsx"), Param("resultVariable", "sheets")),
        ], devices);

        Assert.True(dropped.Succeeded);
        Assert.Equal(["Data"], store.Local.Values["sheets"].Items.Select(name => name.AsText()));

        // A workbook with no sheets in it is not one Excel will open, so the step is refused
        // instead of the file being written that way.
        var (last, _, _) = await RunAsync(
            [Step("excel.deleteSheet", Param("path", "book.xlsx"), Param("sheet", "Data"))], devices);

        Assert.Equal("Run.LastSheet", last.Key);
    }

    [Fact]
    public async Task Saving_and_loading_variables_round_trips_them()
    {
        var devices = new FakeDeviceLayer();
        var (_, _, store) = await RunAsync(
        [
            Step("control.setVariable", Param("name", "counter"), Param("value", "5")),
            Step("file.saveVariables", Param("path", "state.json")),
            Step("control.setVariable", Param("name", "counter"), Param("value", "0")),
            Step("file.loadVariables", Param("path", "state.json")),
        ], devices);

        Assert.Equal("5", store.Local.Values["counter"].AsText());
        Assert.Contains("\"counter\": 5", devices.Files["state.json"]);
    }

    [Fact]
    public async Task A_file_that_cannot_be_read_stops_the_step_with_a_reason()
    {
        var devices = new FakeDeviceLayer
        {
            Refusal = new DeviceActionException("Run.FileNotFound", "ghost.txt"),
        };
        var (result, _, _) = await RunAsync(
            [Step("file.readText", Param("path", "ghost.txt"))], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.FileNotFound", result.Key);
        Assert.Equal("ghost.txt", result.Detail);
    }

    [Fact]
    public async Task Copying_text_puts_it_on_the_clipboard()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
            [Step("clipboard.writeText", Param("text", "hello"))], devices);

        Assert.True(result.Succeeded);
        Assert.Equal("hello", devices.ClipboardText);
        Assert.Contains("clipboardWrite hello", devices.Calls);
    }

    [Fact]
    public async Task Reading_the_clipboard_fills_a_variable()
    {
        var devices = new FakeDeviceLayer { ClipboardText = "from the clipboard" };
        var (_, _, store) = await RunAsync(
            [Step("clipboard.readText", Param("resultVariable", "picked"))], devices);

        Assert.Equal("from the clipboard", store.Local.Values["picked"].AsText());
    }

    [Fact]
    public async Task Reading_the_clipboard_without_a_name_uses_a_default()
    {
        var devices = new FakeDeviceLayer { ClipboardText = "kept" };
        var (_, _, store) = await RunAsync([Step("clipboard.readText")], devices);

        Assert.Equal("kept", store.Local.Values["clipboard"].AsText());
    }

    [Fact]
    public async Task The_picture_on_the_clipboard_can_be_read_into_a_variable()
    {
        var devices = new FakeDeviceLayer
        {
            ClipboardCopy = new ImageFrame(4, 3, new byte[4 * 3 * 4]),
        };
        var (result, _, store) = await RunAsync(
            [Step("clipboard.readImage", Param("resultVariable", "shot"))], devices);

        Assert.True(result.Succeeded);
        Assert.Equal("<image 4x3>", store.Local.Values["shot"].AsText());
        Assert.Equal(4, store.Local.Values["shot.width"].AsNumber());
        Assert.Equal(3, store.Local.Values["shot.height"].AsNumber());
    }

    [Fact]
    public async Task A_clipboard_with_no_picture_empties_the_variable()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, store) = await RunAsync(
            [Step("clipboard.readImage", Param("resultVariable", "shot"))], devices);

        Assert.True(result.Succeeded);
        Assert.Equal(string.Empty, store.Local.Values["shot"].AsText());
        Assert.Contains("clipboardReadImage", devices.Calls);
    }

    [Fact]
    public async Task A_picture_can_be_put_on_the_clipboard_and_taken_off_again()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, store) = await RunAsync(
        [
            Step("clipboard.writeImage", Param("image", "ok.png")),
            Step("clipboard.readImage", Param("resultVariable", "back")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Contains("clipboardWriteImage 2x2", devices.Calls);
        Assert.Equal(2, store.Local.Values["back.width"].AsNumber());
        Assert.Equal(2, store.Local.Values["back.height"].AsNumber());
    }

    [Fact]
    public async Task The_files_on_the_clipboard_come_back_as_a_list()
    {
        var devices = new FakeDeviceLayer();
        devices.ClipboardFiles.Add(@"C:\reports\day.csv");
        devices.ClipboardFiles.Add(@"C:\reports\week.csv");
        var (_, _, store) = await RunAsync(
            [Step("clipboard.readFiles", Param("resultVariable", "picked"))], devices);

        Assert.Equal(["C:\\reports\\day.csv", "C:\\reports\\week.csv"],
            store.Local.Values["picked"].Items.Select(item => item.AsText()));
    }

    [Fact]
    public async Task Files_can_be_put_on_the_clipboard()
    {
        var devices = new FakeDeviceLayer { PathExists = true };
        var (result, _, _) = await RunAsync(
        [
            // A list is built by the actions that build lists, and handing it straight over is
            // what a macro does after reading a folder.
            Step("control.listCreate", Param("name", "chosen"),
                Param("items", @"G:\fake\a.txt; G:\fake\b.txt")),
            Step("clipboard.writeFiles", Param("files", "$chosen")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Equal([@"G:\fake\a.txt", @"G:\fake\b.txt"], devices.ClipboardFiles);
    }

    [Fact]
    public async Task A_relative_path_on_the_clipboard_is_taken_from_the_macros_folder()
    {
        var devices = new FakeDeviceLayer { PathExists = true };
        var (result, _, _) = await RunAsync(
            [Step("clipboard.writeFiles", Param("files", "notes.txt"))], devices);

        // Explorer can only paste a path that really points somewhere, so it goes out as a full one.
        Assert.True(result.Succeeded);
        Assert.Equal([@"G:\fake\notes.txt"], devices.ClipboardFiles);
    }

    [Fact]
    public async Task Putting_a_file_on_the_clipboard_that_is_not_there_is_a_failure()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
            [Step("clipboard.writeFiles", Param("files", "ghost.txt"))], devices);

        // The paste would happen in another program, where nothing could report this back.
        Assert.False(result.Succeeded);
        Assert.Equal("Run.FileNotFound", result.Key);
    }

    [Fact]
    public async Task Clearing_the_clipboard_empties_it()
    {
        var devices = new FakeDeviceLayer { ClipboardText = "gone" };
        await RunAsync([Step("clipboard.clear")], devices);

        Assert.Equal(string.Empty, devices.ClipboardText);
        Assert.Contains("clipboardClear", devices.Calls);
    }

    [Fact]
    public async Task Waiting_for_the_clipboard_keeps_what_was_copied()
    {
        var devices = new FakeDeviceLayer
        {
            ClipboardText = "copied meanwhile",
            ClipboardMovesAfter = 1,
        };
        var (result, _, store) = await RunAsync(
            [Step("clipboard.waitChange", Param("timeoutMs", "500"),
                Param("resultVariable", "picked"))], devices);

        Assert.True(result.Succeeded);
        Assert.Equal("copied meanwhile", store.Local.Values["picked"].AsText());
    }

    [Fact]
    public async Task Waiting_for_the_clipboard_gives_up_after_the_timeout()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
            [Step("clipboard.waitChange", Param("timeoutMs", "0"))], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.ClipboardTimeout", result.Key);
    }

    [Fact]
    public async Task Copying_a_selection_presses_the_shortcut_and_keeps_the_result()
    {
        var devices = new FakeDeviceLayer
        {
            ClipboardText = "the selection",
            ClipboardMovesAfter = 1,
        };
        var (result, _, store) = await RunAsync(
            [Step("clipboard.copy", Param("keys", "Ctrl+C"), Param("timeoutMs", "500"),
                Param("resultVariable", "picked"))], devices);

        Assert.True(result.Succeeded);
        Assert.Equal("the selection", store.Local.Values["picked"].AsText());
        Assert.Contains("hotkey Ctrl|C 50", devices.Calls);
        Assert.Contains("clipboardRead", devices.Calls);
    }

    [Fact]
    public async Task Pasting_puts_the_text_on_the_clipboard_then_presses_the_shortcut()
    {
        var devices = new FakeDeviceLayer();
        await RunAsync(
            [Step("clipboard.paste", Param("text", "typed"), Param("keys", "Ctrl+V"))], devices);

        Assert.Equal(["clipboardWrite typed", "hotkey Ctrl|V 50"], devices.Calls);
    }

    [Fact]
    public async Task Pasting_with_no_text_just_presses_the_shortcut()
    {
        var devices = new FakeDeviceLayer();
        await RunAsync([Step("clipboard.paste", Param("keys", "Ctrl+V"))], devices);

        Assert.Equal(["hotkey Ctrl|V 50"], devices.Calls);
    }

    [Fact]
    public async Task A_waiting_timeout_is_not_bent_by_the_speed_factor()
    {
        // A timeout says when to stop trying, which is not the same thing as pacing, so the
        // same wait gives up after the same time at ×10 as it does at ×1. The two waits are
        // compared with each other rather than against a fixed number of milliseconds: a busy
        // machine stretches whichever wait happens to be running, and a fixed bound turns that
        // into a failure that has nothing to do with the speed factor.
        var plain = await WaitedAsync(1, "1000");
        var fast = await WaitedAsync(10, "1000");

        Assert.True(fast < (plain * 2) + 2000,
            $"×10 waited {fast:0}ms, which is nowhere near the ×1 wait of {plain:0}ms");
    }

    /// <summary>How long a clipboard wait of the given length takes at a playback speed.</summary>
    private static async Task<double> WaitedAsync(double speed, string timeoutMs)
    {
        var devices = new FakeDeviceLayer();
        var watch = Stopwatch.StartNew();
        var result = await new MacroRunner(new VariableStore(), new SilentRunHost(), devices, speed)
            .RunAsync([Step("clipboard.waitChange", Param("timeoutMs", timeoutMs))]);
        watch.Stop();

        Assert.False(result.Succeeded);
        Assert.Equal("Run.ClipboardTimeout", result.Key);
        return watch.Elapsed.TotalMilliseconds;
    }

    [Fact]
    public async Task Starting_a_program_hands_back_its_id()
    {
        var devices = new FakeDeviceLayer { NextProcessId = 4321 };
        var (result, _, store) = await RunAsync(
        [
            Step("process.start", Param("file", "notepad.exe"), Param("arguments", "/a"),
                Param("workingDirectory", @"C:\temp"), Param("hidden", "false"),
                Param("resultVariable", "pid")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Equal(4321d, store.Local.Values["pid"].Number);
        Assert.Contains(@"start notepad.exe|/a|C:\temp|False", devices.Calls);
    }

    [Fact]
    public async Task Starting_a_program_without_a_name_uses_a_default()
    {
        var devices = new FakeDeviceLayer { NextProcessId = 7 };
        var (_, _, store) = await RunAsync(
            [Step("process.start", Param("file", "calc.exe"))], devices);

        Assert.Equal(7d, store.Local.Values["processId"].Number);
    }

    [Fact]
    public async Task Starting_a_program_as_administrator_asks_for_the_elevation_prompt()
    {
        var devices = new FakeDeviceLayer();
        await RunAsync(
        [
            Step("process.start", Param("file", "cmd.exe"), Param("arguments", "/c whoami"),
                Param("runAsAdmin", "true")),
        ], devices);

        // The device is the one that decides how to raise the prompt; the step only says it wants
        // elevation, and the flag is on the request rather than left out of it.
        Assert.Contains(devices.Calls, call =>
            call.StartsWith("start cmd.exe|/c whoami|") && call.EndsWith("|admin"));
    }

    [Fact]
    public async Task Starting_a_program_normally_does_not_ask_for_elevation()
    {
        var devices = new FakeDeviceLayer();
        await RunAsync([Step("process.start", Param("file", "calc.exe"))], devices);

        Assert.DoesNotContain(devices.Calls, call => call.EndsWith("|admin"));
    }

    [Fact]
    public async Task Starting_a_program_can_hand_it_its_own_environment()
    {
        var devices = new FakeDeviceLayer();
        await RunAsync(
        [
            Step("control.setVariable", Param("name", "who"), Param("value", "Ann")),
            Step("process.start", Param("file", "tool.exe"),
                Param("environment", "LANG=zh_CN.UTF-8\n#TOKEN=off\n\nUSER=$who")),
        ], devices);

        // Only the lines that name something are passed on, and a value can be built out of a
        // variable the same way every other text field can.
        Assert.Contains(devices.Calls, call => call.EndsWith("|env LANG=zh_CN.UTF-8;USER=Ann"));
    }

    [Fact]
    public async Task Running_a_command_can_give_it_its_own_environment()
    {
        var devices = new FakeDeviceLayer();
        await RunAsync(
        [
            Step("command.run", Param("file", "cmd.exe"), Param("arguments", "/c set TOKEN"),
                Param("environment", "TOKEN=abc\nTOKEN=def")),
        ], devices);

        // The last line wins when the same name is written twice, so a macro can override a
        // setting further up without editing that line.
        Assert.Contains(devices.Calls, call => call.Contains("|env TOKEN=def"));
        Assert.DoesNotContain(devices.Calls, call => call.Contains("TOKEN=abc"));
    }

    [Fact]
    public async Task A_line_that_is_not_a_name_and_a_value_stops_the_step()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
        [
            Step("command.run", Param("file", "cmd.exe"), Param("environment", "TOKEN")),
        ], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.BadEnvironment", result.Key);
    }

    [Fact]
    public async Task Running_a_command_can_tell_it_what_to_read()
    {
        var devices = new FakeDeviceLayer();
        await RunAsync(
        [
            Step("control.setVariable", Param("name", "who"), Param("value", "Ann")),
            Step("command.run", Param("file", "python"), Param("standardInput", "hello\n$who")),
        ], devices);

        // The line ending the macro wrote is kept, and a value can be written into the input the
        // same way it can be written into any other field.
        Assert.Contains(devices.Calls, call => call.Contains("|in hello\\nAnn"));
    }

    [Fact]
    public async Task A_command_that_is_not_given_anything_to_read_is_given_nothing()
    {
        var devices = new FakeDeviceLayer();
        await RunAsync(
        [
            Step("command.run", Param("file", "cmd.exe"), Param("standardInput", "  \n ")),
        ], devices);

        // A field that holds nothing but spaces is an untouched field, not a program that is meant
        // to be fed a blank line.
        Assert.Contains(devices.Calls, call => call.StartsWith("run cmd.exe|") && !call.Contains("|in "));
    }

    [Fact]
    public async Task A_command_can_report_what_it_prints_while_it_is_still_running()
    {
        var devices = new FakeDeviceLayer
        {
            Command = new CommandResult(0, "first\n\nthird", "watch out"),
        };
        var host = new SilentRunHost();
        var store = new VariableStore();

        await new MacroRunner(store, host, devices).RunAsync(
        [
            Step("command.run", Param("file", "build.exe"), Param("streamOutput", "true"),
                Param("resultVariable", "out")),
        ]);

        // Every line is handed over as it arrives, blank lines included, and the result variable
        // still holds all of it.
        Assert.Equal(
            ["first", "", "third"],
            host.Entries.Where(entry => entry.Key == "Run.CommandOutput")
                .Select(entry => (string)entry.Arguments[0]).ToArray());
        Assert.Equal("watch out",
            Assert.Single(host.Entries, entry => entry.Key == "Run.CommandError").Arguments[0]);
        Assert.Equal("first\n\nthird", store.Local.Values["out"].AsText());
    }

    [Fact]
    public async Task A_command_that_is_not_watched_keeps_its_output_to_itself()
    {
        var devices = new FakeDeviceLayer
        {
            Command = new CommandResult(0, "first\nsecond", string.Empty),
        };
        var host = new SilentRunHost();

        await new MacroRunner(new VariableStore(), host, devices).RunAsync(
        [
            Step("command.run", Param("file", "build.exe"), Param("resultVariable", "out")),
        ]);

        Assert.DoesNotContain(host.Entries, entry => entry.Key == "Run.CommandOutput");
        Assert.DoesNotContain(host.Entries, entry => entry.Key == "Run.CommandStarted");
    }

    [Fact]
    public async Task A_command_is_read_in_this_machines_own_code_page_unless_it_says_otherwise()
    {
        var devices = new FakeDeviceLayer();
        await RunAsync(
        [
            Step("command.run", Param("file", "cmd.exe")),
            Step("command.run", Param("file", "node"), Param("outputEncoding", "utf8")),
        ], devices);

        // Left alone, the output is read in this machine's own code page — the one cmd.exe and
        // Windows PowerShell print Chinese in — because getting that wrong turns every Chinese word
        // into question marks, and a program that prints UTF-8 wherever it runs can say so.
        Assert.Contains(devices.Calls, call => call.StartsWith("run cmd.exe|") && call.EndsWith("|out default"));
        Assert.Contains(devices.Calls, call => call.StartsWith("run node|") && call.EndsWith("|out utf8"));
    }

    [Fact]
    public async Task A_script_is_read_the_way_its_interpreter_prints()
    {
        var devices = new FakeDeviceLayer();
        await RunAsync(
        [
            Step("script.run", Param("language", "powershell"), Param("script", "echo hi")),
            Step("script.run", Param("language", "node"), Param("script", "console.log('hi')")),
        ], devices);

        // Node prints UTF-8 on every machine; Windows PowerShell and the command prompt print in the
        // machine's own code page. Nobody should have to spell that out for each script.
        Assert.Contains(devices.Calls, call =>
            call.StartsWith("run powershell.exe") && call.EndsWith("|out system"));
        Assert.Contains(devices.Calls, call =>
            call.StartsWith("run node|") && call.EndsWith("|out utf8"));
    }

    [Fact]
    public async Task Waiting_for_a_program_keeps_the_id_once_it_appears()
    {
        var devices = new FakeDeviceLayer
        {
            NextProcessId = 55,
            AppearsLater = "notepad",
            AppearsAfter = 1,
        };
        var (result, _, store) = await RunAsync(
        [
            Step("process.waitFor", Param("name", "notepad"), Param("timeoutMs", "2000"),
                Param("resultVariable", "pid")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Equal(55d, store.Local.Values["pid"].Number);
    }

    [Fact]
    public async Task Waiting_for_a_program_gives_up_after_the_timeout()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
            [Step("process.waitFor", Param("name", "ghost"), Param("timeoutMs", "0"))], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.ProgramNotFound", result.Key);
    }

    [Fact]
    public async Task Checking_whether_a_program_runs_leaves_true_or_false()
    {
        var devices = new FakeDeviceLayer();
        devices.Running["notepad"] = [10];
        var (_, _, store) = await RunAsync(
        [
            Step("process.exists", Param("name", "notepad"), Param("resultVariable", "here")),
            Step("process.exists", Param("name", "gone"), Param("resultVariable", "there")),
        ], devices);

        Assert.True(store.Local.Values["here"].Flag);
        Assert.False(store.Local.Values["there"].Flag);
    }

    [Fact]
    public async Task Listing_programs_names_them_without_repeats()
    {
        var devices = new FakeDeviceLayer();
        devices.Running["notepad"] = [1, 2];
        devices.Running["calc"] = [3];
        var (_, _, store) = await RunAsync(
            [Step("process.list", Param("resultVariable", "names"))], devices);

        var names = store.Local.Values["names"];
        Assert.Equal(2, names.Items.Count);
        Assert.Equal(["calc", "notepad"], names.Items.Select(item => item.AsText()));
    }

    [Fact]
    public async Task Reading_a_programs_details_fills_the_parts_it_is_broken_into()
    {
        var devices = new FakeDeviceLayer();
        devices.Running["notepad"] = [42];
        var (result, _, store) = await RunAsync(
            [Step("process.info", Param("target", "notepad"), Param("resultVariable", "note"))],
            devices);

        Assert.True(result.Succeeded);
        Assert.Equal(@"C:\fake\notepad.exe", store.Local.Values["note"].AsText());
        Assert.Equal(@"C:\fake\notepad.exe", store.Local.Values["note.path"].AsText());
        Assert.Equal(42d, store.Local.Values["note.id"].Number);
        Assert.Equal("notepad", store.Local.Values["note.name"].AsText());
        Assert.Equal(12.5d, store.Local.Values["note.memoryMb"].Number);
        Assert.Equal(3.4d, store.Local.Values["note.cpuSeconds"].Number);
    }

    [Fact]
    public async Task Asking_about_a_program_that_is_not_running_fails_the_step()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, store) = await RunAsync(
            [Step("process.info", Param("target", "ghost"), Param("resultVariable", "info"))],
            devices);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal("Run.NoSuchProcess", result.Key);
        Assert.False(store.TryGet("info", out _));
    }

    [Fact]
    public async Task Stopping_a_program_by_name_counts_what_it_closed()
    {
        var devices = new FakeDeviceLayer();
        devices.Running["notepad"] = [1];
        var (_, _, store) = await RunAsync(
        [
            Step("process.kill", Param("target", "notepad"),
                Param("resultVariable", "closed")),
        ], devices);

        Assert.Equal(1d, store.Local.Values["closed"].Number);
        Assert.Contains("stopName notepad|False", devices.Calls);
    }

    [Fact]
    public async Task Stopping_a_program_by_id_uses_the_id()
    {
        var devices = new FakeDeviceLayer();
        var (_, _, store) = await RunAsync(
        [
            Step("process.kill", Param("target", "77"), Param("force", "true"),
                Param("resultVariable", "closed")),
        ], devices);

        Assert.Equal(1d, store.Local.Values["closed"].Number);
        Assert.Contains("stopId 77|True", devices.Calls);
    }

    [Fact]
    public async Task Waiting_for_exit_keeps_the_exit_code()
    {
        var devices = new FakeDeviceLayer();
        devices.Exited.Add(42);
        devices.ExitCodes[42] = 7;
        var (result, _, store) = await RunAsync(
        [
            Step("process.waitExit", Param("id", "42"), Param("timeoutMs", "1000"),
                Param("resultVariable", "code")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Equal(7d, store.Local.Values["code"].Number);
    }

    [Fact]
    public async Task Waiting_for_exit_gives_up_after_the_timeout()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
            [Step("process.waitExit", Param("id", "42"), Param("timeoutMs", "0"))], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.ProcessTimeout", result.Key);
    }

    [Fact]
    public async Task Waiting_for_exit_rejects_an_id_that_is_not_a_number()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
            [Step("process.waitExit", Param("id", "nothing"))], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.BadProcessId", result.Key);
    }

    [Fact]
    public async Task Running_a_command_keeps_its_output_and_exit_code()
    {
        var devices = new FakeDeviceLayer
        {
            Command = new CommandResult(3, "hello", "watch out"),
        };
        var (_, _, store) = await RunAsync(
        [
            Step("command.run", Param("file", "cmd.exe"), Param("arguments", "/c echo hello"),
                Param("timeoutMs", "5000"), Param("resultVariable", "out"),
                Param("errorVariable", "err"), Param("exitCodeVariable", "code")),
        ], devices);

        Assert.Equal("hello", store.Local.Values["out"].AsText());
        Assert.Equal("watch out", store.Local.Values["err"].AsText());
        Assert.Equal(3d, store.Local.Values["code"].Number);
        Assert.Contains(@"run cmd.exe|/c echo hello||5000|out default", devices.Calls);
    }

    [Fact]
    public async Task Running_a_script_keeps_what_it_printed_and_cleans_up_after_itself()
    {
        var devices = new FakeDeviceLayer
        {
            Command = new CommandResult(0, "\n  hello from a script  \n\n", string.Empty),
            PathExists = true,
        };

        var (result, _, store) = await RunAsync(
        [
            Step("script.run", Param("language", "powershell"),
                Param("script", "Write-Host 'hello from a script'"),
                Param("resultVariable", "said")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Equal("  hello from a script  ", store.Local.Values["said"].AsText());

        // The script lives in the macro, so the copy handed to the interpreter is removed again.
        Assert.Contains(devices.Calls, call => call.StartsWith("writeFile") && call.Contains(".ps1"));
        Assert.Contains(devices.Calls, call => call.StartsWith("deleteFile") && call.Contains(".ps1"));
        Assert.Empty(devices.Files);
        Assert.Contains(devices.Calls,
            call => call.StartsWith("run powershell.exe|-NoProfile") && call.Contains("|G:\\fake|"));
    }

    [Fact]
    public async Task A_script_runs_where_the_macros_are_when_that_folder_is_there()
    {
        var devices = new FakeDeviceLayer { PathExists = true };
        await RunAsync([Step("script.run", Param("script", "echo hi"))], devices);

        Assert.Contains(devices.Calls,
            call => call.StartsWith("run powershell.exe") && call.Contains("|G:\\fake|60000"));
    }

    [Fact]
    public async Task A_script_still_runs_when_the_macros_folder_is_not_there_yet()
    {
        // A program cannot be started in a folder that does not exist, so a folder that is not
        // there yet is left to whoever started WhaleGenie rather than failing the step.
        var devices = new FakeDeviceLayer();
        await RunAsync([Step("script.run", Param("script", "echo hi"))], devices);

        Assert.Contains(devices.Calls,
            call => call.StartsWith("run powershell.exe") && call.EndsWith("||60000|out system"));
    }

    [Fact]
    public async Task A_script_can_be_given_arguments_and_a_folder_of_its_own()
    {
        var devices = new FakeDeviceLayer();
        await RunAsync(
        [
            Step("script.run", Param("script", "echo hi"), Param("arguments", "--quiet"),
                Param("folder", @"C:\work"), Param("timeoutMs", "5000")),
        ], devices);

        Assert.Contains(devices.Calls,
            call => call.StartsWith("run powershell.exe")
                && call.EndsWith("--quiet|C:\\work|5000|out system"));
    }

    [Fact]
    public async Task A_script_can_have_a_macro_value_written_into_it()
    {
        var devices = new FakeDeviceLayer();
        await RunAsync(
        [
            Step("control.setVariable", Param("name", "who"), Param("value", "Ann")),
            Step("script.run", Param("script", "Write-Host {{who}}"),
                Param("arguments", "--to {{who}} --x {{nope}}"), Param("resultVariable", "out")),
        ], devices);

        // The value goes in where it was asked for, in the script and on the command line alike,
        // and a name the macro does not know is left where it can be seen rather than quietly
        // turning into nothing.
        Assert.Contains(devices.Calls,
            call => call.StartsWith("writeFile") && call.EndsWith("Write-Host Ann False utf8bom"));
        Assert.Contains(devices.Calls, call =>
            call.StartsWith(@"run powershell.exe|-NoProfile")
            && call.Contains("--to Ann --x {{nope}}"));
    }

    [Fact]
    public async Task A_script_can_be_run_by_a_program_the_macro_names_itself()
    {
        var devices = new FakeDeviceLayer();
        await RunAsync(
        [
            Step("script.run", Param("language", "custom"), Param("interpreter", "dotnet-script"),
                Param("extension", "csx"), Param("script", "Console.WriteLine(1);")),
        ], devices);

        // The program is the macro's to name, the script's path goes after it, and an ending
        // written without a full stop gets one.
        Assert.Contains(devices.Calls, call =>
            call.StartsWith("run dotnet-script|") && call.Contains(".csx\"||60000"));
        Assert.Contains(devices.Calls, call =>
            call.StartsWith("writeFile") && call.EndsWith(".csx Console.WriteLine(1); False utf8"));
    }

    [Fact]
    public async Task A_script_for_another_program_keeps_the_flags_and_the_encoding_it_was_given()
    {
        var devices = new FakeDeviceLayer();
        await RunAsync(
        [
            Step("script.run", Param("language", "custom"), Param("interpreter", "cscript //nologo"),
                Param("extension", ".vbs"), Param("encoding", "gbk"),
                Param("script", "WScript.Echo \"中文\"")),
        ], devices);

        // cscript reads a script through the system code page, so a Chinese script has to be
        // written in GBK rather than the UTF-8 everything else gets.
        Assert.Contains(devices.Calls, call =>
            call.StartsWith(@"run cscript|//nologo """) && call.Contains(".vbs\"||60000"));
        Assert.Contains(devices.Calls, call =>
            call.StartsWith("writeFile") && call.EndsWith("gbk"));
    }

    [Fact]
    public async Task A_script_for_another_program_says_so_when_it_names_no_program()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
        [
            Step("script.run", Param("language", "custom"), Param("extension", "csx"),
                Param("script", "echo hi")),
        ], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.MissingInterpreter", result.Key);
    }

    [Fact]
    public async Task A_script_for_another_program_says_so_when_it_names_no_ending()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
        [
            Step("script.run", Param("language", "custom"), Param("interpreter", "dotnet-script"),
                Param("script", "echo hi")),
        ], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.MissingScriptExtension", result.Key);
    }

    [Fact]
    public async Task A_script_that_stops_with_an_error_fails_the_step()
    {
        var devices = new FakeDeviceLayer
        {
            Command = new CommandResult(1, string.Empty, "cannot open the file"),
        };

        var (result, _, _) = await RunAsync(
        [Step("script.run", Param("script", "throw 'no'"))], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.ScriptFailed", result.Key);
        Assert.Contains("cannot open the file", result.Detail);
    }

    [Fact]
    public async Task A_script_step_with_nothing_in_it_fails()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync([Step("script.run", Param("script", "   "))], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.MissingScript", result.Key);
    }

    [Fact]
    public async Task A_script_for_an_interpreter_WhaleGenie_does_not_know_fails()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
        [Step("script.run", Param("language", "brainfuck"), Param("script", "+++"))], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.UnknownInterpreter", result.Key);
    }

    [Fact]
    public async Task System_info_and_environment_variables_reach_variables()
    {
        var devices = new FakeDeviceLayer();
        devices.SystemValues["desktopFolder"] = @"C:\Users\me\Desktop";
        devices.EnvironmentValues["TEMP"] = @"C:\Temp";
        var (_, _, store) = await RunAsync(
        [
            Step("system.info", Param("field", "desktopFolder"), Param("resultVariable", "desk")),
            Step("system.environment", Param("name", "TEMP"), Param("resultVariable", "temp")),
        ], devices);

        Assert.Equal(@"C:\Users\me\Desktop", store.Local.Values["desk"].AsText());
        Assert.Equal(@"C:\Temp", store.Local.Values["temp"].AsText());
    }

    [Fact]
    public async Task The_power_action_asks_for_the_thing_the_macro_picked()
    {
        var devices = new FakeDeviceLayer();
        await RunAsync(
        [
            Step("system.power", Param("what", "shutDown"), Param("graceSeconds", "30")),
            Step("system.power", Param("what", "monitorOff")),
        ], devices);

        Assert.Equal(["power ShutDown 30", "power MonitorOff 0"], devices.Calls);
    }

    [Fact]
    public async Task The_power_action_locks_the_screen_when_it_is_not_told_what_to_do()
    {
        // The gentlest of the lot, because a step that was added and left alone should not be able
        // to close anything down.
        var devices = new FakeDeviceLayer();
        await RunAsync([Step("system.power")], devices);

        Assert.Equal(["power Lock 0"], devices.Calls);
    }

    [Fact]
    public async Task The_power_action_refuses_a_name_it_does_not_know()
    {
        // Locking the screen would be a strange thing to do on the way to shutting a machine down,
        // so a mistyped name stops the step instead of quietly turning into a different action.
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
            [Step("system.power", Param("what", "shutdownnow"))], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.UnknownPowerAction", result.Key);
        Assert.Empty(devices.Calls);
    }

    [Fact]
    public async Task The_volume_action_leaves_behind_the_level_the_machine_ended_at()
    {
        var devices = new FakeDeviceLayer { SpeakerVolume = 30 };
        var (_, _, store) = await RunAsync(
        [
            Step("system.volume", Param("what", "set"), Param("percent", "80"),
                Param("resultVariable", "was")),
            Step("system.volume", Param("what", "down"), Param("stepPercent", "15"),
                Param("resultVariable", "now")),
        ], devices);

        Assert.Equal(80d, store.Local.Values["was"].Number);
        Assert.Equal(65d, store.Local.Values["now"].Number);
        Assert.Contains("setVolume 80", devices.Calls);
        Assert.Contains("setVolume 65", devices.Calls);
    }

    [Fact]
    public async Task Turning_the_volume_down_never_goes_below_nothing()
    {
        var devices = new FakeDeviceLayer { SpeakerVolume = 4 };
        var (_, _, store) = await RunAsync(
        [
            Step("system.volume", Param("what", "down"), Param("stepPercent", "20")),
        ], devices);

        // The device clamps as well, so a macro cannot talk the machine into a volume it cannot have.
        Assert.Equal(0d, store.Local.Values["volume"].Number);
    }

    [Fact]
    public async Task Muting_says_which_way_the_sound_went()
    {
        var devices = new FakeDeviceLayer();
        var host = new SilentRunHost();

        await new MacroRunner(new VariableStore(), host, devices).RunAsync(
        [
            Step("system.volume", Param("what", "toggleMute")),
            Step("system.volume", Param("what", "toggleMute")),
        ]);

        // The level does not change when the sound is switched off, so each way is said out loud
        // rather than leaving a reader to work it out from a number that never moved.
        Assert.Single(host.Entries, entry => entry.Key == "Run.SoundOff");
        Assert.Single(host.Entries, entry => entry.Key == "Run.SoundOn");
    }

    [Fact]
    public async Task The_volume_action_refuses_a_name_it_does_not_know()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
            [Step("system.volume", Param("what", "louder"))], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.UnknownVolumeAction", result.Key);
        Assert.Empty(devices.Calls);
    }

    [Fact]
    public async Task The_sound_action_plays_the_sound_the_macro_picked()
    {
        var devices = new FakeDeviceLayer();
        await RunAsync(
        [
            Step("system.sound", Param("what", "error")),
            Step("system.sound"),
        ], devices);

        // A step added and left alone plays the machine's own default sound rather than a particular
        // one, so nothing in a macro shouts by accident.
        Assert.Equal(["sound Error", "sound Default"], devices.Calls);
    }

    [Fact]
    public async Task The_sound_action_refuses_a_name_it_does_not_know()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
            [Step("system.sound", Param("what", "fanfare"))], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.UnknownSound", result.Key);
        Assert.Empty(devices.Calls);
    }

    [Fact]
    public async Task The_notify_action_shows_what_the_macro_asked_for()
    {
        var devices = new FakeDeviceLayer();
        await RunAsync(
        [
            Step("system.notify",
                Param("heading", "Backup"), Param("message", "All files copied."), Param("what", "warning")),
            Step("system.notify", Param("message", "Done.")),
        ], devices);

        // A step added and left alone is an ordinary note rather than a warning, and its heading is
        // the program's own name, which the notification area fills in.
        Assert.Equal(
            ["notify Warning [Backup] All files copied.", "notify Information [] Done."],
            devices.Calls);
    }

    [Fact]
    public async Task The_notify_action_refuses_a_kind_it_does_not_know()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
            [Step("system.notify", Param("message", "Hello"), Param("what", "fanfare"))], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.UnknownNotification", result.Key);
        Assert.Empty(devices.Calls);
    }

    [Fact]
    public async Task The_input_method_action_reads_the_layout_the_focused_window_is_using()
    {
        var devices = new FakeDeviceLayer();
        var (_, _, store) = await RunAsync(
        [
            Step("system.ime", Param("what", "get"), Param("resultVariable", "which")),
            Step("system.ime", Param("what", "list"), Param("resultVariable", "all")),
        ], devices);

        Assert.Equal("中文(简体) - 微软拼音", store.Local.Values["which"].AsText());

        // The list is what the layout field can be filled in from, so it is the same wording.
        Assert.Equal("中文(简体) - 微软拼音;英语(美国)", store.Local.Values["all"].AsText());
    }

    [Fact]
    public async Task The_input_method_action_switches_to_a_layout_by_part_of_its_name()
    {
        var devices = new FakeDeviceLayer();
        var (_, _, store) = await RunAsync(
        [
            Step("system.ime", Param("what", "switch"), Param("layout", "英语"),
                Param("resultVariable", "now")),
        ], devices);

        // The variable holds the layout the window really ended up on, which is the name the device
        // matched rather than the part the macro typed.
        Assert.Equal("英语(美国)", store.Local.Values["now"].AsText());
        Assert.Contains("switchInputMethod 英语", devices.Calls);
    }

    [Fact]
    public async Task The_input_method_action_says_so_when_no_layout_matches()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
            [Step("system.ime", Param("what", "switch"), Param("layout", "法语"))], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.LayoutNotFound", result.Key);
        Assert.Contains("法语", result.Detail);
    }

    [Fact]
    public async Task The_input_method_action_refuses_a_name_it_does_not_know()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
            [Step("system.ime", Param("what", "chinese"))], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.UnknownInputMethodAction", result.Key);
        Assert.Empty(devices.Calls);
    }

    [Fact]
    public async Task The_brightness_action_leaves_behind_the_brightness_the_screens_ended_at()
    {
        var devices = new FakeDeviceLayer { ScreenBrightness = 20 };
        var (_, _, store) = await RunAsync(
        [
            Step("system.brightness", Param("what", "up"), Param("stepPercent", "30"),
                Param("resultVariable", "up")),
            Step("system.brightness", Param("what", "down"), Param("stepPercent", "15"),
                Param("resultVariable", "down")),
        ], devices);

        Assert.Equal(50d, store.Local.Values["up"].Number);
        Assert.Equal(35d, store.Local.Values["down"].Number);
        Assert.Contains("setBrightness 50", devices.Calls);
        Assert.Contains("setBrightness 35", devices.Calls);
    }

    [Fact]
    public async Task The_brightness_action_reads_without_changing_anything()
    {
        var devices = new FakeDeviceLayer { ScreenBrightness = 65 };
        var (_, _, store) = await RunAsync([Step("system.brightness")], devices);

        Assert.Equal(65d, store.Local.Values["brightness"].Number);

        // Reading is the whole step, so the screens are asked exactly once.
        Assert.Equal(["brightness"], devices.Calls);
    }

    [Fact]
    public async Task The_brightness_action_refuses_a_name_it_does_not_know()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
            [Step("system.brightness", Param("what", "darker"))], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.UnknownBrightnessAction", result.Key);
        Assert.Empty(devices.Calls);
    }

    [Fact]
    public async Task Checking_for_a_window_leaves_true_or_false()
    {
        var devices = new FakeDeviceLayer();
        devices.Windows.Add(new WindowInfo(7, "Untitled - Notepad",
            new ScreenPoint(10, 20), new ScreenSize(300, 200), false, false));
        var (_, _, store) = await RunAsync(
        [
            Step("window.exists", Param("title", "notepad"), Param("resultVariable", "here")),
            Step("window.exists", Param("title", "calculator"), Param("resultVariable", "there")),
        ], devices);

        Assert.True(store.Local.Values["here"].Flag);
        Assert.False(store.Local.Values["there"].Flag);
    }

    [Fact]
    public async Task Waiting_for_a_window_keeps_its_title()
    {
        var devices = new FakeDeviceLayer
        {
            WindowAppearsLater = "Untitled - Notepad",
            WindowAppearsAfter = 1,
        };
        var (result, _, store) = await RunAsync(
        [
            Step("window.waitFor", Param("title", "notepad"), Param("timeoutMs", "2000"),
                Param("resultVariable", "win")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Equal("Untitled - Notepad", store.Local.Values["win"].AsText());
    }

    [Fact]
    public async Task A_window_can_be_found_by_process_or_class()
    {
        var devices = WithAWindow();
        // The title is "Notepad - notes.txt", so neither of the other two names appears in it:
        // a match can only have come from the way the step asked for.
        devices.WindowFacts[1] = ("notes-app", "WhaleGenieNotes");
        var (_, _, store) = await RunAsync(
        [
            Step("window.exists", Param("title", "notes-app"), Param("matchBy", "process"),
                Param("resultVariable", "byProcess")),
            Step("window.exists", Param("title", "whalegenie"), Param("matchBy", "class"),
                Param("resultVariable", "byClass")),
            Step("window.exists", Param("title", "notes"), Param("matchBy", "title"),
                Param("resultVariable", "byTitle")),
        ], devices);

        Assert.True(store.Local.Values["byProcess"].Flag);
        Assert.True(store.Local.Values["byClass"].Flag);
        Assert.True(store.Local.Values["byTitle"].Flag);
    }

    [Fact]
    public async Task A_window_that_matches_by_nothing_is_not_found()
    {
        var devices = WithAWindow();
        devices.WindowFacts[1] = ("notepad", "Notepad");
        var (_, _, store) = await RunAsync(
        [
            Step("window.exists", Param("title", "explorer"), Param("matchBy", "process"),
                Param("resultVariable", "found")),
        ], devices);

        Assert.False(store.Local.Values["found"].Flag);
    }

    [Fact]
    public async Task Window_info_reports_where_a_window_sits_and_how_big_it_is()
    {
        var devices = WithAWindow();
        var (result, _, store) = await RunAsync(
            [Step("window.info", Param("title", "Notepad"), Param("resultVariable", "box"))], devices);

        Assert.True(result.Succeeded);

        // The rectangle is written the way a search region is spelled, so it can be used as one.
        Assert.Equal("1000,500,800,600", store.Local.Values["box"].AsText());
        Assert.Equal(1000, store.Local.Values["box.x"].AsNumber());
        Assert.Equal(500, store.Local.Values["box.y"].AsNumber());
        Assert.Equal(800, store.Local.Values["box.width"].AsNumber());
        Assert.Equal(600, store.Local.Values["box.height"].AsNumber());
        Assert.Equal("Notepad - notes.txt", store.Local.Values["box.title"].AsText());
    }

    [Fact]
    public async Task Window_info_fails_when_the_window_is_not_open()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
            [Step("window.info", Param("title", "ghost"), Param("resultVariable", "box"))], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.WindowNotFound", result.Key);
    }

    [Fact]
    public async Task Capturing_a_window_copies_its_whole_rectangle()
    {
        var devices = WithAWindow();
        var (result, _, store) = await RunAsync(
            [Step("vision.captureWindow", Param("title", "Notepad"), Param("saveTo", "shot"))], devices);

        Assert.True(result.Succeeded);
        Assert.Contains("capture 1000 500 800 600", devices.Calls);
        Assert.Equal(1000, store.Local.Values["shot.x"].AsNumber());
        Assert.Equal(500, store.Local.Values["shot.y"].AsNumber());
        Assert.Equal(800, store.Local.Values["shot.width"].AsNumber());
        Assert.Equal(600, store.Local.Values["shot.height"].AsNumber());
    }

    [Fact]
    public async Task Capturing_a_window_that_is_not_open_fails()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
            [Step("vision.captureWindow", Param("title", "ghost"), Param("saveTo", "shot"))], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.WindowNotFound", result.Key);
    }

    [Fact]
    public async Task Waiting_for_a_window_gives_up_after_the_timeout()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
            [Step("window.waitFor", Param("title", "ghost"), Param("timeoutMs", "0"))], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.WindowTimeout", result.Key);
    }

    [Fact]
    public async Task Activating_a_window_uses_its_handle()
    {
        var devices = new FakeDeviceLayer();
        devices.Windows.Add(new WindowInfo(21, "Notepad", new ScreenPoint(), new ScreenSize(), false, false));
        var (result, _, _) = await RunAsync(
            [Step("window.activate", Param("title", "note"))], devices);

        Assert.True(result.Succeeded);
        Assert.Contains("activateWindow 21", devices.Calls);
    }

    [Fact]
    public async Task Maximizing_a_window_uses_its_handle()
    {
        var devices = new FakeDeviceLayer();
        devices.Windows.Add(new WindowInfo(21, "Notepad", new ScreenPoint(), new ScreenSize(), false, false));
        await RunAsync([Step("window.maximize", Param("title", "Notepad"))], devices);

        Assert.Contains("maximizeWindow 21", devices.Calls);
    }

    [Fact]
    public async Task Minimizing_a_window_uses_its_handle()
    {
        var devices = new FakeDeviceLayer();
        devices.Windows.Add(new WindowInfo(21, "Notepad", new ScreenPoint(), new ScreenSize(), false, false));
        await RunAsync([Step("window.minimize", Param("title", "Notepad"))], devices);

        Assert.Contains("minimizeWindow 21", devices.Calls);
    }

    [Fact]
    public async Task Moving_a_window_passes_the_place_and_the_size()
    {
        var devices = new FakeDeviceLayer();
        devices.Windows.Add(new WindowInfo(21, "Notepad", new ScreenPoint(), new ScreenSize(), false, false));
        await RunAsync(
        [
            Step("window.move", Param("title", "Notepad"), Param("x", "100"), Param("y", "50"),
                Param("width", "640"), Param("height", "480")),
        ], devices);

        Assert.Contains("moveWindow 21 100 50 640 480", devices.Calls);
    }

    [Fact]
    public async Task Closing_a_window_uses_its_handle()
    {
        var devices = new FakeDeviceLayer();
        devices.Windows.Add(new WindowInfo(21, "Notepad", new ScreenPoint(), new ScreenSize(), false, false));
        await RunAsync([Step("window.close", Param("title", "Notepad"))], devices);

        Assert.Contains("closeWindow 21", devices.Calls);
    }

    [Fact]
    public async Task A_window_that_is_not_open_stops_the_step()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
            [Step("window.close", Param("title", "ghost"))], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.WindowNotFound", result.Key);
        Assert.Equal("ghost", result.Detail);
    }

    [Fact]
    public async Task A_window_the_system_says_no_to_stops_the_step()
    {
        var devices = new FakeDeviceLayer { WindowActionWorks = false };
        devices.Windows.Add(new WindowInfo(5, "Locked", new ScreenPoint(), new ScreenSize(), false, false));
        var (result, _, _) = await RunAsync(
            [Step("window.restore", Param("title", "Locked"))], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.WindowFailed", result.Key);
        Assert.Equal("Locked", result.Detail);
    }

    [Fact]
    public async Task Listing_windows_names_them_without_repeats()
    {
        var devices = new FakeDeviceLayer();
        devices.Windows.Add(new WindowInfo(1, "Notepad", new ScreenPoint(), new ScreenSize(), false, false));
        devices.Windows.Add(new WindowInfo(2, "Notepad", new ScreenPoint(), new ScreenSize(), false, false));
        devices.Windows.Add(new WindowInfo(3, "Calculator", new ScreenPoint(), new ScreenSize(), false, false));
        var (_, _, store) = await RunAsync(
            [Step("window.list", Param("resultVariable", "titles"))], devices);

        var titles = store.Local.Values["titles"];
        Assert.Equal(["Notepad", "Calculator"], titles.Items.Select(item => item.AsText()));
    }

    [Fact]
    public async Task Listing_windows_can_be_narrowed_down()
    {
        var devices = new FakeDeviceLayer();
        devices.Windows.Add(new WindowInfo(1, "Notepad", new ScreenPoint(), new ScreenSize(), false, false));
        devices.Windows.Add(new WindowInfo(2, "Calculator", new ScreenPoint(), new ScreenSize(), false, false));
        devices.Windows.Add(new WindowInfo(3, "Notepad - notes.txt",
            new ScreenPoint(), new ScreenSize(), false, false));
        devices.WindowFacts[2] = ("calc-app", "CalcFrame");

        var (_, _, store) = await RunAsync(
        [
            Step("window.list", Param("filter", "notepad"), Param("filterBy", "title"),
                Param("resultVariable", "byTitle")),
            Step("window.list", Param("filter", "calc"), Param("filterBy", "process"),
                Param("resultVariable", "byProcess")),
            Step("window.list", Param("filter", "CalcFrame"), Param("filterBy", "class"),
                Param("resultVariable", "byClass")),
        ], devices);

        Assert.Equal(["Notepad", "Notepad - notes.txt"],
            store.Local.Values["byTitle"].Items.Select(item => item.AsText()));
        Assert.Equal(["Calculator"], store.Local.Values["byProcess"].Items.Select(item => item.AsText()));
        Assert.Equal(["Calculator"], store.Local.Values["byClass"].Items.Select(item => item.AsText()));
    }

    [Fact]
    public async Task A_found_image_reports_its_parts_by_name()
    {
        var devices = new FakeDeviceLayer
        {
            Match = new ImageMatch(0.98, new ScreenPoint(3, 4), new ScreenSize(10, 10)),
        };

        var (result, _, store) = await RunAsync(
        [
            Step("vision.findImage", Param("image", "a.png"), Param("confidence", "90"),
                Param("region", ""), Param("resultVariable", "where")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Equal("8,9", store.Local.Values["where"].AsText());
        Assert.Equal(8, store.Local.Values["where.x"].AsNumber());
        Assert.Equal(9, store.Local.Values["where.y"].AsNumber());
        Assert.Equal(10, store.Local.Values["where.width"].AsNumber());
        Assert.Equal(10, store.Local.Values["where.height"].AsNumber());
        Assert.Equal(0.98, store.Local.Values["where.score"].AsNumber(), 3);
    }

    [Fact]
    public async Task An_image_that_is_not_there_clears_the_parts()
    {
        var devices = new FakeDeviceLayer { Match = null, Loaded = new ImageFrame(2, 2, new byte[16]) };

        var (result, _, store) = await RunAsync(
        [
            Step("control.setVariable", Param("name", "where.x"), Param("value", "999")),
            Step("vision.findImage", Param("image", "a.png"), Param("confidence", "90"),
                Param("region", ""), Param("resultVariable", "where")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Equal(string.Empty, store.Local.Values["where"].AsText());
        Assert.Equal(string.Empty, store.Local.Values["where.x"].AsText());
    }

    [Fact]
    public async Task A_captured_image_is_found_through_its_variable()
    {
        var devices = new FakeDeviceLayer
        {
            Match = new ImageMatch(0.95, new ScreenPoint(0, 0), new ScreenSize(10, 10)),
        };

        var (result, _, _) = await RunAsync(
        [
            Step("vision.capture", Param("x", "0"), Param("y", "0"), Param("width", "10"),
                Param("height", "10"), Param("saveTo", "shot")),
            Step("vision.findImage", Param("image", "$shot"), Param("confidence", "90"),
                Param("region", ""), Param("resultVariable", "where")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Contains("findAll 10x10 90", devices.Calls);
    }

    [Fact]
    public async Task Text_keeps_its_commas_instead_of_becoming_a_number()
    {
        var (result, devices, _) = await RunAsync(
            [Step("input.typeText", Param("text", "1,000"), Param("intervalMs", "0"))]);

        Assert.True(result.Succeeded, result.Key);
        Assert.Contains("type 1,000 0", devices.Calls);
    }

    [Fact]
    public async Task A_search_region_can_come_from_a_variable()
    {
        var devices = new FakeDeviceLayer
        {
            Loaded = new ImageFrame(2, 2, new byte[16]),
            Match = new ImageMatch(0.99, new ScreenPoint(0, 0), new ScreenSize(2, 2)),
        };

        var (result, _, _) = await RunAsync(
        [
            Step("control.setVariable", Param("name", "box"), Param("value", "10,20,30,40")),
            Step("vision.findImage", Param("image", "a.png"), Param("confidence", "90"),
                Param("region", "$box"), Param("resultVariable", "where")),
        ], devices);

        Assert.True(result.Succeeded, result.Key);
        Assert.Contains("capture 10 20 30 40", devices.Calls);
    }

    [Fact]
    public async Task A_found_text_reports_its_parts_by_name()
    {
        var devices = new FakeDeviceLayer
        {
            Spans = [new TextSpan("Save", new ScreenPoint(10, 20), new ScreenSize(40, 12), 0.9)],
        };

        var (result, _, store) = await RunAsync(
        [
            Step("ocr.findText", Param("text", "Save"), Param("region", ""),
                Param("matchMode", "contains"), Param("resultVariable", "found")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Equal("30,26", store.Local.Values["found"].AsText());
        Assert.Equal("Save", store.Local.Values["found.text"].AsText());
        Assert.Equal(30, store.Local.Values["found.x"].AsNumber());
        Assert.Equal(0.9, store.Local.Values["found.score"].AsNumber(), 3);
    }

    [Fact]
    public async Task A_number_may_be_written_as_an_expression()
    {
        var (result, devices, store) = await RunAsync(
        [
            Step("control.setVariable", Param("name", "spot"), Param("value", "5")),
            Step("input.mouseMove", Param("x", "$spot + 1"), Param("y", "$spot * 2"),
                Param("durationMs", "0")),
        ]);

        Assert.True(result.Succeeded);
        Assert.Contains("move 6 10 0", devices.Calls);
        Assert.Equal(5, store.Local.Values["spot"].AsNumber());
    }

    [Fact]
    public async Task A_step_moves_to_the_match_a_find_reported()
    {
        var devices = new FakeDeviceLayer
        {
            Match = new ImageMatch(0.97, new ScreenPoint(100, 200), new ScreenSize(20, 20)),
        };

        var (result, _, _) = await RunAsync(
        [
            Step("vision.findImage", Param("image", "a.png"), Param("confidence", "90"),
                Param("region", ""), Param("resultVariable", "where")),
            Step("input.mouseMove", Param("x", "$where.x + 3"), Param("y", "$where.y"),
                Param("durationMs", "0")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Contains("move 113 210 0", devices.Calls);
    }

    // ------------------------------------------------------------- wait until

    [Fact]
    public async Task A_wait_until_holds_on_the_first_look_and_keeps_how_long_it_waited()
    {
        var devices = new FakeDeviceLayer { Pixel = new PixelColor(0x10, 0x20, 0x30) };

        var (result, _, store) = await RunAsync(
        [
            Step("control.waitUntil",
                When("condition", Step("condition.colorEquals",
                    Param("x", "0"), Param("y", "0"), Param("color", "#102030"),
                    Param("tolerance", "5"))),
                Param("timeoutMs", "2000"), Param("pollMs", "10"),
                Param("onTimeout", "stop"), Param("elapsedVariable", "waited")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.True(store.Local.Values["waited"].AsNumber() >= 0);

        // A wait that already holds costs nothing: the colour was looked at once, not once a poll.
        Assert.Single(devices.Calls, call => call == "pixel 0 0");
    }

    [Fact]
    public async Task A_wait_until_asks_the_question_again_until_it_holds()
    {
        var devices = new FakeDeviceLayer
        {
            Match = new ImageMatch(0.99, new ScreenPoint(3, 4), new ScreenSize(6, 6)),
            MatchAfter = 3,
        };

        var (result, _, _) = await RunAsync(
        [
            Step("control.waitUntil",
                When("condition", Step("condition.imageExists",
                    Param("image", "a.png"), Param("confidence", "90"), Param("region", ""))),
                Param("timeoutMs", "2000"), Param("pollMs", "10"), Param("onTimeout", "stop")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.True(devices.Searches >= 3, $"the picture should be looked for again, looked {devices.Searches} times");
    }

    [Fact]
    public async Task A_wait_until_that_never_holds_stops_the_macro()
    {
        var devices = new FakeDeviceLayer { Pixel = new PixelColor(0xFF, 0xFF, 0xFF) };

        var (result, _, store) = await RunAsync(
        [
            Step("control.waitUntil",
                When("condition", Step("condition.colorEquals",
                    Param("x", "0"), Param("y", "0"), Param("color", "#102030"),
                    Param("tolerance", "5"))),
                Param("timeoutMs", "80"), Param("pollMs", "10"),
                Param("onTimeout", "stop"), Param("elapsedVariable", "waited")),
        ], devices);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal("Run.WaitTimeout", result.Key);
        Assert.True(store.Local.Values["waited"].AsNumber() >= 80,
            "how long it waited is worth keeping even when it gave up");
    }

    [Fact]
    public async Task A_wait_until_can_be_told_to_carry_on_when_the_time_runs_out()
    {
        var devices = new FakeDeviceLayer { Pixel = new PixelColor(0xFF, 0xFF, 0xFF) };
        var host = new SilentRunHost();
        var store = new VariableStore();

        var result = await new MacroRunner(store, host, devices).RunAsync(
        [
            Step("control.waitUntil",
                When("condition", Step("condition.colorEquals",
                    Param("x", "0"), Param("y", "0"), Param("color", "#102030"),
                    Param("tolerance", "5"))),
                Param("timeoutMs", "80"), Param("pollMs", "10"), Param("onTimeout", "continue")),
            Step("control.setVariable", Param("name", "after"), Param("scope", "local"),
                Param("value", "1")),
        ]);

        Assert.True(result.Succeeded);
        Assert.Contains(host.Entries, entry => entry.Key == "Run.WaitGaveUp");
        Assert.Equal(1, store.Local.Values["after"].AsNumber());
    }

    [Fact]
    public async Task A_wait_until_says_which_condition_it_waited_for()
    {
        var devices = new FakeDeviceLayer { Pixel = new PixelColor(0x10, 0x20, 0x30) };
        var host = new SilentRunHost();

        await new MacroRunner(new VariableStore(), host, devices).RunAsync(
        [
            Step("control.waitUntil",
                When("condition", Step("condition.colorEquals",
                    Param("x", "0"), Param("y", "0"), Param("color", "#102030"),
                    Param("tolerance", "5"))),
                Param("timeoutMs", "2000"), Param("pollMs", "10")),
        ]);

        var line = Assert.Single(host.Entries, entry => entry.Key == "Run.WaitedFor");
        Assert.Equal("condition.colorEquals", line.Arguments[1]);
    }

    [Fact]
    public async Task A_wait_until_with_nothing_to_wait_for_is_refused()
    {
        var (result, _, _) = await RunAsync([Step("control.waitUntil", Param("timeoutMs", "10"))]);

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
            Step("control.waitUntil",
                When("condition", Step("condition.randomChance", Param("percent", "100"))),
                Param("timeoutMs", "200"), Param("pollMs", "10"), Param("onTimeout", "stop"),
                Param("elapsedVariable", "certainly")),
        ]);

        Assert.True(certain.Succeeded);
        Assert.True(certainStore.Local.Values["certainly"].AsNumber() < 50,
            "a certainty holds the first time it is asked, so there is nothing to wait for");

        var (never, _, store) = await RunAsync(
        [
            Step("control.waitUntil",
                When("condition", Step("condition.randomChance", Param("percent", "0"))),
                Param("timeoutMs", "80"), Param("pollMs", "10"), Param("onTimeout", "continue"),
                Param("elapsedVariable", "never")),
        ]);

        Assert.True(never.Succeeded);
        Assert.True(store.Local.Values["never"].AsNumber() >= 80,
            "a chance of none never comes up, so the whole time has to be waited out");
    }

    // ------------------------------------------------------------ mouse routes

    [Fact]
    public async Task A_straight_move_is_the_move_it_has_always_been()
    {
        var (result, devices, _) = await RunAsync(
            [Step("input.mouseMove", Param("x", "400"), Param("y", "0"), Param("durationMs", "300"))]);

        Assert.True(result.Succeeded);
        Assert.Equal(["move 400 0 300"], devices.Calls);
        Assert.Empty(devices.Paths);
    }

    [Fact]
    public async Task A_bent_move_walks_a_path_instead_of_jumping()
    {
        var devices = new FakeDeviceLayer { Cursor = new ScreenPoint(0, 0) };

        var (result, _, _) = await RunAsync(
        [
            Step("input.mouseMove", Param("x", "400"), Param("y", "0"),
                Param("durationMs", "300"), Param("style", "smooth")),
        ], devices);

        Assert.True(result.Succeeded);
        var path = Assert.Single(devices.Paths);
        Assert.Equal(new ScreenPoint(0, 0), path[0]);
        Assert.Equal(new ScreenPoint(400, 0), path[^1]);
        Assert.True(path.Any(point => Math.Abs(point.Y) >= 20), "the path should bow out of the line");
        Assert.DoesNotContain(devices.Calls, call => call.StartsWith("move ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_bent_move_with_no_duration_borrows_one_so_that_it_can_bend()
    {
        var devices = new FakeDeviceLayer { Cursor = new ScreenPoint(0, 0) };

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
        var devices = new FakeDeviceLayer { Cursor = new ScreenPoint(20, 30) };

        var (result, _, _) = await RunAsync(
        [
            Step("input.mouseMoveRelative", Param("dx", "200"), Param("dy", "0"),
                Param("durationMs", "300"), Param("style", "smooth")),
        ], devices);

        Assert.True(result.Succeeded);
        var path = Assert.Single(devices.Paths);
        Assert.Equal(new ScreenPoint(20, 30), path[0]);
        Assert.Equal(new ScreenPoint(220, 30), path[^1]);
    }

    [Fact]
    public async Task A_straight_drag_is_still_one_call_with_its_steps()
    {
        var (result, devices, _) = await RunAsync(
        [
            Step("input.mouseDrag", Param("startX", "0"), Param("startY", "0"), Param("endX", "300"),
                Param("endY", "0"), Param("durationMs", "300"), Param("steps", "30")),
        ]);

        Assert.True(result.Succeeded);
        Assert.Equal(["drag left 0 0 300 0 300 30"], devices.Calls);
    }

    [Fact]
    public async Task A_hand_like_drag_is_the_same_path_with_the_button_held()
    {
        var (result, devices, _) = await RunAsync(
        [
            Step("input.mouseDrag", Param("startX", "0"), Param("startY", "0"), Param("endX", "300"),
                Param("endY", "0"), Param("durationMs", "300"), Param("steps", "30"),
                Param("style", "human")),
        ]);

        Assert.True(result.Succeeded);
        var path = Assert.Single(devices.Paths);
        Assert.Equal(31, path.Count);
        Assert.Equal(new ScreenPoint(0, 0), path[0]);
        Assert.Equal(new ScreenPoint(300, 0), path[^1]);
        Assert.Contains("dragAlong left 31 300", devices.Calls);
    }

    [Fact]
    public async Task A_browser_is_opened_driven_and_closed()
    {
        var devices = new FakeDeviceLayer { BrowserPage = "Hello from the page" };
        var (result, _, store) = await RunAsync(
        [
            Step("browser.open", Param("browser", "edge"), Param("url", "https://example.com"),
                Param("headless", "true")),
            Step("browser.goTo", Param("url", "https://example.com/next")),
            Step("browser.click", Param("target", "#go")),
            Step("browser.fill", Param("target", "#q"), Param("text", "macro")),
            Step("browser.readText", Param("target", "#result"), Param("resultVariable", "answer")),
            Step("browser.close"),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Equal("Hello from the page", store.Local.Values["answer"].AsText());
        Assert.Contains("browserOpen edge https://example.com True", devices.Calls);
        Assert.Contains("browserGoTo https://example.com/next", devices.Calls);
        Assert.Contains("browserClick #go", devices.Calls);
        Assert.Contains("browserFill #q macro", devices.Calls);
        Assert.Contains("browserText #result", devices.Calls);
        Assert.Contains("browserClose", devices.Calls);
        Assert.False(devices.BrowserOpen);
    }

    [Fact]
    public async Task A_browser_that_is_not_installed_says_what_to_install()
    {
        var devices = new FakeDeviceLayer { BrowserReady = false };
        var (result, _, _) = await RunAsync(
            [Step("browser.open", Param("url", "https://example.com"))], devices);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal("Run.BrowserMissing", result.Key);
        Assert.DoesNotContain("browserOpen", devices.Calls);
    }

    [Fact]
    public async Task A_selector_that_was_picked_off_a_page_is_used_as_written()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
        [
            Step("browser.click", Param("target", "[data-testid=\"row-2\"]")),
            Step("browser.click", Param("target", "div > p:nth-of-type(2)")),
            Step("browser.click", Param("target", "input[name=\"user\"]")),
            Step("browser.click",
                Param("target", "#frame >> internal:control=enter-frame >> #inner")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Contains("browserClick [data-testid=\"row-2\"]", devices.Calls);
        Assert.Contains("browserClick div > p:nth-of-type(2)", devices.Calls);
        Assert.Contains("browserClick input[name=\"user\"]", devices.Calls);
        Assert.Contains("browserClick #frame >> internal:control=enter-frame >> #inner", devices.Calls);
    }

    [Fact]
    public async Task A_macro_follows_a_click_into_the_tab_it_opened()
    {
        // Sites hand the page the macro came for to a new tab, and left on the old one every step
        // after the click aims at the wrong page: on a site whose pages look alike, a step finds an
        // element that happens to match over there and works on it without anything looking wrong.
        var devices = new FakeDeviceLayer { BrowserTabAddress = "https://example.com/receipt" };
        var (result, _, _) = await RunAsync(
        [
            Step("browser.open", Param("url", "https://example.com")),
            Step("browser.click", Param("target", "#pay")),
            Step("browser.switchTab", Param("how", "newest"), Param("index", "1")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Contains("browserSwitchTab Newest 1 ", devices.Calls);
    }

    [Fact]
    public async Task A_tab_can_be_asked_for_by_number_by_title_or_by_address()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
        [
            Step("browser.switchTab", Param("how", "index"), Param("index", "3")),
            Step("browser.switchTab", Param("how", "title"), Param("match", "收据")),
            Step("browser.switchTab", Param("how", "url"), Param("match", "example.com/receipt")),
            // A word the engine does not know reads as "the newest one": the words come from the
            // action catalogue, so an unknown one is either a file written by hand or a value this
            // engine is older than, and the newest tab is the one a click leaves behind.
            Step("browser.switchTab", Param("how", "sideways"), Param("index", "1")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Contains("browserSwitchTab Index 3 ", devices.Calls);
        Assert.Contains("browserSwitchTab Title 0 收据", devices.Calls);
        Assert.Contains("browserSwitchTab Address 0 example.com/receipt", devices.Calls);
        Assert.Contains("browserSwitchTab Newest 1 ", devices.Calls);
    }

    [Fact]
    public async Task A_tab_the_macro_is_done_with_can_be_closed()
    {
        // Closing the tab a click opened is what a macro does with it afterwards, and the browser
        // itself stays open: the pages the macro still wants are in the tabs next to it.
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
        [
            Step("browser.open", Param("url", "https://example.com")),
            Step("browser.closeTab"),
            Step("browser.close"),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Contains("browserCloseTab", devices.Calls);
        Assert.Contains("browserClose", devices.Calls);
        Assert.Equal(2, devices.Calls.Count(call => call is "browserCloseTab" or "browserClose"));
    }

    // -------------------------------------------------------------- virtual controller

    [Fact]
    public async Task The_steps_that_drive_a_controller_reach_the_device()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
        [
            Step("gamepad.connect"),
            Step("gamepad.button", Param("button", "a"), Param("mode", "down")),
            Step("gamepad.stick", Param("stick", "left"), Param("x", "60"), Param("y", "0")),
            Step("gamepad.trigger", Param("trigger", "right"), Param("amount", "80")),
            Step("gamepad.button", Param("button", "a"), Param("mode", "up")),
            Step("gamepad.release"),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Contains("gamepadConnect", devices.Calls);
        Assert.Contains("gamepadButton a True", devices.Calls);
        Assert.Contains("gamepadStick left 60 0", devices.Calls);
        Assert.Contains("gamepadTrigger right 80", devices.Calls);
        Assert.Contains("gamepadButton a False", devices.Calls);
        Assert.Contains("gamepadRelease", devices.Calls);
    }

    [Fact]
    public async Task A_tapped_button_is_pressed_and_let_go_of()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
        [
            Step("gamepad.button", Param("button", "b"), Param("mode", "tap")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Equal("gamepadButton b True", devices.Calls[0]);
        Assert.Equal("gamepadButton b False", devices.Calls[1]);
    }

    [Fact]
    public async Task A_run_lets_go_of_the_controller_it_was_driving()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
        [
            Step("gamepad.button", Param("button", "a"), Param("mode", "down")),
        ], devices);

        Assert.True(result.Succeeded);

        // The release comes after the step that pressed it, so a game is not left walking into
        // the wall the macro drove it towards.
        Assert.Equal("gamepadRelease", devices.Calls[^1]);
    }

    [Fact]
    public async Task A_run_that_never_touched_a_controller_does_not_release_one()
    {
        var devices = new FakeDeviceLayer();
        var (_, _, _) = await RunAsync([Step("input.keyPress", Param("key", "A"))], devices);

        Assert.DoesNotContain("gamepadRelease", devices.Calls);
    }

    // ---------------------------------------------------------------- trying one step

    [Fact]
    public async Task A_step_can_be_tried_on_its_own()
    {
        var devices = new FakeDeviceLayer();
        var outcome = await MacroRunner.TryAsync(
            Step("input.keyPress", Param("key", "F5"), Param("holdMs", "30")), devices);

        Assert.Equal(RunStatus.Completed, outcome.Status);
        Assert.Contains("keyPress F5 30", devices.Calls);
    }

    /// <summary>
    /// What is tried is the step as written, so the settings it carries have to reach the devices:
    /// testing a driver-level step through the front window would answer the wrong question.
    /// </summary>
    [Fact]
    public async Task A_tried_step_goes_the_way_its_own_settings_say()
    {
        var devices = new FakeDeviceLayer();
        await MacroRunner.TryAsync(
            Step("input.keyPress", Param("key", "A"), Param("inputMode", "driver")), devices);

        Assert.Equal(InputDelivery.Driver, Assert.Single(devices.Routes).Delivery);
    }

    [Fact]
    public async Task A_tried_step_that_cannot_be_done_says_why()
    {
        var devices = new FakeDeviceLayer();

        // Posting to a window needs the step to name one, and this one does not.
        var outcome = await MacroRunner.TryAsync(
            Step("input.keyPress", Param("key", "A"), Param("inputMode", "background")), devices);

        Assert.Equal(RunStatus.Failed, outcome.Status);
        Assert.Equal("Run.MissingTargetWindow", outcome.Key);
        Assert.Empty(devices.Calls);
    }

    [Fact]
    public async Task A_tried_controller_button_is_let_go_of_again()
    {
        var devices = new FakeDeviceLayer();
        var outcome = await MacroRunner.TryAsync(
            Step("gamepad.button", Param("button", "a"), Param("mode", "down")), devices);

        Assert.Equal(RunStatus.Completed, outcome.Status);
        Assert.Contains("gamepadButton a True", devices.Calls);

        // Nothing else is coming along to let go of it, and a game left with a button held down
        // is a game the user cannot get out of.
        Assert.Equal("gamepadRelease", devices.Calls[^1]);
    }

    [Fact]
    public async Task A_tried_key_held_down_is_let_go_of_again()
    {
        var devices = new FakeDeviceLayer();
        await MacroRunner.TryAsync(Step("input.keyDown", Param("key", "Shift")), devices);

        Assert.Equal("keyDown Shift", devices.Calls[0]);
        Assert.Equal("keyUp Shift", devices.Calls[^1]);
    }
}

/// <summary>
/// A device layer that writes down what it was asked to do instead of doing it, so the
/// engine can be checked without touching the real keyboard, mouse or screen.
/// </summary>
internal sealed class FakeDeviceLayer
    : IDeviceLayer, IInputDevice, IGamepadDevice, IScreenDevice, IVisionDevice, IOcrDevice,
      IUiDevice, IFileDevice, IClipboardDevice, IProcessDevice, ISystemDevice, IWindowDevice,
      IBrowserDevice
{
    public List<string> Calls { get; } = [];

    /// <summary>The routes the engine asked input to take, in the order it asked.</summary>
    public List<InputRoute> Routes { get; } = [];

    private readonly RecordingRouter _router;

    public FakeDeviceLayer() => _router = new RecordingRouter(this);

    IInputRouter IDeviceLayer.Inputs => _router;

    /// <summary>The paths the pointer was asked to travel, in the order they were asked for.</summary>
    public List<IReadOnlyList<ScreenPoint>> Paths { get; } = [];

    public ScreenPoint Cursor { get; set; } = new(7, 9);

    public ScreenSize PrimarySize { get; set; } = new(100, 50);

    public PixelColor Pixel { get; set; }

    public ImageMatch? Match { get; set; }

    /// <summary>
    /// The places the fake reports the reference picture at. Normally the one <see cref="Match"/>
    /// sets up; a check that wants several fills this in instead.
    /// </summary>
    public List<ImageMatch> Matches { get; } = [];

    public ImageFrame? Loaded { get; set; } = new(2, 2, new byte[16]);

    /// <summary>How many searches happen before a match starts being returned.</summary>
    public int MatchAfter { get; set; } = 1;

    /// <summary>How many times the reference picture was looked for.</summary>
    public int Searches { get; private set; }

    /// <summary>When set, every call is refused this way instead of being recorded.</summary>
    public System.Exception? Refusal { get; set; }

    /// <summary>When set, every call reports the device as missing.</summary>
    public bool Unavailable { get; set; }

    /// <summary>What text recognition should report.</summary>
    public List<TextSpan> Spans { get; set; } = [];

    /// <summary>Whether UI Automation should say the element is there.</summary>
    public bool ElementExists { get; set; } = true;

    /// <summary>What a UI Automation search turns up, in the order it reports them.</summary>
    public List<UiElementInfo> Elements { get; } = [];

    /// <summary>What reading an element gives back.</summary>
    public string? ElementText { get; set; }

    /// <summary>Whether writing into an element is allowed.</summary>
    public bool ElementWritable { get; set; } = true;

    /// <summary>Whether the window can be brought to the front.</summary>
    public bool WindowFocused { get; set; } = true;

    /// <summary>The pretend folder relative paths resolve under.</summary>
    public string BaseFolder { get; set; } = "G:\\fake";

    /// <summary>The full path a relative one stands for: the fake reads them from its base folder.</summary>
    public string Resolve(string path)
        => Path.IsPathRooted(path) ? path : Path.Combine(BaseFolder, path);

    /// <summary>The pretend files on disk, keyed by full path, for reading and copying.</summary>
    public Dictionary<string, string> Files { get; } = [];

    /// <summary>The pretend files of raw bytes on disk, keyed by the path they were written to.</summary>
    public Dictionary<string, byte[]> Blobs { get; } = [];

    /// <summary>When set, every copy reports the target already there and refuses to overwrite.</summary>
    public bool TargetExists { get; set; }

    /// <summary>The folders the fake has been told about, by name.</summary>
    public List<string> Folders { get; } = [];

    /// <summary>True when a folder should refuse to go away on its own, like a real non-empty one.</summary>
    public bool FolderHasFiles { get; set; }

    /// <summary>What listing a folder gives back.</summary>
    public List<string> FolderEntries { get; } = [];

    /// <summary>What <see cref="Exists"/> reports.</summary>
    public bool PathExists { get; set; }

    /// <summary>What is on the pretend clipboard.</summary>
    public string ClipboardText { get; set; } = string.Empty;

    /// <summary>Goes up whenever the pretend clipboard changes.</summary>
    public int ClipboardChanges { get; private set; }

    /// <summary>
    /// How many times the change counter may be read before it starts moving by itself, which
    /// stands in for another program copying something while a step waits.
    /// </summary>
    public int ClipboardMovesAfter { get; set; } = int.MaxValue;

    private int _clipboardReads;

    /// <summary>The running programs the fake reports, by name.</summary>
    public Dictionary<string, List<int>> Running { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Process ids that have finished.</summary>
    public HashSet<int> Exited { get; } = [];

    /// <summary>What a finished process returned.</summary>
    public Dictionary<int, int> ExitCodes { get; } = [];

    /// <summary>A program name that only shows up once the process list has been read a few times.</summary>
    public string? AppearsLater { get; set; }

    /// <summary>How many reads happen before <see cref="AppearsLater"/> shows up.</summary>
    public int AppearsAfter { get; set; } = 1;

    /// <summary>The next process id a start hands out.</summary>
    public int NextProcessId { get; set; } = 1000;

    /// <summary>What a command line prints.</summary>
    public CommandResult Command { get; set; } = new(0, string.Empty, string.Empty);

    /// <summary>What reading a machine fact answers.</summary>
    public Dictionary<string, string> SystemValues { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>What reading an environment variable answers.</summary>
    public Dictionary<string, string> EnvironmentValues { get; } = new(StringComparer.OrdinalIgnoreCase);

    private int _processReads;

    /// <summary>The windows the fake reports as open.</summary>
    public List<WindowInfo> Windows { get; } = [];

    /// <summary>
    /// The process and class names the fake windows answer to. A real device looks these up from
    /// the machine, so a test that matches that way has to say what should come back.
    /// </summary>
    public Dictionary<long, (string Process, string ClassName)> WindowFacts { get; } = [];

    /// <summary>Whether the next window operation reports that it worked.</summary>
    public bool WindowActionWorks { get; set; } = true;

    /// <summary>A window title that only shows up once the window list has been read a few times.</summary>
    public string? WindowAppearsLater { get; set; }

    /// <summary>How many reads happen before <see cref="WindowAppearsLater"/> shows up.</summary>
    public int WindowAppearsAfter { get; set; } = 1;

    private int _windowReads;

    IInputDevice IDeviceLayer.Input => this;

    IGamepadDevice IDeviceLayer.Gamepad => this;

    IScreenDevice IDeviceLayer.Screen => this;

    IVisionDevice IDeviceLayer.Vision => this;

    IOcrDevice IDeviceLayer.Ocr => this;

    IUiDevice IDeviceLayer.Ui => this;

    IFileDevice IDeviceLayer.Files => this;

    IClipboardDevice IDeviceLayer.Clipboard => this;

    IProcessDevice IDeviceLayer.Processes => this;

    ISystemDevice IDeviceLayer.System => this;

    IWindowDevice IDeviceLayer.Windows => this;

    IBrowserDevice IDeviceLayer.Browser => this;

    public bool BrowserReady { get; set; } = true;

    public string BrowserPage { get; set; } = "page text";

    bool IBrowserDevice.Ready(string browser) => BrowserReady;

    string IBrowserDevice.InstallHint => "install the browser";

    bool IBrowserDevice.IsOpen => BrowserOpen;

    public bool BrowserOpen { get; private set; }

    string IBrowserDevice.Url => BrowserAddress;

    public string BrowserAddress { get; private set; } = string.Empty;

    void IBrowserDevice.Open(string browser, string url, bool headless)
    {
        Note($"browserOpen {browser} {url} {headless}");
        BrowserOpen = true;
        BrowserAddress = url;
    }

    void IBrowserDevice.GoTo(string url)
    {
        Note($"browserGoTo {url}");
        BrowserAddress = url;
    }

    void IBrowserDevice.Click(string selector) => Note($"browserClick {selector}");

    void IBrowserDevice.Fill(string selector, string text) => Note($"browserFill {selector} {text}");

    /// <summary>The address the tab a switch moves to is showing, as though it had a page of its own.</summary>
    public string BrowserTabAddress { get; set; } = string.Empty;

    void IBrowserDevice.SwitchTab(TabChoice choice, int index, string match)
    {
        Note($"browserSwitchTab {choice} {index} {match}");
        if (BrowserTabAddress.Length > 0)
        {
            BrowserAddress = BrowserTabAddress;
        }
    }

    void IBrowserDevice.CloseTab() => Note("browserCloseTab");

    string IBrowserDevice.Text(string selector)
    {
        Note($"browserText {selector}");
        return BrowserPage;
    }

    /// <summary>What the next pick answers with, as though a person had clicked that element.</summary>
    public string BrowserPicked { get; set; } = "#picked";

    string IBrowserDevice.Pick(string hint, int timeoutMs)
    {
        Note($"browserPick {hint} {timeoutMs}");
        return BrowserPicked;
    }

    void IBrowserDevice.Close()
    {
        Note("browserClose");
        BrowserOpen = false;
    }

    public void KeyPress(string key, int holdMs) => Note($"keyPress {key} {holdMs}");

    public void Connect() => Note("gamepadConnect");

    public void Button(string button, bool down) => Note($"gamepadButton {button} {down}");

    public void Stick(string stick, int x, int y) => Note($"gamepadStick {stick} {x} {y}");

    public void Trigger(string trigger, int amount) => Note($"gamepadTrigger {trigger} {amount}");

    public void ReleaseAll() => Note("gamepadRelease");

    public void KeyDown(string key) => Note($"keyDown {key}");

    public void KeyUp(string key) => Note($"keyUp {key}");

    public void Hotkey(IReadOnlyList<string> keys, int holdMs) => Note($"hotkey {string.Join('|', keys)} {holdMs}");

    public void TypeText(string text, int intervalMs) => Note($"type {text} {intervalMs}");

    public void MoveMouse(int x, int y, int durationMs) => Note($"move {x} {y} {durationMs}");

    public void MoveMouseAlong(IReadOnlyList<ScreenPoint> path, int durationMs)
    {
        Paths.Add(path);
        Note($"along {path.Count} {durationMs}");
    }

    public void MoveMouseRelative(int dx, int dy, int durationMs) => Note($"moveBy {dx} {dy} {durationMs}");

    public void MouseDown(string button, int x, int y) => Note($"down {button} {x} {y}");

    public void MouseUp(string button, int x, int y) => Note($"up {button} {x} {y}");

    public void Click(string button, int x, int y, int clicks, int intervalMs)
        => Note($"click {button} {x} {y} {clicks} {intervalMs}");

    public void Scroll(string direction, int delta, int x, int y) => Note($"scroll {direction} {delta} {x} {y}");

    public void Drag(string button, int startX, int startY, int endX, int endY, int durationMs, int steps)
        => Note($"drag {button} {startX} {startY} {endX} {endY} {durationMs} {steps}");

    public void DragAlong(string button, IReadOnlyList<ScreenPoint> path, int durationMs)
    {
        Paths.Add(path);
        Note($"dragAlong {button} {path.Count} {durationMs}");
    }

    public PixelColor PixelAt(int x, int y)
    {
        Note($"pixel {x} {y}");
        return Display is { } display ? display[x, y] : Pixel;
    }

    public ImageFrame Capture(int x, int y, int width, int height)
    {
        Note($"capture {x} {y} {width} {height}");
        var picture = Display is { } display
            ? ScreenCut(display, x, y, width, height)
            : new ImageFrame(width, height, new byte[width * height * 4]);

        // A screen that moves between two captures is what waiting for the picture to settle is
        // about, so a check writes what the screen does next.
        if (Next is { } next)
        {
            Display = next();
        }

        return picture;
    }

    /// <summary>What the pretend screen shows: a capture cuts the rectangle asked for out of it.</summary>
    public ImageFrame? Display { get; set; }

    /// <summary>
    /// What the pretend screen shows next, asked after every capture, for the checks that need a
    /// picture that moves: one that moves twice and then holds, or one that never holds at all.
    /// Nothing here leaves the screen as it was.
    /// </summary>
    public Func<ImageFrame>? Next { get; set; }

    public ImageFrame? Load(string path)
    {
        Note($"load {path}");
        return Loaded;
    }

    public ImageMatch? Find(ImageFrame haystack, ImageFrame needle, double confidencePercent)
    {
        Searches++;
        Note($"find {needle.Width}x{needle.Height} {confidencePercent:0}");
        return Searches >= MatchAfter ? Match : null;
    }

    public IReadOnlyList<ImageMatch> FindAll(ImageFrame haystack, ImageFrame needle,
        double confidencePercent, int limit)
    {
        Searches++;
        Note($"findAll {needle.Width}x{needle.Height} {confidencePercent:0}");

        IEnumerable<ImageMatch> hits = Matches.Count > 0 ? Matches : Match is null ? [] : [Match];
        return Searches >= MatchAfter ? [.. hits.Take(limit)] : [];
    }

    public IReadOnlyList<TextSpan> Recognize(ImageFrame frame, string language)
    {
        Note($"ocr {language}");
        OcrFrames.Add((frame.Width, frame.Height));
        return Spans;
    }

    /// <summary>The sizes of the pictures the OCR was handed, so a test can see what was read.</summary>
    public List<(int Width, int Height)> OcrFrames { get; } = [];

    public bool Exists(UiQuery query, int timeoutMs)
    {
        Note($"ui {query.ControlType}/{query.Name}/{query.AutomationId}");
        return ElementExists;
    }

    public bool Click(UiQuery query, string button)
    {
        Note($"clickElement {query.Name} #{query.Index} {button}");
        return ElementExists;
    }

    public bool FocusWindow(string title)
    {
        Note($"focus {title}");
        return WindowFocused;
    }

    public string? GetText(UiQuery query)
    {
        Note($"read {query.Name}");
        return ElementText;
    }

    public bool SetText(UiQuery query, string text, bool clearFirst)
    {
        Note($"fill {query.ControlType}/{query.AutomationId} {text} {clearFirst}");
        return ElementWritable;
    }

    public IReadOnlyList<UiElementInfo> FindAll(UiQuery query, int limit)
    {
        Note($"findElements {query.ControlType}/{query.Name}/{query.AutomationId} #{query.Index} take {limit}");
        return [.. Elements.Take(Math.Max(1, limit))];
    }

    /// <summary>What picking an entry of a drop-down or a list answers.</summary>
    public bool ItemSelectable { get; set; } = true;

    /// <summary>What turning a check box on, off or over answers.</summary>
    public bool Checkable { get; set; } = true;

    /// <summary>What opening or closing a node answers.</summary>
    public bool Expandable { get; set; } = true;

    /// <summary>What scrolling an element into view answers.</summary>
    public bool Scrollable { get; set; } = true;

    public bool Select(UiQuery query, string text, int itemIndex)
    {
        Note($"selectItem {query.ControlType}/{query.Name}/{query.AutomationId}#{query.Index} \"{text}\" {itemIndex}");
        return ItemSelectable;
    }

    public bool SetChecked(UiQuery query, bool? state)
    {
        Note($"setChecked {query.Name} {state?.ToString() ?? "flip"}");
        return Checkable;
    }

    public bool SetExpanded(UiQuery query, string action)
    {
        Note($"setExpanded {query.Name} {action}");
        return Expandable;
    }

    public bool ScrollIntoView(UiQuery query)
    {
        Note($"scrollIntoView {query.Name}");
        return Scrollable;
    }

    /// <summary>What reading a table answers: one array of cells per row.</summary>
    public List<string[]> Table { get; } = [];

    public UiTable ReadTable(UiQuery query, int limit)
    {
        Note($"readTable {query.Name}#{query.Index} take {limit}");
        return new UiTable([.. Columns],
            [.. Table.Take(Math.Max(1, limit)).Select(row => (IReadOnlyList<string>)row)]);
    }

    /// <summary>What reading a table answers about the names of its columns.</summary>
    public List<string> Columns { get; } = [];

    bool IFileDevice.Exists(string path)
    {
        Note($"fileExists {path}");
        return PathExists || Files.ContainsKey(path) || Blobs.ContainsKey(path);
    }

    string IFileDevice.ReadText(string path, string encoding)
    {
        Note($"readFile {path} {encoding}");
        return Files.TryGetValue(path, out var text) ? text : string.Empty;
    }

    byte[] IFileDevice.ReadBytes(string path)
    {
        Note($"readBytes {path}");
        return Blobs.TryGetValue(path, out var bytes) ? bytes : [];
    }

    void IFileDevice.WriteText(string path, string text, bool append, string encoding)
    {
        Note($"writeFile {path} {text} {append} {encoding}");
        if (append && Files.TryGetValue(path, out var old))
        {
            Files[path] = old + text;
        }
        else
        {
            Files[path] = text;
        }
    }

    void IFileDevice.WriteBytes(string path, byte[] bytes)
    {
        Note($"writeBytes {path} {bytes.Length}");
        Blobs[path] = bytes;
    }

    void IFileDevice.Delete(string path)
    {
        Note($"deleteFile {path}");
        Files.Remove(path);
    }

    void IFileDevice.Copy(string from, string to, bool overwrite)
    {
        Note($"copyFile {from} {to} {overwrite}");
        if (TargetExists && !overwrite)
        {
            throw new DeviceActionException("Run.FileExists", to);
        }

        if (Files.TryGetValue(from, out var text))
        {
            Files[to] = text;
        }
    }

    void IFileDevice.Move(string from, string to, bool overwrite)
    {
        Note($"moveFile {from} {to} {overwrite}");
        if (TargetExists && !overwrite)
        {
            throw new DeviceActionException("Run.FileExists", to);
        }

        if (Files.TryGetValue(from, out var text))
        {
            Files.Remove(from);
            Files[to] = text;
        }
    }

    IReadOnlyList<string> IFileDevice.List(string folder, string pattern, bool recurse)
    {
        Note($"listFiles {folder} {pattern} {recurse}");
        return FolderEntries;
    }

    void IFileDevice.CreateFolder(string path)
    {
        Note($"createFolder {path}");
        Folders.Add(path);
    }

    void IFileDevice.Unzip(string from, string folder, bool overwrite)
    {
        Note($"unzip {from} {folder} {overwrite}");
        if (UnzipWorks)
        {
            Files[Path.Combine(folder, "readme.txt")] = "unpacked";
        }
        else
        {
            throw new DeviceActionException("Run.FileFailed", $"{from}: not a zip file");
        }
    }

    /// <summary>Whether the fake's zip file unpacks, or turns out to be something else.</summary>
    public bool UnzipWorks { get; set; } = true;

    void IFileDevice.Zip(string folder, string to)
    {
        Note($"zip {folder} {to}");
        Files[to] = "zipped";
    }

    void IFileDevice.DeleteFolder(string path, bool recurse)
    {
        Note($"deleteFolder {path} {recurse}");

        // A folder with something still in it is what the real one refuses to remove on its own,
        // and the fake has to answer the same way for the check to mean anything.
        if (FolderHasFiles && !recurse)
        {
            throw new DeviceActionException("Run.FileFailed", $"{path}: the folder is not empty");
        }

        Folders.Remove(path);
    }

    int IClipboardDevice.ChangeCount
    {
        get
        {
            _clipboardReads++;
            if (_clipboardReads > ClipboardMovesAfter)
            {
                ClipboardChanges++;
            }

            return ClipboardChanges;
        }
    }

    bool IClipboardDevice.HasText => !string.IsNullOrEmpty(ClipboardText);

    string IClipboardDevice.ReadText()
    {
        Note("clipboardRead");
        return ClipboardText;
    }

    void IClipboardDevice.WriteText(string text)
    {
        Note($"clipboardWrite {text}");
        ClipboardText = text;
        ClipboardChanges++;
    }

    ImageFrame? IClipboardDevice.ReadImage()
    {
        Note("clipboardReadImage");
        return ClipboardCopy;
    }

    void IClipboardDevice.WriteImage(ImageFrame image)
    {
        Note($"clipboardWriteImage {image.Width}x{image.Height}");
        ClipboardCopy = image;
        ClipboardChanges++;
    }

    /// <summary>The picture the fake clipboard is holding, if any.</summary>
    public ImageFrame? ClipboardCopy { get; set; }

    IReadOnlyList<string> IClipboardDevice.ReadFiles()
    {
        Note("clipboardReadFiles");
        return [.. ClipboardFiles];
    }

    void IClipboardDevice.WriteFiles(IReadOnlyList<string> paths)
    {
        Note($"clipboardWriteFiles {string.Join("|", paths)}");
        ClipboardFiles.Clear();
        ClipboardFiles.AddRange(paths);
        ClipboardChanges++;
    }

    /// <summary>The file paths the fake clipboard is holding.</summary>
    public List<string> ClipboardFiles { get; } = [];

    void IClipboardDevice.Clear()
    {
        Note("clipboardClear");
        ClipboardText = string.Empty;
        ClipboardChanges++;
    }

    int IProcessDevice.Start(StartRequest request)
    {
        Note($"start {request.FileName}|{request.Arguments}|{request.WorkingDirectory}"
             + $"|{request.Hidden}{(request.RunAsAdmin ? "|admin" : string.Empty)}"
             + EnvironmentNote(request.Environment));
        return NextProcessId++;
    }

    /// <summary>The environment a call carried, spelled out so a test can read it back.</summary>
    private static string EnvironmentNote(IReadOnlyDictionary<string, string>? values)
        => values is null or { Count: 0 }
            ? string.Empty
            : "|env " + string.Join(";",
                values.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => $"{pair.Key}={pair.Value}"));

    /// <summary>What a call fed the program, with the line endings spelled out.</summary>
    private static string InputNote(string? text)
        => text is null ? string.Empty : "|in " + text.Replace("\r\n", "\n").Replace("\n", "\\n");

    IReadOnlyList<int> IProcessDevice.Find(string name)
    {
        Note($"find {name}");
        _processReads++;
        if (AppearsLater is not null && _processReads > AppearsAfter)
        {
            Running[AppearsLater] = [NextProcessId];
        }

        return Running.TryGetValue(name, out var ids) ? ids : [];
    }

    IReadOnlyList<string> IProcessDevice.List()
    {
        Note("listPrograms");
        return [.. Running.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)];
    }

    ProcessDetails? IProcessDevice.Details(string target)
    {
        Note($"processDetails {target}");
        return Running.TryGetValue(target, out var ids) && ids.Count > 0
            ? new ProcessDetails(ids[0], target, $@"C:\fake\{target}.exe", 12.5, 3.4)
            : null;
    }

    bool IProcessDevice.HasExited(int id)
    {
        Note($"hasExited {id}");
        return Exited.Contains(id);
    }

    int? IProcessDevice.ExitCode(int id)
    {
        Note($"exitCode {id}");
        return Exited.Contains(id) ? ExitCodes.TryGetValue(id, out var code) ? code : 0 : null;
    }

    int IProcessDevice.StopByName(string name, bool force)
    {
        Note($"stopName {name}|{force}");
        return Running.Remove(name) ? 1 : 0;
    }

    bool IProcessDevice.StopById(int id, bool force)
    {
        Note($"stopId {id}|{force}");
        Exited.Add(id);
        return true;
    }

    CommandResult IProcessDevice.Run(CommandRequest request)
    {
        Note($"run {request.FileName}|{request.Arguments}|{request.WorkingDirectory}"
             + $"|{request.TimeoutMs}{EnvironmentNote(request.Environment)}"
             + InputNote(request.StandardInput)
             + $"|out {request.OutputEncoding ?? "default"}");

        // A real device hands each line over while the program is still running; the fake has all
        // of them already and hands them over the same way, blank lines included.
        foreach (var line in Lines(Command.StandardOutput))
        {
            request.OnOutput?.Invoke(line);
        }

        foreach (var line in Lines(Command.StandardError))
        {
            request.OnError?.Invoke(line);
        }

        return Command;
    }

    /// <summary>The lines a stream holds, the way reading it a line at a time would find them.</summary>
    private static IEnumerable<string> Lines(string text)
        => text.Length == 0
            ? []
            : text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');

    string ISystemDevice.Info(string field)
    {
        Note($"info {field}");
        return SystemValues.TryGetValue(field, out var value) ? value : string.Empty;
    }

    string ISystemDevice.Environment(string name)
    {
        Note($"env {name}");
        return EnvironmentValues.TryGetValue(name, out var value) ? value : string.Empty;
    }

    void ISystemDevice.Power(PowerAction action, int graceSeconds)
    {
        Note($"power {action} {graceSeconds}");
    }

    /// <summary>What the speakers are set to in the fake, and whether they are switched off.</summary>
    public int SpeakerVolume { get; set; } = 50;

    public bool SoundOff { get; set; }

    int ISystemDevice.Volume()
    {
        Note("volume");
        return SpeakerVolume;
    }

    void ISystemDevice.SetVolume(int percent)
    {
        Note($"setVolume {percent}");
        SpeakerVolume = Math.Clamp(percent, 0, 100);
    }

    bool ISystemDevice.IsMuted()
    {
        Note("isMuted");
        return SoundOff;
    }

    void ISystemDevice.SetMuted(bool muted)
    {
        Note($"setMuted {muted}");
        SoundOff = muted;
    }

    void ISystemDevice.PlaySound(SoundKind kind) => Note($"sound {kind}");

    void ISystemDevice.Notify(string title, string text, NotificationKind kind)
        => Note($"notify {kind} [{title}] {text}");

    /// <summary>The layouts the fake says are installed, and which of them is being typed in.</summary>
    public List<string> Layouts { get; } = ["\u4e2d\u6587(\u7b80\u4f53) - \u5fae\u8f6f\u62fc\u97f3", "\u82f1\u8bed(\u7f8e\u56fd)"];

    public string Layout { get; set; } = "\u4e2d\u6587(\u7b80\u4f53) - \u5fae\u8f6f\u62fc\u97f3";

    string ISystemDevice.InputMethod()
    {
        Note("inputMethod");
        return Layout;
    }

    IReadOnlyList<string> ISystemDevice.InputMethods()
    {
        Note("inputMethods");
        return Layouts;
    }

    string? ISystemDevice.SwitchInputMethod(string layout)
    {
        Note($"switchInputMethod {layout}");
        var found = Layouts.FirstOrDefault(installed =>
            installed.Contains(layout, StringComparison.OrdinalIgnoreCase));
        if (found is null)
        {
            return null;
        }

        Layout = found;
        return found;
    }

    /// <summary>How bright the fake says the screens are.</summary>
    public int ScreenBrightness { get; set; } = 50;

    int ISystemDevice.Brightness()
    {
        Note("brightness");
        return ScreenBrightness;
    }

    void ISystemDevice.SetBrightness(int percent)
    {
        Note($"setBrightness {percent}");
        ScreenBrightness = Math.Clamp(percent, 0, 100);
    }

    IReadOnlyList<WindowInfo> IWindowDevice.List()
    {
        Note("listWindows");
        return [.. Windows];
    }

    WindowInfo? IWindowDevice.Find(string value, WindowMatch match)
    {
        Note($"findWindow {value}");
        _windowReads++;
        if (WindowAppearsLater is not null
            && _windowReads > WindowAppearsAfter
            && Windows.All(window => window.Title != WindowAppearsLater))
        {
            Windows.Add(new WindowInfo(99, WindowAppearsLater,
                new ScreenPoint(0, 0), new ScreenSize(100, 100), false, false));
        }

        var wanted = (value ?? string.Empty).Trim();
        if (wanted.Length == 0)
        {
            return Windows.Count > 0 ? Windows[0] : null;
        }

        return Windows.FirstOrDefault(window => Answers(window, wanted, match));
    }

    /// <summary>
    /// Whether a fake window answers to a value. A title is always there; the process name and the
    /// class name have to be handed in through <see cref="WindowFacts"/>, the way the real device
    /// has to look them up.
    /// </summary>
    private bool Answers(WindowInfo window, string wanted, WindowMatch match)
    {
        var text = match switch
        {
            WindowMatch.Process => Facts(window).Process,
            WindowMatch.ClassName => Facts(window).ClassName,
            _ => window.Title,
        };

        return text.Contains(wanted, StringComparison.OrdinalIgnoreCase);
    }

    private (string Process, string ClassName) Facts(WindowInfo window)
        => WindowFacts.TryGetValue(window.Handle, out var facts)
            ? facts
            : (string.Empty, string.Empty);

    string IWindowDevice.ProcessOf(long handle)
    {
        Note($"processOf {handle}");
        return Windows.FirstOrDefault(window => window.Handle == handle) is { } window
            ? Facts(window).Process
            : string.Empty;
    }

    string IWindowDevice.ClassOf(long handle)
    {
        Note($"classOf {handle}");
        return Windows.FirstOrDefault(window => window.Handle == handle) is { } window
            ? Facts(window).ClassName
            : string.Empty;
    }

    bool IWindowDevice.Activate(long handle)
    {
        Note($"activateWindow {handle}");
        return WindowActionWorks;
    }

    bool IWindowDevice.Minimize(long handle)
    {
        Note($"minimizeWindow {handle}");
        return WindowActionWorks;
    }

    bool IWindowDevice.Maximize(long handle)
    {
        Note($"maximizeWindow {handle}");
        return WindowActionWorks;
    }

    bool IWindowDevice.Restore(long handle)
    {
        Note($"restoreWindow {handle}");
        return WindowActionWorks;
    }

    bool IWindowDevice.Close(long handle)
    {
        Note($"closeWindow {handle}");
        return WindowActionWorks;
    }

    bool IWindowDevice.Move(long handle, int x, int y, int width, int height)
    {
        Note($"moveWindow {handle} {x} {y} {width} {height}");
        return WindowActionWorks;
    }

    /// <summary>
    /// Where each window's client area starts, by handle. A handle that was not set up answers
    /// the window's own corner, which is what a window with no border looks like.
    /// </summary>
    public Dictionary<long, ScreenPoint> ClientOrigins { get; } = [];

    ScreenPoint IWindowDevice.ClientOrigin(long handle)
    {
        Note($"clientOrigin {handle}");
        return ClientOrigins.TryGetValue(handle, out var origin)
            ? origin
            : Windows.FirstOrDefault(window => window.Handle == handle)?.Location ?? default;
    }

    /// <summary>
    /// Notes which route input was asked to take, then hands back the fake itself so the call
    /// still lands in <see cref="Calls"/> whatever way it was routed.
    /// </summary>
    private sealed class RecordingRouter(FakeDeviceLayer owner) : IInputRouter
    {
        public IInputDevice For(InputRoute route)
        {
            owner.Routes.Add(route);
            return owner;
        }
    }

    private void Note(string call)
    {
        if (Unavailable)
        {
            throw new DeviceUnavailableException("the test device");
        }

        if (Refusal is not null)
        {
            throw Refusal;
        }

        Calls.Add(call);
    }

    /// <summary>
    /// The rectangle of the pretend screen a step asked for, with anything running off the edge
    /// left black the way a real capture leaves it.
    /// </summary>
    private static ImageFrame ScreenCut(ImageFrame display, int x, int y, int width, int height)
    {
        var bytes = new byte[width * height * 4];
        for (var row = 0; row < height; row++)
        {
            for (var column = 0; column < width; column++)
            {
                var from = ((y + row) * display.Width + (x + column)) * 4;
                if (from < 0 || from + 4 > display.Bgra.Length)
                {
                    continue;
                }

                Array.Copy(display.Bgra, from, bytes, (row * width + column) * 4, 4);
            }
        }

        return new ImageFrame(width, height, bytes);
    }
}
