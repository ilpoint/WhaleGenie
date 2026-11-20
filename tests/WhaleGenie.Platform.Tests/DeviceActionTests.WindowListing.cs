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

// Listing windows, narrowing the list, and following the one a step before it named.
public partial class DeviceActionTests
{
    /// <summary>
    /// Two windows with the same title are two windows. Counting them once would leave the lines
    /// that say what each one is and where it is short of an entry, and the one that went missing is
    /// the window that could not be read.
    /// </summary>
    [Fact]
    public async Task Listing_windows_names_every_window_even_when_titles_repeat()
    {
        var devices = new FakeDeviceLayer();
        devices.Window.Open.Add(new WindowInfo(1, "Notepad", new ScreenPoint(), new ScreenSize(), false, false));
        devices.Window.Open.Add(new WindowInfo(2, "Notepad", new ScreenPoint(), new ScreenSize(), false, false));
        devices.Window.Open.Add(new WindowInfo(3, "Calculator", new ScreenPoint(), new ScreenSize(), false, false));
        var (_, _, store) = await RunAsync(
            [Step("window.list", Returns("windows=titles"))], devices);

        Assert.Equal(["Notepad", "Notepad", "Calculator"], Titles(store, "titles"));
    }

    /// <summary>
    /// A text nobody wrote means every window to a listing, where it means the window in front to a
    /// step that acts on one window. Both readings belong to the device, so what the run does and
    /// what the dialog shows come from the same rule.
    /// </summary>
    [Fact]
    public async Task An_empty_match_text_lists_every_window_and_names_the_front_one()
    {
        var devices = new FakeDeviceLayer();
        devices.Window.Open.Add(new WindowInfo(1, "Notepad", new ScreenPoint(), new ScreenSize(), false, false));
        devices.Window.Open.Add(new WindowInfo(2, "Calculator", new ScreenPoint(), new ScreenSize(), false, false));

        var (_, _, store) = await RunAsync(
        [
            Step("window.list", Returns("windows=all")),
            Step("window.activate"),
        ], devices);

        Assert.Equal(["Notepad", "Calculator"], Titles(store, "all"));
        Assert.Contains("activateWindow 1", devices.Calls);
    }

    /// <summary>
    /// 一条窗口记录可以直接当窗口用：按「匹配方式」从记录里取标题、程序名或类名去认，而不是把整条
    /// 记录的 JSON 当成标题去比。
    /// </summary>
    [Fact]
    public async Task A_window_record_can_be_the_window_a_step_names()
    {
        var devices = WithAWindow();

        var (result, _, _) = await RunAsync(
        [
            Step("window.info", Param("window", "Notepad"), Returns("window=win")),
            Step("window.activate", Fields.Reference("window", "win")),
        ], devices);

        Assert.True(result.Succeeded, result.Key);
        Assert.Contains("activateWindow 1", devices.Calls);
    }

    /// <summary>
    /// 「沿用上一次用的窗口」：这一步不再自己认窗口，用前面某一步已经说过的那句话。宏在同一窗口里
    /// 做十件事是很平常的，没有道理十次都把窗口重新说一遍。
    /// </summary>
    [Fact]
    public async Task A_step_can_carry_on_with_the_window_a_step_before_it_named()
    {
        var devices = WithAWindow();

        var (result, _, _) = await RunAsync(
        [
            Step("window.activate", Param("window", "Notepad")),
            Step("window.activate", Param("window", Following)),
        ], devices);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal(2, devices.Calls.Count(call => call == "activateWindow 1"));
    }

    /// <summary>
    /// 一条什么都没提的步骤（空着就是"前台窗口"）不会把已认下来的窗口冲掉：链子从最近一个真的
    /// 指定了窗口的步骤接着走，而不是被中间那一步打断。
    /// </summary>
    [Fact]
    public async Task A_step_that_names_no_window_leaves_the_chain_where_it_was()
    {
        var devices = WithAWindow();
        devices.Window.Open.Add(new WindowInfo(2, "Calculator",
            new ScreenPoint(), new ScreenSize(), false, false));

        var (result, _, _) = await RunAsync(
        [
            Step("window.activate", Param("window", "Notepad")),
            Step("window.activate"),
            Step("window.activate", Param("window", Following)),
        ], devices);

        Assert.True(result.Succeeded, result.Key);

        // 中间那一步用的是前台窗口（替身里是列表第一个），第三步仍旧回到前面说过的记事本。
        Assert.Equal(3, devices.Calls.Count(call => call == "activateWindow 1"));
    }

    /// <summary>
    /// 一次都还没有人指定过窗口时，「沿用上一次」和空着一样用前台窗口，而不是报一个找不到的窗口：
    /// 这是它排在第一个、或者上面全是"看整个屏幕"的步骤时会遇到的情形。
    /// </summary>
    [Fact]
    public async Task Following_before_any_window_is_named_uses_the_one_in_front()
    {
        var devices = WithAWindow();

        var (result, _, _) = await RunAsync(
            [Step("window.activate", Param("window", Following))], devices);

        Assert.True(result.Succeeded, result.Key);
        Assert.Contains("activateWindow 1", devices.Calls);
    }

    /// <summary>
    /// 沿用的规则在文件里是一小段标记，不是一条认识的窗口：读回来还是"沿用"，而且不写这一格时
    /// 规则与从前一字不差。
    /// </summary>
    [Fact]
    public void A_rule_that_follows_is_written_as_one_flag_and_nothing_else_changes()
    {
        var following = WindowRule.FollowPrevious.ToJson();
        Assert.True(following[FieldNames.Follow]!.GetValue<bool>());
        Assert.True(WindowRule.TryParse(following.ToJsonString(), out var read));
        Assert.True(read.Follow);
        Assert.False(read.HasTarget);

        var named = new WindowRule(Title: "Notepad").ToJson();
        Assert.Null(named[FieldNames.Follow]);
        Assert.DoesNotContain(FieldNames.Follow, named.Select(pair => pair.Key));
    }

    /// <summary>
    /// 按程序名认窗口时，交来的记录里没有 process 这一项就报 Run.WindowRecordField：一条命中不是
    /// 窗口，拿它当窗口用要说清楚是哪一项取不到。
    /// </summary>
    [Fact]
    public async Task A_window_record_without_the_field_the_match_reads_is_refused()
    {
        var devices = new FakeDeviceLayer();
        devices.Vision.Matches.Add(new ImageMatch(0.99, new ScreenPoint(30, 40), new ScreenSize(10, 20)));

        var (result, _, _) = await RunAsync(
        [
            Step("vision.findImage", Pictures("ok.png"), Hit("match", "spot")),
            Step("window.activate", Fields.Reference("window", "spot"), Param("how", "processEquals")),
        ], devices);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal("Run.WindowRecordField", result.Key);
        Assert.DoesNotContain(devices.Calls, call => call.StartsWith("activateWindow",
            StringComparison.Ordinal));
    }

    /// <summary>
    /// Each window is one record holding what it is and where it sits, so the second title, the
    /// second program and the second rectangle are the same window without anything being counted.
    /// The program and the class are in it because the step says to read them.
    /// </summary>
    [Fact]
    public async Task Listing_windows_can_say_what_each_one_is_and_where_it_sits()
    {
        var devices = new FakeDeviceLayer();
        devices.Window.Open.Add(new WindowInfo(1, "Notepad", new ScreenPoint(10, 20),
            new ScreenSize(300, 200), false, false));
        devices.Window.Open.Add(new WindowInfo(2, "Calculator", new ScreenPoint(400, 30),
            new ScreenSize(240, 320), false, false));
        devices.Window.Traits[1] = ("notepad", "Notepad");
        devices.Window.Traits[2] = ("calc", "CalcFrame");

        var (result, _, store) = await RunAsync(
        [
            Step("window.list", Param("details", "true"), Returns("windows=listed")),
        ], devices);

        Assert.True(result.Succeeded, result.Key);
        var listed = store.Local.Values["listed"].AsList();
        Assert.Equal(["Notepad", "Calculator"], Titles(store, "listed"));
        Assert.Equal(["notepad", "calc"],
            listed.Select(window => window.Field("process")!.AsText()));
        Assert.Equal(["Notepad", "CalcFrame"],
            listed.Select(window => window.Field("class")!.AsText()));
        Assert.Equal([1, 2], listed.Select(window => window.Field("handle")!.AsNumber()));
        Assert.Equal(["10,20,300,200", "400,30,240,320"],
            listed.Select(window => window.Field("box")!.AsText()));
    }

    /// <summary>
    /// The program a window belongs to is a look at the machine per window, so a step that keeps
    /// nothing from the listing pays for none of them.
    /// </summary>
    [Fact]
    public async Task Listing_windows_looks_nothing_up_that_the_step_does_not_keep()
    {
        var devices = new FakeDeviceLayer();
        devices.Window.Open.Add(new WindowInfo(1, "Notepad", new ScreenPoint(), new ScreenSize(), false, false));
        devices.Window.Traits[1] = ("notepad", "Notepad");

        await RunAsync([Step("window.list")], devices);

        // The windows are still read — a step that keeps nothing still ran — but nothing about the
        // ones found was looked up.
        Assert.Contains(devices.Calls, call => call.StartsWith("filterWindows", StringComparison.Ordinal));

        Assert.DoesNotContain(devices.Calls,
            call => call.StartsWith("processOf", StringComparison.Ordinal));
        Assert.DoesNotContain(devices.Calls,
            call => call.StartsWith("classOf", StringComparison.Ordinal));
    }

    /// <summary>
    /// A title is there for the reading, but a program name and a window class are a look at the
    /// machine each, so a listing that was not told to read them leaves those two parts of the
    /// record empty and asks nothing: telling windows apart by title need not be paid for twice.
    /// </summary>
    [Fact]
    public async Task A_listing_reads_no_program_or_class_until_it_is_told_to()
    {
        var devices = new FakeDeviceLayer();
        devices.Window.Open.Add(new WindowInfo(1, "Notepad", new ScreenPoint(), new ScreenSize(), false, false));
        devices.Window.Open.Add(new WindowInfo(2, "Calculator", new ScreenPoint(), new ScreenSize(), false, false));
        devices.Window.Traits[1] = ("notepad", "Notepad");
        devices.Window.Traits[2] = ("calc", "CalcFrame");

        var (result, _, store) = await RunAsync(
            [Step("window.list", Returns("windows=listed"))], devices);

        Assert.True(result.Succeeded, result.Key);
        Assert.DoesNotContain(devices.Calls,
            call => call.StartsWith("processOf", StringComparison.Ordinal));
        Assert.DoesNotContain(devices.Calls,
            call => call.StartsWith("classOf", StringComparison.Ordinal));

        // Empty rather than missing: one window is still one record of the same shape.
        var listed = store.Local.Values["listed"].AsList();
        Assert.Equal(["Notepad", "Calculator"],
            listed.Select(window => window.Field("title")!.AsText()));
        Assert.All(listed, window => Assert.Equal(string.Empty, window.Field("process")!.AsText()));
        Assert.All(listed, window => Assert.Equal(string.Empty, window.Field("class")!.AsText()));
    }

    /// <summary>
    /// A window has a program and a kind as well as a place, and the part of it inside its border is
    /// where a search anchored to it looks. All of it is handed over in the one record, so a step
    /// that wants one of them is not made to read a line of punctuation to find it.
    /// </summary>
    [Fact]
    public async Task Window_information_says_what_the_window_is_as_well_as_where_it_is()
    {
        var devices = new FakeDeviceLayer();
        devices.Window.Open.Add(new WindowInfo(42, "Notepad - notes.txt", new ScreenPoint(10, 20),
            new ScreenSize(300, 200), false, true));
        devices.Window.Traits[42] = ("notepad", "Notepad");
        devices.Window.ClientOrigins[42] = new ScreenPoint(14, 51);
        devices.Window.ClientSizes[42] = new ScreenSize(292, 160);

        var (result, _, store) = await RunAsync(
        [
            Step("window.info", Param("window", "Notepad"),
                Returns("window=win")),
        ], devices);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal("notepad", Field(store, "win", "process").AsText());
        Assert.Equal("Notepad", Field(store, "win", "class").AsText());
        Assert.Equal(42, Field(store, "win", "handle").AsNumber());
        Assert.False(Field(store, "win", "minimized").AsBool());
        Assert.True(Field(store, "win", "maximized").AsBool());
        Assert.Equal("14,51,292,160", Field(store, "win", "clientBox").AsText());
    }

    /// <summary>
    /// A listing is narrowed down the way every other window action names its window: the text, the
    /// part of a window it is held against, and how. One rule in one place, so the same text picks
    /// out the same windows whether the step acts on one of them or lists them all.
    /// </summary>
    [Fact]
    public async Task Listing_windows_can_be_narrowed_down()
    {
        var devices = new FakeDeviceLayer();
        devices.Window.Open.Add(new WindowInfo(1, "Notepad", new ScreenPoint(), new ScreenSize(), false, false));
        devices.Window.Open.Add(new WindowInfo(2, "Calculator", new ScreenPoint(), new ScreenSize(), false, false));
        devices.Window.Open.Add(new WindowInfo(3, "Notepad - notes.txt",
            new ScreenPoint(), new ScreenSize(), false, false));
        devices.Window.Traits[2] = ("calc-app", "CalcFrame");

        var (_, _, store) = await RunAsync(
        [
            Step("window.list", Param("window", "notepad"), Returns("windows=byTitle")),
            Step("window.list", Param("window", "calc"), Param("how", "process"),
                Param("details", "true"), Returns("windows=byProcess")),
            Step("window.list", Param("window", "CalcFrame"), Param("how", "class"),
                Param("details", "true"), Returns("windows=byClass")),
            Step("window.list", Param("window", @"^Note.*notes\.txt$"), Param("how", "titleRegex"),
                Returns("windows=byPattern")),
        ], devices);

        Assert.Equal(["Notepad", "Notepad - notes.txt"], Titles(store, "byTitle"));
        Assert.Equal(["Calculator"], Titles(store, "byProcess"));
        Assert.Equal(["Calculator"], Titles(store, "byClass"));
        Assert.Equal(["Notepad - notes.txt"], Titles(store, "byPattern"));

        // One window is one record, so the title, the handle and the rectangle all come out of one
        // list rather than five that have to be lined up by counting, and the program and the class
        // sit in the same record when the step asks for them.
        var calculator = store.Local.Values["byProcess"].AsList()[0];
        Assert.Equal("Calculator", calculator.Field("title")!.AsText());
        Assert.Equal("calc-app", calculator.Field("process")!.AsText());
        Assert.Equal("CalcFrame", calculator.Field("class")!.AsText());
        Assert.Equal(2, calculator.Field("handle")!.AsNumber());
        Assert.Equal("0,0,0,0", calculator.Field("box")!.AsText());
    }

    /// <summary>
    /// The text a listing is narrowed down by may be a pattern as well, and one the machine cannot
    /// read is a failed step rather than a stack trace — the same words the other window actions
    /// report it with.
    /// </summary>
    [Fact]
    public async Task A_listing_whose_pattern_will_not_read_fails_the_step()
    {
        var devices = new FakeDeviceLayer();
        devices.Window.Open.Add(new WindowInfo(1, "Notepad", new ScreenPoint(), new ScreenSize(), false, false));

        var (result, _, _) = await RunAsync(
            [Step("window.list", Param("window", "notes("), Param("how", "titleRegex"))], devices);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal("Run.BadPattern", result.Key);
    }
}
