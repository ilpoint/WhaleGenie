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

// Finding a window and deciding whether it matches, by title, process, class or handle.
public partial class DeviceActionTests
{
    [Fact]
    public async Task Checking_for_a_window_leaves_true_or_false()
    {
        var devices = new FakeDeviceLayer();
        devices.Window.Open.Add(new WindowInfo(7, "Untitled - Notepad",
            new ScreenPoint(10, 20), new ScreenSize(300, 200), false, false));
        var (_, _, store) = await RunAsync(
        [
            Step("window.exists", Param("window", "notepad"), Returns("found=here")),
            Step("window.exists", Param("window", "calculator"), Returns("found=there")),
        ], devices);

        Assert.True(store.Local.Values["here"].Flag);
        Assert.False(store.Local.Values["there"].Flag);
    }

    [Fact]
    public async Task Waiting_for_a_window_keeps_its_title()
    {
        var devices = new FakeDeviceLayer();
        devices.Window.AppearsLater = "Untitled - Notepad";
        devices.Window.AppearsAfter = 1;
        var (result, _, store) = await RunAsync(
        [
            Within(2000, Step("window.waitFor", Param("window", "notepad"),
                Returns("title=win", "window=found"))),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Equal("Untitled - Notepad", store.Local.Values["win"].AsText());
        Assert.Equal("Untitled - Notepad", Field(store, "found", "title").AsText());
        Assert.True(Field(store, "found", "handle").AsNumber() > 0);
    }

    [Fact]
    public async Task A_window_can_be_found_by_process_or_class()
    {
        var devices = WithAWindow();
        // The title is "Notepad - notes.txt", so neither of the other two names appears in it:
        // a match can only have come from the way the step asked for.
        devices.Window.Traits[1] = ("notes-app", "WhaleGenieNotes");
        var (_, _, store) = await RunAsync(
        [
            Step("window.exists", Param("window", "notes-app"), Param("how", "process"),
                Returns("found=byProcess")),
            Step("window.exists", Param("window", "whalegenie"), Param("how", "class"),
                Returns("found=byClass")),
            Step("window.exists", Param("window", "notes"), Param("how", "titleContains"),
                Returns("found=byTitle")),
        ], devices);

        Assert.True(store.Local.Values["byProcess"].Flag);
        Assert.True(store.Local.Values["byClass"].Flag);
        Assert.True(store.Local.Values["byTitle"].Flag);
    }

    [Fact]
    public async Task A_window_is_compared_with_its_text_the_way_the_step_says()
    {
        var devices = WithAWindow();
        var (_, _, store) = await RunAsync(
        [
            // The one window is called "Notepad - notes.txt".
            Step("window.exists", Param("window", "notepad"), Param("how", "titleStartsWith"),
                Returns("found=fromStart")),
            Step("window.exists", Param("window", "notes"), Param("how", "titleStartsWith"),
                Returns("found=notFromStart")),
            Step("window.exists", Param("window", "notes\\.txt$"), Param("how", "titleRegex"),
                Returns("found=byPattern")),
            Step("window.exists", Param("window", "notes"), Returns("found=byText")),
        ], devices);

        Assert.True(store.Local.Values["fromStart"].Flag);
        Assert.False(store.Local.Values["notFromStart"].Flag);
        Assert.True(store.Local.Values["byPattern"].Flag);
        Assert.True(store.Local.Values["byText"].Flag);
    }

    [Fact]
    public async Task A_window_pattern_that_cannot_be_read_fails_the_step()
    {
        var devices = WithAWindow();
        devices.Window.Traits[1] = ("notepad", "Notepad");
        var (result, _, _) = await RunAsync(
        [
            Step("window.exists", Param("window", "notes("), Param("how", "titleRegex"),
                Returns("found=found")),
        ], devices);

        Assert.Equal("Run.BadPattern", result.Key);
    }

    [Fact]
    public async Task A_window_that_matches_by_nothing_is_not_found()
    {
        var devices = WithAWindow();
        devices.Window.Traits[1] = ("notepad", "Notepad");
        var (_, _, store) = await RunAsync(
        [
            Step("window.exists", Param("window", "explorer"), Param("how", "process"),
                Returns("found=found")),
        ], devices);

        Assert.False(store.Local.Values["found"].Flag);
    }

    /// <summary>
    /// A name read whole and a name read for a piece of it are two different questions: a macro that
    /// means notepad does not mean notepad++ , which is why the name of a program can also be asked
    /// for exactly.
    /// </summary>
    [Fact]
    public async Task A_programs_name_can_be_asked_for_exactly()
    {
        var devices = WithAWindow();
        devices.Window.Traits[1] = ("notepad-plus-plus", "Notepad");

        var (_, _, store) = await RunAsync(
        [
            Step("window.exists", Param("window", "notepad"), Param("how", WindowHow.Process),
                Returns("found=contained")),
            Step("window.exists", Param("window", "notepad"), Param("how", WindowHow.ProcessEquals),
                Returns("found=exactly")),
            Step("window.exists", Param("window", "notepad-plus-plus"),
                Param("how", WindowHow.ProcessEquals), Returns("found=whole")),
        ], devices);

        Assert.True(store.Local.Values["contained"].Flag);
        Assert.False(store.Local.Values["exactly"].Flag);
        Assert.True(store.Local.Values["whole"].Flag);
    }

    [Fact]
    public async Task A_window_handle_chooses_one_of_two_identical_windows()
    {
        var devices = WithAWindow();
        devices.Window.Open.Add(new WindowInfo(2, "Notepad - notes.txt",
            new ScreenPoint(1000, 500), new ScreenSize(800, 600), false, false));
        devices.Window.Traits[1] = ("notepad", "Notepad");
        devices.Window.Traits[2] = ("notepad", "Notepad");

        var (_, _, store) = await RunAsync(
        [
            Step("window.exists", Param("window", "1"), Param("how", WindowHow.Handle),
                Returns("found=first")),
            Step("window.exists", Param("window", "2"), Param("how", WindowHow.Handle),
                Returns("found=second")),
            Step("window.exists", Param("window", "3"), Param("how", WindowHow.Handle),
                Returns("found=missing")),
        ], devices);

        Assert.True(store.Local.Values["first"].Flag);
        Assert.True(store.Local.Values["second"].Flag);
        Assert.False(store.Local.Values["missing"].Flag);
    }

    [Fact]
    public async Task A_window_handle_rule_without_a_handle_does_not_use_the_foreground()
    {
        var (result, _, _) = await RunAsync(
        [
            Step("window.exists", Param("window", "0"), Param("how", WindowHow.Handle)),
        ], WithAWindow());

        Assert.Equal(RunKeys.BadWindowRule, result.Key);
    }

    /// <summary>
    /// The ways of recognising a window are read the same by every step that names one: what a
    /// window action acts on, what a step that reads the screen counts its coordinates from, what a
    /// step posting its input at a window posts at, and which window an element search looks in. One
    /// rule, read four ways, is the whole point of there being one rule.
    /// </summary>
    [Fact]
    public async Task Every_step_that_names_a_window_reads_the_ways_the_same()
    {
        // The one window is "Notepad - notes.txt", owned by "notepad", registered as "Notepad", so
        // each way is asked with a text only that way answers to.
        (string How, string Text, bool Found)[] ways =
        [
            (WindowHow.TitleContains, "notes", true),
            (WindowHow.TitleStartsWith, "Notepad", true),
            (WindowHow.TitleEndsWith, "notes.txt", true),
            (WindowHow.TitleEquals, "Notepad - notes.txt", true),
            (WindowHow.TitleRegex, @"^Notepad - notes\.txt$", true),
            (WindowHow.Process, "notepad", true),
            (WindowHow.ProcessEquals, "notepad", true),
            (WindowHow.Class, "Notepad", true),
            (WindowHow.ClassEquals, "Notepad", true),
            (WindowHow.TitleStartsWith, "notes", false),
            (WindowHow.TitleEndsWith, "Notepad", false),
            (WindowHow.TitleEquals, "Notepad", false),
            (WindowHow.Process, "explorer", false),
            (WindowHow.ProcessEquals, "notepad-plus-plus", false),
            (WindowHow.Class, "CalcFrame", false),
            (WindowHow.ClassEquals, "Note", false),
        ];

        foreach (var (how, text, found) in ways)
        {
            var (acted, _, store) = await RunAsync(
            [
                Step("window.exists", Param("window", text), Param("how", how),
                    Returns("found=hit")),
            ], Named());
            Assert.Equal(found, store.Local.Values["hit"].Flag);

            var (looked, _, _) = await RunAsync(
            [
                Step("vision.findImage", Pictures("ok.png"), Param("anchorMode", "window"),
                    Param("window", text), Param("how", how)),
            ], Looking());
            Assert.Equal(found, looked.Succeeded);

            var (posted, _, _) = await RunAsync(
            [
                Step("input.keyPress", Param("key", "F5"), Param("inputMode", "background"),
                    Param("window", text), Param("how", how)),
            ], Named());
            Assert.Equal(found, posted.Succeeded);

            // An element search names the window it looks in the same way, and the rule the step
            // wrote is what the device is asked with.
            var elements = Named();
            await RunAsync(
            [
                Step("uia.exists", Param("window", text), Param("how", how),
                    Param("uiSelector", "Button[name='Save']")),
            ], elements);

            var (match, compare) = WindowHow.Read(how);
            Assert.Equal(match, elements.Ui.LastQuery!.WindowMatch);
            Assert.Equal(compare, elements.Ui.LastQuery.WindowCompare);
            Assert.Equal(text, elements.Ui.LastQuery.WindowTitle);

            // Bringing a window to the front is handed the same rule, so choosing the program or a
            // pattern there means what it means everywhere else.
            var focused = Named();
            await RunAsync(
            [
                Step("uia.focusWindow", Param("window", text), Param("how", how)),
            ], focused);

            Assert.Equal(match, focused.Ui.LastQuery!.WindowMatch);
            Assert.Equal(compare, focused.Ui.LastQuery.WindowCompare);
            Assert.Equal(text, focused.Ui.LastQuery.WindowTitle);
        }
    }

    /// <summary>One window, which can be named by its title, by its program or by its class.</summary>
    private static FakeDeviceLayer Named()
    {
        var devices = WithAWindow();
        devices.Window.Traits[1] = ("notepad", "Notepad");
        return devices;
    }

    /// <summary>The same, with a picture waiting to be found in it.</summary>
    private static FakeDeviceLayer Looking()
    {
        var devices = Named();
        devices.Vision.Match = new ImageMatch(0.99, new ScreenPoint(5, 6), new ScreenSize(4, 4));
        return devices;
    }
}
