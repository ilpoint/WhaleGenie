using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using WhaleGenie.Models;
using WhaleGenie.Storage;
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

    /// <summary>
    /// A name that is taken gets the day attached, and a day that is taken as well gets a count: the
    /// rule has to answer with a name nobody has, not with a name that is nearly free.
    /// </summary>
    [Fact]
    public void A_taken_name_gets_the_day_and_then_a_count()
    {
        var day = new DateTime(2026, 10, 26);

        Assert.Equal("整理", MacroNames.Free("整理", ["别的"], day));
        Assert.Equal("整理 (2026-10-26)", MacroNames.Free("整理", ["整理"], day));
        Assert.Equal("整理 (2026-10-26) 2",
            MacroNames.Free("整理", ["整理", "整理 (2026-10-26)"], day));
        Assert.Equal("2026-10-26", MacroNames.Free("", ["整理"], day));
    }

    /// <summary>
    /// Importing a package that holds a macro this project already has: both are work, so the one
    /// arriving now keeps its steps under a dated name rather than living unreachable behind a name
    /// a call resolves to the other macro.
    /// </summary>
    [Fact]
    public void Importing_a_macro_whose_name_is_taken_dates_the_one_that_arrives()
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        try
        {
            var path = Path.Combine(folder, "incoming.wgmacro");
            var incoming = new MacroItem { Name = "整理" };
            incoming.Steps.Add(new MacroStep
            {
                Type = "control.delay",
                Parameters =
                [new StepParameter { Name = "ms", Kind = ActionParameterKind.Text, Value = "5" }]
            });
            MacroPackage.Save(path, [incoming], [], "test");

            var contents = MacroPackage.Load(path);
            var project = new MainViewModel();
            project.AddMacro(new MacroItem { Name = "整理" });
            MacroImport.IntoProject(project, contents, contents.Macros,
                MacroImport.Decisions(contents.Macros, contents.Globals));

            Assert.Equal(2, project.Macros.Count);
            Assert.Equal("整理", project.Macros[0].Name);
            var arrived = project.Macros[1];
            Assert.StartsWith("整理 (", arrived.Name, StringComparison.Ordinal);
            Assert.Single(arrived.Steps);

            // Its trigger entries follow the new name, or the entry and the macro would disagree
            // about which macro the package brought.
            Assert.NotEmpty(arrived.Triggers);
            Assert.All(arrived.Triggers,
                entry => Assert.Equal(arrived.Name, entry.MacroName));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
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

            var name = window.FindControl<TextBox>("NameInput");
            Assert.NotNull(name);
            Assert.True(name!.IsFocused);
        });
    }
}
