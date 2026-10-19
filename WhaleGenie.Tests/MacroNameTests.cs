using Avalonia.Controls;
using Avalonia.Threading;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// A new macro has to be saveable before its author has named it, and a refused save has to put
/// the reason where the author can see and fix it.
/// </summary>
public class MacroNameTests
{
    [Fact]
    public void Default_names_follow_the_highest_one_in_the_project()
    {
        Assert.Equal("Macro 1", MacroNames.NextDefault([]));
        Assert.Equal("Macro 4", MacroNames.NextDefault(["Macro 1", "macro 3", "custom"]));
    }

    [Fact]
    public void A_new_editor_starts_with_a_saveable_default_name()
    {
        Ui.Run(() =>
        {
            var project = new[]
            {
                new MacroItem { Name = "Macro 1" },
                new MacroItem { Name = "macro 3" },
                new MacroItem { Name = "custom" },
            };
            var window = new MacroEditorWindow(null, project);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = Assert.IsType<MacroEditorViewModel>(window.DataContext);
            Assert.Equal("Macro 4", viewModel.Name);
            Assert.False(viewModel.IsDirty);
            Assert.True(viewModel.CanSave);

            viewModel.SaveCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Macro 4", window.Result?.Name);
        });
    }

    [Fact]
    public void A_blank_macro_added_without_the_editor_gets_the_same_default()
    {
        var viewModel = new MainViewModel();
        viewModel.AddMacro(new MacroItem { Name = "Macro 5" });
        viewModel.AddMacro(new MacroItem());

        Assert.Equal("Macro 6", viewModel.Macros[1].Name);
    }

    [Fact]
    public void A_blocked_save_brings_the_macro_settings_back()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "probe" };
            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = Assert.IsType<MacroEditorViewModel>(window.DataContext);
            viewModel.NewStepCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.FindControl<Border>("InspectorHost")!.IsVisible);

            viewModel.Name = string.Empty;
            var blocked = 0;
            var closed = false;
            viewModel.SaveBlocked += () => blocked++;
            viewModel.CloseRequested += _ => closed = true;
            viewModel.SaveCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, blocked);
            Assert.False(closed);
            Assert.True(window.FindControl<Border>("MacroSettingsDrawer")!.IsVisible);
            Assert.False(window.FindControl<Border>("InspectorHost")!.IsVisible);

            var name = window.FindControl<TextBox>("NameInput");
            Assert.NotNull(name);
            Assert.True(name!.IsFocused);
        });
    }
}
