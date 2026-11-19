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

// Searching for a colour or a pattern of colours, and what it takes to count as found.
public partial class DeviceActionTests
{
    [Fact]
    public async Task A_colour_is_found_where_the_region_shows_it()
    {
        var devices = new FakeDeviceLayer();
        devices.Screen.Display = Picture("#000000,#FF0000", "#000000,#000000");

        var (result, _, store) = await RunAsync(
        [
            Step("vision.findColor", Param("color", "#FF0000"), Param("tolerance", "1"),
                Region((0, 0, 2, 2)), Hit("match", "spot")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Equal("1,0", Centre(store, "spot"));
        Assert.Equal(1, Field(store, "spot", "x").AsNumber());
    }

    /// <summary>
    /// A step that asks how much of the region shows the colour is told, which is how a bar is read
    /// rather than a place: a progress bar is not a pixel anywhere, it is how much of it is filled.
    /// </summary>
    [Fact]
    public async Task The_share_of_the_region_that_shows_the_colour_can_be_read()
    {
        var devices = new FakeDeviceLayer();
        devices.Screen.Display = Picture("#FF0000,#FF0000", "#000000,#000000");

        var (result, _, store) = await RunAsync(
        [
            Step("vision.findColor", Param("color", "#FF0000"), Param("tolerance", "1"),
                Region((0, 0, 2, 2)), Returns("coverage=share")),
        ], devices);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal(50, store.Local.Values["share"].AsNumber());
    }

    /// <summary>
    /// A search that was told a miss is its failure fails with the colour's own key, so a branch can
    /// be written on "the colour was not there" without a condition step to say so. The same search
    /// left alone succeeds and hands back found = false: one action, two readings, said where the
    /// step is written rather than guessed from what is missing.
    /// </summary>
    [Fact]
    public async Task A_miss_can_be_the_failure_the_step_asked_for()
    {
        var devices = new FakeDeviceLayer();
        devices.Screen.Display = Picture("#000000,#000000", "#000000,#000000");

        var (missed, _, _) = await RunAsync(
        [
            Step("vision.findColor", Param("color", "#FF0000"), Param("tolerance", "1"),
                Param("missingIsFailure", "true"), Region((0, 0, 2, 2))),
        ], devices);

        Assert.False(missed.Succeeded);
        Assert.Equal(RunKeys.ColourNotFound, missed.Key);

        var (tolerated, _, quiet) = await RunAsync(
        [
            Step("vision.findColor", Param("color", "#FF0000"), Param("tolerance", "1"),
                Region((0, 0, 2, 2)), Returns("found=seen")),
        ], devices);

        Assert.True(tolerated.Succeeded, tolerated.Key);
        Assert.False(quiet.Local.Values["seen"].AsBool());
    }

    /// <summary>
    /// Clicking a colour is the colour family's step that has to work: it waits for the colour the
    /// way clicking a picture waits, aims at the middle of what it found moved by the offset, and a
    /// colour that never turns up fails with the colour's own key rather than with "too slow" —
    /// the step knew what it was waiting for.
    /// </summary>
    [Fact]
    public async Task Clicking_a_colour_aims_at_it_and_names_the_colour_when_it_is_not_there()
    {
        var devices = new FakeDeviceLayer();
        devices.Screen.Display = Picture("#00FF00,#000000", "#000000,#000000");

        var (hit, _, _) = await RunAsync(
        [
            Step("vision.clickColor", Param("color", "#00FF00"), Param("tolerance", "5"),
                Param("offsetX", "3"), Param("offsetY", "4"), Region((0, 0, 2, 2))),
        ], devices);

        Assert.True(hit.Succeeded, hit.Key);
        Assert.Equal("click left 3 4 1 0", Assert.Single(devices.Calls,
            call => call.StartsWith("click ", StringComparison.Ordinal)));

        var empty = new FakeDeviceLayer();
        empty.Screen.Display = Picture("#000000,#000000", "#000000,#000000");

        var (missed, _, _) = await RunAsync(
        [
            Within(40, Step("vision.clickColor", Param("color", "#FF0000"), Param("tolerance", "1"),
                Region((0, 0, 2, 2)))),
        ], empty);

        Assert.False(missed.Succeeded);
        Assert.Equal(RunKeys.ColourNotFound, missed.Key);
        Assert.DoesNotContain(empty.Calls,
            call => call.StartsWith("click ", StringComparison.Ordinal));
    }

    /// <summary>
    /// A share the step will not act below is waited for and, when the region never shows that much
    /// of the colour, there is nothing to hand over — the same answer as the colour not being there
    /// at all, because half a bar is not a full one.
    /// </summary>
    [Fact]
    public async Task A_colour_short_of_the_share_asked_for_is_not_found()
    {
        var devices = new FakeDeviceLayer();
        devices.Screen.Display = Picture("#FF0000,#000000", "#000000,#000000");

        var (result, _, store) = await RunAsync(
        [
            Step("vision.findColor", Param("color", "#FF0000"), Param("tolerance", "1"),
                Param("minCoverage", "60"), Region((0, 0, 2, 2)),
                Returns("match=spot", "coverage=share")),
        ], devices);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal(string.Empty, store.Local.Values["spot"].AsText());
        Assert.Equal(25, store.Local.Values["share"].AsNumber());
    }

    /// <summary>
    /// A colour on its own is not enough to tell two things drawn alike apart, so a step can name
    /// the colours that have to be around it and be handed the place the pattern sits at.
    /// </summary>
    [Fact]
    public async Task A_pattern_is_found_where_its_colours_sit()
    {
        var devices = new FakeDeviceLayer();
        devices.Screen.Display = Picture("#000000,#000000,#000000",
                "#000000,#FF0000,#00FF00",
                "#000000,#0000FF,#000000");

        var (result, _, store) = await RunAsync(
        [
            Step("vision.findColors", Param("color", "#FF0000"),
                Points(("1", "0", "#00FF00"), ("0", "1", "#0000FF")), Param("tolerance", "1"),
                Region((0, 0, 3, 3)), Hit("match", "spot")),
        ], devices);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal("1,1", Centre(store, "spot"));
    }

    /// <summary>
    /// One colour of the pattern out of place is what tells the red thing this step wants from the
    /// red thing that merely looks like it.
    /// </summary>
    [Fact]
    public async Task A_pattern_with_one_colour_out_of_place_is_not_found()
    {
        var devices = new FakeDeviceLayer();
        devices.Screen.Display = Picture("#000000,#000000,#000000",
                "#000000,#FF0000,#123456",
                "#000000,#0000FF,#000000");

        var (result, _, store) = await RunAsync(
        [
            Step("vision.findColors", Param("color", "#FF0000"),
                Points(("1", "0", "#00FF00"), ("0", "1", "#0000FF")), Param("tolerance", "1"),
                Region((0, 0, 3, 3)), Hit("match", "spot")),
        ], devices);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal(string.Empty, store.Local.Values["spot"].AsText());
    }

    /// <summary>
    /// 图案里的偏移与颜色可以是算出来的：另一个步骤算出的数写进变量，这里直接引用，不必先把它
    /// 换算成一个数字再敲进来。写死的数仍然照旧。
    /// </summary>
    [Fact]
    public async Task A_pattern_can_name_its_offsets_and_colour_with_variables()
    {
        var devices = new FakeDeviceLayer();
        devices.Screen.Display = Picture("#000000,#000000,#000000",
                "#000000,#FF0000,#00FF00",
                "#000000,#0000FF,#000000");

        var (result, _, store) = await RunAsync(
        [
            Step("control.setVariable", Param("name", "dx"), Param("value", "0")),
            Step("control.setVariable", Param("name", "dy"), Param("value", "0")),
            Step("control.setVariable", Param("name", "side"), Param("value", "#00FF00")),
            Step("vision.findColors", Param("color", "#FF0000"),
                // dx + 1 是零加一，落在右边那一格：变量与算式都算数。
                Fields.Rows("points",
                Cells.Of(("dx", Formula.Binary("+", Formula.Name("dx"), Formula.Number(1))),
                    ("dy", Formula.Name("dy")), ("colour", Formula.Name("side"))),
                Cells.Of(("dx", "0"), ("dy", "1"), ("colour", "#0000FF"))), Param("tolerance", "1"),
                Region((0, 0, 3, 3)),
                Hit("match", "spot"), Returns("found=yes")),
        ], devices);

        Assert.True(result.Succeeded, result.Key);
        Assert.True(store.Local.Values["yes"].AsBool());
        Assert.Equal("1,1", Centre(store, "spot"));
    }
    /// <summary>
    /// 基准色对了、旁边一处不对，就不是这个图案：这正是多点找色要分的那件事。
    /// </summary>
    [Fact]
    public async Task A_pattern_is_false_when_a_colour_beside_the_base_is_off()
    {
        var devices = new FakeDeviceLayer();
        devices.Screen.Display = Picture("#000000,#000000,#000000",
                "#000000,#FF0000,#123456",
                "#000000,#0000FF,#000000");

        var (result, _, store) = await RunAsync(
        [
            Step("vision.findColors", Param("color", "#FF0000"),
                Points(("1", "0", "#00FF00"), ("0", "1", "#0000FF")), Param("tolerance", "1"),
                Region((0, 0, 3, 3)),
                Hit("match", "spot"), Returns("found=no")),
        ], devices);

        Assert.True(result.Succeeded, result.Key);
        Assert.False(store.Local.Values["no"].AsBool());
        Assert.Equal(string.Empty, store.Local.Values["spot"].AsText());
    }

    /// <summary>
    /// A row of the pattern that leaves a cell out is a mistake in the step rather than an answer
    /// about the screen: it is refused with the key that says the pattern cannot be read, and the
    /// message carries what that row did say so the reader can see which one it was.
    /// </summary>
    [Fact]
    public async Task A_pattern_row_that_leaves_a_cell_out_fails_as_bad_points()
    {
        var devices = new FakeDeviceLayer();
        devices.Screen.Display = Picture("#FF0000,#000000");

        var (result, _, _) = await RunAsync(
        [
            Step("vision.findColors", Param("color", "#FF0000"), Param("tolerance", "1"),
                Points(("1", "", "#00FF00")), Region((0, 0, 2, 1))),
        ], devices);

        Assert.False(result.Succeeded);
        Assert.Equal(RunKeys.BadPoints, result.Key);
    }

    /// <summary>
    /// 每一处可以有自己的容差。同一个邻居、同一个全局 0，只改这一处写的容差，它就从「不算命中」
    /// 变成「算命中」：锚点要严、周围那些颜色随光影偏，这是多点找色最常见的调法。
    /// </summary>
    [Fact]
    public async Task A_colour_beside_the_base_can_carry_a_tolerance_of_its_own()
    {
        var devices = new FakeDeviceLayer();
        devices.Screen.Display = Picture("#FF0000,#E60000");

        var (strict, _, tight) = await RunAsync(
        [
            Step("vision.findColors", Param("color", "#FF0000"), Param("tolerance", "0"),
                Points(("1", "0", "#FF0000")), Region((0, 0, 2, 1)), Returns("found=tight")),
        ], devices);

        Assert.True(strict.Succeeded, strict.Key);
        Assert.False(tight.Local.Values["tight"].AsBool());

        var (loose, _, wide) = await RunAsync(
        [
            Step("vision.findColors", Param("color", "#FF0000"), Param("tolerance", "0"),
                PointsWithTolerance(("1", "0", "#FF0000", "20")), Region((0, 0, 2, 1)),
                Returns("found=wide")),
        ], devices);

        Assert.True(loose.Succeeded, loose.Key);
        Assert.True(wide.Local.Values["wide"].AsBool());
    }

    /// <summary>
    /// 同一个位置可以给几个备选色：按钮按下前后是同一个点、同一个按钮，只是颜色换了。
    /// 命中其中任意一个就算这一处成立。
    /// </summary>
    [Fact]
    public async Task A_place_beside_the_base_may_accept_any_of_several_colours()
    {
        var devices = new FakeDeviceLayer();
        devices.Screen.Display = Picture("#FF0000,#0000FF");

        static ExecutableParameter Palette(string dx, string dy, string colours)
            => new()
            {
                Name = "points",
                Rows =
                [
                    Cells.Of(("dx", dx), ("dy", dy), ("colour", colours)),
                ],
            };

        var (either, _, first) = await RunAsync(
        [
            Step("vision.findColors", Param("color", "#FF0000"), Param("tolerance", "0"),
                Palette("1", "0", "#00FF00|#0000FF"), Region((0, 0, 2, 1)),
                Returns("found=either")),
        ], devices);

        Assert.True(either.Succeeded, either.Key);
        Assert.True(first.Local.Values["either"].AsBool());

        // 一个都没写进去，就还是找不到：备选色不是「随便什么都行」。
        var (neither, _, none) = await RunAsync(
        [
            Step("vision.findColors", Param("color", "#FF0000"), Param("tolerance", "0"),
                Palette("1", "0", "#00FF00|#00FFFF"), Region((0, 0, 2, 1)),
                Returns("found=none")),
        ], devices);

        Assert.True(neither.Succeeded, neither.Key);
        Assert.False(none.Local.Values["none"].AsBool());
    }

    /// <summary>
    /// 勾上「必须不是」的那一处，要的是「不是这些颜色」：屏幕上是红的就不成立，换成绿的才成立。
    /// </summary>
    [Fact]
    public async Task A_place_can_ask_to_be_none_of_its_colours()
    {
        static ExecutableParameter MustNot(string dx, string dy, string colour)
            => new()
            {
                Name = "points",
                Rows =
                [
                    Cells.Of(("dx", dx), ("dy", dy), ("colour", colour), ("mustNot", "true")),
                ],
            };

        var red = new FakeDeviceLayer();
        red.Screen.Display = Picture("#00FF00,#FF0000");
        var (wrong, _, wrongStore) = await RunAsync(
        [
            Step("vision.findColors", Param("color", "#00FF00"), Param("tolerance", "0"),
                MustNot("1", "0", "#FF0000"), Region((0, 0, 2, 1)), Returns("found=wrong")),
        ], red);

        Assert.True(wrong.Succeeded, wrong.Key);
        Assert.False(wrongStore.Local.Values["wrong"].AsBool());

        var green = new FakeDeviceLayer();
        green.Screen.Display = Picture("#00FF00,#0000FF");
        var (right, _, rightStore) = await RunAsync(
        [
            Step("vision.findColors", Param("color", "#00FF00"), Param("tolerance", "0"),
                MustNot("1", "0", "#FF0000"), Region((0, 0, 2, 1)), Returns("found=right")),
        ], green);

        Assert.True(right.Succeeded, right.Key);
        Assert.True(rightStore.Local.Values["right"].AsBool());
    }

    /// <summary>
    /// 四个方向决定「第一个」是哪一个：同一个图案，横着排就分左右，竖着排就分上下。
    /// 「第几个命中」数是按这个起点数的，所以换方向必须真的换到另一头去数。
    /// </summary>
    [Fact]
    public async Task The_order_of_the_hits_can_start_from_another_corner()
    {
        static ExecutableParameter OnePoint()
            => new()
            {
                Name = "points",
                Rows =
                [
                    new Dictionary<string, ExpressionNode>(StringComparer.OrdinalIgnoreCase)
        {
            ["dx"] = Cells.Line("1"), ["dy"] = Cells.Line("0"),
            ["colour"] = Cells.Line("#00FF00"),
        },
                ],
            };

        static async Task<string> Chosen(FakeDeviceLayer devices, string order)
        {
            var (result, _, store) = await RunAsync(
            [
                Step("vision.findColors", Param("color", "#FF0000"), Param("tolerance", "0"),
                    Param("orderBy", order), OnePoint(), Hit("match", "spot")),
            ], devices);
            Assert.True(result.Succeeded, result.Key);
            return Centre(store, "spot");
        }

        var across = new FakeDeviceLayer();
        across.Screen.Display = Picture("#FF0000,#00FF00,#FF0000,#00FF00");
        Assert.Equal("0,0", await Chosen(across, "reading"));
        Assert.Equal("2,0", await Chosen(across, "readingBack"));

        var down = new FakeDeviceLayer();
        down.Screen.Display = Picture("#FF0000,#00FF00", "#FF0000,#00FF00");
        Assert.Equal("0,0", await Chosen(down, "reading"));
        Assert.Equal("0,1", await Chosen(down, "readingUp"));
        Assert.Equal("0,1", await Chosen(down, "readingBackUp"));
    }

    /// <summary>
    /// A pattern with no colours in it would match anywhere, so it is not a search at all: an empty
    /// list is refused with a key of its own, the one that says to use the plain colour search
    /// rather than read the emptiness as "never mind the surroundings".
    /// </summary>
    [Fact]
    public async Task A_pattern_with_no_rows_in_it_is_refused()
    {
        var devices = new FakeDeviceLayer();
        devices.Screen.Display = Picture("#FF0000,#000000");

        var (result, _, _) = await RunAsync(
        [
            Step("vision.findColors", Param("color", "#FF0000"), Param("tolerance", "1"),
                Points(), Region((0, 0, 2, 1))),
        ], devices);

        Assert.False(result.Succeeded);
        Assert.Equal(RunKeys.NoColourPoints, result.Key);
    }

    [Fact]
    public async Task A_colour_that_is_not_there_leaves_the_result_empty()
    {
        var devices = new FakeDeviceLayer();
        devices.Screen.Display = Picture("#000000");

        var (result, _, store) = await RunAsync(
            [Step("vision.findColor", Param("color", "#FF0000"), Hit("match", "spot"))],
            devices);

        Assert.True(result.Succeeded);
        Assert.Equal(string.Empty, store.Local.Values["spot"].AsText());

        // Only the hits are counted when the step was told to record every one, and this step was
        // not, so there is no count to read at all.
        Assert.False(store.TryGet("spot.count", out _));
    }

    [Fact]
    public async Task Query_actions_report_false_without_failing()
    {
        var imageDevices = new FakeDeviceLayer();
        imageDevices.Ui.ElementExists = false;
        imageDevices.Vision.Loaded = new ImageFrame(2, 2, new byte[16]);
        imageDevices.Vision.Match = null;
        imageDevices.Ocr.Spans = [];

        var (result, _, store) = await RunAsync(
        [
            Step("vision.findImage", Pictures("a.png"), Returns("found=imageFound")),
            Step("vision.findColor", Param("color", "#FF0000"), Returns("found=colorFound")),
            Step("vision.findColors", Param("color", "#FF0000"),
                Points(("1", "0", "#00FF00")), Returns("found=colorsFound")),
            Step("vision.detect", Param("model", "model.onnx"), Returns("found=detectFound")),
            Step("ocr.findText", Param("queryText", "Ready"), Returns("found=textFound")),
            Step("uia.find", Param("uiSelector", "Button[name='Save']"), Returns("found=elementFound")),
        ], imageDevices);

        Assert.True(result.Succeeded, result.Key);
        foreach (var name in new[]
                 {
                     "imageFound", "colorFound", "colorsFound", "detectFound", "textFound",
                     "elementFound",
                 })
        {
            Assert.False(store.Local.Values[name].AsBool(), name);
        }
    }

    /// <summary>
    /// A colour is checked at one named pixel without searching a region, and the answer says what
    /// the pixel actually shows as well as whether it matches: a "no" that comes without the colour
    /// it saw leaves the reader guessing what to change the tolerance to.
    /// </summary>
    [Fact]
    public async Task A_colour_can_be_checked_at_one_pixel_without_searching_a_region()
    {
        var devices = new FakeDeviceLayer();
        devices.Screen.Pixel = new PixelColor(0x10, 0x20, 0x30);
        var (result, _, store) = await RunAsync(
        [
            Step("vision.matchColor", Param("x", "10"), Param("y", "20"),
                Param("color", "#102030"), Param("tolerance", "5"),
                Returns("matches=hit", "color=seen", "delta=gap")),
        ], devices);

        Assert.True(result.Succeeded, result.Key);
        Assert.True(store.Local.Values["hit"].AsBool());
        Assert.Equal("#102030", store.Local.Values["seen"].AsText());
        Assert.Equal(0, store.Local.Values["gap"].AsNumber());
        Assert.Contains("pixel 10 20", devices.Calls);
        Assert.Empty(devices.Screen.Captures);
    }

    [Fact]
    public async Task A_colour_search_at_a_point_is_false_when_that_pixel_does_not_match()
    {
        var devices = new FakeDeviceLayer();
        devices.Screen.Pixel = new PixelColor(0xFF, 0xFF, 0xFF);
        var (result, _, store) = await RunAsync(
        [
            Step("vision.findColor", Param("x", "10"), Param("y", "20"),
                Param("color", "#102030"), Param("tolerance", "5"),
                Returns("found=no")),
        ], devices);

        Assert.True(result.Succeeded, result.Key);
        Assert.False(store.Local.Values["no"].AsBool());
    }

    [Fact]
    public async Task Browser_exists_asks_once_and_reports_the_state()
    {
        var devices = new FakeDeviceLayer();
        devices.Browser.HasElement = true;
        var (result, _, store) = await RunAsync(
        [
            Step("browser.exists", Param("pageSelector", "#ready"),
                Param("elementState", "attached"), Param("matchIndex", "2"),
                Returns("found=there")),
        ], devices);

        Assert.True(result.Succeeded, result.Key);
        Assert.True(store.Local.Values["there"].AsBool());
        Assert.Contains("browserExists #ready #2 Attached", devices.Calls);
    }
}
