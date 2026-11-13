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

// Searching for pictures, which one is taken when several match, and where it was found.
public partial class DeviceActionTests
{
    /// <summary>
    /// Shuffled, a step that has to pick one of several does not keep picking the same one; whatever
    /// it picks is still one of the places the picture was found.
    /// </summary>
    [Fact]
    public async Task A_shuffled_search_hands_back_one_of_the_places_it_found()
    {
        var devices = new FakeDeviceLayer();
        devices.Vision.Matches.Add(new ImageMatch(0.99, new ScreenPoint(10, 10), new ScreenSize(1, 1)));
        devices.Vision.Matches.Add(new ImageMatch(0.99, new ScreenPoint(20, 20), new ScreenSize(1, 1)));
        devices.Vision.Matches.Add(new ImageMatch(0.99, new ScreenPoint(30, 30), new ScreenSize(1, 1)));

        var (_, _, store) = await RunAsync(
        [
            Step("vision.findImage", Pictures("ok.png"), Param("orderBy", "random"),
                Param("matchIndex", "3"), Hit("match", "where")),
        ], devices);

        Assert.Contains(Centre(store, "where"),
            new[] { "10,10", "20,20", "30,30" });
    }

    [Fact]
    public async Task A_search_can_watch_two_regions_at_once()
    {
        var devices = new FakeDeviceLayer();
        devices.Vision.Matches.Add(new ImageMatch(0.99, new ScreenPoint(2, 3), new ScreenSize(1, 1)));

        var (_, _, store) = await RunAsync(
        [
            Step("vision.findImage", Pictures("ok.png"),
                Region((0, 0, 10, 10), (100, 100, 10, 10)),
                Param("matchIndex", "2"), Hit("match", "where")),
        ], devices);

        Assert.Contains("capture 0 0 10 10", devices.Calls);
        Assert.Contains("capture 100 100 10 10", devices.Calls);
        Assert.Equal("102,103", Centre(store, "where"));
    }

    /// <summary>
    /// One window to look through, standing in for a game the macro counts its pixels from.
    /// </summary>
    private static FakeDeviceLayer WithWindow(long handle = 4, string title = "Game",
        int x = 100, int y = 50, int width = 400, int height = 300,
        int clientX = 108, int clientY = 81)
    {
        var devices = new FakeDeviceLayer();
        devices.Window.Open.Add(new WindowInfo(handle, title, new ScreenPoint(x, y),
            new ScreenSize(width, height), false, false));
        devices.Window.ClientOrigins[handle] = new ScreenPoint(clientX, clientY);
        return devices;
    }

    /// <summary>
    /// What the fields of a step come to as one request for a picture: which rectangle, out of the
    /// window its coordinates are counted from, and by the way it named. Everything the steps do
    /// with the screen goes through this one call, so it is where the fields have to be right.
    /// </summary>
    [Fact]
    public async Task A_picture_is_asked_for_from_the_window_the_step_counts_from()
    {
        var devices = WithWindow();

        await RunAsync(
        [
            Capture(10, 20, 30, 40, Param("anchorMode", "window"),
                Param("window", "Game"), Parts("image", "shot")),
        ], devices);

        var asked = Assert.Single(devices.Screen.Captures);
        Assert.Equal((118, 101, 30, 40), (asked.X, asked.Y, asked.Width, asked.Height));
        Assert.Equal(4, asked.Window);
        Assert.Equal(CaptureWay.Auto, asked.Method);
    }

    /// <summary>
    /// How the pixels come off this machine is an answer about the machine rather than a field of a
    /// step, so the way a step's picture is taken is whatever the machine was set up with: a step
    /// counting its coordinates from a window is read the window way, and a step reading the desktop
    /// the screen way.
    /// </summary>
    [Fact]
    public async Task The_way_of_taking_the_picture_is_the_machines_own()
    {
        var devices = WithWindow();
        devices.Screen.Ways.Window = CaptureWay.PrintWindow;
        devices.Screen.Ways.Screen = CaptureWay.DesktopDuplication;

        await RunAsync(
        [
            Capture(1, 1, 2, 2, Param("anchorMode", "window"), Param("window", "Game"),
                Parts("image", "window", "method")),
            Capture(1, 1, 2, 2, Parts("image", "screen", "method")),
        ], devices);

        Assert.Equal(CaptureWay.PrintWindow, devices.Screen.Captures[0].Method);
        Assert.Equal(CaptureWay.DesktopDuplication, devices.Screen.Captures[1].Method);
    }

    /// <summary>
    /// A rectangle counted from a window's own corner is turned into the screen pixels the device
    /// is asked for, and what a step that named no means gets back says where the picture came
    /// from: the pixels of the desktop when the source is the desktop, and the window itself when
    /// the source is a window.
    /// </summary>
    [Fact]
    public async Task A_rectangle_counted_from_a_window_is_asked_for_where_the_window_is()
    {
        var devices = WithWindow(x: 100, y: 50);

        var (_, _, store) = await RunAsync(
        [
            Capture(7, 9, 10, 10, Param("anchorMode", "window"),
                Param("window", "Game"), Parts("image", "fromWindow", "method")),
            Capture(7, 9, 10, 10, Parts("image", "fromScreen", "method")),
        ], devices);

        Assert.Equal((115, 90, 10, 10),
            (devices.Screen.Captures[0].X, devices.Screen.Captures[0].Y, devices.Screen.Captures[0].Width,
                devices.Screen.Captures[0].Height));
        Assert.Equal(4, devices.Screen.Captures[0].Window);
        Assert.Equal(0, devices.Screen.Captures[1].Window);

        Assert.Equal("graphicsCapture", store.Local.Values["fromWindow.method"].AsText());
        Assert.Equal("gdi", store.Local.Values["fromScreen.method"].AsText());
    }

    /// <summary>
    /// A way this machine cannot do is reported with the key the interface knows, rather than the
    /// step being answered by some other way behind its back: a step whose picture cannot be taken
    /// the way the machine was set up to take it fails, and says why.
    /// </summary>
    [Fact]
    public async Task A_way_the_machine_cannot_do_is_reported_rather_than_swapped_for_another()
    {
        var devices = WithWindow();
        devices.Screen.Ways.Window = CaptureWay.GraphicsCapture;
        devices.Refusal = new DeviceActionException("Run.NoGraphicsCapture", "this machine has none");

        var (result, _, _) = await RunAsync(
        [
            Capture(0, 0, 10, 10, Param("anchorMode", "window"), Param("window", "Game"),
                Parts("image", "shot")),
        ], devices);

        Assert.Equal("Run.NoGraphicsCapture", result.Key);
        Assert.Equal("this machine has none", result.Detail);
    }

    /// <summary>
    /// A step with nowhere written down looks at the whole of what it is about: the window its
    /// coordinates are counted from when it names one, and the desktop when it does not.
    /// </summary>
    [Fact]
    public async Task A_step_with_nowhere_to_look_reads_the_whole_window_it_counts_from()
    {
        var devices = WithWindow(handle: 9);
        devices.Vision.Matches.Add(new ImageMatch(0.99, new ScreenPoint(1, 1), new ScreenSize(1, 1)));

        await RunAsync(
        [
            Step("vision.findImage", Pictures("ok.png"), Param("confidence", "90"),
                Param("anchorMode", "window"), Param("window", "Game"),
                Hit("match", "match")),
        ], devices);

        var asked = Assert.Single(devices.Screen.Captures);
        Assert.Equal(9, asked.Window);
        Assert.Equal((108, 81, 400, 300), (asked.X, asked.Y, asked.Width, asked.Height));
    }

    /// <summary>
    /// A step that copies or reads the screen and gives no rectangle at all reads the whole screen:
    /// a number nobody filled in is not a zero, which would have left it looking at one pixel in the
    /// corner and saying it had done what it was asked.
    /// </summary>
    [Fact]
    public async Task A_capture_that_names_no_rectangle_reads_the_whole_screen()
    {
        var devices = new FakeDeviceLayer();

        await RunAsync([CaptureWholeScreen(Parts("image", "shot"))], devices);

        var asked = Assert.Single(devices.Screen.Captures);
        Assert.Equal((0, 0, 100, 50), (asked.X, asked.Y, asked.Width, asked.Height));
    }

    [Fact]
    public async Task A_reading_that_names_no_rectangle_reads_the_whole_screen()
    {
        var devices = new FakeDeviceLayer();

        await RunAsync([RecognizeWholeScreen(Returns("text=words"))], devices);

        var asked = Assert.Single(devices.Screen.Captures);
        Assert.Equal((0, 0, 100, 50), (asked.X, asked.Y, asked.Width, asked.Height));
    }

    /// <summary>
    /// What a step says is read; what it leaves out reaches the edge of the screen. A step that named
    /// only how wide to read starts at the corner and takes the whole height, rather than one row of
    /// pixels; one that named where to start and nothing else takes everything from there on.
    /// </summary>
    [Fact]
    public async Task A_number_a_step_leaves_out_reaches_the_edge_of_the_screen()
    {
        var devices = new FakeDeviceLayer();

        await RunAsync(
        [
            Step("vision.capture", Param("width", "40"), Parts("image", "shot")),
            Step("vision.capture", Param("x", "20"), Param("y", "10"), Parts("image", "shot")),
        ], devices);

        Assert.Equal(2, devices.Screen.Captures.Count);
        Assert.Equal((0, 0, 40, 50), (devices.Screen.Captures[0].X, devices.Screen.Captures[0].Y,
            devices.Screen.Captures[0].Width, devices.Screen.Captures[0].Height));
        Assert.Equal((20, 10, 80, 40), (devices.Screen.Captures[1].X, devices.Screen.Captures[1].Y,
            devices.Screen.Captures[1].Width, devices.Screen.Captures[1].Height));
    }

    /// <summary>
    /// A rectangle read from a display left of the primary one starts at a negative coordinate. The
    /// corner reaches the capture device as written, so "read what is on that display" reads the
    /// pixels that are there rather than the corner of the screen nearest to it.
    /// </summary>
    [Fact]
    public async Task A_rectangle_on_a_display_left_of_the_primary_one_stays_negative()
    {
        var devices = new FakeDeviceLayer();

        await RunAsync(
        [
            Step("vision.capture", Param("x", "-30"), Param("y", "-10"),
                Param("width", "40"), Param("height", "20"), Parts("image", "shot")),
        ], devices);

        var shot = Assert.Single(devices.Screen.Captures);
        Assert.Equal((-30, -10, 40, 20), (shot.X, shot.Y, shot.Width, shot.Height));
    }

    /// <summary>
    /// 截图留下的「位置」除了左上角与大小，还带着这块画面的中心：下一步"移到刚截的这块正中"因此
    /// 写两格字段就够，不必自己拿 left 与 width 算一半。宽高是奇数时中心按整数除法取整，与命中
    /// 那条路（MacroRunner.Middle）同一个算法，两处不会差一个像素。
    /// </summary>
    [Fact]
    public async Task The_place_a_picture_was_taken_at_carries_its_middle()
    {
        var devices = new FakeDeviceLayer();

        var (_, _, store) = await RunAsync(
        [
            Step("vision.capture", Param("x", "10"), Param("y", "5"), Param("width", "61"),
                Param("height", "25"), Returns("place=spot")),
        ], devices);

        Assert.Equal("40,17", Centre(store, "spot"));
        Assert.Equal("10,5,61,25", Field(store, "spot", "box").AsText());
    }

    /// <summary>
    /// A result says which window the picture came out of and how it was taken, because a step that
    /// found nothing is answered by asking whether it was even looking at the right window.
    /// </summary>
    [Fact]
    public async Task A_result_says_which_window_it_came_from()
    {
        var devices = WithWindow();
        devices.Vision.Matches.Add(new ImageMatch(0.99, new ScreenPoint(3, 4), new ScreenSize(2, 2)));

        var (_, _, store) = await RunAsync(
        [
            Step("vision.findImage", Pictures("ok.png"), Param("confidence", "90"),
                Param("anchorMode", "window"), Param("window", "Game"),
                Returns("match=match", "window=match.window", "method=match.method")),
        ], devices);

        Assert.Equal("Game", store.Local.Values["match.window"].AsText());
        Assert.Equal("graphicsCapture", store.Local.Values["match.method"].AsText());
    }

    /// <summary>
    /// A step that found nothing empties the window and the way along with the rest of the match, so
    /// a step inside a loop never reads what the pass before it was looking at.
    /// </summary>
    [Fact]
    public async Task A_step_that_found_nothing_empties_the_window_it_read()
    {
        var devices = WithWindow();
        devices.Vision.Match = null;

        var (_, _, store) = await RunAsync(
        [
            Step("vision.findImage", Pictures("ok.png"), Param("confidence", "90"),
                Param("anchorMode", "window"), Param("window", "Game"),
                Returns("match=match", "window=match.window", "method=match.method")),
        ], devices);

        Assert.Equal(string.Empty, store.Local.Values["match.window"].AsText());
        Assert.Equal(string.Empty, store.Local.Values["match.method"].AsText());
    }

    /// <summary>
    /// A pixel counted from a window comes out of a picture of that window, so a window another
    /// window covers still gives its own colour; a pixel counted from the screen is read straight
    /// off the screen, which is a fraction of the cost.
    /// </summary>
    [Fact]
    public async Task A_pixel_counted_from_a_window_comes_out_of_a_picture_of_it()
    {
        var devices = WithWindow(clientX: 200, clientY: 100);

        await RunAsync(
        [
            Step("vision.getPixel", Param("x", "5"), Param("y", "6"),
                Param("anchorMode", "window"), Param("window", "Game"),
                Returns("color=color")),
        ], devices);

        Assert.DoesNotContain(devices.Calls,
            call => call.StartsWith("pixel ", StringComparison.Ordinal));
        var asked = Assert.Single(devices.Screen.Captures);
        Assert.Equal((205, 106, 1, 1), (asked.X, asked.Y, asked.Width, asked.Height));
        Assert.Equal(4, asked.Window);
    }

    [Fact]
    public async Task A_pixel_counted_from_the_screen_is_read_off_the_screen()
    {
        var devices = new FakeDeviceLayer();

        await RunAsync(
        [
            Step("vision.getPixel", Param("x", "5"), Param("y", "6"),
                Returns("color=color")),
        ], devices);

        Assert.Contains("pixel 5 6", devices.Calls);
        Assert.Empty(devices.Screen.Captures);
    }

    /// <summary>
    /// A step that runs while the settings window has reuse on is handed the picture an earlier
    /// step took of the same screen, so looking at the same place again and again reads it once.
    /// The first such step reads the whole screen, which is what makes the readings after it free.
    /// </summary>
    [Fact]
    public async Task A_step_may_be_given_the_picture_an_earlier_step_took()
    {
        var devices = new FakeDeviceLayer();
        devices.Frames.CacheMilliseconds = 200;
        devices.Vision.Matches.Add(new ImageMatch(0.99, new ScreenPoint(2, 3), new ScreenSize(1, 1)));

        var (_, _, store) = await RunAsync(
        [
            Capture(10, 10, 20, 20, Parts("image", "shot", "place")),
            Step("vision.findImage", Pictures("ok.png"), Region((30, 5, 10, 10)),
                Param("confidence", "90"), Hit("match", "match")),
        ], devices);

        // One reading of the screen between them: the whole of it, because a picture that is to be
        // used again has to hold whatever the next step asks for.
        var asked = Assert.Single(devices.Screen.Captures);
        Assert.Equal((0, 0, 100, 50), (asked.X, asked.Y, asked.Width, asked.Height));

        // Both steps were answered out of that one picture, cut where each of them asked.
        Assert.Equal("10,10", Corner(store, "shot.place"));
        Assert.Equal("32,8", Centre(store, "match"));
    }

    /// <summary>
    /// With reuse off, every step reads the screen for itself however recently an earlier step read
    /// it: the setting is a trade the person makes for the whole run rather than one a step makes.
    /// </summary>
    [Fact]
    public async Task Every_step_reads_the_screen_for_itself_when_reuse_is_off()
    {
        var devices = new FakeDeviceLayer();
        devices.Vision.Matches.Add(new ImageMatch(0.99, new ScreenPoint(2, 3), new ScreenSize(1, 1)));

        await RunAsync(
        [
            Capture(10, 10, 20, 20, Parts("image", "shot")),
            Step("vision.findImage", Pictures("ok.png"), Region((30, 5, 10, 10)),
                Param("confidence", "90"), Hit("match", "match")),
        ], devices);

        Assert.Equal(2, devices.Screen.Captures.Count);
        Assert.Equal((10, 10, 20, 20), (devices.Screen.Captures[0].X, devices.Screen.Captures[0].Y,
            devices.Screen.Captures[0].Width, devices.Screen.Captures[0].Height));
        Assert.Equal((30, 5, 10, 10), (devices.Screen.Captures[1].X, devices.Screen.Captures[1].Y,
            devices.Screen.Captures[1].Width, devices.Screen.Captures[1].Height));
    }

    /// <summary>
    /// A picture that has been sitting there longer than the cache allows is taken again, so a macro
    /// waiting for something to change still sees it change.
    /// </summary>
    [Fact]
    public async Task A_picture_older_than_the_cache_allows_is_taken_again()
    {
        var devices = new FakeDeviceLayer();
        devices.Frames.CacheMilliseconds = 20;
        devices.Vision.Matches.Add(new ImageMatch(0.99, new ScreenPoint(2, 3), new ScreenSize(1, 1)));

        await RunAsync(
        [
            Capture(10, 10, 20, 20, Parts("image", "shot")),
            Step("control.delay", Param("ms", "60")),
            Step("vision.findImage", Pictures("ok.png"), Region((30, 5, 10, 10)),
                Param("confidence", "90"), Hit("match", "match")),
        ], devices);

        Assert.Equal(2, devices.Screen.Captures.Count);
    }

    /// <summary>
    /// Two looks are held apart by the run's frame gap, which is what a step inside a loop runs into:
    /// it reads the same place on every pass, and the gap is what stops the run asking the machine
    /// for more pictures than a second of looking can tell anything about.
    /// </summary>
    [Fact]
    public async Task A_second_look_waits_out_the_runs_frame_gap()
    {
        var devices = new FakeDeviceLayer();
        devices.Frames.MaxFramesPerSecond = 5;
        devices.Vision.Matches.Add(new ImageMatch(0.99, new ScreenPoint(1, 1), new ScreenSize(1, 1)));

        var started = Stopwatch.GetTimestamp();
        await RunAsync(
        [
            Step("control.repeat", Param("times", "3"), Body("body",
                Step("vision.findImage", Pictures("ok.png"), Param("confidence", "90"),
                    Hit("match", "match")))),
        ], devices);

        var took = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        Assert.True(took >= 390, $"three looks 200 ms apart take at least 400 ms, took {took:0}");
    }

    /// <summary>
    /// A step that waits for something looks again on that same beat: a wait of three looks a
    /// hundred milliseconds apart cannot come back before two hundred have gone.
    /// </summary>
    [Fact]
    public async Task A_wait_looks_again_on_the_runs_frame_gap()
    {
        var devices = new FakeDeviceLayer();
        devices.Frames.MaxFramesPerSecond = 10;
        devices.Vision.Loaded = new ImageFrame(2, 2, new byte[16]);
        devices.Vision.Match = new ImageMatch(0.95, new ScreenPoint(1, 1), new ScreenSize(2, 2));
        devices.Vision.MatchAfter = 3;

        var started = Stopwatch.GetTimestamp();
        var (result, _, store) = await RunAsync(
        [
            Within(5000, Step("vision.waitImage", Pictures("anything.png"), Param("confidence", "90"),
                Hit("match", "spot"))),
        ], devices);

        var took = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        Assert.True(result.Succeeded);
        Assert.Equal("2,2", Centre(store, "spot"));
        Assert.Equal(3, devices.Vision.Searches);
        Assert.True(took >= 190, $"three looks 100 ms apart take at least 200 ms, took {took:0}");
    }

    /// <summary>
    /// A wait that says nothing about how often to look looks once a screen refresh, which is what
    /// makes the default follow the display the macro runs on rather than the machine it was
    /// written on: the same wait looks four times as often on a 240 Hz screen as on a 60 Hz one,
    /// and either way it is looking on a beat rather than as fast as the machine can manage.
    /// </summary>
    [Fact]
    public async Task A_wait_without_a_gap_of_its_own_looks_on_the_screens_beat()
    {
        // A 240 Hz screen: one refresh every four milliseconds.
        var devices = new FakeDeviceLayer();
        devices.Screen.Pixel = new PixelColor(0xFF, 0xFF, 0xFF);
        devices.Screen.RefreshMilliseconds = 4;

        var (result, _, _) = await RunAsync(
        [
            Within(160, Step("vision.waitColor", Param("x", "0"), Param("y", "0"),
                Param("color", "#000000"))),
        ], devices);

        Assert.Equal("Run.WaitColorTimeout", result.Key);
        var looked = devices.Calls.Count(call => call == "pixel 0 0");

        // The far end is the refresh's own doing: 160 ms of looking on a 4 ms beat is forty looks at
        // most. How few there were is the machine's business — a round that was held up reads the
        // same place fewer times in the time it was given — so the near end only asks for more than
        // one look, which is what tells a beat apart from looking once and giving up.
        Assert.InRange(looked, 2, 40);
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
                    Fields.Computed("expression", "count > 3 and contains(name, \"o\")"))),
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
                    Fields.Computed("expression", "count > 3 or count == 0"))),
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
                Fields.Computed("condition", TestConditions.Expression(
                    Step("condition.expression", Fields.Computed("expression", "count +")))),
                Body("then", Step("control.setVariable", Param("name", "hit"), Param("value", "yes")))),
        ], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.BadExpression", result.Key);
    }
}
