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

// The switches around the wheel: in-programs only, the hotkey, and the icons drawn.
public partial class WheelMenuTests
{
    [Fact]
    public void The_only_in_programs_switch_is_saved_and_warns_when_no_scene_exists()
    {
        InOwnFiles(() =>
        {
            var settings = new SettingsViewModel(() => DriverInputState.Ready);
            Assert.False(settings.OnlyInPrograms);

            settings.OnlyInPrograms = true;
            Assert.True(WheelMenuStore.Load().Options.OnlyInPrograms);
            Assert.Equal(Strings.Get("Wheel.OnlyInProgramsNoScene"),
                settings.WheelLauncherStatus);
        });
    }

    [Fact]
    public void A_system_hotkey_cannot_take_the_key_the_menu_already_opens_on()
    {
        InOwnFiles(() =>
        {
            var settings = new SettingsViewModel(() => DriverInputState.Ready);
            Assert.True(settings.CaptureMenuLauncher("F9"));

            Assert.False(settings.CaptureSystemHotkey("F9"));
            Assert.Equal(Strings.Get("Settings.HotkeyConflictMenu"),
                settings.SystemHotkeyProblem);
            Assert.NotEqual("F9", settings.SystemHotkey);
        });
    }


    [Fact]
    public void A_chosen_icon_is_drawn_even_when_the_label_is_shown()
    {
        Ui.Run(() =>
        {
            var rules = new WheelMenuRules();
            rules.Global.At(Top).Macro = "copy";
            rules.Global.At(Top).Icon = new MenuIconReference(MenuIconKind.Builtin,
                MenuIconCatalog.Options[0].Key);
            var layout = rules.Resolve(null, WheelSource.Global, false,
                [new MacroItem { Name = "copy", IsEnabled = true }]);
            var options = new WheelOptions
            {
                PopAnimation = false,
                HideLabelWithIcon = false,
                FollowTheme = false,
            };

            var window = new WheelMenuWindow(layout, options, new PixelPoint(0, 0), preview: true);
            window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            // The picker used to change nothing unless "labels off" was also on. The icon now has
            // to be there with the label, which is the state a wheel is normally in.
            Assert.NotEmpty(window.GetVisualDescendants().OfType<IconView>());
            window.Close();
        });
    }


    private static WheelGesture Gesture() => new(
        () => WheelOptions.RightButton,
        () => WheelOptions.UsualRepeatKey,
        () => true);

    private static void InOwnFiles(Action check)
    {
        var folder = Path.Combine(Path.GetTempPath(),
            "whalegenie-wheel-" + Guid.NewGuid().ToString("N"));
        var settings = LocalSettings.FilePath;
        var wheel = WheelMenuStore.FilePathOverride;
        try
        {
            Directory.CreateDirectory(folder);
            LocalSettings.FilePath = Path.Combine(folder, "settings.json");
            WheelMenuStore.FilePathOverride = Path.Combine(folder, "wheel-menu.json");
            check();
        }
        finally
        {
            LocalSettings.FilePath = settings;
            WheelMenuStore.FilePathOverride = wheel;
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }
    }
}
