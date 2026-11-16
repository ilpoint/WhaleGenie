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

// What a run reads while the editor owns the draft, and the live value it shows.
public partial class GlobalVariablesTests
{
    [Fact]
    public void Run_debug_reads_globals_while_the_variable_center_owns_the_editor()
    {
        // The debug window shows the variables the macro in front of it uses, and nothing else:
        // creating or editing a global belongs to the variable center, not to a run.
        var run = File.ReadAllText(ViewSource.PathOf("RunWindow.axaml"));
        Assert.Contains("ItemsSource=\"{Binding Variables}\"", run, System.StringComparison.Ordinal);
        Assert.DoesNotContain("AddGlobalCommand", run, System.StringComparison.Ordinal);
        Assert.DoesNotContain("RemoveGlobalCommand", run, System.StringComparison.Ordinal);
        Assert.DoesNotContain("NewPersistValue", run, System.StringComparison.Ordinal);

        var editor = File.ReadAllText(ViewSource.PathOf("MacroEditorWindow.axaml"));
        Assert.Contains("Name=\"ManageGlobalsButton\"", editor, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Run_debug_lists_the_live_global_value_without_an_editor()
    {
        var session = new VariableSession();
        session.Adopt(new VariableDefinition
        {
            Name = "liveTotal",
            Scope = VariableScope.Global,
            Type = ValueKind.Number,
            Default = Formula.Text("1"),
            PersistValue = true,
        });

        Ui.Run(() =>
        {
            var window = new RunWindow([], new ChecksDevices(), variables: session);
            window.Show();
            var viewModel = (RunViewModel)window.DataContext!;
            viewModel.SelectPageCommand.Execute("Variables");
            viewModel.SelectTabCommand.Execute("Global");
            session.Values.SetNumber("liveTotal", 9);
            Dispatcher.UIThread.RunJobs();

            var row = viewModel.Globals.Single(variable => variable.Name == "liveTotal");
            Assert.Equal("9", row.CurrentValue);
            Assert.True(row.PersistValue);
            window.Close();
        });
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
            Ui.CloseAndDiscard(window);
        });
    }
}
