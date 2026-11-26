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

// Places written as records: the coordinate spaces, the anchors, and what a record wins over.
public partial class DeviceActionTests
{
    /// <summary>
    /// 记录格对鼠标的每个动作都是同一个说法：移动、点击、双击、按下、松开、滚动都落在记录说的
    /// 那一点上，取的是那块矩形的中心。
    /// </summary>
    [Fact]
    public async Task Every_mouse_action_can_land_where_a_record_says()
    {
        var devices = new FakeDeviceLayer();
        devices.Vision.Matches.Add(new ImageMatch(0.99, new ScreenPoint(30, 40), new ScreenSize(10, 20)));

        var (result, _, _) = await RunAsync(
        [
            Step("vision.findImage", Pictures("ok.png"), Hit("match", "spot")),
            Step("input.mouseDoubleClick", Param("button", "left"), Fields.Reference("place", "spot")),
            Step("input.mouseDown", Param("button", "left"), Fields.Reference("place", "spot")),
            Step("input.mouseUp", Param("button", "left"), Fields.Reference("place", "spot")),
        ], devices);

        Assert.True(result.Succeeded, result.Key);
        Assert.Contains("click left 35 50 2 0", devices.Calls);
        Assert.Contains("down left 35 50", devices.Calls);
        Assert.Contains("up left 35 50", devices.Calls);
    }

    [Fact]
    public async Task A_scroll_can_be_sent_more_than_once()
    {
        var (_, devices, _) = await RunAsync(
        [
            Step("input.mouseScroll", Param("direction", "down"), Param("amount", "3"),
                Param("x", "1"), Param("y", "2"), Param("repeat", "2"), Param("intervalMs", "0")),
        ]);

        Assert.Equal(["scroll down 360 1 2", "scroll down 360 1 2"], devices.Calls);
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
                Param("button", "left"), Param("durationMs", "200"), Param("dragSteps", "10")),
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

    /// <summary>
    /// A display left of the primary one starts at a negative coordinate, and a place on it is a
    /// place the step has to be able to write. The number reaches the device as written rather than
    /// being read as zero, which is what a box that starts at zero would have done to it.
    /// </summary>
    [Fact]
    public async Task A_place_on_a_display_left_of_the_primary_one_stays_negative()
    {
        var (_, devices, _) = await RunAsync(
        [
            Step("input.mouseMove", Param("x", "-40"), Param("y", "-12"), Param("durationMs", "0")),
        ]);

        Assert.Contains("move -40 -12 0", devices.Calls);
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
        devices.Window.Open.Add(new WindowInfo(1, "Notepad - notes.txt",
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
                Param("anchorMode", "screen"), Param("window", "Notepad")),
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
                Param("anchorMode", "window"), Param("window", "Notepad")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Equal(["findWindow Notepad", "clientOrigin 1", "click left 1010 520 1 0"],
            devices.Calls);
    }

    [Fact]
    public async Task A_point_can_be_measured_from_inside_the_window_border()
    {
        var devices = WithAWindow();
        devices.Window.ClientOrigins[1] = new ScreenPoint(1008, 531);

        var (_, _, _) = await RunAsync(
        [
            Step("input.mouseDown", Param("button", "left"), Param("x", "5"), Param("y", "6"),
                Param("anchorMode", "window"), Param("window", "Notepad")),
        ], devices);

        // The button is also let go where it was pressed once the run ends, from the place the
        // press was read at rather than a fresh reading of where the window is now.
        Assert.Equal(["findWindow Notepad", "clientOrigin 1", "down left 1013 537", "up left 1013 537"],
            devices.Calls);
    }

    [Fact]
    public async Task A_window_search_returns_a_screen_point_and_a_window_point()
    {
        var devices = WithWindow();
        devices.Vision.Match = new ImageMatch(0.99, new ScreenPoint(10, 20), new ScreenSize(4, 6));

        var (result, _, store) = await RunAsync(
        [
            Step("vision.findImage", Pictures("ok.png"), Region((0, 0, 40, 40)),
                Param("anchorMode", "window"), Param("window", "Game"),
                Returns("match=hit", "screenPoint=screen", "windowPoint=window")),
        ], devices);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal(Field(store, "hit", "x").AsText(), Field(store, "screen", "x").AsText());
        Assert.Equal(Field(store, "hit", "y").AsText(), Field(store, "screen", "y").AsText());
        Assert.Equal("screen", Field(store, "screen", "basis").AsText());
        Assert.Equal("window", Field(store, "window", "basis").AsText());
        Assert.Equal(108, Field(store, "screen", "x").AsNumber()
            - Field(store, "window", "x").AsNumber());
        Assert.Equal(81, Field(store, "screen", "y").AsNumber()
            - Field(store, "window", "y").AsNumber());
    }

    [Fact]
    public async Task Background_input_posts_client_pixels_and_returns_both_points()
    {
        var devices = WithWindow();
        var (result, _, store) = await RunAsync(
        [
            Step("input.mouseClick", Param("inputMode", "background"), Param("window", "Game"),
                Param("button", "left"), Param("x", "5"), Param("y", "6"),
                Returns("screenPoint=screen", "windowPoint=window")),
        ], devices);

        Assert.True(result.Succeeded, result.Key);
        Assert.Contains("click left 5 6 1 0", devices.Calls);
        Assert.Equal(113, Field(store, "screen", "x").AsNumber());
        Assert.Equal(87, Field(store, "screen", "y").AsNumber());
        Assert.Equal(5, Field(store, "window", "x").AsNumber());
        Assert.Equal(6, Field(store, "window", "y").AsNumber());
    }

    [Fact]
    public async Task Background_input_can_take_its_window_from_a_record_variable()
    {
        var devices = WithWindow();
        var store = new VariableStore();
        store.Set("win", Value.FromRecord(
        [
            (FieldNames.Title, Value.FromText("Game")),
            (FieldNames.Process, Value.FromText("game")),
            (FieldNames.Class, Value.FromText("GameClass")),
            (FieldNames.How, Value.FromText(WindowHow.TitleContains)),
        ]));

        var result = await new MacroRunner(store, new SilentRunHost(), devices)
            .RunAsync([Step("input.mouseClick", Param("inputMode", "background"),
                Fields.Reference("window", "win"), Param("button", "left"),
                Param("x", "5"), Param("y", "6"))]);

        Assert.True(result.Succeeded, result.Key);
        Assert.Contains("click left 5 6 1 0", devices.Calls);
        Assert.Equal(4, Assert.Single(devices.Input.Routes).WindowHandle);
    }

    [Fact]
    public async Task A_drag_returns_both_ends_in_both_coordinate_spaces()
    {
        var devices = WithWindow();
        var (result, _, store) = await RunAsync(
        [
            Step("input.mouseDrag", Param("anchorMode", "window"), Param("window", "Game"),
                Param("startX", "10"), Param("startY", "20"),
                Param("endX", "30"), Param("endY", "40"),
                Param("button", "left"), Param("durationMs", "0"), Param("dragSteps", "2"),
                Returns("screenStartPoint=ss", "screenEndPoint=se",
                    "windowStartPoint=ws", "windowEndPoint=we")),
        ], devices);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal((118, 101), (Field(store, "ss", "x").AsNumber(),
            Field(store, "ss", "y").AsNumber()));
        Assert.Equal((138, 121), (Field(store, "se", "x").AsNumber(),
            Field(store, "se", "y").AsNumber()));
        Assert.Equal((10, 20), (Field(store, "ws", "x").AsNumber(),
            Field(store, "ws", "y").AsNumber()));
        Assert.Equal((30, 40), (Field(store, "we", "x").AsNumber(),
            Field(store, "we", "y").AsNumber()));
    }

    [Fact]
    public async Task A_screen_basis_does_not_write_a_window_point()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, store) = await RunAsync(
        [
            Step("input.mouseMove", Param("x", "10"), Param("y", "20"),
                Returns("windowPoint=window")),
        ], devices);

        Assert.True(result.Succeeded, result.Key);
        Assert.True(store.Local.TryGet("window", out var window));
        Assert.Equal(string.Empty, window.AsText());
    }

    [Fact]
    public async Task Capturing_a_whole_window_returns_its_middle_in_both_spaces()
    {
        var devices = WithWindow();
        var (result, _, store) = await RunAsync(
        [
            Step("vision.captureWindow", Param("window", "Game"),
                Returns("screenPoint=screen", "windowPoint=window")),
        ], devices);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal(300, Field(store, "screen", "x").AsNumber());
        Assert.Equal(200, Field(store, "screen", "y").AsNumber());
        Assert.Equal(192, Field(store, "window", "x").AsNumber());
        Assert.Equal(119, Field(store, "window", "y").AsNumber());
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
                Param("button", "left"), Param("durationMs", "0"), Param("dragSteps", "2"),
                Param("anchorMode", "window"), Param("window", "Notepad")),
        ], devices);

        Assert.Equal(["findWindow Notepad", "clientOrigin 1", "drag left 1010 520 1030 540 0 2"],
            devices.Calls);
    }

    /// <summary>
    /// 拖拽的两头各有自己的开关与记录格：找图留下的那条命中带着 x、y（矩形中心），勾上「起点取自一条
    /// 记录」再把它填进「起点记录」就是"把刚找到的东西从那儿拖走"，不必再把坐标抄一遍。
    /// </summary>
    [Fact]
    public async Task A_drag_can_start_where_something_was_just_found()
    {
        var devices = new FakeDeviceLayer();
        devices.Vision.Matches.Add(new ImageMatch(0.99, new ScreenPoint(30, 40), new ScreenSize(10, 20)));

        var (result, _, _) = await RunAsync(
        [
            Step("vision.findImage", Pictures("ok.png"), Hit("match", "spot")),
            Step("input.mouseDrag",
                Fields.Reference("startPlace", "spot"),
                Param("endX", "300"), Param("endY", "400"),
                Param("durationMs", "0"), Param("dragSteps", "4")),
        ], devices);

        Assert.True(result.Succeeded, result.Key);

        // 命中的中心是 (35, 50)，终点照写出来的两个数读；前面几次是找图那一步自己的事。
        Assert.Equal("drag left 35 50 300 400 0 4", devices.Calls[^1]);
    }

    /// <summary>
    /// 两头各自取一条记录：一端取上一步找到的东西，另一端取另一样，四个数字一格都不用解析出来。
    /// 两个开关各管一头，所以两头谁取记录、谁写数字是分开说的。
    /// </summary>
    [Fact]
    public async Task Both_ends_of_a_drag_can_each_come_from_their_own_record()
    {
        var devices = WithAWindow();
        devices.Vision.Matches.Add(new ImageMatch(0.99, new ScreenPoint(30, 40), new ScreenSize(10, 20)));

        var (result, _, _) = await RunAsync(
        [
            Step("vision.findImage", Pictures("ok.png"), Hit("match", "spot")),
            Step("window.info", Param("window", "Notepad"), Returns("window=to")),
            Step("input.mouseDrag",
                Fields.Reference("startPlace", "spot"),
                Fields.Reference("endPlace", "to"),
                Param("durationMs", "0"), Param("dragSteps", "4")),
        ], devices);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal("drag left 35 50 1400 800 0 4", devices.Calls[^1]);
    }

    /// <summary>
    /// 滚轮的位置也由同一个开关与同一个记录格说了算：在找到的东西上滚，指针落在它自己的中心。
    /// </summary>
    [Fact]
    public async Task A_scroll_can_happen_where_something_was_just_found()
    {
        var devices = new FakeDeviceLayer();
        devices.Vision.Matches.Add(new ImageMatch(0.99, new ScreenPoint(30, 40), new ScreenSize(10, 20)));

        var (result, _, _) = await RunAsync(
        [
            Step("vision.findImage", Pictures("ok.png"), Hit("match", "spot")),
            Step("input.mouseScroll", Param("direction", "down"), Param("amount", "2"),
                Fields.Reference("place", "spot")),
        ], devices);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal("scroll down 240 35 50", devices.Calls[^1]);
    }

    /// <summary>
    /// 一条记录说的是屏幕上的那一点，只有写出来的数字才从「坐标基准」量起：命中记录的 x、y 是屏幕像素
    /// （同一条命中在窗口基准下交出来的也是屏幕像素），坐标基准选窗口时若再量一次，起点就整个偏了一个
    /// 窗口的位置 —— 在默认的屏幕基准下对，换个基准就拖到别处去了。
    /// </summary>
    [Fact]
    public async Task A_drag_from_a_record_starts_where_the_record_says_whatever_the_anchor()
    {
        var devices = WithAWindow();
        devices.Vision.Matches.Add(new ImageMatch(0.99, new ScreenPoint(30, 40), new ScreenSize(10, 20)));

        var (result, _, _) = await RunAsync(
        [
            Step("vision.findImage", Pictures("ok.png"), Hit("match", "spot")),
            Step("input.mouseDrag",
                Fields.Reference("startPlace", "spot"),
                Param("endX", "30"), Param("endY", "40"),
                Param("durationMs", "0"), Param("dragSteps", "2"),
                Param("anchorMode", "window"), Param("window", "Notepad")),
        ], devices);

        Assert.True(result.Succeeded, result.Key);

        // 起点是那条记录自己的中心 (35,50)；终点是写出来的两个数，照旧从窗口角 (1000,500) 量起。
        Assert.Equal("drag left 35 50 1030 540 0 2", devices.Calls[^1]);
    }

    /// <summary>
    /// 两头照同一个「坐标基准」量起，窗口挪到别处之后这一次拖拽的四个数跟着挪，所以"从窗口里的哪个
    /// 地方拖到窗口里的哪个地方"在两个窗口位置上说的是同一件事。
    /// </summary>
    [Fact]
    public async Task A_drag_inside_a_window_is_carried_along_when_the_window_moves()
    {
        var devices = WithAWindow();
        var step = Step("input.mouseDrag",
            Param("startX", "10"), Param("startY", "20"), Param("endX", "30"), Param("endY", "40"),
            Param("durationMs", "0"), Param("dragSteps", "2"),
            Param("anchorMode", "window"), Param("window", "Notepad"));

        await RunAsync([step], devices);
        devices.Window.Open[0] = devices.Window.Open[0] with { Location = new ScreenPoint(300, 400) };
        await RunAsync([step], devices);

        Assert.Equal(
        [
            "findWindow Notepad", "clientOrigin 1", "drag left 1010 520 1030 540 0 2",
            "findWindow Notepad", "clientOrigin 1", "drag left 310 420 330 440 0 2",
        ], devices.Calls);
    }

    /// <summary>
    /// 一条记录不是数字：把它填进 X 这种数字格，读的时候直接说清楚，而不是悄悄按 0 或者按字段顺序
    /// 取一个数。设计期会标出来，跑到这一步时也要给出能看懂的失败键。
    /// </summary>
    [Fact]
    public async Task A_record_in_a_number_field_is_refused()
    {
        var devices = new FakeDeviceLayer();
        devices.Vision.Matches.Add(new ImageMatch(0.99, new ScreenPoint(30, 40), new ScreenSize(10, 20)));

        var (result, _, _) = await RunAsync(
        [
            Step("vision.findImage", Pictures("ok.png"), Hit("match", "spot")),
            Step("input.mouseMove", Fields.Reference("x", "spot"), Param("y", "40")),
        ], devices);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal("Run.RecordWhereNumber", result.Key);
    }

    /// <summary>
    /// 位置格里写的不是记录（一个数字、一段文字）时说 Run.BadPoint，并把写进去的东西说出来：这一格
    /// 收的是"带 x、y 的记录"，不是随便一个值。
    /// </summary>
    [Fact]
    public async Task A_place_that_is_not_a_record_is_refused()
    {
        var devices = new FakeDeviceLayer();

        var (result, _, _) = await RunAsync(
        [
            Step("input.mouseMove", Param("place", "10"), Param("y", "40")),
        ], devices);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal("Run.BadPoint", result.Key);
    }

    /// <summary>
    /// 记录格空着时这一端按旁边的数字说：两种说法是同一个答案，没写记录不是失败。
    /// </summary>
    [Fact]
    public async Task A_drag_starts_at_its_numbers_when_no_record_is_written()
    {
        var devices = new FakeDeviceLayer();

        var (result, _, _) = await RunAsync(
        [
            Step("input.mouseDrag",
                Param("startX", "10"), Param("startY", "20"),
                Param("endX", "300"), Param("endY", "0"),
                Param("durationMs", "0"), Param("dragSteps", "3")),
        ], devices);

        Assert.True(result.Succeeded, result.Key);
        Assert.Contains("drag left 10 20 300 0 0 3", devices.Calls);
    }

    /// <summary>
    /// 记录格里那条记录没有 x、y 时报 Run.BadPoint，并把它读到的东西写出来：一条 CSV 的行也是一条
    /// 记录，但里面没有 x、y，拿它当位置用不该静默地拖到 (0,0)。
    /// </summary>
    [Fact]
    public async Task A_drag_end_that_is_not_a_place_is_refused()
    {
        var devices = new FakeDeviceLayer();
        devices.Files.Store["in.csv"] = "name,age\nAda,36\n";

        var (result, _, _) = await RunAsync(
        [
            Step("file.readCsv", Param("path", "in.csv"), Returns("rows=table")),
            Step("input.mouseDrag",
                Param("startPlace", Formula.Call("get", Formula.Name("table"), Formula.Number(0))),
                Param("endX", "300"), Param("endY", "0"),
                Param("durationMs", "0"), Param("dragSteps", "3")),
        ], devices);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal("Run.BadPoint", result.Key);
        Assert.DoesNotContain(devices.Calls, call => call.StartsWith("drag", StringComparison.Ordinal));
    }

    /// <summary>
    /// 鼠标动作的位置也收一条记录：找图留下的那条命中带着 x、y（矩形中心），勾上开关再把它填进记录格
    /// 就是"把刚找到的东西点一下"。以前只能把同一件事写两遍（field(命中, "x") 与 field(命中, "y")），
    /// 其中一格写错，点下去的地方就不是找到的那一处。
    /// </summary>
    [Fact]
    public async Task A_click_can_go_where_something_was_just_found()
    {
        var devices = new FakeDeviceLayer();
        devices.Vision.Matches.Add(new ImageMatch(0.99, new ScreenPoint(30, 40), new ScreenSize(10, 20)));

        var (result, _, _) = await RunAsync(
        [
            Step("vision.findImage", Pictures("ok.png"), Hit("match", "spot")),
            Step("input.mouseClick", Param("button", "left"),
                Fields.Reference("place", "spot")),
        ], devices);

        Assert.True(result.Succeeded, result.Key);

        // 命中的中心是 (35, 50)。
        Assert.Equal("click left 35 50 1 0", devices.Calls[^1]);
    }

    /// <summary>
    /// 两格收一条记录时的读法与拖拽那一端一致：记录说的是屏幕上的那一点，只有写出来的数字才从「坐标
    /// 基准」量起。命中记录在窗口基准下交出来的 x、y 也是屏幕像素，再按窗口量一次，指针就落在偏出
    /// 一个窗口的位置上。
    /// </summary>
    [Fact]
    public async Task A_record_in_a_mouse_step_says_where_it_is_whatever_the_anchor()
    {
        var devices = WithAWindow();
        devices.Vision.Matches.Add(new ImageMatch(0.99, new ScreenPoint(30, 40), new ScreenSize(10, 20)));

        var (result, _, _) = await RunAsync(
        [
            Step("vision.findImage", Pictures("ok.png"), Hit("match", "spot")),
            Step("input.mouseMove", Fields.Reference("place", "spot"),
                Param("anchorMode", "window"), Param("window", "Notepad")),
            Step("input.mouseMove", Param("x", "30"), Param("y", "40"),
                Param("anchorMode", "window"), Param("window", "Notepad")),
        ], devices);

        Assert.True(result.Succeeded, result.Key);

        // 第一条落在记录自己的中心 (35,50)；第二条是写出来的两个数，照旧从窗口角 (1000,500) 量起。
        Assert.Equal(["move 35 50 0", "move 1030 540 0"],
            devices.Calls.Where(call => call.StartsWith("move", StringComparison.Ordinal)));
    }

    /// <summary>
    /// 记录格里有一条记录时它说了算：手写的文件里同时留着数字也不该把这一步悄悄挪到别的地方去，
    /// 界面本来也不会让两样同时留下。
    /// </summary>
    [Fact]
    public async Task A_record_wins_over_numbers_written_beside_it()
    {
        var devices = new FakeDeviceLayer();
        devices.Vision.Matches.Add(new ImageMatch(0.99, new ScreenPoint(30, 40), new ScreenSize(10, 20)));

        var (result, _, _) = await RunAsync(
        [
            Step("vision.findImage", Pictures("ok.png"), Hit("match", "spot")),
            Step("input.mouseClick", Param("button", "left"), Param("x", "100"), Param("y", "200"),
                Fields.Reference("place", "spot")),
        ], devices);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal("click left 35 50 1 0", devices.Calls[^1]);
    }
}
