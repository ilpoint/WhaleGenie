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

// Taking a global away, which asks before it touches the draft.
public partial class GlobalVariablesTests
{
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
}
