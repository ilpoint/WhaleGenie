using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.Models;
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
            var session = new VariableSession();
            session.Adopt(new VariableDefinition
            {
                Name = "unchanged",
                Scope = VariableScope.Global,
                Type = ValueKind.Number,
                Default = Formula.Text("1"),
            });
            session.Values.SetNumber("unchanged", 3);

            var macro = new MacroItem { Name = "managerCancel" };
            var window = new MacroEditorWindow(macro, [macro], variables: session);
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

            Assert.NotNull(session.FindGlobal("unchanged"));
            Assert.Null(session.FindGlobal("changed"));
            Assert.Equal(3, session.Values.Values["unchanged"].AsNumber());

            Ui.CloseAndAnswer(window);
        });
    }

    [Fact]
    public void Saving_untouched_rows_does_not_overwrite_a_newer_running_value()
    {
        Ui.RunAsync(async () =>
        {
            var session = new VariableSession();
            session.Adopt(new VariableDefinition
            {
                Name = "liveOnly",
                Scope = VariableScope.Global,
                Type = ValueKind.Number,
                Default = Formula.Text("1"),
            });
            session.Values.SetNumber("liveOnly", 1);

            var macro = new MacroItem { Name = "untouchedCurrent" };
            var window = new MacroEditorWindow(macro, [macro], variables: session);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.FindControl<Button>("ManageGlobalsButton")!
                .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            var dialog = Assert.Single(window.OwnedWindows.OfType<GlobalVariablesWindow>());
            var manager = (GlobalVariablesViewModel)dialog.DataContext!;
            session.Values.SetNumber("liveOnly", 11);
            manager.Selected!.Description = "Edited without touching its value";
            manager.SaveCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(11, session.Values.Values["liveOnly"].AsNumber());
            Ui.CloseAndDiscard(window);
            return true;
        });
    }

    private static string Repository()
    {
        for (var at = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory); at is not null; at = at.Parent)
        {
            if (File.Exists(Path.Combine(at.FullName, "WhaleGenie.slnx")))
            {
                return at.FullName;
            }
        }

        throw new System.InvalidOperationException("WhaleGenie.slnx was not found above the test binaries.");
    }
}
