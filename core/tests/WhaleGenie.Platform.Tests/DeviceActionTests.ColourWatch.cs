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

// Watching a colour or a still picture until it holds, and what happens when it never does.
public partial class DeviceActionTests
{
    [Fact]
    public async Task A_colour_search_looks_once_and_leaves_the_colour_to_the_wait()
    {
        var devices = new FakeDeviceLayer();
        devices.Screen.Display = Picture("#000000");

        var (result, _, store) = await RunAsync(
        [
            Step("vision.findColor", Param("color", "#FF0000"),
                Hit("match", "spot")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Equal(string.Empty, store.Local.Values["spot"].AsText());

        // One look, however long the step is allowed: a colour search answers "is the colour there
        // now", and waiting for it is what vision.waitColor is for.
        Assert.Equal(1, devices.Calls.Count(call => call.StartsWith("capture", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Watching_one_pixel_goes_on_when_it_shows_the_colour()
    {
        var devices = new FakeDeviceLayer();
        devices.Screen.Pixel = new PixelColor(0xFF, 0x00, 0x00);
        var host = new SilentRunHost();

        var result = await new MacroRunner(new VariableStore(), host, devices).RunAsync(
            [Step("vision.waitColor", Param("x", "5"), Param("y", "6"), Param("color", "#FF0000"),
                Param("tolerance", "5"))]);

        Assert.True(result.Succeeded);
        Assert.Contains(host.Entries, entry => entry.Key == "Log.SawColor");
    }

    [Fact]
    public async Task Watching_one_pixel_that_stays_the_wrong_colour_gives_up()
    {
        var devices = new FakeDeviceLayer();
        devices.Screen.Pixel = new PixelColor(0x00, 0x00, 0x00);

        var (result, _, _) = await RunAsync(
            [Within(20, Step("vision.waitColor", Param("x", "5"), Param("y", "6"),
                Param("color", "#FFFFFF"), Param("tolerance", "1")))],
            devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.WaitColorTimeout", result.Key);
    }

    [Fact]
    public async Task The_second_place_a_colour_shows_is_the_one_a_step_can_take()
    {
        var devices = new FakeDeviceLayer();
        devices.Screen.Display = Picture("#FF0000,#000000,#FF0000");

        var (_, _, store) = await RunAsync(
        [
            Step("vision.findColor", Param("color", "#FF0000"), Param("tolerance", "1"),
                Param("matchIndex", "2"), Region((0, 0, 3, 1)),
                Hit("match", "spot")),
        ], devices);

        Assert.Equal("2,0", Centre(store, "spot"));
    }

    [Fact]
    public async Task Every_place_a_colour_shows_can_be_recorded()
    {
        var devices = new FakeDeviceLayer();
        devices.Screen.Display = Picture("#FF0000,#000000,#FF0000");

        var (_, _, store) = await RunAsync(
        [
            Step("vision.findColor", Param("color", "#FF0000"), Param("tolerance", "1"),
                Region((0, 0, 3, 1)), Hit("match", "spot", every: true)),
        ], devices);

        Assert.Equal(2, store.Local.Values["spot.every"].AsList().Count);
        Assert.Equal("0,0, 2,0", Centres(store, "spot"));
    }

    [Fact]
    public async Task Waiting_for_a_still_picture_goes_on_once_the_screen_stops_moving()
    {
        var devices = new FakeDeviceLayer();
        devices.Screen.Display = Picture("#000000");
        var frames = new Queue<ImageFrame>([Picture("#FFFFFF"), Picture("#00FF00")]);
        devices.Screen.Next = () => frames.Count > 1 ? frames.Dequeue() : frames.Peek();

        var host = new SilentRunHost();
        var result = await new MacroRunner(new VariableStore(), host, devices).RunAsync(
            [Within(5000, Step("vision.waitStable", Param("quietMs", "0")))]);

        Assert.True(result.Succeeded);
        Assert.Contains(host.Entries, entry => entry.Key == "Log.FrameStill");

        // Four looks: the picture the wait opened on, the two the screen moved through, and the one
        // that shows it has stopped. The quiet stretch was nothing, so there was nothing left to
        // wait for rather than a fixed pause.
        Assert.Equal(4, devices.Calls.Count(call => call.StartsWith("capture", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_screen_that_never_holds_still_fails_the_step()
    {
        var devices = new FakeDeviceLayer();
        devices.Screen.Display = Picture("#000000");
        var moving = true;
        devices.Screen.Next = () =>
        {
            moving = !moving;
            return Picture(moving ? "#FF0000" : "#0000FF");
        };

        var (result, _, _) = await RunAsync(
        [
            Within(60, Step("vision.waitStable", Param("quietMs", "50"))),
        ], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.ScreenNotStable", result.Key);
    }

    /// <summary>
    /// A picture with a little noise on it, or one that fades slowly, is still a picture that is
    /// not being drawn any more: what counts as a change is the caller's tolerance, not any
    /// difference at all.
    /// </summary>
    [Fact]
    public async Task A_picture_that_only_drifts_a_little_is_still_a_picture()
    {
        var devices = new FakeDeviceLayer();
        devices.Screen.Display = Picture("#808080");
        var frames = new Queue<ImageFrame>([Picture("#838383")]);
        devices.Screen.Next = () => frames.Count > 0 ? frames.Dequeue() : Picture("#838383");

        var (result, _, _) = await RunAsync(
        [
            Within(5000, Step("vision.waitStable", Param("quietMs", "0"),
                Param("tolerance", "5"))),
        ], devices);

        Assert.True(result.Succeeded);
    }

    /// <summary>Hits are counted the way a person counts them on screen, not the order they came back in.</summary>
    [Fact]
    public async Task A_picture_can_be_taken_by_its_place_on_the_screen()
    {
        var devices = new FakeDeviceLayer();
        devices.Vision.Matches.Add(new ImageMatch(0.99, new ScreenPoint(30, 40), new ScreenSize(1, 1)));
        devices.Vision.Matches.Add(new ImageMatch(0.99, new ScreenPoint(10, 10), new ScreenSize(1, 1)));
        devices.Vision.Matches.Add(new ImageMatch(0.99, new ScreenPoint(50, 5), new ScreenSize(1, 1)));

        var (_, _, store) = await RunAsync(
        [
            Step("vision.findImage", Pictures("ok.png"), Param("matchIndex", "2"),
                Hit("match", "where", every: true)),
        ], devices);

        Assert.Equal("10,10", Centre(store, "where"));
        Assert.Equal(3, store.Local.Values["where.every"].AsList().Count);
        Assert.Equal("50,5, 10,10, 30,40", Centres(store, "where"));
    }

    /// <summary>
    /// A game drawn at a size the reference picture was not taken at is found by its features, so
    /// what the step asked for has to reach the device rather than being guessed at.
    /// </summary>
    [Fact]
    public async Task How_a_picture_is_looked_for_reaches_the_device()
    {
        var devices = new FakeDeviceLayer();
        devices.Vision.Match = new ImageMatch(0.97, new ScreenPoint(1, 2), new ScreenSize(3, 3));

        var (_, _, _) = await RunAsync(
        [
            Step("vision.findImage", Pictures("ok.png"), Param("confidence", "88"),
                Param("detectAlgorithm", "feature"),
                Rows("transparentColors", Cells.Of(("colour", "#00FF00"), ("tolerance", "4"))),
                Param("tolerance", "10"), Param("minFeatures", "12"),
                Hit("match", "where")),
        ], devices);

        var query = Assert.Single(devices.Vision.Queries);
        Assert.Equal(MatchAlgorithm.Feature, query.Algorithm);
        Assert.Equal(88d, query.ConfidencePercent);
        Assert.Equal(12, query.MinFeatures);
        Assert.Equal([new TransparentColour(new PixelColor(0x00, 0xFF, 0x00), 4)],
            query.Transparent);
        Assert.Equal(1, query.Limit);
    }

    /// <summary>
    /// One picker answers both "which way" and "comparing what", so each of its values has to arrive
    /// as itself: a step that says "pixel for pixel" is not a step that says "normalised". A step
    /// that says nothing is the usual one.
    /// </summary>
    [Theory]
    [InlineData("normed", MatchAlgorithm.Normed)]
    [InlineData("correlated", MatchAlgorithm.Correlated)]
    [InlineData("difference", MatchAlgorithm.Difference)]
    [InlineData("feature", MatchAlgorithm.Feature)]
    [InlineData("", MatchAlgorithm.Normed)]
    public async Task The_recognition_algorithm_reaches_the_device(string written,
        MatchAlgorithm expected)
    {
        var devices = new FakeDeviceLayer();
        devices.Vision.Match = new ImageMatch(0.97, new ScreenPoint(1, 2), new ScreenSize(3, 3));

        var (_, _, _) = await RunAsync(
        [
            written.Length == 0
                ? Step("vision.findImage", Pictures("ok.png"),
                    Hit("match", "where"))
                : Step("vision.findImage", Pictures("ok.png"),
                    Param("detectAlgorithm", written), Hit("match", "where")),
        ], devices);

        Assert.Equal(expected, Assert.Single(devices.Vision.Queries).Algorithm);
    }

    /// <summary>Left alone, a search is done the way every macro did it before the fields existed.</summary>
    [Fact]
    public async Task A_picture_search_left_alone_is_still_a_plain_matching_search()
    {
        var devices = new FakeDeviceLayer();
        devices.Vision.Match = new ImageMatch(0.97, new ScreenPoint(1, 2), new ScreenSize(3, 3));

        var (_, _, _) = await RunAsync(
            [Step("vision.findImage", Pictures("ok.png"), Hit("match", "where"))],
            devices);

        var query = Assert.Single(devices.Vision.Queries);
        Assert.Equal(MatchAlgorithm.Normed, query.Algorithm);
        Assert.Equal(90d, query.ConfidencePercent);
        Assert.Equal(6, query.MinFeatures);
        Assert.Null(query.Transparent);
    }

    /// <summary>
    /// A colour nobody can read used to be read as black, which masked pixels the step never meant.
    /// A picture search that says something else is refused, and the device is not asked at all.
    /// </summary>
    [Fact]
    public async Task A_transparent_colour_that_is_not_one_stops_the_step()
    {
        var devices = new FakeDeviceLayer();

        var (result, _, _) = await RunAsync(
        [
            Step("vision.findImage", Pictures("ok.png"),
                Rows("transparentColors", Cells.Of(("colour", "not a colour"))),
                Hit("match", "where")),
        ], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.BadPoint", result.Key);
        Assert.Empty(devices.Vision.Queries);
    }

    /// <summary>
    /// The surest place is not always the first one in reading order, so a step that wants the one
    /// that looks most like the picture asks for the weighted score: a picture search finds every
    /// box at the reference picture's own size, so that score is the likeness alone.
    /// </summary>
    [Fact]
    public async Task The_surest_picture_can_be_the_one_taken()
    {
        var devices = new FakeDeviceLayer();
        devices.Vision.Matches.Add(new ImageMatch(0.91, new ScreenPoint(50, 5), new ScreenSize(1, 1)));
        devices.Vision.Matches.Add(new ImageMatch(0.99, new ScreenPoint(30, 40), new ScreenSize(1, 1)));
        devices.Vision.Matches.Add(new ImageMatch(0.95, new ScreenPoint(10, 10), new ScreenSize(1, 1)));

        var (_, _, store) = await RunAsync(
        [
            Step("vision.findImage", Pictures("ok.png"), Param("orderBy", "combined"),
                Hit("match", "where", every: true)),
        ], devices);

        Assert.Equal("30,40", Centre(store, "where"));
        Assert.Equal("30,40, 10,10, 50,5", Centres(store, "where"));
    }

    /// <summary>
    /// Looked for by features a picture comes back at whatever size it was drawn at, so weighing the
    /// size alone is how a step says "the whole banner, not a piece of it": a share of 0 leaves the
    /// likeness out of the weighted score.
    /// </summary>
    [Fact]
    public async Task The_biggest_picture_can_be_the_one_taken()
    {
        var devices = new FakeDeviceLayer();
        devices.Vision.Matches.Add(new ImageMatch(0.99, new ScreenPoint(50, 5), new ScreenSize(4, 4)));
        devices.Vision.Matches.Add(new ImageMatch(0.91, new ScreenPoint(10, 10), new ScreenSize(120, 40)));
        devices.Vision.Matches.Add(new ImageMatch(0.95, new ScreenPoint(30, 70), new ScreenSize(40, 20)));

        var (_, _, store) = await RunAsync(
        [
            Step("vision.findImage", Pictures("ok.png"), Param("orderBy", "combined"),
                Param("scoreWeight", "0"), Hit("match", "where", every: true)),
        ], devices);

        Assert.Equal("70,30", Centre(store, "where"));
        Assert.Equal("70,30, 50,80, 52,7", Centres(store, "where"));
    }

    /// <summary>
    /// Two reference pictures the fake tells apart by their own first pixel, each answered the way
    /// the check says: how a step that lists several pictures is asked about them one by one.
    /// </summary>
    private static FakeDeviceLayer TwoPictures(ImageMatch? one, ImageMatch? two)
    {
        var devices = new FakeDeviceLayer();
        devices.Vision.Pictures["one.png"] = Picture("#FF0000");
        devices.Vision.Pictures["two.png"] = Picture("#00FF00");
        devices.Vision.PictureAnswers = (needle, _) => (needle[0, 0].R == 0xFF ? one : two) is { } hit
            ? [hit]
            : [];
        return devices;
    }

    /// <summary>
    /// Picture one at the left, picture two at the right: what a check reads to see which of them
    /// the step went with.
    /// </summary>
    private static ImageMatch Left => new(0.91, new ScreenPoint(5, 6), new ScreenSize(2, 2));

    private static ImageMatch Right => new(0.99, new ScreenPoint(40, 50), new ScreenSize(2, 2));

    /// <summary>
    /// A step that lists several pictures normally goes with the first that turns up, and the ones
    /// after it are not looked for at all: that is what a button drawn two ways asks for.
    /// </summary>
    [Fact]
    public async Task The_first_picture_that_turns_up_is_the_one_taken()
    {
        var devices = TwoPictures(Left, Right);

        var (result, _, store) = await RunAsync(
        [
            Step("vision.findImage", Pictures("one.png", "two.png"), Hit("match", "where")),
        ], devices);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal("6,7", Centre(store, "where"));
        Assert.Equal(1, devices.Vision.Searches);
    }

    /// <summary>
    /// Asked for the surest of them, every picture is looked at and the one whose closest match
    /// scored highest is the one the step goes with — the closer of two similar icons.
    /// </summary>
    [Fact]
    public async Task The_surest_of_the_pictures_is_the_one_taken()
    {
        var devices = TwoPictures(Left, Right);

        var (result, _, store) = await RunAsync(
        [
            Step("vision.findImage", Pictures("one.png", "two.png"), Param("imageSet", "best"),
                Hit("match", "where")),
        ], devices);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal("41,51", Centre(store, "where"));
        Assert.Equal(2, devices.Vision.Searches);
    }

    /// <summary>
    /// Asked for every one of them, a picture the screen does not show is what stops the step: it
    /// finds nothing rather than going with the one picture that is there.
    /// </summary>
    [Fact]
    public async Task Every_picture_has_to_turn_up()
    {
        var devices = TwoPictures(Left, null);

        var (result, _, store) = await RunAsync(
        [
            Step("vision.findImage", Pictures("one.png", "two.png"), Param("imageSet", "every"),
                Hit("match", "where")),
        ], devices);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal(string.Empty, store.Local.Values["where"].AsText());
    }

    /// <summary>
    /// With the whole set on the screen the step goes with the picture it listed first, and the
    /// "which one" that follows counts inside that picture as it always did.
    /// </summary>
    [Fact]
    public async Task Every_picture_there_goes_with_the_first_listed()
    {
        var devices = TwoPictures(Left, Right);

        var (result, _, store) = await RunAsync(
        [
            Step("vision.findImage", Pictures("one.png", "two.png"), Param("imageSet", "every"),
                Hit("match", "where")),
        ], devices);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal("6,7", Centre(store, "where"));
        Assert.Equal(2, devices.Vision.Searches);
    }

    /// <summary>
    /// A colour search looked at by score keeps the pixel closest to the colour asked for, even when
    /// that pixel is not the first one the area is walked over.
    /// </summary>
    [Fact]
    public async Task The_pixel_closest_to_the_colour_is_the_one_taken()
    {
        var devices = new FakeDeviceLayer();
        devices.Screen.Display = Picture("#F00000,#FF0000,#FB0000");

        var (_, _, store) = await RunAsync(
        [
            Step("vision.findColor", Param("color", "#FF0000"), Param("tolerance", "10"),
                Param("orderBy", "score"), Region((0, 0, 3, 1)),
                Hit("match", "spot")),
        ], devices);

        Assert.Equal("1,0", Centre(store, "spot"));
    }

    /// <summary>Read off the screen, the surest reading is the one a step can act on.</summary>
    [Fact]
    public async Task The_surest_reading_can_be_the_one_taken()
    {
        var devices = new FakeDeviceLayer();
        devices.Ocr.Spans = [
                new TextSpan("Continue", new ScreenPoint(10, 10), new ScreenSize(30, 8), 12),
                new TextSpan("Continue", new ScreenPoint(10, 60), new ScreenSize(30, 8), 38),
            ];

        var (_, _, store) = await RunAsync(
        [
            Step("ocr.findText", Param("queryText", "Continue"), Param("matchMode", "equals"),
                Param("orderBy", "score"), Hit("match", "where")),
        ], devices);

        Assert.Equal("25,64", Centre(store, "where"));
    }

    /// <summary>
    /// The model hands a line and the words on it back separately, so reading a whole line rather
    /// than one word off it is the biggest box as well.
    /// </summary>
    [Fact]
    public async Task The_widest_reading_can_be_the_one_taken()
    {
        var devices = new FakeDeviceLayer();
        devices.Ocr.Spans = [
                new TextSpan("Ready", new ScreenPoint(10, 10), new ScreenSize(30, 8), 30),
                new TextSpan("Ready to go", new ScreenPoint(100, 90), new ScreenSize(80, 8), 30),
            ];

        var (_, _, store) = await RunAsync(
        [
            Step("ocr.findText", Param("queryText", "Ready"), Param("matchMode", "contains"),
                Param("orderBy", "area"),
                Hit("match", "where")),
        ], devices);

        Assert.Equal("140,94", Centre(store, "where"));
    }
}
