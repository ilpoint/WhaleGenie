using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.GraphicsCapture;
using WhaleGenie.Runtime;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// The picture a picker aims at, and the list a right-click falls back on.
/// </summary>
/// <remarks>
/// Both are answers to "nothing happened": a picker that shows the program's own windows is a picker
/// aimed at the wrong thing, and a menu that never appears is a menu nobody can use. What is checked
/// here is the part that can be checked without a screen — the waiting for the picture to stop
/// changing, and the list standing in for a menu.
/// </remarks>
public class PickerSnapshotTests
{
    [Fact]
    public void The_picture_is_taken_again_until_the_screen_has_stopped_changing()
    {
        var (pixels, shots) = Ui.Run(() =>
        {
            var screen = new ChangingScreen();
            var frame = ScreenReady.Capture(screen, 0, 0, 4, 4);
            return (frame.Bgra[0], screen.Shots);
        });

        // The first shot is the screen as it was before the windows went; the one handed back is the
        // screen after. A single shot would have answered with the old one.
        Assert.Equal(0x22, pixels);
        Assert.True(shots >= 2, "the screen was read once, so nothing could have settled");
    }

    [Fact]
    public void A_screen_that_is_already_still_is_read_twice_and_no_more()
    {
        var (pixels, shots) = Ui.Run(() =>
        {
            var screen = new ChangingScreen { Still = true };
            var frame = ScreenReady.Capture(screen, 0, 0, 4, 4);
            return (frame.Bgra[0], screen.Shots);
        });

        Assert.Equal(0x33, pixels);
        Assert.Equal(2, shots);
    }

    /// <summary>
    /// Every way of asking the user to point at the screen takes its picture through the one that
    /// waits, rather than asking the screen once and hoping it has caught up.
    /// </summary>
    [Fact]
    public void Every_picker_takes_its_picture_once_the_screen_has_settled()
    {
        foreach (var name in new[]
                 {
                     "PointPickerWindow.cs",
                     "RegionPickerWindow.axaml.cs",
                     "ColorPickerWindow.axaml.cs",
                 })
        {
            var source = Source(name);
            Assert.Contains("ScreenReady.Capture(", source, StringComparison.Ordinal);
        }

        // The picture kept of a dragged rectangle is taken the same way: it is the same problem, and
        // it is the one the user keeps.
        Assert.Contains("ScreenReady.Capture(",
            File.ReadAllText(Path.Combine(Repository(), "WhaleGenie", "Storage", "ImageAssets.cs")),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Both_right_click_menus_watch_whether_the_menu_arrived()
    {
        var run = Source("RunWindow.axaml.cs");
        Assert.Contains("menu.notopened", run, StringComparison.Ordinal);


        var palette = Source("MacroMenuWindow.cs");
        Assert.Contains("menu.notopened", palette, StringComparison.Ordinal);


        // The menu is opened once, from the release: opening it while the press is still being
        // routed is what closes it again as the release arrives.
        Assert.Contains("InitialPressMouseButton != MouseButton.Right", run, StringComparison.Ordinal);
        Assert.Contains("InitialPressMouseButton != MouseButton.Right", palette, StringComparison.Ordinal);
    }

    /// <summary>
    /// A screen whose first shot is the one taken before the program's windows went away. Every shot
    /// after it is the screen without them.
    /// </summary>
    private sealed class ChangingScreen : IScreenDevice
    {
        private int _shots;

        /// <summary>Whether every shot says the same thing, which is a screen nobody is drawing.</summary>
        public bool Still { get; init; }

        public int Shots => _shots;

        public ScreenLayout Desktop { get; } = new(
            new ScreenArea(0, 0, 64, 64),
            [new ScreenDisplay(new ScreenArea(0, 0, 64, 64), true)]);

        public ScreenPoint Cursor { get; set; }

        public PixelColor PixelAt(int x, int y) => default;

        public ImageFrame Capture(int x, int y, int width, int height)
        {
            var shot = ++_shots;
            var value = Still ? (byte)0x33 : shot == 1 ? (byte)0x11 : (byte)0x22;
            var pixels = new byte[width * height * 4];
            for (var at = 0; at < pixels.Length; at += 4)
            {
                pixels[at] = value;
                pixels[at + 1] = value;
                pixels[at + 2] = value;
                pixels[at + 3] = 0xFF;
            }

            return new ImageFrame(width, height, pixels);
        }

        public ScreenShot Capture(ScreenCaptureRequest request)
            => throw new NotSupportedException("this screen is only read as a picture of the desktop");
    }

    private static string Source(string name)
        => File.ReadAllText(Path.Combine(Repository(), "WhaleGenie", "Views", name));

    private static string Repository()
    {
        for (var at = new DirectoryInfo(AppContext.BaseDirectory); at is not null; at = at.Parent)
        {
            if (File.Exists(Path.Combine(at.FullName, "WhaleGenie.slnx")))
            {
                return at.FullName;
            }
        }

        throw new InvalidOperationException("WhaleGenie.slnx was not found above the test binaries.");
    }
}
