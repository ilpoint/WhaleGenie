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

// The wheel page and its files, and a scene added while the editor is open.
public partial class WheelMenuTests
{
    [Fact]
    public void Every_new_wheel_choice_round_trips_through_its_own_file()
    {
        InOwnFiles(() =>
        {
            var state = new WheelMenuState();
            var options = state.Options;
            options.FontSize = 18;
            options.TimeoutMs = 5000;
            options.ThirdCapacity = 16;
            options.RepeatKey = "F8";
            options.ShowGuide = false;
            options.FollowTheme = false;
            options.Gloss = false;
            options.HoverBorder = false;
            options.PopAnimation = false;
            options.HideEmptyOuter = false;
            options.OnlyInPrograms = true;
            WheelMenuStore.Save(state);

            var read = WheelMenuStore.Load();
            Assert.Equal(18, read.Options.FontSize);
            Assert.Equal(5000, read.Options.TimeoutMs);
            Assert.Equal(16, read.Options.ThirdCapacity);
            Assert.Equal("F8", read.Options.RepeatKey);
            Assert.False(read.Options.ShowGuide);
            Assert.False(read.Options.FollowTheme);
            Assert.False(read.Options.HideEmptyOuter);
            Assert.True(read.Options.OnlyInPrograms);
            Assert.False(new WheelOptions().OnlyInPrograms);
            Assert.Equal(0, new WheelOptions { ThirdCapacity = 7 }.Clamped().ThirdCapacity);

            // A note is part of the position, so it belongs to the project's file.
            var triggers = new ProjectTriggers();
            triggers.Wheel.Global.At(Top).Macro = "copy";
            triggers.Wheel.Global.At(Top).Note = "Copy the text";
            var reread = ProjectTriggers.FromJson(triggers.ToJson());
            Assert.Equal("Copy the text", reread.Wheel.Global.At(Top).Note);
        });
    }

    [Fact]
    public void The_wheel_actions_page_hosts_the_editor()
    {
        InOwnFiles(() => Ui.Run(() =>
        {
            var window = new SettingsWindow(null, null, SettingsSubPage.WheelActions);
            window.Show();

            var viewModel = Assert.IsType<SettingsViewModel>(window.DataContext);
            Assert.True(viewModel.IsWheelActionsPage);
            Assert.Equal(SettingsRoot.Triggers, viewModel.SelectedRoot);
            Assert.NotEmpty(viewModel.WheelScenes);
            Assert.IsType<WheelMenuEditorView>(
                Assert.IsType<ContentControl>(window.FindControl<ContentControl>("WheelEditorHost"))
                    .Content);

            window.Close();
        }));
    }
}
