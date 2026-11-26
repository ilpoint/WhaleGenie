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

namespace WhaleGenie.Core.Tests;

// Keys and the ways input reaches a window: presses, chords, typing, and the background route.
public partial class DeviceActionTests
{
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
        Assert.Contains(host.Entries, entry => entry.Key == "Log.LetGo");
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
            Asked(devices));
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
            [Step("input.typeText", Param("typedText", "hello"), Param("intervalMs", "25"))]);

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

        Assert.Equal([InputRoute.Front], devices.Input.Routes);
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

        var route = Assert.Single(devices.Input.Routes);
        Assert.Equal(InputDelivery.Driver, route.Delivery);
        Assert.Equal(["keyPress F5 10"], devices.Calls);
    }

    [Fact]
    public async Task Background_input_is_posted_at_the_window_it_names()
    {
        var devices = new FakeDeviceLayer();
        devices.Window.Open.Add(new WindowInfo(4242, "Notepad - notes.txt",
            new ScreenPoint(0, 0), new ScreenSize(100, 100), false, false));

        var (result, _, _) = await RunAsync(
        [
            Step("input.keyPress",
                Param("key", "F5"), Param("holdMs", "10"),
                Param("inputMode", "background"), Param("window", "Notepad")),
        ], devices);

        Assert.True(result.Succeeded);

        var route = Assert.Single(devices.Input.Routes);
        Assert.Equal(InputDelivery.Background, route.Delivery);
        Assert.Equal(4242, route.WindowHandle);
    }

    /// <summary>
    /// The window background input is posted at is recognised the way a window action recognises
    /// one: by the program that owns it, or by the class it registered, rather than by a title that
    /// moves with the document or the level. That is what keeps a step typing into a game aimed at
    /// the game after the level it was written for is over.
    /// </summary>
    [Fact]
    public async Task Background_input_can_name_its_window_by_the_program()
    {
        var devices = new FakeDeviceLayer();
        devices.Window.Open.Add(new WindowInfo(4242, "笔记 - 第 3 关", new ScreenPoint(0, 0),
            new ScreenSize(100, 100), false, false));
        devices.Window.Traits[4242] = ("notepad", "Notepad");

        var (result, _, _) = await RunAsync(
        [
            Step("input.keyPress", Param("key", "F5"), Param("holdMs", "10"),
                Param("inputMode", "background"), Param("window", "notepad"),
                Param("how", "process")),
        ], devices);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal(4242, Assert.Single(devices.Input.Routes).WindowHandle);
    }

    /// <summary>
    /// A pattern the machine cannot read is the macro's problem to see, and it is seen before
    /// anything is looked at or typed: the step fails in the same words the window actions use
    /// rather than posting the keys at whichever window happened to match.
    /// </summary>
    [Fact]
    public async Task Background_input_with_a_pattern_it_cannot_read_stops_the_step()
    {
        var devices = new FakeDeviceLayer();
        devices.Window.Open.Add(new WindowInfo(4242, "Notepad - notes.txt",
            new ScreenPoint(0, 0), new ScreenSize(100, 100), false, false));

        var (result, _, _) = await RunAsync(
        [
            Step("input.keyPress", Param("key", "F5"), Param("inputMode", "background"),
                Param("window", "Notepad["), Param("how", "titleRegex")),
        ], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.BadPattern", result.Key);

        // The pattern was refused before the windows were even read, so nothing was posted at
        // whatever came back.
        Assert.Empty(Asked(devices));
    }

    [Fact]
    public async Task Background_input_without_a_window_fails_instead_of_typing_into_the_wrong_one()
    {
        var (result, devices, _) = await RunAsync(
            [Step("input.keyPress", Param("key", "F5"), Param("inputMode", "background"))]);

        Assert.False(result.Succeeded);
        Assert.Empty(Asked(devices));
    }

    /// <summary>
    /// 相对移动问的是指针现在在哪儿，后台那一档只把消息投给窗口、指针自己没动过：把偏移当成
    /// 绝对坐标发出去就成了"去 (200, 0) 这个点"，发错数还看不出来。所以这一档干脆说做不了，
    /// 一个鼠标消息也不发。
    /// </summary>
    [Fact]
    public async Task A_relative_move_has_no_background_mode()
    {
        var devices = new FakeDeviceLayer();
        devices.Window.Open.Add(new WindowInfo(4242, "Notepad - notes.txt",
            new ScreenPoint(0, 0), new ScreenSize(100, 100), false, false));

        var (result, _, _) = await RunAsync(
        [
            Step("input.mouseMoveRelative", Param("dx", "200"), Param("dy", "0"),
                Param("inputMode", "background"), Param("window", "Notepad")),
        ], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.BadOption", result.Key);
        Assert.Empty(Asked(devices));
    }

    /// <summary>
    /// 走弧线的相对移动也算相对移动：前台那一路是读回指针当前位置自己往前推，而后台这一档的
    /// 指针位置根本不是它自己的。两条路一起判，免得只剩其中一条还在发错的数。
    /// </summary>
    [Fact]
    public async Task A_bent_relative_move_has_no_background_mode_either()
    {
        var devices = new FakeDeviceLayer();
        devices.Window.Open.Add(new WindowInfo(4242, "Notepad - notes.txt",
            new ScreenPoint(0, 0), new ScreenSize(100, 100), false, false));

        var (result, _, _) = await RunAsync(
        [
            Step("input.mouseMoveRelative", Param("dx", "200"), Param("dy", "0"),
                Param("style", "smooth"), Param("durationMs", "100"),
                Param("inputMode", "background"), Param("window", "Notepad")),
        ], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.BadOption", result.Key);
        Assert.Empty(devices.Input.Paths);
        Assert.Empty(Asked(devices));
    }
}
