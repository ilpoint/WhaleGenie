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

namespace WhaleGenie.Core.Tests;

// Waiting for a picture or a condition to hold, and what a wait that never holds does.
public partial class DeviceActionTests
{
    [Fact]
    public async Task A_capture_can_be_searched_for_again()
    {
        var devices = new FakeDeviceLayer();
        devices.Vision.Match = new ImageMatch(0.98, new ScreenPoint(3, 4), new ScreenSize(10, 10));

        var (result, _, store) = await RunAsync(
        [
            Capture(0, 0, 10, 10, Parts("image", "shot")),
            Step("vision.findImage", Pictures("shot"), Param("confidence", "90"),
                Param("region", ""), Hit("match", "where")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Equal("<image 10x10>", store.Local.Values["shot"].AsText());
        Assert.Equal("8,9", Centre(store, "where"));
        Assert.Contains("capture 0 0 10 10", devices.Calls);
        Assert.Contains("findAll 10x10 90 Normed", devices.Calls);
    }

    [Fact]
    public async Task Waiting_for_an_image_keeps_looking_until_it_appears()
    {
        var devices = new FakeDeviceLayer();
        devices.Vision.Loaded = new ImageFrame(2, 2, new byte[16]);
        devices.Vision.Match = new ImageMatch(0.95, new ScreenPoint(1, 1), new ScreenSize(2, 2));
        devices.Vision.MatchAfter = 3;

        var (result, _, store) = await RunAsync(
        [
            Within(2000, Step("vision.waitImage", Pictures("anything.png"), Param("confidence", "90"),
                Hit("match", "spot"))),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Equal("2,2", Centre(store, "spot"));
        Assert.Equal(3, devices.Vision.Searches);
    }

    [Fact]
    public async Task Waiting_for_an_image_that_never_appears_fails()
    {
        var devices = new FakeDeviceLayer();
        devices.Vision.Loaded = new ImageFrame(2, 2, new byte[16]);
        devices.Vision.Match = null;

        var (result, _, _) = await RunAsync(
        [
            Within(20, Step("vision.waitImage", Pictures("anything.png"), Param("confidence", "90"),
                Hit("match", "spot"))),
        ], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.ImageNotFound", result.Key);
    }

    /// <summary>
    /// A step that looks again before clicking does not click a picture that was gone by the time
    /// of the second look: a button that flashed past is not worth pressing.
    /// </summary>
    [Fact]
    public async Task A_click_that_looks_again_does_not_click_what_has_gone()
    {
        var looked = 0;
        var devices = new FakeDeviceLayer();
        devices.Vision.PictureAnswers = (_, _) => ++looked == 1
            ? [new ImageMatch(0.99, new ScreenPoint(5, 6), new ScreenSize(2, 2))]
            : [];

        var (result, _, _) = await RunAsync(
        [
            Step("vision.clickImage", Pictures("ok.png"), Param("confidence", "90"),
                Param("confirmMs", "10")),
        ], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.ImageNotFound", result.Key);
        Assert.DoesNotContain(devices.Calls, call => call.StartsWith("click ", StringComparison.Ordinal));
    }

    /// <summary>
    /// A click that confirms what it found goes with where the second look put it, which is where
    /// the thing is by the time the click leaves.
    /// </summary>
    [Fact]
    public async Task A_click_that_looks_again_uses_where_the_second_look_found_it()
    {
        var looked = 0;
        var devices = new FakeDeviceLayer();
        devices.Vision.PictureAnswers = (_, _) => ++looked == 1
            ? [new ImageMatch(0.99, new ScreenPoint(5, 6), new ScreenSize(2, 2))]
            : [new ImageMatch(0.99, new ScreenPoint(40, 50), new ScreenSize(2, 2))];

        var (result, _, _) = await RunAsync(
        [
            Step("vision.clickImage", Pictures("ok.png"), Param("confidence", "90"),
                Param("confirmMs", "10")),
        ], devices);

        Assert.True(result.Succeeded, result.Key);
        var click = Assert.Single(devices.Calls,
            call => call.StartsWith("click ", StringComparison.Ordinal));
        Assert.Equal("click left 41 51 1 0", click);
    }

    /// <summary>
    /// 找图之后的点击不再自己取门。别人正占着门时它照点不误：要独占就把「找图并点击」整步放进
    /// 「独占输入」块，门由块来取。
    /// </summary>
    [Fact]
    public async Task A_click_that_found_its_place_does_not_wait_at_the_input_door()
    {
        var devices = new FakeDeviceLayer();
        devices.Vision.PictureAnswers = (_, _) =>
            [new ImageMatch(0.99, new ScreenPoint(5, 6), new ScreenSize(2, 2))];
        using var inside = await InputGate.Take("别人", default);

        var result = await new MacroRunner(new VariableStore(), new SilentRunHost(), devices)
        {
            RunName = "看图",
        }.RunAsync([Step("vision.clickImage", Pictures("ok.png"), Param("confidence", "90"))]);

        Assert.True(result.Succeeded, result.Key);

        // 点击发生时门还在别人手里：这一步既没有等，也没有把门拿走。
        Assert.Equal("别人", devices.Input.GateAtClick);
        Assert.Equal("别人", InputGate.Owner);
    }

    /// <summary>
    /// A step that clicks what it found hands the hit over the way the step that only looks for a
    /// picture does, so where the click landed can be read by a later step.
    /// </summary>
    [Fact]
    public async Task A_click_on_a_picture_hands_over_where_it_clicked()
    {
        var devices = new FakeDeviceLayer();
        devices.Vision.Match = new ImageMatch(0.99, new ScreenPoint(5, 6), new ScreenSize(2, 2));

        var (result, _, store) = await RunAsync(
        [
            Step("vision.clickImage", Pictures("ok.png"), Param("confidence", "90"),
                Returns("match=spot")),
        ], devices);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal("6,7", Centre(store, "spot"));
        Assert.Equal(0.99, Field(store, "spot", "score").AsNumber(), 3);
    }
}
