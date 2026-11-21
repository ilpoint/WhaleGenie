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

// Scenes and their positions: what overrides what, and how the choices are saved.
public partial class WheelMenuTests
{
    [Fact]
    public void A_program_scene_overrides_only_the_positions_it_fills_in()
    {
        var rules = new WheelMenuRules();
        rules.Global.At(Top).Macro = "copy";
        rules.Global.At(new WheelSlotRef(WheelRing.Inner, 1)).Macro = "paste";
        var scene = rules.AddScene(new WindowRule(Process: "notepad",
            How: WindowHow.ProcessEquals));
        scene.At(Top).Macro = "cut";
        var macros = new[]
        {
            new MacroItem { Name = "copy", IsEnabled = true },
            new MacroItem { Name = "paste", IsEnabled = true },
            new MacroItem { Name = "cut", IsEnabled = true },
        };

        var merged = rules.Resolve(scene, WheelSource.Merged, false, macros);
        Assert.Equal("cut", merged.At(Top)?.Macro?.Name);
        Assert.True(merged.At(Top)?.FromProgram);
        Assert.Equal("paste", merged.At(new WheelSlotRef(WheelRing.Inner, 1))?.Macro?.Name);
        Assert.Null(rules.Resolve(scene, WheelSource.Program, false, macros)
            .At(new WheelSlotRef(WheelRing.Inner, 1))?.Macro);
    }

    [Fact]
    public void The_machines_options_round_trip_through_their_own_file()
    {
        InOwnFiles(() =>
        {
            Assert.Equal(WheelOptions.RightButton,
                WheelMenuStore.Load().Options.Launcher);

            var state = new WheelMenuState();
            state.Options.Launcher = "Mouse 4";
            state.Options.Diameter = 520;
            WheelMenuStore.Save(state);

            var read = WheelMenuStore.Load();
            Assert.Equal("Mouse 4", read.Options.Launcher);
            Assert.Equal(520, read.Options.Diameter);
        });
    }

    /// <summary>
    /// A position names a macro, and a macro belongs to a project, so the positions travel in the
    /// project's trigger file rather than in the machine's wheel file.
    /// </summary>
    [Fact]
    public void The_positions_travel_with_the_project()
    {
        var triggers = new ProjectTriggers();
        triggers.Wheel.Global.At(Top).Macro = "copy";
        var scene = triggers.Wheel.AddScene(new WindowRule(Process: "notepad",
            How: WindowHow.ProcessEquals));
        scene.At(Right).Macro = "cut";

        var read = ProjectTriggers.FromJson(triggers.ToJson());
        Assert.Equal("copy", read.Wheel.Global.At(Top).Macro);
        Assert.Equal("cut", Assert.Single(read.Wheel.Scenes).At(Right).Macro);
        Assert.Equal(scene.Key, read.Wheel.Scenes[0].Key);

        // Opening a project replaces everything the previous one held, the wheel included.
        var opened = new ProjectTriggers();
        opened.ReplaceAll(read);
        Assert.Equal("copy", opened.Wheel.Global.At(Top).Macro);
        Assert.Single(opened.Wheel.Scenes);
    }

    [Fact]
    public void The_wheel_page_opens_with_its_preview_button()
    {
        InOwnFiles(() => Ui.Run(() =>
        {
            var window = new AdvancedSettingsWindow(null, null, SettingsSubPage.WheelMenu);
            window.Show();

            var viewModel = Assert.IsType<SettingsViewModel>(window.DataContext);
            Assert.True(viewModel.IsWheelMenuPage);
            Assert.Equal(SettingsRoot.Triggers, viewModel.SelectedRoot);
            Assert.Equal(WheelOptions.RightButton, viewModel.SelectedWheelLauncher?.Binding);
            Assert.NotNull(SettingsView.Find<Button>(window, "PreviewWheelButton"));

            window.Close();
        }));
    }

    /// <summary>
    /// The four buttons a wheel can be opened with are the ones a hand can hold while it moves.
    /// A keyboard binding is refused, and a stored one is read back as the default.
    /// </summary>
    [Fact]
    public void The_launcher_is_one_of_four_mouse_buttons_and_nothing_else()
    {
        InOwnFiles(() => Ui.Run(() =>
        {
            var viewModel = new SettingsViewModel(() => DriverInputState.Ready, project: null);
            Assert.Equal(WheelOptions.LauncherButtons,
                viewModel.WheelLauncherChoices.Select(choice => choice.Binding));

            Assert.Equal(WheelOptions.RightButton,
                new WheelOptions { Launcher = "F2" }.Clamped().Launcher);
            Assert.Equal(string.Empty, new WheelOptions { Launcher = "" }.Clamped().Launcher);
            Assert.False(WheelOptions.IsAllowedLauncher(WheelOptions.MiddleButton + "x"));
        }));
    }

    /// <summary>
    /// The menu and the wheel both start on a press, so one button cannot open both: whichever ran
    /// second would never be seen. The page refuses the change and says which gesture took it.
    /// </summary>
    [Fact]
    public void A_button_the_menu_already_opens_on_cannot_open_the_wheel()
    {
        InOwnFiles(() => Ui.Run(() =>
        {
            var viewModel = new SettingsViewModel(() => DriverInputState.Ready, project: null);
            Assert.True(viewModel.CaptureMenuLauncher(WheelOptions.MiddleButton));

            Assert.False(viewModel.SetWheelLauncher(WheelOptions.MiddleButton));
            Assert.NotEqual(WheelOptions.MiddleButton, viewModel.WheelLauncher);
            Assert.Equal(Strings.Get("Wheel.LauncherConflict"), viewModel.WheelLauncherStatus);

            // The other three are untouched by the refusal.
            Assert.True(viewModel.SetWheelLauncher(WheelOptions.BackButton));
            Assert.Equal(WheelOptions.BackButton, viewModel.WheelLauncher);
        }));
    }

    [Fact]
    public void The_menu_cannot_take_the_button_the_wheel_opens_on()
    {
        InOwnFiles(() => Ui.Run(() =>
        {
            var viewModel = new SettingsViewModel(() => DriverInputState.Ready, project: null);
            Assert.True(viewModel.SetWheelLauncher(WheelOptions.ForwardButton));

            Assert.False(viewModel.CaptureMenuLauncher(WheelOptions.ForwardButton));
            Assert.NotEqual(WheelOptions.ForwardButton, viewModel.MenuLauncher);
            Assert.Equal(Strings.Get("Settings.MenuLauncherConflictWheel"),
                viewModel.MenuLauncherStatus);
        }));
    }
}

