using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;


namespace WhaleGenie.Tests;

// Closing without changes, and a save that must not overwrite a newer run.
public partial class GlobalVariablesTests
{
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
                Type = ValueKind.Number,
                Default = Formula.Text("1"),
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
                manager.Selected.Default = Formula.Text("8");
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
                Type = ValueKind.Number,
                Default = Formula.Text("1"),
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
                Ui.CloseAndDiscard(window);
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
