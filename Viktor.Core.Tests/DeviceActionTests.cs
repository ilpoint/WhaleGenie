using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Viktor.Core.Devices;
using Viktor.Core.Execution;
using Viktor.Core.Variables;

namespace Viktor.Core.Tests;

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
            "scroll up 3 5 6",
        ], devices.Calls);
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

        Assert.Equal(["findWindow Notepad", "clientOrigin 1", "down left 1013 537"], devices.Calls);
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
        Assert.Contains("readFile notes.txt", devices.Calls);
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

        Assert.Equal("a,\"b,c\"", devices.Files["out.csv"]);
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
        Assert.Contains(@"run cmd.exe|/c echo hello||5000", devices.Calls);
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
        // there yet is left to whoever started Viktor rather than failing the step.
        var devices = new FakeDeviceLayer();
        await RunAsync([Step("script.run", Param("script", "echo hi"))], devices);

        Assert.Contains(devices.Calls,
            call => call.StartsWith("run powershell.exe") && call.EndsWith("||60000"));
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
            call => call.StartsWith("run powershell.exe") && call.EndsWith("--quiet|C:\\work|5000"));
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
            call => call.StartsWith("writeFile") && call.EndsWith("Write-Host Ann False"));
        Assert.Contains(devices.Calls, call =>
            call.StartsWith(@"run powershell.exe|-NoProfile")
            && call.Contains("--to Ann --x {{nope}}"));
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
    public async Task A_script_for_an_interpreter_viktor_does_not_know_fails()
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
}

/// <summary>
/// A device layer that writes down what it was asked to do instead of doing it, so the
/// engine can be checked without touching the real keyboard, mouse or screen.
/// </summary>
internal sealed class FakeDeviceLayer
    : IDeviceLayer, IInputDevice, IScreenDevice, IVisionDevice, IOcrDevice, IUiDevice, IFileDevice,
      IClipboardDevice, IProcessDevice, ISystemDevice, IWindowDevice
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

    /// <summary>The pretend files on disk, keyed by full path, for reading and copying.</summary>
    public Dictionary<string, string> Files { get; } = [];

    /// <summary>When set, every copy reports the target already there and refuses to overwrite.</summary>
    public bool TargetExists { get; set; }

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

    /// <summary>Whether the next window operation reports that it worked.</summary>
    public bool WindowActionWorks { get; set; } = true;

    /// <summary>A window title that only shows up once the window list has been read a few times.</summary>
    public string? WindowAppearsLater { get; set; }

    /// <summary>How many reads happen before <see cref="WindowAppearsLater"/> shows up.</summary>
    public int WindowAppearsAfter { get; set; } = 1;

    private int _windowReads;

    IInputDevice IDeviceLayer.Input => this;

    IScreenDevice IDeviceLayer.Screen => this;

    IVisionDevice IDeviceLayer.Vision => this;

    IOcrDevice IDeviceLayer.Ocr => this;

    IUiDevice IDeviceLayer.Ui => this;

    IFileDevice IDeviceLayer.Files => this;

    IClipboardDevice IDeviceLayer.Clipboard => this;

    IProcessDevice IDeviceLayer.Processes => this;

    ISystemDevice IDeviceLayer.System => this;

    IWindowDevice IDeviceLayer.Windows => this;

    public void KeyPress(string key, int holdMs) => Note($"keyPress {key} {holdMs}");

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

    public void Scroll(string direction, int amount, int x, int y) => Note($"scroll {direction} {amount} {x} {y}");

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
        return Display is { } display
            ? ScreenCut(display, x, y, width, height)
            : new ImageFrame(width, height, new byte[width * height * 4]);
    }

    /// <summary>What the pretend screen shows: a capture cuts the rectangle asked for out of it.</summary>
    public ImageFrame? Display { get; set; }

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
        return Spans;
    }

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

    public IReadOnlyList<IReadOnlyList<string>> ReadTable(UiQuery query, int limit)
    {
        Note($"readTable {query.Name}#{query.Index} take {limit}");
        return [.. Table.Take(Math.Max(1, limit)).Select(row => (IReadOnlyList<string>)row)];
    }

    bool IFileDevice.Exists(string path)
    {
        Note($"fileExists {path}");
        return PathExists || Files.ContainsKey(path);
    }

    string IFileDevice.ReadText(string path)
    {
        Note($"readFile {path}");
        return Files.TryGetValue(path, out var text) ? text : string.Empty;
    }

    void IFileDevice.WriteText(string path, string text, bool append)
    {
        Note($"writeFile {path} {text} {append}");
        if (append && Files.TryGetValue(path, out var old))
        {
            Files[path] = old + text;
        }
        else
        {
            Files[path] = text;
        }
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

    IReadOnlyList<string> IFileDevice.List(string folder, string pattern, bool recurse)
    {
        Note($"listFiles {folder} {pattern} {recurse}");
        return FolderEntries;
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

    void IClipboardDevice.Clear()
    {
        Note("clipboardClear");
        ClipboardText = string.Empty;
        ClipboardChanges++;
    }

    int IProcessDevice.Start(string fileName, string arguments, string workingDirectory, bool hidden)
    {
        Note($"start {fileName}|{arguments}|{workingDirectory}|{hidden}");
        return NextProcessId++;
    }

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

    CommandResult IProcessDevice.Run(string fileName, string arguments, string workingDirectory, int timeoutMs)
    {
        Note($"run {fileName}|{arguments}|{workingDirectory}|{timeoutMs}");
        return Command;
    }

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

    IReadOnlyList<WindowInfo> IWindowDevice.List()
    {
        Note("listWindows");
        return [.. Windows];
    }

    WindowInfo? IWindowDevice.Find(string title)
    {
        Note($"findWindow {title}");
        _windowReads++;
        if (WindowAppearsLater is not null
            && _windowReads > WindowAppearsAfter
            && Windows.All(window => window.Title != WindowAppearsLater))
        {
            Windows.Add(new WindowInfo(99, WindowAppearsLater,
                new ScreenPoint(0, 0), new ScreenSize(100, 100), false, false));
        }

        var wanted = (title ?? string.Empty).Trim();
        if (wanted.Length == 0)
        {
            return Windows.Count > 0 ? Windows[0] : null;
        }

        return Windows.FirstOrDefault(
            window => window.Title.Contains(wanted, StringComparison.OrdinalIgnoreCase));
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
