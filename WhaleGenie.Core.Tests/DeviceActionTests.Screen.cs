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

// Reading the screen: pixels, captures, anchors, and the results a step hands over.
public partial class DeviceActionTests
{
    /// <summary>
    /// 看一个像素的两格是同一个读法：上一步找到的东西，这一步直接读它中心那一个像素的颜色。
    /// </summary>
    [Fact]
    public async Task A_pixel_can_be_read_where_something_was_just_found()
    {
        var devices = new FakeDeviceLayer();
        devices.Screen.Pixel = new PixelColor(0xAB, 0xCD, 0xEF);
        devices.Vision.Matches.Add(new ImageMatch(0.99, new ScreenPoint(30, 40), new ScreenSize(10, 20)));

        var (result, _, store) = await RunAsync(
        [
            Step("vision.findImage", Pictures("ok.png"), Hit("match", "spot")),
            Step("vision.getPixel", Fields.Reference("place", "spot"),
                Returns("color=tone")),
        ], devices);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal("pixel 35 50", devices.Calls[^1]);
        Assert.Equal("#ABCDEF", store.Local.Values["tone"].AsText());
    }

    /// <summary>
    /// 要截的那一块也收一条记录：找图留下的命中带着 left、top、width、height，填进 X 就是"把刚找到的
    /// 那一块截下来"，其余三格留空。以前要在四格里各写一遍 field()，四格错一格，截下来的就是旁边那一块。
    /// </summary>
    [Fact]
    public async Task A_picture_can_be_taken_of_what_was_just_found()
    {
        var devices = new FakeDeviceLayer();
        devices.Vision.Matches.Add(new ImageMatch(0.99, new ScreenPoint(30, 40), new ScreenSize(10, 20)));

        var (result, _, store) = await RunAsync(
        [
            Step("vision.findImage", Pictures("ok.png"), Hit("match", "spot")),
            Step("vision.capture", Fields.Reference("rect", "spot"),
                Returns("place=cut")),
        ], devices);

        Assert.True(result.Succeeded, result.Key);

        // 截的是命中那一块：左上角 (30,40)，10×20；留下的「位置」说的也是这一块。
        var asked = devices.Screen.Captures[^1];
        Assert.Equal((30, 40, 10, 20), (asked.X, asked.Y, asked.Width, asked.Height));
        Assert.Equal("30,40,10,20", Field(store, "cut", "box").AsText());
    }

    /// <summary>
    /// 读文字的那一块是同一个读法：上一步找到的东西，这一步直接读它上面的字。
    /// </summary>
    [Fact]
    public async Task Reading_can_be_aimed_at_what_was_just_found()
    {
        var devices = new FakeDeviceLayer();
        devices.Screen.Display = Picture("#FFFFFF,#FFFFFF");
        devices.Ocr.Spans = [new TextSpan("42", new ScreenPoint(0, 0), new ScreenSize(2, 1), 40)];
        devices.Vision.Matches.Add(new ImageMatch(0.99, new ScreenPoint(30, 40), new ScreenSize(10, 20)));

        var (result, _, store) = await RunAsync(
        [
            Step("vision.findImage", Pictures("ok.png"), Hit("match", "spot")),
            Step("ocr.recognize", RegionFrom("$spot"),
                Returns("text=words")),
        ], devices);

        Assert.True(result.Succeeded, result.Key);

        var asked = devices.Screen.Captures[^1];
        Assert.Equal((30, 40, 10, 20), (asked.X, asked.Y, asked.Width, asked.Height));
        Assert.Equal("42", store.Local.Values["words"].AsText());
    }

    /// <summary>
    /// 搜索范围写下几处时，每一处都读：同一段话分在两个框里，读出来的仍是一句话。
    /// </summary>
    [Fact]
    public async Task Reading_looks_at_every_place_the_search_region_names()
    {
        var devices = new FakeDeviceLayer();
        devices.Screen.Display = Picture("#FFFFFF,#FFFFFF");
        devices.Ocr.Spans = [new TextSpan("42", new ScreenPoint(0, 0), new ScreenSize(2, 1), 40)];

        var (result, _, store) = await RunAsync(
        [
            Step("ocr.recognize", Region((0, 0, 10, 10), (20, 20, 10, 10)),
                Returns("text=words")),
        ], devices);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal(2, devices.Screen.Captures.Count);
        Assert.Equal("42 42", store.Local.Values["words"].AsText());
    }

    /// <summary>
    /// 四格收一条记录时的读法同样与拖拽、鼠标那两处一致：记录说的是屏幕上那一块，「坐标基准」只挪写出来
    /// 的四个数。命中记录在窗口基准下交出来的 left、top 也是屏幕像素，再按窗口量一次就截到旁边去了。
    /// </summary>
    [Fact]
    public async Task A_rectangle_from_a_record_says_where_it_is_whatever_the_anchor()
    {
        var devices = WithAWindow();
        devices.Vision.Matches.Add(new ImageMatch(0.99, new ScreenPoint(30, 40), new ScreenSize(10, 20)));

        var (result, _, _) = await RunAsync(
        [
            Step("vision.findImage", Pictures("ok.png"), Hit("match", "spot")),
            Step("vision.capture", Fields.Reference("rect", "spot"),
                Param("anchorMode", "window"), Param("window", "Notepad"),
                Returns("image=cut")),
            Step("vision.capture", Param("x", "10"), Param("y", "20"), Param("width", "30"),
                Param("height", "40"),
                Param("anchorMode", "window"), Param("window", "Notepad"),
                Returns("image=cut")),
        ], devices);

        Assert.True(result.Succeeded, result.Key);

        // 前几次是找图那一步自己的事；第一条截图截的是记录自己那一块 (30,40,10,20)，第二条是写出来的
        // 四个数，照旧从窗口角 (1000,500) 量起。
        Assert.Equal(3, devices.Screen.Captures.Count);
        Assert.Equal((30, 40, 10, 20), (devices.Screen.Captures[1].X, devices.Screen.Captures[1].Y,
            devices.Screen.Captures[1].Width, devices.Screen.Captures[1].Height));
        Assert.Equal((1010, 520, 30, 40), (devices.Screen.Captures[2].X, devices.Screen.Captures[2].Y,
            devices.Screen.Captures[2].Width, devices.Screen.Captures[2].Height));
    }


    /// <summary>
    /// 一条里面没有矩形的记录（例如 CSV 的一行）当"哪一块"用时报 Run.BadRegion，并把它读到的东西写出来：
    /// 静默地按 (0,0) 读下去，读的就是屏幕左上角那一块，谁也不知道为什么。
    /// </summary>
    [Fact]
    public async Task A_record_with_no_rectangle_in_it_is_refused_where_a_rectangle_is_read()
    {
        var devices = new FakeDeviceLayer();
        devices.Files.Store["in.csv"] = "name,age\nAda,36\n";

        var (result, _, _) = await RunAsync(
        [
            Step("file.readCsv", Param("path", "in.csv"), Returns("rows=table")),
            Step("ocr.recognize", RegionFrom("get($table, 0)"),
                Returns("text=words")),
        ], devices);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal("Run.BadRegion", result.Key);
        Assert.Empty(devices.Screen.Captures);
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
            Param("anchorMode", "window"), Param("window", "Notepad"));

        await RunAsync([step], devices);
        devices.Window.Open[0] = devices.Window.Open[0] with { Location = new ScreenPoint(300, 400) };
        await RunAsync([step], devices);

        Assert.Equal(
            [
                "findWindow Notepad", "clientOrigin 1", "move 1010 520 0",
                "findWindow Notepad", "clientOrigin 1", "move 310 420 0",
            ],
            devices.Calls);
    }

    [Fact]
    public async Task Anchoring_to_a_window_that_is_not_open_fails_the_step()
    {
        var (result, devices, _) = await RunAsync(
            [Step("input.mouseMove", Param("x", "1"), Param("y", "2"),
                Param("anchorMode", "window"), Param("window", "Gone"))]);

        Assert.False(result.Succeeded);

        // The window was looked for and nothing else was touched.
        Assert.Equal(["findWindow Gone"], Asked(devices));
    }

    [Fact]
    public async Task A_pixel_can_be_read_from_a_window_corner()
    {
        var devices = WithAWindow();
        devices.Window.ClientOrigins[1] = new ScreenPoint(1000, 520);

        var (_, _, _) = await RunAsync(
        [
            Step("vision.getPixel", Param("x", "7"), Param("y", "8"),
                Param("anchorMode", "window"), Param("window", "Notepad"),
                Returns("color=tone")),
        ], devices);

        // A pixel counted from a window is read out of a picture of that window, so a window that
        // another window covers still gives its own colour rather than the covering one's.
        Assert.Equal(["findWindow Notepad", "clientOrigin 1", "capture 1007 528 1 1"], devices.Calls);
    }

    /// <summary>
    /// A step hands over only the results it was told to. The ones left unticked are not written at
    /// all, and the ones ticked are written under the names this step gave them rather than under
    /// one name every part of the answer hangs off.
    /// </summary>
    [Fact]
    public async Task Only_the_results_a_step_was_told_to_hand_over_are_written()
    {
        var devices = new FakeDeviceLayer();
        devices.Vision.Match = new ImageMatch(0.75, new ScreenPoint(10, 20), new ScreenSize(4, 6));

        var (result, _, store) = await RunAsync(
        [
            Step("vision.findImage", Pictures("ok.png"), Returns("match=spot")),
        ], devices);

        Assert.True(result.Succeeded);

        // One hit is one record, so the parts of it are read by name: the middle a click aims at,
        // the corner a region counts from, how big it was and how sure the reading was.
        Assert.Equal("12,23", Centre(store, "spot"));
        Assert.Equal(10, Field(store, "spot", "left").AsNumber());
        Assert.Equal(4, Field(store, "spot", "width").AsNumber());
        Assert.Equal(0.75, Field(store, "spot", "score").AsNumber(), 3);
        Assert.Equal("10,20,4,6", Field(store, "spot", "box").AsText());

        // The whole list of hits was not ticked, so nothing holds it — not even an empty list.
        Assert.False(store.TryGet("spot.every", out _));
    }

    /// <summary>
    /// A hit is handed over twice over: the middle of it, which is what a click aims at, and its
    /// top-left corner with the rectangle it makes, which is where a search region counts from — so
    /// a step that looks again at what it just found can say so without arithmetic first.
    /// </summary>
    [Fact]
    public async Task A_hit_hands_over_its_middle_and_its_corner()
    {
        var devices = new FakeDeviceLayer();
        devices.Vision.Match = new ImageMatch(0.99, new ScreenPoint(30, 40), new ScreenSize(10, 6));

        var (result, _, store) = await RunAsync(
        [
            Step("vision.findImage", Pictures("ok.png"), Hit("match", "spot")),
        ], devices);

        Assert.True(result.Succeeded, result.Key);

        // The middle of a 10 by 6 match that starts at (30,40).
        Assert.Equal("35,43", Centre(store, "spot"));
        Assert.Equal(35, Field(store, "spot", "x").AsNumber());
        Assert.Equal(43, Field(store, "spot", "y").AsNumber());

        // And the corner, which is what a region is written from, plus the whole rectangle ready
        // to be named in one variable box.
        Assert.Equal(30, Field(store, "spot", "left").AsNumber());
        Assert.Equal(40, Field(store, "spot", "top").AsNumber());
        Assert.Equal("30,40,10,6", Field(store, "spot", "box").AsText());
    }

    /// <summary>
    /// A result the step was not told to hand over is undefined rather than empty: reading it is
    /// what the design-time check warns about, and a run that reaches it says so instead of quietly
    /// working on "nothing".
    /// </summary>
    [Fact]
    public async Task An_expression_reading_a_result_nobody_asked_for_fails_the_step()
    {
        var devices = new FakeDeviceLayer();
        devices.Vision.Match = new ImageMatch(0.9, new ScreenPoint(1, 2), new ScreenSize(3, 4));

        var (result, _, _) = await RunAsync(
        [
            Step("vision.findImage", Pictures("ok.png")),
            Step("control.log", Fields.Computed("message", "match + 1")),
        ], devices);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal("Run.BadExpression", result.Key);
    }

    /// <summary>
    /// 数字格里的算式算不出来时，这一步按 Run.BadExpression 失败，而不是把答案当成 0 接着往下走：
    /// X 写成 field($match, "x") 而 $match 这个名字根本不存在时，以前指针会去 (0, 40)——发错的
    /// 位置，而且没有任何一处说为什么。文本字段里的同一句话一直是当场失败的，数字字段现在也一样。
    /// </summary>
    [Fact]
    public async Task A_number_whose_expression_cannot_be_worked_out_fails_the_step()
    {
        var (result, devices, _) = await RunAsync(
        [
            Step("input.mouseMove", Param("x", "field($match, \"x\")"), Param("y", "40")),
        ]);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal("Run.BadExpression", result.Key);

        Assert.Empty(Asked(devices));
    }

    /// <summary>
    /// 空字段是"没说"，不是错：没人回答的那一格照旧读成 0，这一步照常走完。零点本身也是宏能
    /// 表达的意思（屏幕最左边、不等待），所以留空与"写不出数"必须分开。
    /// </summary>
    [Fact]
    public async Task A_number_left_empty_is_still_nothing_said()
    {
        var (result, devices, _) = await RunAsync(
        [
            Step("input.mouseMove", Param("x", ""), Param("y", "40")),
        ]);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal(["move 0 40 0"], devices.Calls);
    }

    /// <summary>
    /// 找图没找到时，它交出的那条记录是一段空文本；把这段空文本当数字用（读 $spot，或者
    /// field($spot, "x")）是宏写错了。以前这一格悄悄当成 0，于是"没找到就点到 (0, 5)"；现在
    /// 当场失败，一个鼠标消息都不发。
    /// </summary>
    [Fact]
    public async Task A_number_read_from_what_was_not_found_fails_rather_than_aiming_at_zero()
    {
        var devices = new FakeDeviceLayer();

        var (result, _, _) = await RunAsync(
        [
            Step("vision.findImage", Pictures("ok.png"), Hit("match", "spot")),
            Step("input.mouseClick", Fields.Reference("x", "spot"), Param("y", "5")),
        ], devices);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal("Run.BadExpression", result.Key);

        // 找图那一步自己的事照做（抓屏、读参考图、比对），点下去这一件事没有发生。
        Assert.DoesNotContain(devices.Calls, call => call.StartsWith("click", StringComparison.Ordinal));
    }

    /// <summary>
    /// A region may name the record a step handed over, which is how "look again at what you just
    /// found" is written: the four sides are read out of the hit rather than typed again, so a
    /// rectangle that moved between the two steps is searched where it is now.
    /// </summary>
    [Fact]
    public async Task A_search_region_can_be_named_by_the_record_a_step_handed_over()
    {
        var devices = new FakeDeviceLayer();
        devices.Vision.Match = new ImageMatch(0.99, new ScreenPoint(10, 20), new ScreenSize(4, 6));

        var (result, _, _) = await RunAsync(
        [
            Step("vision.findImage", Pictures("a.png"), Region((0, 0, 40, 30)),
                Hit("match", "spot")),
            Step("vision.findImage", Pictures("b.png"), RegionFrom("$spot"),
                Hit("match", "again")),
        ], devices);

        Assert.True(result.Succeeded, result.Key);

        // The second search looked at the rectangle the first one found — whose top-left corner is
        // what a region counts from — rather than at the whole screen.
        Assert.Contains("capture 10 20 4 6", devices.Calls);
    }

    /// <summary>
    /// 搜索区域那一格交给一条记录时，看的是记录自己那一块（屏幕像素），不是从窗口角再量一次：
    /// "在刚找到的那块里再找一遍"因此在哪个坐标基准下看的都是同一块画面 —— 记录里的数是屏幕上量出来的，
    /// 窗口角只挪写出来的四个数。
    /// </summary>
    [Fact]
    public async Task A_region_handed_a_record_reads_where_the_record_says_whatever_the_anchor()
    {
        var devices = WithAWindow();
        devices.Vision.Match = new ImageMatch(0.99, new ScreenPoint(10, 20), new ScreenSize(4, 6));

        var (result, _, _) = await RunAsync(
        [
            Step("vision.findImage", Pictures("a.png"), Region((0, 0, 40, 30)),
                Hit("match", "spot")),
            Step("vision.findImage", Pictures("b.png"), RegionFrom("$spot"),
                Param("anchorMode", "window"), Param("window", "Notepad"),
                Hit("match", "again")),
        ], devices);

        Assert.True(result.Succeeded, result.Key);

        // 第二遍看的是那条记录自己那一块，而不是它加上窗口角之后的那一块。
        Assert.Contains("capture 10 20 4 6", devices.Calls);
        Assert.DoesNotContain("capture 1010 520 4 6", devices.Calls);
    }

    [Fact]
    public async Task A_search_region_can_be_measured_from_a_window_corner()
    {
        var devices = WithAWindow();
        devices.Vision.Match = new ImageMatch(0.99, new ScreenPoint(5, 6), new ScreenSize(4, 4));

        var (_, _, store) = await RunAsync(
        [
            Step("vision.findImage", Pictures("ok.png"), Region((10, 20, 30, 40)),
                Param("anchorMode", "window"), Param("window", "Notepad"),
                Hit("match", "where")),
        ], devices);

        Assert.Contains("capture 1010 520 30 40", devices.Calls);

        // The match is reported in screen pixels, so the step that clicks it needs no arithmetic.
        Assert.Equal("1017", Field(store, "where", "x").AsText());
        Assert.Equal("528", Field(store, "where", "y").AsText());
    }

    /// <summary>
    /// A step that reads the screen names its window the way a window action does: by the program
    /// that owns it, or by the class it registered, rather than by a title that moves with the
    /// document or the level. The window it names is also the one its picture is read out of, so
    /// this is what keeps such a step aimed at the right window once the title has changed.
    /// </summary>
    [Fact]
    public async Task A_step_that_reads_the_screen_can_name_its_window_by_the_program()
    {
        var devices = new FakeDeviceLayer();
        devices.Window.Open.Add(new WindowInfo(1, "笔记 - 第 3 关", new ScreenPoint(1000, 500),
            new ScreenSize(800, 600), false, false));
        devices.Window.Traits[1] = ("notepad", "Notepad");
        devices.Vision.Match = new ImageMatch(0.99, new ScreenPoint(5, 6), new ScreenSize(4, 4));

        var (result, _, store) = await RunAsync(
        [
            Step("vision.findImage", Pictures("ok.png"), Region((10, 20, 30, 40)),
                Param("anchorMode", "window"), Param("window", "notepad"),
                Param("how", "process"), Hit("match", "where")),
        ], devices);

        Assert.True(result.Succeeded, result.Key);

        // The picture comes out of the window the program named and the area is counted from that
        // window's own corner, which is what a step measuring from a window means.
        Assert.Contains("capture 1010 520 30 40", devices.Calls);
        Assert.Equal("1017", Field(store, "where", "x").AsText());
    }

    /// <summary>
    /// A pattern the machine cannot read is the macro's problem to see, in the same words the window
    /// actions report it with — a step that reads the screen says so rather than reading the desktop.
    /// </summary>
    [Fact]
    public async Task A_window_pattern_a_screen_step_cannot_read_stops_the_step()
    {
        var devices = WithAWindow();
        var (result, _, _) = await RunAsync(
        [
            Step("vision.findImage", Pictures("ok.png"),
                Param("anchorMode", "window"), Param("window", "Notepad["),
                Param("how", "titleRegex")),
        ], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.BadPattern", result.Key);
    }

    [Fact]
    public async Task Reading_a_pixel_stores_its_colour_both_ways()
    {
        var devices = new FakeDeviceLayer();
        devices.Screen.Pixel = new PixelColor(0xAB, 0xCD, 0xEF);
        var (_, _, store) = await RunAsync(
        [
            Step("vision.getPixel", Param("x", "5"), Param("y", "6"),
                Returns("color=tone", "number=value")),
        ], devices);

        Assert.Equal("#ABCDEF", store.Local.Values["tone"].AsText());
        Assert.Equal(0xEFCDAB, store.Local.Values["value"].Number);
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
}
