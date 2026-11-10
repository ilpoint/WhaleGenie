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

// The new-row button, and where a bad form puts the focus.
public partial class GlobalVariablesTests
{
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
}
