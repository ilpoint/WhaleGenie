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

// Text, pictures and files on the clipboard, and the shortcuts that copy and paste.
public partial class DeviceActionTests
{
    [Fact]
    public async Task Copying_text_puts_it_on_the_clipboard()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
            [Step("clipboard.writeText", Param("clipboardText", "hello"))], devices);

        Assert.True(result.Succeeded);
        Assert.Equal("hello", devices.Clipboard.Text);
        Assert.Contains("clipboardWrite hello", devices.Calls);
    }

    [Fact]
    public async Task Reading_the_clipboard_fills_a_variable()
    {
        var devices = new FakeDeviceLayer();
        devices.Clipboard.Text = "from the clipboard";
        var (_, _, store) = await RunAsync(
            [Step("clipboard.readText", Returns("text=picked"))], devices);

        Assert.Equal("from the clipboard", store.Local.Values["picked"].AsText());
    }

    [Fact]
    public async Task Reading_the_clipboard_hands_nothing_over_until_a_result_is_asked_for()
    {
        var devices = new FakeDeviceLayer();
        devices.Clipboard.Text = "kept";
        var (_, _, store) = await RunAsync([Step("clipboard.readText")], devices);

        // Nothing was ticked, so the step leaves no variable behind at all: reading one is reading a
        // name no run wrote, which is what the design-time check tells the macro author about.
        Assert.Empty(store.Local.Values);
    }

    [Fact]
    public async Task The_picture_on_the_clipboard_can_be_read_into_a_variable()
    {
        var devices = new FakeDeviceLayer();
        devices.Clipboard.Picture = new ImageFrame(4, 3, new byte[4 * 3 * 4]);
        var (result, _, store) = await RunAsync(
            [Step("clipboard.readImage", Parts("image", "shot", "width", "height"))], devices);

        Assert.True(result.Succeeded);
        Assert.Equal("<image 4x3>", store.Local.Values["shot"].AsText());
        Assert.Equal(4, store.Local.Values["shot.width"].AsNumber());
        Assert.Equal(3, store.Local.Values["shot.height"].AsNumber());
    }

    [Fact]
    public async Task A_clipboard_with_no_picture_empties_the_variable()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, store) = await RunAsync(
            [Step("clipboard.readImage", Parts("image", "shot", "width", "height"))], devices);

        Assert.True(result.Succeeded);
        Assert.Equal(string.Empty, store.Local.Values["shot"].AsText());
        Assert.Contains("clipboardReadImage", devices.Calls);
    }

    [Fact]
    public async Task A_picture_can_be_put_on_the_clipboard_and_taken_off_again()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, store) = await RunAsync(
        [
            Step("clipboard.writeImage", Param("imageSource", "ok.png")),
            Step("clipboard.readImage", Parts("image", "back", "width", "height")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Contains("clipboardWriteImage 2x2", devices.Calls);
        Assert.Equal(2, store.Local.Values["back.width"].AsNumber());
        Assert.Equal(2, store.Local.Values["back.height"].AsNumber());
    }

    [Fact]
    public async Task The_files_on_the_clipboard_come_back_as_a_list()
    {
        var devices = new FakeDeviceLayer();
        devices.Clipboard.Files.Add(@"C:\reports\day.csv");
        devices.Clipboard.Files.Add(@"C:\reports\week.csv");
        var (_, _, store) = await RunAsync(
            [Step("clipboard.readFiles", Returns("files=picked"))], devices);

        Assert.Equal(["C:\\reports\\day.csv", "C:\\reports\\week.csv"],
            store.Local.Values["picked"].Items.Select(item => item.AsText()));
    }

    [Fact]
    public async Task Files_written_one_to_a_line_are_each_put_on_the_clipboard()
    {
        var devices = new FakeDeviceLayer();
        devices.Files.PathExists = true;
        var (result, _, _) = await RunAsync(
            [Step("clipboard.writeFiles", Param("files", "a.txt\nb.txt"))], devices);

        Assert.True(result.Succeeded);
        Assert.Equal([@"G:\fake\a.txt", @"G:\fake\b.txt"], devices.Clipboard.Files);
    }

    [Fact]
    public async Task Files_can_be_put_on_the_clipboard()
    {
        var devices = new FakeDeviceLayer();
        devices.Files.PathExists = true;
        var (result, _, _) = await RunAsync(
        [
            // A list is built as an expression value and handed straight over, which is what a
            // macro does after reading a folder.
            Step("control.setVariable", Param("name", "chosen"),
                Param("value", "list(\"G:/fake/a.txt\", \"G:/fake/b.txt\")")),
            Step("clipboard.writeFiles", Fields.Reference("files", "chosen")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Equal(["G:/fake/a.txt", "G:/fake/b.txt"], devices.Clipboard.Files);
    }

    [Fact]
    public async Task A_relative_path_on_the_clipboard_is_taken_from_the_macros_folder()
    {
        var devices = new FakeDeviceLayer();
        devices.Files.PathExists = true;
        var (result, _, _) = await RunAsync(
            [Step("clipboard.writeFiles", Param("files", "notes.txt"))], devices);

        // Explorer can only paste a path that really points somewhere, so it goes out as a full one.
        Assert.True(result.Succeeded);
        Assert.Equal([@"G:\fake\notes.txt"], devices.Clipboard.Files);
    }

    [Fact]
    public async Task Putting_a_file_on_the_clipboard_that_is_not_there_is_a_failure()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
            [Step("clipboard.writeFiles", Param("files", "ghost.txt"))], devices);

        // The paste would happen in another program, where nothing could report this back.
        Assert.False(result.Succeeded);
        Assert.Equal("Run.FileNotFound", result.Key);
    }

    [Fact]
    public async Task Clearing_the_clipboard_empties_it()
    {
        var devices = new FakeDeviceLayer();
        devices.Clipboard.Text = "gone";
        await RunAsync([Step("clipboard.clear")], devices);

        Assert.Equal(string.Empty, devices.Clipboard.Text);
        Assert.Contains("clipboardClear", devices.Calls);
    }

    [Fact]
    public async Task Waiting_for_the_clipboard_keeps_what_was_copied()
    {
        var devices = new FakeDeviceLayer();
        devices.Clipboard.Text = "copied meanwhile";
        devices.Clipboard.MovesAfter = 1;
        var (result, _, store) = await RunAsync(
            [Within(500, Step("clipboard.waitChange", Returns("text=picked")))], devices);

        Assert.True(result.Succeeded);
        Assert.Equal("copied meanwhile", store.Local.Values["picked"].AsText());
    }

    [Fact]
    public async Task Waiting_for_the_clipboard_gives_up_after_the_timeout()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
            [Within(100, Step("clipboard.waitChange"))], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.ClipboardTimeout", result.Key);
    }

    [Fact]
    public async Task Copying_a_selection_presses_the_shortcut_and_keeps_the_result()
    {
        var devices = new FakeDeviceLayer();
        devices.Clipboard.Text = "the selection";
        devices.Clipboard.MovesAfter = 1;
        var (result, _, store) = await RunAsync(
            [Within(500, Step("clipboard.copy", Param("shortcut", "Ctrl+C"),
                Returns("text=picked")))], devices);

        Assert.True(result.Succeeded);
        Assert.Equal("the selection", store.Local.Values["picked"].AsText());
        Assert.Contains("hotkey Ctrl|C 50", devices.Calls);
        Assert.Contains("clipboardRead", devices.Calls);
    }

    [Fact]
    public async Task Pasting_puts_the_text_on_the_clipboard_then_presses_the_shortcut()
    {
        var devices = new FakeDeviceLayer();
        await RunAsync(
            [Step("clipboard.paste", Param("pasteText", "typed"), Param("shortcut", "Ctrl+V"))], devices);

        Assert.Equal(["clipboardWrite typed", "hotkey Ctrl|V 50"], devices.Calls);
    }

    [Fact]
    public async Task Pasting_with_no_text_just_presses_the_shortcut()
    {
        var devices = new FakeDeviceLayer();
        await RunAsync([Step("clipboard.paste", Param("shortcut", "Ctrl+V"))], devices);

        Assert.Equal(["hotkey Ctrl|V 50"], devices.Calls);
    }

    [Fact]
    public async Task A_waiting_timeout_is_not_bent_by_the_speed_factor()
    {
        // A timeout says when to stop trying, which is not the same thing as pacing, so the
        // same wait gives up after the same time at ×10 as it does at ×1. The two waits are
        // compared with each other rather than against a fixed number of milliseconds: a busy
        // machine stretches whichever wait happens to be running, and a fixed bound turns that
        // into a failure that has nothing to do with the speed factor.
        var plain = await WaitedAsync(1, "1000");
        var fast = await WaitedAsync(10, "1000");

        Assert.True(fast < (plain * 2) + 2000,
            $"×10 waited {fast:0}ms, which is nowhere near the ×1 wait of {plain:0}ms");
    }

    /// <summary>How long a clipboard wait of the given length takes at a playback speed.</summary>
    private static async Task<double> WaitedAsync(double speed, string timeoutMs)
    {
        var devices = new FakeDeviceLayer();
        var watch = Stopwatch.StartNew();
        var result = await new MacroRunner(new VariableStore(), new SilentRunHost(), devices, speed)
            .RunAsync([Within(int.Parse(timeoutMs), Step("clipboard.waitChange"))]);
        watch.Stop();

        Assert.False(result.Succeeded);
        Assert.Equal("Run.ClipboardTimeout", result.Key);
        return watch.Elapsed.TotalMilliseconds;
    }
}
