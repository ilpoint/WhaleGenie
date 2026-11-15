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

// Driving a game controller, and letting go of it when the run ends.
public partial class DeviceActionTests
{
    // -------------------------------------------------------------- virtual controller

    [Fact]
    public async Task The_steps_that_drive_a_controller_reach_the_device()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
        [
            Step("gamepad.connect"),
            Step("gamepad.button", Param("button", "a"), Param("buttonMode", "down")),
            Step("gamepad.stick", Param("stick", "left"), Param("x", "60"), Param("y", "0")),
            Step("gamepad.trigger", Param("trigger", "right"), Param("amount", "80")),
            Step("gamepad.button", Param("button", "a"), Param("buttonMode", "up")),
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
            Step("gamepad.button", Param("button", "b"), Param("buttonMode", "tap")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Equal("gamepadButton b True", devices.Calls[0]);
        Assert.Equal("gamepadButton b False", devices.Calls[1]);
    }

    [Fact]
    public async Task A_button_can_be_tapped_several_times_in_a_row()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
        [
            Step("gamepad.button", Param("button", "a"), Param("buttonMode", "tap"),
                Param("repeat", "3"), Param("intervalMs", "10")),
        ], devices);

        // Three taps are three presses and three releases, in that order, with nothing held down
        // in between: a repeat that stayed pressed would be one long press to the game.
        Assert.True(result.Succeeded);
        Assert.Equal(
            ["gamepadButton a True", "gamepadButton a False",
             "gamepadButton a True", "gamepadButton a False",
             "gamepadButton a True", "gamepadButton a False"],
            devices.Calls.Where(call => call.StartsWith("gamepadButton", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_run_lets_go_of_the_controller_it_was_driving()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
        [
            Step("gamepad.button", Param("button", "a"), Param("buttonMode", "down")),
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

        Assert.Equal(InputDelivery.Driver, Assert.Single(devices.Input.Routes).Delivery);
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
            Step("gamepad.button", Param("button", "a"), Param("buttonMode", "down")), devices);

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
