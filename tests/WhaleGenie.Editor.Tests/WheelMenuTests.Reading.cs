using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;


namespace WhaleGenie.Tests;

// What the wheel reads: the launcher, the pointer between frames and the scene it opens in.
public partial class WheelMenuTests
{
    /// <summary>
    /// Adding a scene rebuilds the list by clearing it, and a recycled row container is built once
    /// with nothing in it. Reading the row without a guard threw from the template and took the
    /// whole program down, which is what made "add" look like "crash".
    /// </summary>
    [Fact]
    public void Adding_a_scene_selects_it_without_losing_the_list()
    {
        InOwnFiles(() =>
        {
            var viewModel = new SettingsViewModel(() => DriverInputState.Ready);
            viewModel.AddWheelScene(new WindowRule(Title: "Notepad"));
            viewModel.AddWheelScene(new WindowRule(Title: "Browser"));

            Assert.Equal(3, viewModel.WheelScenes.Count);
            Assert.NotNull(viewModel.SelectedWheelScene);
        });
    }

    [Fact]
    public void The_menu_launcher_check_boxes_set_the_middle_button_or_a_custom_key()
    {
        InOwnFiles(() =>
        {
            var viewModel = new SettingsViewModel(() => DriverInputState.Ready);
            viewModel.MenuLauncherMiddle = true;
            Assert.Equal(WheelOptions.MiddleButton, LocalSettings.LoadMenuLauncher());
            Assert.True(viewModel.MenuLauncherMiddle);

            Assert.True(viewModel.CaptureMenuLauncher("F9"));
            Assert.Equal("F9", LocalSettings.LoadMenuLauncher());
            Assert.True(viewModel.MenuLauncherCustom);
            Assert.False(viewModel.MenuLauncherMiddle);
        });
    }

    /// <summary>
    /// The hook writes the pointer position and the window reads it once a frame: what happened
    /// between two frames is one drawing, and nothing written is lost by the reading that took it.
    /// </summary>
    [Fact]
    public void A_pointer_that_moves_between_frames_is_read_once_per_frame()
    {
        var aim = new WheelAim();
        aim.Move(10, 20);
        aim.Move(30, 40);
        aim.Move(50, 60);

        Assert.True(aim.Take(out var x, out var y));
        Assert.Equal(50, x);
        Assert.Equal(60, y);
        Assert.False(aim.Take(out _, out _), "a pointer that has not moved drew again");
        Assert.Equal((3L, 1L), aim.Counts);
    }

    [Fact]
    public void A_move_written_after_a_reading_is_read_by_the_next_one()
    {
        var aim = new WheelAim();
        aim.Move(1, 2);
        Assert.True(aim.Take(out _, out _));

        aim.Move(3, 4);
        Assert.True(aim.Take(out var x, out var y));
        Assert.Equal(3, x);
        Assert.Equal(4, y);
    }

    [Fact]
    public void A_closed_wheel_forgets_the_move_it_never_drew()
    {
        var aim = new WheelAim();
        aim.Move(7, 8);
        aim.Clear();

        Assert.False(aim.Take(out _, out _));
    }


    [Fact]
    public void The_wheel_only_opens_where_a_program_scene_matches()
    {
        var windows = new WindowsForChecks()
            .Add(1, "Notes", "notepad", "Notepad")
            .Add(2, "Browser", "chrome", "Chrome");
        var notepad = windows.Open[0];
        var browser = windows.Open[1];
        var rules = new WheelMenuRules();

        // Without the switch the wheel opens everywhere, which is what it did until now.
        Assert.True(rules.OpensFor(notepad, windows, onlyInPrograms: false));
        Assert.True(rules.OpensFor(browser, windows, onlyInPrograms: false));

        // With it on, and no scene at all, it opens nowhere.
        Assert.False(rules.OpensFor(notepad, windows, onlyInPrograms: true));

        rules.AddScene(new WindowRule(Process: "notepad", How: WindowHow.ProcessEquals));
        Assert.True(rules.OpensFor(notepad, windows, onlyInPrograms: true));
        Assert.False(rules.OpensFor(browser, windows, onlyInPrograms: true));

        // A missing foreground window is not a match: the wheel stays out of the way.
        Assert.False(rules.OpensFor(null, windows, onlyInPrograms: true));
    }
}

