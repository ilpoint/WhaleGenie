using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using WhaleGenie.Core.Variables;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>The project global editor works on drafts and commits only when Save succeeds.</summary>
[RestoreSharedState]
public class GlobalVariablesTests
{
    [Fact]
    public void Adding_and_editing_a_global_does_not_change_the_declaration_until_save()
    {
        var existing = new VariableDefinition
        {
            Name = "total",
            Scope = VariableScope.Global,
            Type = "number",
            DefaultValue = "1",
            PersistValue = true,
        };
        var viewModel = new GlobalVariablesViewModel([existing]);
        IReadOnlyList<VariableDefinitionDraft>? saved = null;
        viewModel.Saved += rows => saved = rows;

        viewModel.Rows.Single().DefaultValue = "2";
        viewModel.NewCommand.Execute(null);
        viewModel.Selected!.Name = "count";
        viewModel.Selected.DefaultValue = "0";
        Assert.Equal("1", existing.DefaultValue);
        Assert.Null(saved);

        viewModel.SaveCommand.Execute(null);

        Assert.NotNull(saved);
        Assert.Equal(["total", "count"], saved.Select(row => row.Name));
        Assert.Equal("2", saved.Single(row => row.Name == "total").DefaultValue);
        Assert.True(saved.Single(row => row.Name == "total").PersistValue);
    }

    [Fact]
    public void A_duplicate_or_reserved_name_blocks_save()
    {
        var viewModel = new GlobalVariablesViewModel(
        [
            new VariableDefinition { Name = "total", Scope = VariableScope.Global },
        ]);
        viewModel.NewCommand.Execute(null);
        viewModel.Selected!.Name = "TOTAL";
        Assert.False(viewModel.CanSave);
        Assert.NotEqual(string.Empty, viewModel.ValidationMessage);

        viewModel.Selected.Name = "sys.fake";
        Assert.False(viewModel.CanSave);
        Assert.NotEqual(string.Empty, viewModel.ValidationMessage);
    }

    [Fact]
    public void A_new_blank_row_waits_for_a_name_without_calling_it_unreadable()
    {
        var viewModel = new GlobalVariablesViewModel([]);

        viewModel.NewCommand.Execute(null);

        Assert.NotNull(viewModel.Selected);
        Assert.Equal(string.Empty, viewModel.Selected.Name);
        Assert.False(viewModel.CanSave);
        Assert.True(viewModel.HasValidationProblem);
        Assert.True(viewModel.NeedsName);
        Assert.Equal(Strings.Get("GlobalVariables.NameRequired"), viewModel.ValidationMessage);

        viewModel.Selected.Name = "not a name";
        Assert.True(viewModel.HasValidationProblem);
    }

    [Fact]
    public void A_numeric_name_can_be_saved()
    {
        var viewModel = new GlobalVariablesViewModel([]);
        IReadOnlyList<VariableDefinitionDraft>? saved = null;
        viewModel.Saved += rows => saved = rows;

        viewModel.NewCommand.Execute(null);
        viewModel.Selected!.Name = "123";
        viewModel.Selected.DefaultValue = "7";

        Assert.True(viewModel.CanSave, viewModel.ValidationMessage);
        viewModel.SaveCommand.Execute(null);

        var row = Assert.Single(saved!);
        Assert.Equal("123", row.Name);
        Assert.Equal("7", row.DefaultValue);
    }

    [Fact]
    public void Dirty_state_follows_actual_changes()
    {
        var existing = new VariableDefinition
        {
            Name = "total",
            Scope = VariableScope.Global,
            Type = "number",
            DefaultValue = "1",
            Description = "running total",
            PersistValue = true,
        };
        var viewModel = new GlobalVariablesViewModel([existing]);
        var row = Assert.Single(viewModel.Rows);
        Assert.False(viewModel.HasChanges);

        row.Description = "changed";
        Assert.True(viewModel.HasChanges);
        row.Description = "running total";
        Assert.False(viewModel.HasChanges);

        row.CurrentValue = "2";
        Assert.True(viewModel.HasChanges);
        row.CurrentValue = row.CurrentOriginal;
        Assert.False(viewModel.HasChanges);

        row.PersistValue = false;
        Assert.True(viewModel.HasChanges);
        row.PersistValue = true;
        Assert.False(viewModel.HasChanges);

        viewModel.NewCommand.Execute(null);
        Assert.True(viewModel.HasChanges);
        viewModel.RemoveCommand.Execute(null);
        Assert.False(viewModel.HasChanges);

        viewModel.Selected = row;
        viewModel.RemoveCommand.Execute(null);
        Assert.True(viewModel.HasChanges);
    }

    [Fact]
    public void Editing_a_name_keeps_the_selected_row_visible_under_an_old_search()
    {
        var viewModel = new GlobalVariablesViewModel(
        [
            new VariableDefinition
            {
                Name = "oldName",
                Scope = VariableScope.Global,
            },
        ]);
        viewModel.SearchText = "old";
        var row = Assert.IsType<VariableDefinitionDraft>(viewModel.Selected);

        row.Name = "newName";

        Assert.Same(row, viewModel.Selected);
        Assert.Contains(row, viewModel.Shown);
        Assert.True(viewModel.HasChanges);
        row.Name = "oldName";
        Assert.False(viewModel.HasChanges);
    }

    [Fact]
    public void Empty_state_tells_an_empty_project_from_an_empty_filter()
    {
        var empty = new GlobalVariablesViewModel([]);
        Assert.True(empty.ShowsNoVariables);
        Assert.False(empty.ShowsNoMatches);
        Assert.Equal(Strings.Get("GlobalVariables.NoVariables"), empty.EmptyMessage);

        var filtered = new GlobalVariablesViewModel(
        [
            new VariableDefinition
            {
                Name = "total",
                Scope = VariableScope.Global,
            },
        ]);
        filtered.SearchText = "missing";
        Assert.False(filtered.ShowsNoVariables);
        Assert.True(filtered.ShowsNoMatches);
        Assert.Equal(Strings.Get("GlobalVariables.NoMatches"), filtered.EmptyMessage);

        filtered.ClearFiltersCommand.Execute(null);
        Assert.False(filtered.ShowsNoMatches);
        Assert.Single(filtered.Shown);
    }

    [Fact]
    public void Saving_an_invalid_hidden_row_reveals_and_selects_its_problem()
    {
        var viewModel = new GlobalVariablesViewModel(
        [
            new VariableDefinition
            {
                Name = "visible",
                Scope = VariableScope.Global,
            },
            new VariableDefinition
            {
                Name = "hidden",
                Scope = VariableScope.Global,
                Type = "number",
                DefaultValue = "not a number",
            },
        ]);
        GlobalVariableProblemField? focus = null;
        var blocked = false;
        IReadOnlyList<VariableDefinitionDraft>? saved = null;
        viewModel.FocusRequested += field => focus = field;
        viewModel.SaveBlocked += () => blocked = true;
        viewModel.Saved += rows => saved = rows;
        viewModel.SearchText = "visible";

        viewModel.SaveCommand.Execute(null);

        Assert.True(blocked);
        Assert.Null(saved);
        Assert.Equal(string.Empty, viewModel.SearchText);
        Assert.Equal(GlobalVariableFilterKind.All, viewModel.SelectedFilter!.Kind);
        Assert.Equal("hidden", viewModel.Selected!.Name);
        Assert.Equal(GlobalVariableProblemField.DefaultValue, focus);
    }

    [Fact]
    public void The_expression_builder_uses_the_current_draft_order()
    {
        Ui.Run(() =>
        {
            var window = new GlobalVariablesWindow(
            [
                new VariableDefinition
                {
                    Name = "base",
                    Scope = VariableScope.Global,
                    Type = "number",
                    DefaultValue = "2",
                },
            ], []);
            var manager = (GlobalVariablesViewModel)window.DataContext!;
            manager.NewCommand.Execute(null);
            var later = manager.Selected!;
            later.Name = "derived";

            var choices = window.ExpressionVariablesBefore(later);

            Assert.Contains(choices, choice =>
                choice.Name == "base" && choice.Group == VariableGroup.Global);
            Assert.DoesNotContain(choices, choice => choice.Name == "derived");
            manager.RemoveCommand.Execute(null);
            Ui.CloseAndAnswer(window);
        });
    }

    [Fact]
    public void Editing_a_draft_leaves_its_existing_name_alone()
    {
        var draft = new VariableDefinitionDraft(new VariableDefinition
        {
            Name = "total",
            Scope = VariableScope.Global,
        });

        Assert.Equal("total", draft.Name);
        draft.Description = "The running total.";
        draft.PersistValue = true;

        Assert.Equal("total", draft.Name);
        Assert.Equal("The running total.", draft.ToDefinition().Description);
        Assert.True(draft.ToDefinition().PersistValue);
    }

    [Fact]
    public void Default_values_are_expressions_evaluated_in_declaration_order()
    {
        var previousGlobals = VariableCatalog.Globals.ToList();
        var previousValues = SharedVariables.Current.Values;
        VariableCatalog.Globals.Clear();
        SharedVariables.Current.Clear();
        var source = new VariableDefinition
        {
            Name = "baseCount",
            Scope = VariableScope.Global,
            Type = "number",
            DefaultValue = "4",
        };
        var derived = new VariableDefinition
        {
            Name = "derivedCount",
            Scope = VariableScope.Global,
            Type = "number",
            DefaultValue = "$baseCount + 1",
        };
        VariableCatalog.Globals.Add(source);
        VariableCatalog.Globals.Add(derived);
        try
        {
            var store = MacroVariables.Seed();

            Assert.Equal(4, store.Global.Values["baseCount"].AsNumber());
            Assert.Equal(5, store.Global.Values["derivedCount"].AsNumber());
        }
        finally
        {
            VariableCatalog.Globals.Clear();
            foreach (var global in previousGlobals)
            {
                VariableCatalog.Globals.Add(global);
            }

            SharedVariables.Current.Clear();
            foreach (var (name, value) in previousValues)
            {
                SharedVariables.Current.Set(name, value);
            }
        }
    }

    [Fact]
    public void The_manager_can_use_an_earlier_draft_in_a_later_default_expression()
    {
        var manager = new GlobalVariablesViewModel(
        [
            new VariableDefinition
            {
                Name = "baseCount",
                Scope = VariableScope.Global,
                Type = "number",
                DefaultValue = "3",
            },
        ]);
        manager.NewCommand.Execute(null);
        manager.Selected!.Name = "derivedCount";
        manager.Selected.Type = "number";
        manager.Selected.DefaultValue = "$baseCount + 2";

        Assert.False(manager.Selected.DefaultHasError);
        Assert.Equal("5", manager.Selected.DefaultPreview);
    }

    [Fact]
    public void Run_debug_reads_globals_while_the_variable_center_owns_the_editor()
    {
        // The debug window shows the variables the macro in front of it uses, and nothing else:
        // creating or editing a global belongs to the variable center, not to a run.
        var run = File.ReadAllText(Path.Combine(Repository(), "WhaleGenie", "Views", "RunWindow.axaml"));
        Assert.Contains("ItemsSource=\"{Binding Variables}\"", run, System.StringComparison.Ordinal);
        Assert.DoesNotContain("AddGlobalCommand", run, System.StringComparison.Ordinal);
        Assert.DoesNotContain("RemoveGlobalCommand", run, System.StringComparison.Ordinal);
        Assert.DoesNotContain("NewPersistValue", run, System.StringComparison.Ordinal);

        var editor = File.ReadAllText(Path.Combine(Repository(), "WhaleGenie", "Views",
            "MacroEditorWindow.axaml"));
        Assert.Contains("Name=\"ManageGlobalsButton\"", editor, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Run_debug_lists_the_live_global_value_without_an_editor()
    {
        var previousGlobals = VariableCatalog.Globals.ToList();
        var previousValues = SharedVariables.Current.Values;
        VariableCatalog.Globals.Clear();
        SharedVariables.Current.Clear();
        var definition = new VariableDefinition
        {
            Name = "liveTotal",
            Scope = VariableScope.Global,
            Type = "number",
            DefaultValue = "1",
            PersistValue = true,
        };
        VariableCatalog.Globals.Add(definition);
        try
        {
            Ui.Run(() =>
            {
                var window = new RunWindow([], new ChecksDevices());
                window.Show();
                var viewModel = (RunViewModel)window.DataContext!;
                viewModel.SelectPageCommand.Execute("Variables");
                viewModel.SelectTabCommand.Execute("Global");
                SharedVariables.Current.SetNumber("liveTotal", 9);
                Dispatcher.UIThread.RunJobs();

                var row = viewModel.Globals.Single(variable => variable.Name == "liveTotal");
                Assert.Equal("9", row.CurrentValue);
                Assert.True(row.PersistValue);
                window.Close();
            });
        }
        finally
        {
            VariableCatalog.Globals.Clear();
            foreach (var global in previousGlobals)
            {
                VariableCatalog.Globals.Add(global);
            }
            SharedVariables.Current.Clear();
            foreach (var (name, value) in previousValues)
            {
                SharedVariables.Current.Set(name, value);
            }
        }
    }

    [Fact]
    public void The_variable_center_opens_the_global_editor()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "manageGlobals" };
            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            window.FindControl<Button>("ManageGlobalsButton")!
                .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Dispatcher.UIThread.RunJobs();

            var dialog = Assert.Single(window.OwnedWindows.OfType<GlobalVariablesWindow>());
            dialog.Close();
            Dispatcher.UIThread.RunJobs();
            window.Close(null);
        });
    }

    [Fact]
    public void New_reuses_the_blank_draft_and_focuses_its_name()
    {
        Ui.Run(() =>
        {
            var window = new GlobalVariablesWindow([], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var manager = (GlobalVariablesViewModel)window.DataContext!;
            var newButton = window.FindControl<Button>("NewButton")!;

            Assert.NotNull(newButton.Command);
            newButton.Command.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Dispatcher.UIThread.RunJobs();

            Assert.Single(manager.Rows);
            Assert.True(window.FindControl<TextBox>("NameBox")!.IsFocused);

            newButton.Command.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Single(manager.Rows);

            manager.RemoveCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            window.Close();
            Dispatcher.UIThread.RunJobs();
        });
    }

    [Fact]
    public void Saving_an_invalid_form_focuses_the_first_problem_field()
    {
        Ui.Run(() =>
        {
            var window = new GlobalVariablesWindow(
            [
                new VariableDefinition
                {
                    Name = "not a name",
                    Scope = VariableScope.Global,
                },
            ], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var manager = (GlobalVariablesViewModel)window.DataContext!;

            manager.SaveCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Dispatcher.UIThread.RunJobs();

            Assert.True(window.FindControl<TextBox>("NameBox")!.IsFocused);
            Assert.True(window.IsVisible);
            window.Close();
            Dispatcher.UIThread.RunJobs();
        });
    }

    [Fact]
    public void Removing_a_variable_asks_before_touching_the_draft()
    {
        Ui.RunAsync(async () =>
        {
            var window = new GlobalVariablesWindow(
            [
                new VariableDefinition
                {
                    Name = "total",
                    Scope = VariableScope.Global,
                },
            ], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var manager = (GlobalVariablesViewModel)window.DataContext!;

            window.FindControl<Button>("RemoveButton")!
                .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            await Ui.Until(() => window.OwnedWindows.OfType<ConfirmDialog>().Any(),
                "removing a variable did not ask first");

            var confirm = Assert.Single(window.OwnedWindows.OfType<ConfirmDialog>());
            var prompt = (ConfirmDialogViewModel)confirm.DataContext!;
            Assert.Equal(Strings.Get("GlobalVariables.DeleteTitle"), prompt.Header);
            prompt.CancelCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            await Ui.Until(() => !window.OwnedWindows.Any(), "the prompt did not close");

            Assert.Single(manager.Rows);
            Assert.True(window.IsVisible);
            window.Close();
            Dispatcher.UIThread.RunJobs();
            return true;
        });
    }

    [Fact]
    public void Closing_dirty_work_can_be_cancelled_or_discarded()
    {
        Ui.RunAsync(async () =>
        {
            var window = new GlobalVariablesWindow([], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var manager = (GlobalVariablesViewModel)window.DataContext!;
            manager.NewCommand.Execute(null);
            manager.Selected!.Name = "kept";
            Dispatcher.UIThread.RunJobs();

            window.Close(null);
            Dispatcher.UIThread.RunJobs();
            await Ui.Until(() => window.OwnedWindows.OfType<ConfirmDialog>().Any(),
                "closing a window with unsaved work did not ask anything");

            var first = Assert.Single(window.OwnedWindows.OfType<ConfirmDialog>());
            var firstPrompt = (ConfirmDialogViewModel)first.DataContext!;
            Assert.Equal(Strings.Get("GlobalVariables.UnsavedTitle"), firstPrompt.Header);
            firstPrompt.CancelCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            await Ui.Until(() => !window.OwnedWindows.Any(), "the prompt did not close");
            Assert.True(window.IsVisible);

            window.Close(null);
            Dispatcher.UIThread.RunJobs();
            await Ui.Until(() => window.OwnedWindows.OfType<ConfirmDialog>().Any(),
                "closing it a second time did not ask again");

            var second = Assert.Single(window.OwnedWindows.OfType<ConfirmDialog>());
            var secondPrompt = (ConfirmDialogViewModel)second.DataContext!;
            secondPrompt.SecondaryCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            await Ui.Until(() => !window.IsVisible, "discarding did not close the window");
            return true;
        });
    }

    [Fact]
    public void Saving_from_the_close_prompt_saves_the_draft()
    {
        Ui.RunAsync(async () =>
        {
            var window = new GlobalVariablesWindow([], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var manager = (GlobalVariablesViewModel)window.DataContext!;
            IReadOnlyList<VariableDefinitionDraft>? saved = null;
            manager.Saved += rows => saved = rows;
            manager.NewCommand.Execute(null);
            manager.Selected!.Name = "fromClose";
            Dispatcher.UIThread.RunJobs();

            window.Close(null);
            Dispatcher.UIThread.RunJobs();
            await Ui.Until(() => window.OwnedWindows.OfType<ConfirmDialog>().Any(),
                "closing a window with unsaved work did not ask anything");
            var confirm = Assert.Single(window.OwnedWindows.OfType<ConfirmDialog>());
            var prompt = (ConfirmDialogViewModel)confirm.DataContext!;
            prompt.PrimaryCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            await Ui.Until(() => !window.IsVisible, "saving from the prompt did not close the window");

            Assert.Equal("fromClose", Assert.Single(saved!).Name);
            Assert.False(window.IsVisible);
            return true;
        });
    }

    [Fact]
    public void The_window_uses_the_project_dialog_shell_and_one_status_area()
    {
        var xaml = File.ReadAllText(Path.Combine(Repository(), "WhaleGenie", "Views",
            "GlobalVariablesWindow.axaml"));

        Assert.Contains("WindowDecorations=\"BorderOnly\"", xaml, System.StringComparison.Ordinal);
        Assert.Contains("Name=\"CloseButton\"", xaml, System.StringComparison.Ordinal);
        Assert.Contains("Name=\"SearchBox\"", xaml, System.StringComparison.Ordinal);
        Assert.Contains("Name=\"ProblemStatus\"", xaml, System.StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"{Binding NamePrompt}\"", xaml, System.StringComparison.Ordinal);
        Assert.Contains("Gesture=\"Ctrl+S\"", xaml, System.StringComparison.Ordinal);
        Assert.DoesNotContain("IsEnabled=\"{Binding CanSave}\"", xaml,
            System.StringComparison.Ordinal);
    }

    [Fact]
    public void The_manager_saves_a_dirty_current_value_and_its_persistence_flag()
    {
        Ui.RunAsync(async () =>
        {
            var folder = Path.Combine(Path.GetTempPath(), "whalegenie-tests",
                Guid.NewGuid().ToString("N"));
            using var persistence = PersistentVariables.UsePathForTests(
                Path.Combine(folder, "persistent-variables.json"));
            var previousGlobals = VariableCatalog.Globals.ToList();
            var previousValues = SharedVariables.Current.Values;
            VariableCatalog.Globals.Clear();
            SharedVariables.Current.Clear();
            VariableCatalog.Globals.Add(new VariableDefinition
            {
                Name = "editedByManager",
                Scope = VariableScope.Global,
                Type = "number",
                DefaultValue = "1",
            });
            try
            {
                var macro = new MacroItem { Name = "managerSave" };
                var window = new MacroEditorWindow(macro, [macro]);
                window.Show();
                Dispatcher.UIThread.RunJobs();
                window.FindControl<Button>("ManageGlobalsButton")!
                    .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();

                var dialog = Assert.Single(window.OwnedWindows.OfType<GlobalVariablesWindow>());
                var manager = (GlobalVariablesViewModel)dialog.DataContext!;
                manager.Selected!.CurrentValue = "9";
                manager.Selected.PersistValue = true;
                Assert.True(manager.CanSave, manager.ValidationMessage);
                manager.SaveCommand.Execute(null);
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(50);
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(9, SharedVariables.Current.Values["editedByManager"].AsNumber());
                Assert.True(VariableCatalog.FindGlobal("editedByManager")!.PersistValue);
                PersistentVariables.Flush();
                Assert.True(PersistentVariables.TryGet("editedByManager", out var saved));
                Assert.Equal("9", saved);
                window.Close(null);
                return true;
            }
            finally
            {
                VariableCatalog.Globals.Clear();
                foreach (var global in previousGlobals)
                {
                    VariableCatalog.Globals.Add(global);
                }

                SharedVariables.Current.Clear();
                foreach (var (name, value) in previousValues)
                {
                    SharedVariables.Current.Set(name, value);
                }
            }
        });
    }

    [Fact]
    public void Renaming_a_global_updates_the_open_macro_and_keeps_its_value()
    {
        Ui.RunAsync(async () =>
        {
            var previousGlobals = VariableCatalog.Globals.ToList();
            var previousValues = SharedVariables.Current.Values;
            VariableCatalog.Globals.Clear();
            SharedVariables.Current.Clear();
            VariableCatalog.Globals.Add(new VariableDefinition
            {
                Name = "oldGlobal",
                Scope = VariableScope.Global,
                Type = "number",
                DefaultValue = "4",
            });
            SharedVariables.Current.SetNumber("oldGlobal", 6);
            try
            {
                var macro = new MacroItem { Name = "renameGlobal" };
                macro.Steps.Add(new MacroStep
                {
                    Type = "control.log",
                    Parameters =
                    [
                        new StepParameter
                        {
                            Name = "message",
                            Kind = ActionParameterKind.Expression,
                            Value = "$oldGlobal + 1",
                        },
                    ],
                });
                var window = new MacroEditorWindow(macro, [macro]);
                window.Show();
                Dispatcher.UIThread.RunJobs();
                window.FindControl<Button>("ManageGlobalsButton")!
                    .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();

                var dialog = Assert.Single(window.OwnedWindows.OfType<GlobalVariablesWindow>());
                var manager = (GlobalVariablesViewModel)dialog.DataContext!;
                manager.Selected!.Name = "newGlobal";
                Assert.True(manager.CanSave, manager.ValidationMessage);
                manager.SaveCommand.Execute(null);
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(50);
                Dispatcher.UIThread.RunJobs();

                Assert.NotNull(VariableCatalog.FindGlobal("newGlobal"));
                Assert.Null(VariableCatalog.FindGlobal("oldGlobal"));
                Assert.Equal(6, SharedVariables.Current.Values["newGlobal"].AsNumber());
                var root = (MacroEditorViewModel)window.DataContext!;
                Assert.Equal("$newGlobal + 1", root.Steps[0].Text("message"));
                window.Close(null);
                return true;
            }
            finally
            {
                VariableCatalog.Globals.Clear();
                foreach (var global in previousGlobals)
                {
                    VariableCatalog.Globals.Add(global);
                }

                SharedVariables.Current.Clear();
                foreach (var (name, value) in previousValues)
                {
                    SharedVariables.Current.Set(name, value);
                }
            }
        });
    }

    [Fact]
    public void Closing_the_manager_without_save_changes_nothing()
    {
        Ui.Run(() =>
        {
            var previousGlobals = VariableCatalog.Globals.ToList();
            var previousValues = SharedVariables.Current.Values;
            VariableCatalog.Globals.Clear();
            SharedVariables.Current.Clear();
            var definition = new VariableDefinition
            {
                Name = "unchanged",
                Scope = VariableScope.Global,
                Type = "number",
                DefaultValue = "1",
            };
            VariableCatalog.Globals.Add(definition);
            SharedVariables.Current.SetNumber("unchanged", 3);
            try
            {
                var macro = new MacroItem { Name = "managerCancel" };
                var window = new MacroEditorWindow(macro, [macro]);
                window.Show();
                Dispatcher.UIThread.RunJobs();
                window.FindControl<Button>("ManageGlobalsButton")!
                    .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();

                var dialog = Assert.Single(window.OwnedWindows.OfType<GlobalVariablesWindow>());
                var manager = (GlobalVariablesViewModel)dialog.DataContext!;
                manager.Selected!.Name = "changed";
                manager.Selected.DefaultValue = "8";
                manager.Selected.CurrentValue = "12";
                manager.Selected.PersistValue = true;
                dialog.Close(null);
                Dispatcher.UIThread.RunJobs();

                // The manager asks about its staged changes before it goes, and that question is
                // modal. Leaving it standing would both keep the editor from closing cleanly —
                // it is the manager's owner — and leave a dispatcher loop running for whichever
                // check is next. "Discard" is the answer this check is after: nothing staged is
                // written either way, and the window really does go.
                var prompt = Assert.Single(dialog.OwnedWindows.OfType<ConfirmDialog>());
                ((ConfirmDialogViewModel)prompt.DataContext!).SecondaryCommand.Execute(null);
                Dispatcher.UIThread.RunJobs();

                Assert.NotNull(VariableCatalog.FindGlobal("unchanged"));
                Assert.Null(VariableCatalog.FindGlobal("changed"));
                Assert.Equal(3, SharedVariables.Current.Values["unchanged"].AsNumber());

                Ui.CloseAndAnswer(window);
            }
            finally
            {
                VariableCatalog.Globals.Clear();
                foreach (var global in previousGlobals)
                {
                    VariableCatalog.Globals.Add(global);
                }

                SharedVariables.Current.Clear();
                foreach (var (name, value) in previousValues)
                {
                    SharedVariables.Current.Set(name, value);
                }
            }
        });
    }

    [Fact]
    public void Saving_untouched_rows_does_not_overwrite_a_newer_running_value()
    {
        Ui.RunAsync(async () =>
        {
            var previousGlobals = VariableCatalog.Globals.ToList();
            var previousValues = SharedVariables.Current.Values;
            VariableCatalog.Globals.Clear();
            SharedVariables.Current.Clear();
            VariableCatalog.Globals.Add(new VariableDefinition
            {
                Name = "liveOnly",
                Scope = VariableScope.Global,
                Type = "number",
                DefaultValue = "1",
            });
            SharedVariables.Current.SetNumber("liveOnly", 1);
            try
            {
                var macro = new MacroItem { Name = "untouchedCurrent" };
                var window = new MacroEditorWindow(macro, [macro]);
                window.Show();
                Dispatcher.UIThread.RunJobs();
                window.FindControl<Button>("ManageGlobalsButton")!
                    .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();

                var dialog = Assert.Single(window.OwnedWindows.OfType<GlobalVariablesWindow>());
                var manager = (GlobalVariablesViewModel)dialog.DataContext!;
                SharedVariables.Current.SetNumber("liveOnly", 11);
                manager.Selected!.Description = "Edited without touching its value";
                manager.SaveCommand.Execute(null);
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(50);
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(11, SharedVariables.Current.Values["liveOnly"].AsNumber());
                window.Close(null);
                return true;
            }
            finally
            {
                VariableCatalog.Globals.Clear();
                foreach (var global in previousGlobals)
                {
                    VariableCatalog.Globals.Add(global);
                }

                SharedVariables.Current.Clear();
                foreach (var (name, value) in previousValues)
                {
                    SharedVariables.Current.Set(name, value);
                }
            }
        });
    }

    private static string Repository()
    {
        for (var at = new DirectoryInfo(System.AppContext.BaseDirectory); at is not null; at = at.Parent)
        {
            if (File.Exists(Path.Combine(at.FullName, "WhaleGenie.slnx")))
            {
                return at.FullName;
            }
        }

        throw new System.InvalidOperationException("WhaleGenie.slnx was not found above the test binaries.");
    }
}
