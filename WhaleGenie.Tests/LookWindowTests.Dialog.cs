using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.GraphicsCapture;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;


namespace WhaleGenie.Tests;

// The dialog the window is opened from, and the step it runs.
public partial class LookWindowTests
{
    /// <summary>
    /// The dialog carries one button that runs the step being written, so a step can be tried
    /// without closing the macro and going into a run. It is there for the steps a run can do — a
    /// condition is not a step of the macro, so there is nothing to run — and it waits until the
    /// step is complete, because half a step is not the step the user means yet.
    /// </summary>
    [Fact]
    public void The_dialog_runs_the_step_it_is_editing()
    {
        Ui.Run(() =>
        {
            var window = new ActionEditorWindow(null, ActionCatalog.Definitions,
                VariableChoicesForChecks.Named("match"), []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;
            Assert.False(viewModel.ShowsRun);

            viewModel.SelectAction("input.keyPress");
            Dispatcher.UIThread.RunJobs();
            Assert.True(viewModel.ShowsRun);

            // The key is still empty, which is a step that would send nothing.
            Assert.False(viewModel.CanRun);
            Assert.False(FindRunButton(window).IsEnabled);

            viewModel.Parameters.First(parameter => parameter.Definition.Name == "key").Text = "F5";
            Dispatcher.UIThread.RunJobs();
            Assert.True(viewModel.CanRun);
            Assert.True(FindRunButton(window).IsEnabled);
            Assert.Equal(Strings.Get("Add.RunStep"), FindRunButton(window).Content);

            // Blocks are runnable steps now; their condition is edited inside the same form.
            viewModel.SelectAction("control.if");
            Dispatcher.UIThread.RunJobs();
            Assert.True(viewModel.ShowsRun);
        });
    }
}
