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

// Reading, capturing, moving and closing the window a step names.
public partial class DeviceActionTests
{
    [Fact]
    public async Task Window_info_reports_where_a_window_sits_and_how_big_it_is()
    {
        var devices = WithAWindow();
        var (result, _, store) = await RunAsync(
            [Step("window.info", Param("window", "Notepad"), Parts("window", "win"))], devices);

        Assert.True(result.Succeeded);

        // The rectangle is written the way a search region is spelled, so it can be used as one.
        Assert.Equal("1000,500,800,600", Field(store, "win", "box").AsText());
        Assert.Equal(1000, Field(store, "win", "left").AsNumber());
        Assert.Equal(500, Field(store, "win", "top").AsNumber());
        Assert.Equal(800, Field(store, "win", "width").AsNumber());
        Assert.Equal(600, Field(store, "win", "height").AsNumber());
        Assert.Equal("Notepad - notes.txt", Field(store, "win", "title").AsText());
    }

    /// <summary>
    /// One window is one record however it was come by: a listing and an asked-for window carry the
    /// same fields, and only the part inside the border differs — a listing leaves it empty because
    /// reading it costs a look at every window found.
    /// </summary>
    [Fact]
    public async Task A_window_asked_for_by_name_is_shaped_like_one_from_a_listing()
    {
        var devices = new FakeDeviceLayer();
        devices.Window.Open.Add(new WindowInfo(42, "Notepad - notes.txt", new ScreenPoint(10, 20),
            new ScreenSize(300, 200), false, true));
        devices.Window.Traits[42] = ("notepad", "Notepad");

        var (result, _, store) = await RunAsync(
        [
            Step("window.info", Param("window", "Notepad"), Returns("window=win")),
            Step("window.list", Returns("windows=list")),
        ], devices);

        Assert.True(result.Succeeded, result.Key);
        var listed = store.Local.Values["list"].AsList()[0];

        Assert.Equal(store.Local.Values["win"].Fields.Select(field => field.Name),
            listed.Fields.Select(field => field.Name));
        Assert.Equal(Field(store, "win", "title").AsText(), listed.Field("title")!.AsText());
        Assert.Equal(Field(store, "win", "handle").Number, listed.Field("handle")!.Number);
        Assert.Equal(string.Empty, listed.Field("clientBox")!.AsText());
        Assert.NotEqual(string.Empty, Field(store, "win", "clientBox").AsText());
    }

    /// <summary>
    /// Every record a step hands over says exactly what the catalogue says it says: the fields of a
    /// hit, a rectangle, a window, a display, a program and an element are the declared ones, in
    /// the declared order, so a macro can read them by name before the step has ever run.
    /// </summary>
    [Fact]
    public async Task Every_record_a_step_hands_over_carries_the_fields_the_catalogue_declares()
    {
        var devices = new FakeDeviceLayer();
        devices.Ocr.Spans = [new TextSpan("Save", new ScreenPoint(10, 20), new ScreenSize(40, 12), 0.9)];
        devices.Vision.Matches.Add(new ImageMatch(0.99, new ScreenPoint(30, 40), new ScreenSize(10, 20)));
        devices.Window.Open.Add(new WindowInfo(42, "Notepad - notes.txt", new ScreenPoint(10, 20),
            new ScreenSize(300, 200), false, true));
        devices.Window.Traits[42] = ("notepad", "Notepad");
        devices.Ui.Elements.Add(new UiElementInfo("Count", "countText", "Text", "TextClass",
            new ScreenPoint(10, 20), new ScreenSize(80, 24), "Untitled - Notepad"));
        devices.Processes.Running["notepad"] = [42];

        var (result, _, store) = await RunAsync(
        [
            Step("vision.findImage", Pictures("ok.png"), Returns("match=findHit")),
            Step("vision.capture", Param("x", "10"), Param("y", "10"),
                Param("width", "20"), Param("height", "20"), Returns("place=shot")),
            Step("window.info", Param("window", "Notepad"), Returns("window=win")),
            Step("window.list", Returns("windows=list")),
            Step("screen.list", Returns("displays=desk")),
            Step("process.info", Param("window", "notepad"), Param("how", WindowHow.ProcessEquals),
                Returns("program=prog")),
            Step("uia.read", Param("window", ""), Param("uiSelector", "Text[name='Count']"),
                Returns("element=el")),
            Step("uia.find", Param("window", ""), Param("uiSelector", "Text[name='Count']"),
                Returns("match=found")),
            Step("ocr.findText", Param("queryText", "Save"), Hit("match", "words")),
        ], devices);

        Assert.True(result.Succeeded, result.Key);

        Assert.Equal(Declared("vision.findImage", "match"), Names(store.Local.Values["findHit"]));
        Assert.Equal(Declared("vision.capture", "place"), Names(store.Local.Values["shot"]));
        Assert.Equal(Declared("window.info", "window"), Names(store.Local.Values["win"]));
        Assert.Equal(Declared("window.list", "windows"),
            Names(store.Local.Values["list"].AsList()[0]));
        Assert.Equal(Declared("screen.list", "displays"),
            Names(store.Local.Values["desk"].AsList()[0]));
        Assert.Equal(Declared("process.info", "program"), Names(store.Local.Values["prog"]));
        Assert.Equal(Declared("uia.read", "element"), Names(store.Local.Values["el"]));
        Assert.Equal(Declared("uia.find", "match"), Names(store.Local.Values["found"]));
        Assert.Equal(Declared("ocr.findText", "match"), Names(store.Local.Values["words"]));
    }

    /// <summary>The fields a result is declared to carry, in the catalogue's order.</summary>
    private static IReadOnlyList<string> Declared(string action, string result)
        => [.. ActionSemanticsCatalog.Find(action)!.Returns
            .First(entry => entry.Key == result).Fields.Select(field => field.Name)];

    /// <summary>The fields a record actually carries, in the order it wrote them.</summary>
    private static IReadOnlyList<string> Names(Value record)
        => [.. record.Fields.Select(field => field.Name)];

    /// <summary>The macros of a small project, with no declarations of their own.</summary>
    private sealed class Macros : IMacroLibrary
    {
        private readonly Dictionary<string, IReadOnlyList<ExecutableStep>> _byName =
            new(StringComparer.OrdinalIgnoreCase);

        public Macros(params (string Name, ExecutableStep[] Steps)[] entries)
        {
            foreach (var (name, steps) in entries)
            {
                _byName[name] = steps;
            }
        }

        public IReadOnlyList<string> Names => [.. _byName.Keys];

        public IReadOnlyList<ExecutableStep>? Steps(string name)
            => _byName.TryGetValue(name, out var steps) ? steps : null;

        public MacroSignature? Signature(string name)
            => _byName.ContainsKey(name) ? MacroSignature.Empty : null;
    }

    /// <summary>
    /// 没找到的那一次不给命中：这一步勾的「命中」在尝试开始时先清空，所以读到的是空，而不是上一步
    /// 或上一次尝试留在同名变量里的值。重试也一样，每一次试都从空开始。
    /// </summary>
    [Fact]
    public async Task A_look_that_fails_leaves_no_hit_behind()
    {
        var devices = new FakeDeviceLayer();
        var failing = new ExecutableStep
        {
            Type = "vision.findImage",
            Parameters = [Pictures("ok.png"), Returns("match=found")],
            Meta = new StepMeta { RetryCount = 2, OnError = StepErrorAction.Continue },
        };

        var (result, _, store) = await RunAsync(
        [
            Step("control.setVariable", Param("name", "found"), Param("scope", "local"),
                Param("value", "left over")),
            failing,
        ], devices);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal(string.Empty, store.Local.Values["found"].AsText());
    }

    /// <summary>
    /// 循环的每一轮各算各的：第一轮找到、第二轮没找到，循环结束时「命中」是空的，不是第一轮那一处。
    /// </summary>
    [Fact]
    public async Task A_second_round_does_not_keep_the_first_rounds_hit()
    {
        var devices = new FakeDeviceLayer();
        var calls = 0;
        devices.Vision.PictureAnswers = (_, _) => calls++ == 0
            ? [new ImageMatch(0.99, new ScreenPoint(10, 10), new ScreenSize(20, 20))]
            : [];
        var find = new ExecutableStep
        {
            Type = "vision.findImage",
            Parameters = [Pictures("ok.png"), Returns("match=found")],
            Meta = new StepMeta { OnError = StepErrorAction.Continue },
        };
        var loop = Step("control.repeat", Param("times", "2"), Param("intervalMs", "0"),
            Body("body", find));

        var (result, _, store) = await RunAsync([loop], devices);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal(string.Empty, store.Local.Values["found"].AsText());
    }

    /// <summary>
    /// 一条窗口记录里关于位置有三种说法，各说各的：中心（x、y，动作要点的那个点）、左上角
    /// （left、top，搜索区域从那里量起）、整块矩形（box）。中心按整数除法取，宽高是奇数时也一样，
    /// 所以"点窗口正中"写两格字段就够；列表里的每一条是同一形状，中心也照同一处算。
    /// </summary>
    [Fact]
    public async Task A_window_record_says_the_middle_of_the_window_as_well_as_its_corner()
    {
        var devices = new FakeDeviceLayer();
        devices.Window.Open.Add(new WindowInfo(42, "Notepad - notes.txt", new ScreenPoint(7, 9),
            new ScreenSize(301, 201), false, false));

        var (result, _, store) = await RunAsync(
        [
            Step("window.info", Param("window", "Notepad"), Returns("window=win")),
            Step("window.list", Returns("windows=list")),
        ], devices);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal("157,109", Centre(store, "win"));
        Assert.Equal("7,9,301,201", Field(store, "win", "box").AsText());

        var listed = store.Local.Values["list"].AsList()[0];
        Assert.Equal("157,109", MiddleOf(listed));
    }

    [Fact]
    public async Task Window_info_fails_when_the_window_is_not_open()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
            [Step("window.info", Param("window", "ghost"), Parts("window", "win"))], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.WindowNotFound", result.Key);
    }

    [Fact]
    public async Task Capturing_a_window_copies_its_whole_rectangle()
    {
        var devices = WithAWindow();
        var (result, _, store) = await RunAsync(
            [Step("vision.captureWindow", Param("window", "Notepad"),
                Parts("image", "shot", "place"))], devices);

        Assert.True(result.Succeeded);
        Assert.Contains("capture 1000 500 800 600", devices.Calls);
        Assert.Equal(1000, Field(store, "shot.place", "left").AsNumber());
        Assert.Equal(500, Field(store, "shot.place", "top").AsNumber());
        Assert.Equal(800, Field(store, "shot.place", "width").AsNumber());
        Assert.Equal(600, Field(store, "shot.place", "height").AsNumber());
        Assert.Equal("1000,500,800,600", Field(store, "shot.place", "box").AsText());
    }

    [Fact]
    public async Task Capturing_a_window_that_is_not_open_fails()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
            [Step("vision.captureWindow", Param("window", "ghost"), Parts("image", "shot"))], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.WindowNotFound", result.Key);
    }

    [Fact]
    public async Task Waiting_for_a_window_gives_up_after_the_timeout()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
            [Within(100, Step("window.waitFor", Param("window", "ghost")))], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.WindowTimeout", result.Key);
    }

    [Fact]
    public async Task Activating_a_window_uses_its_handle()
    {
        var devices = new FakeDeviceLayer();
        devices.Window.Open.Add(new WindowInfo(21, "Notepad", new ScreenPoint(), new ScreenSize(), false, false));
        var (result, _, _) = await RunAsync(
            [Step("window.activate", Param("window", "note"))], devices);

        Assert.True(result.Succeeded);
        Assert.Contains("activateWindow 21", devices.Calls);
    }

    [Fact]
    public async Task Maximizing_a_window_uses_its_handle()
    {
        var devices = new FakeDeviceLayer();
        devices.Window.Open.Add(new WindowInfo(21, "Notepad", new ScreenPoint(), new ScreenSize(), false, false));
        await RunAsync([Step("window.maximize", Param("window", "Notepad"))], devices);

        Assert.Contains("maximizeWindow 21", devices.Calls);
    }

    [Fact]
    public async Task Minimizing_a_window_uses_its_handle()
    {
        var devices = new FakeDeviceLayer();
        devices.Window.Open.Add(new WindowInfo(21, "Notepad", new ScreenPoint(), new ScreenSize(), false, false));
        await RunAsync([Step("window.minimize", Param("window", "Notepad"))], devices);

        Assert.Contains("minimizeWindow 21", devices.Calls);
    }

    [Fact]
    public async Task Moving_a_window_passes_the_place_and_the_size()
    {
        var devices = new FakeDeviceLayer();
        devices.Window.Open.Add(new WindowInfo(21, "Notepad", new ScreenPoint(), new ScreenSize(), false, false));
        await RunAsync(
        [
            Step("window.move", Param("window", "Notepad"), Param("x", "100"), Param("y", "50"),
                Param("width", "640"), Param("height", "480")),
        ], devices);

        Assert.Contains("moveWindow 21 100 50 640 480", devices.Calls);
    }

    /// <summary>
    /// A side left out keeps what the window has, so a step can move a window without resizing it,
    /// or resize it without moving it, rather than having to read the rectangle first and write all
    /// four numbers back.
    /// </summary>
    [Fact]
    public async Task Moving_a_window_can_change_only_what_the_step_says()
    {
        var devices = new FakeDeviceLayer();
        devices.Window.Open.Add(new WindowInfo(21, "Notepad", new ScreenPoint(1000, 500),
            new ScreenSize(800, 600), false, false));

        await RunAsync(
        [
            Step("window.move", Param("window", "Notepad"), Param("x", "200")),
            Step("window.move", Param("window", "Notepad"), Param("width", "320")),
        ], devices);

        Assert.Equal(["moveWindow 21 200 500 800 600", "moveWindow 21 1000 500 320 600"],
            devices.Calls.Where(call => call.StartsWith("moveWindow", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_window_move_with_no_bounds_leaves_the_window_alone()
    {
        var devices = new FakeDeviceLayer();
        devices.Window.Open.Add(new WindowInfo(21, "Notepad", new ScreenPoint(1000, 500),
            new ScreenSize(800, 600), false, false));

        await RunAsync([Step("window.move", Param("window", "Notepad"))], devices);

        Assert.DoesNotContain(devices.Calls,
            call => call.StartsWith("moveWindow", StringComparison.Ordinal));
    }

    /// <summary>
    /// 窗口挪到哪儿的四格也收一条记录：`window.info` 交出来的那条记录里正好是这四个数，填进 X 就是"把
    /// 这个窗口摆成刚读到的那个窗口那样"，不必在四格里各写一遍 field()（四格错一格，窗口就摆到别处或
    /// 变成另一个大小）。
    /// </summary>
    [Fact]
    public async Task A_window_can_be_moved_where_a_record_says()
    {
        var devices = new FakeDeviceLayer();
        devices.Window.Open.Add(new WindowInfo(21, "Notepad", new ScreenPoint(1000, 500),
            new ScreenSize(800, 600), false, false));

        var (result, _, _) = await RunAsync(
        [
            Step("window.info", Param("window", "Notepad"), Returns("window=place")),
            Step("window.move", Param("window", "Notepad"),
                Param("rect", "$place")),
        ], devices);

        Assert.True(result.Succeeded, result.Key);

        // 与写四个数字的那条用例得到同一个结果：位置与大小都按记录自己的数来。
        Assert.Contains("moveWindow 21 1000 500 800 600", devices.Calls);
    }

    /// <summary>
    /// 「矩形」格里有一条记录时它说了算：手写的文件里同时留着四格数字也不该把窗口摆到别处去，
    /// 界面本来也不会让两样同时留下。
    /// </summary>
    [Fact]
    public async Task A_window_move_record_wins_over_numbers_beside_it()
    {
        var devices = new FakeDeviceLayer();
        devices.Window.Open.Add(new WindowInfo(21, "Notepad", new ScreenPoint(1000, 500),
            new ScreenSize(800, 600), false, false));

        var (result, _, _) = await RunAsync(
        [
            Step("window.info", Param("window", "Notepad"), Returns("window=place")),
            Step("window.move", Param("window", "Notepad"), Param("x", "100"), Param("y", "50"),
                Param("width", "640"), Param("height", "300"), Param("rect", "$place")),
        ], devices);

        Assert.True(result.Succeeded, result.Key);
        Assert.Contains("moveWindow 21 1000 500 800 600", devices.Calls);
    }

    /// <summary>
    /// 一条里面没有矩形的记录（例如 CSV 的一行）当"挪到哪儿"用时报 Run.BadRegion，并把它读到的东西写
    /// 出来：静默地按 (0,0) 摆过去，窗口就自己跑到屏幕左上角了。
    /// </summary>
    [Fact]
    public async Task A_record_with_no_rectangle_in_it_is_refused_as_a_window_rectangle()
    {
        var devices = new FakeDeviceLayer();
        devices.Files.Store["in.csv"] = "name,age\nAda,36\n";
        devices.Window.Open.Add(new WindowInfo(21, "Notepad", new ScreenPoint(1000, 500),
            new ScreenSize(800, 600), false, false));

        var (result, _, _) = await RunAsync(
        [
            Step("file.readCsv", Param("path", "in.csv"), Returns("rows=table")),
            Step("window.move", Param("window", "Notepad"),
                Param("rect", "get($table, 0)")),
        ], devices);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal("Run.BadRegion", result.Key);
        Assert.DoesNotContain(devices.Calls, call => call.StartsWith("moveWindow", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Closing_a_window_uses_its_handle()
    {
        var devices = new FakeDeviceLayer();
        devices.Window.Open.Add(new WindowInfo(21, "Notepad", new ScreenPoint(), new ScreenSize(), false, false));
        await RunAsync([Step("window.close", Param("window", "Notepad"))], devices);

        Assert.Contains("closeWindow 21", devices.Calls);
    }

    [Fact]
    public async Task A_window_that_is_not_open_stops_the_step()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
            [Step("window.close", Param("window", "ghost"))], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.WindowNotFound", result.Key);
        Assert.Equal("ghost", result.Detail);
    }

    [Fact]
    public async Task A_window_the_system_says_no_to_stops_the_step()
    {
        var devices = new FakeDeviceLayer();
        devices.Window.ActionWorks = false;
        devices.Window.Open.Add(new WindowInfo(5, "Locked", new ScreenPoint(), new ScreenSize(), false, false));
        var (result, _, _) = await RunAsync(
            [Step("window.restore", Param("window", "Locked"))], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.WindowFailed", result.Key);
        Assert.Equal("Locked", result.Detail);
    }
}
