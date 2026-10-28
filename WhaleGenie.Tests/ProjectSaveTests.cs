using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Threading;
using WhaleGenie.Models;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// Whether the project in the list differs from the file it was written to. It used to be nobody's
/// question: only the macro being edited was asked about, so a project that had grown a second
/// macro was thrown away at exit without a word. The main window asks this before the program stops.
/// </summary>
public class ProjectSaveTests
{
    [Fact]
    public void A_project_that_was_not_touched_has_nothing_unsaved()
    {
        Assert.False(new MainViewModel().HasUnsavedChanges);
    }

    [Fact]
    public void Adding_and_removing_macros_are_changes()
    {
        var viewModel = new MainViewModel();
        var macro = new MacroItem { Name = "login" };

        viewModel.AddMacro(macro);
        Assert.True(viewModel.HasUnsavedChanges);

        viewModel.RemoveMacro(macro);

        // Nothing is left and there is no file it came from, so there is nothing to lose either.
        Assert.False(viewModel.HasUnsavedChanges);
    }

    /// <summary>
    /// Every save keeps the file it replaced as .bak. The window's own Save goes through the package
    /// writer just as the MCP tool does, so this is the one place that has to remember — a save that
    /// turns out to be wrong is the one mistake nobody can undo from inside the program.
    /// </summary>
    [Fact]
    public void Every_save_keeps_the_file_it_replaced()
    {
        var folder = TempFolder();
        try
        {
            var path = Path.Combine(folder, "macros" + MacroPackage.Extension);
            var first = new MainViewModel();
            first.AddMacro(new MacroItem { Name = "login" });
            first.SavePackage(path);

            // Nothing was replaced yet, so there is nothing to keep.
            Assert.False(File.Exists(path + ".bak"));

            var second = new MainViewModel();
            second.AddMacro(new MacroItem { Name = "logout" });
            second.SavePackage(path);

            // What is kept is the package that was there before, not the one just written.
            var kept = MacroPackage.Load(path + ".bak");
            Assert.Contains(kept.Macros, macro => macro.Name == "login");
            Assert.DoesNotContain(kept.Macros, macro => macro.Name == "logout");
            Assert.Contains(MacroPackage.Load(path).Macros, macro => macro.Name == "logout");
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void Arming_a_macro_is_a_change_because_the_package_holds_it()
    {
        var folder = TempFolder();
        try
        {
            var viewModel = new MainViewModel();
            var macro = new MacroItem { Name = "login" };
            viewModel.AddMacro(macro);
            viewModel.SavePackage(Path.Combine(folder, "macros" + MacroPackage.Extension));

            Assert.False(viewModel.HasUnsavedChanges);

            viewModel.ToggleMacro(macro);

            Assert.False(macro.IsEnabled);
            Assert.True(viewModel.HasUnsavedChanges);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void An_edited_macro_that_replaces_another_one_is_a_change()
    {
        var folder = TempFolder();
        try
        {
            var viewModel = new MainViewModel();
            var macro = new MacroItem { Name = "login" };
            viewModel.AddMacro(macro);
            viewModel.SavePackage(Path.Combine(folder, "macros" + MacroPackage.Extension));

            viewModel.ReplaceMacro(macro, new MacroItem { Name = "login", BindKey = "F5" });

            Assert.True(viewModel.HasUnsavedChanges);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void Saving_and_opening_a_package_leave_nothing_unsaved()
    {
        var folder = TempFolder();
        var path = Path.Combine(folder, "macros" + MacroPackage.Extension);
        try
        {
            var viewModel = new MainViewModel();
            viewModel.AddMacro(new MacroItem { Name = "login" });
            viewModel.SavePackage(path);

            Assert.False(viewModel.HasUnsavedChanges);
            Assert.Equal(path, viewModel.CurrentPath);

            // Reading it back is where the file is now the truth, so there is nothing on top of it.
            var reopened = new MainViewModel();
            reopened.OpenPackage(path);

            Assert.False(reopened.HasUnsavedChanges);
            Assert.Single(reopened.Macros);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void A_project_that_is_emptied_is_still_unsaved_when_a_file_is_behind_it()
    {
        var folder = TempFolder();
        try
        {
            var viewModel = new MainViewModel();
            var macro = new MacroItem { Name = "login" };
            viewModel.AddMacro(macro);
            viewModel.SavePackage(Path.Combine(folder, "macros" + MacroPackage.Extension));

            viewModel.ClearMacros();

            // The file on disk still holds a macro, so writing this project out would change it.
            Assert.True(viewModel.HasUnsavedChanges);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void A_shared_variable_that_was_added_is_a_change_the_list_cannot_see()
    {
        var folder = TempFolder();
        var variable = new VariableDefinition { Name = "notes", Scope = VariableScope.Global };
        try
        {
            var viewModel = new MainViewModel();
            viewModel.AddMacro(new MacroItem { Name = "login" });
            viewModel.SavePackage(Path.Combine(folder, "macros" + MacroPackage.Extension));

            var before = MainViewModel.SharedVariables();
            Assert.False(MainViewModel.SharedVariablesChangedSince(before));

            // The Variable Center writes straight into the shared list, so nothing on the macro
            // list moves and the change would otherwise go unnoticed.
            VariableCatalog.Globals.Add(variable);

            Assert.True(MainViewModel.SharedVariablesChangedSince(before));

            viewModel.MarkSharedVariablesChanged();
            Assert.True(viewModel.HasUnsavedChanges);
        }
        finally
        {
            VariableCatalog.Globals.Remove(variable);
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void The_window_does_not_go_quietly_with_a_project_that_is_not_saved()
    {
        Ui.Run(() =>
        {
            // No notification area in a test session, so closing the window is the program leaving
            // — which is exactly the close that has to ask.
            var asked = 0;
            var answer = ConfirmChoice.Cancel;
            var window = new MainWindow
            {
                DataContext = new MainViewModel(),
                AskToSaveProject = () =>
                {
                    asked++;
                    return Task.FromResult(answer);
                },
            };
            window.Show();

            var viewModel = (MainViewModel)window.DataContext!;
            viewModel.AddMacro(new MacroItem { Name = "login" });

            window.Close();
            Dispatcher.UIThread.RunJobs();

            // The question was asked, and the window and the macro it asks about are still here.
            Assert.Equal(1, asked);
            Assert.True(window.IsVisible);
            Assert.Single(viewModel.Macros);

            // Answering "do not save" is what lets it go.
            answer = ConfirmChoice.Secondary;
            window.Close();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(2, asked);
            Assert.False(window.IsVisible);
        });
    }

    [Fact]
    public void A_window_with_nothing_to_save_closes_without_a_question()
    {
        Ui.Run(() =>
        {
            var asked = 0;
            var window = new MainWindow
            {
                DataContext = new MainViewModel(),
                AskToSaveProject = () =>
                {
                    asked++;
                    return Task.FromResult(ConfirmChoice.Cancel);
                },
            };
            window.Show();

            window.Close();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(0, asked);
            Assert.False(window.IsVisible);
        });
    }

    /// <summary>
    /// What a step holds has to survive the package it is written to. A position written as an
    /// expression is the one value that can be lost quietly this way: read back as a zero it is
    /// still a number of the right kind, so nothing complains — the macro simply clicks the corner
    /// of the screen instead of following the mouse.
    /// </summary>
    [Fact]
    public void A_package_keeps_a_position_written_as_an_expression()
    {
        var folder = TempFolder();
        try
        {
            Ui.Run(() =>
            {
                var macro = new MacroItem { Name = "click", BindKey = "F5" };
                macro.Steps.Add(new MacroStep
                {
                    Type = "input.mouseClick",
                    Parameters =
                    [
                        new StepParameter
                        {
                            Name = "button", Kind = ActionParameterKind.Choice, Value = "left",
                        },
                        new StepParameter
                        {
                            Name = "x", Kind = ActionParameterKind.Number, Value = "$sys.mouseX",
                        },
                        new StepParameter
                        {
                            Name = "y", Kind = ActionParameterKind.Number, Value = "$sys.mouseY",
                        },
                    ],
                });

                var path = Path.Combine(folder, "macros" + MacroPackage.Extension);
                MacroPackage.Save(path, [macro], [], "v0");
                var read = MacroPackage.Load(path).Macros[0];

                Assert.Equal("$sys.mouseX", read.Steps[0].Parameters
                    .First(parameter => parameter.Name == "x").Value);
                Assert.Equal("$sys.mouseY", read.Steps[0].Parameters
                    .First(parameter => parameter.Name == "y").Value);
            });
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    /// <summary>A folder of its own under the temporary directory, removed by the caller.</summary>
    private static string TempFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }
}
