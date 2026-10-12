using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// The run-and-debug window: it debugs one macro and manages the variables the project can use, and
/// which of the two it is showing follows from how it was opened. Opened from the button on its own
/// there is nothing to run, so the run controls are not drawn and the line that says where a debug
/// run starts from stands in their place.
/// </summary>
public class RunDebugTests
{
    [Fact]
    public void Opened_on_its_own_it_shows_the_variables_and_no_run_controls()
    {
        Ui.Run(() =>
        {
            var window = new RunWindow([]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (RunViewModel)window.DataContext!;

            Assert.True(viewModel.IsVariablesPage);
            Assert.False(viewModel.HasSteps);
            Assert.False(viewModel.CanStart);
            Assert.NotEmpty(viewModel.SystemVariables);

            Assert.False(Find<Border>(window, "RunToolbar").IsVisible);
            Assert.False(Find<Border>(window, "PageTabs").IsVisible);
            Assert.True(Find<TextBlock>(window, "DebugFromCardNote").IsVisible);

            window.Close();
        });
    }

    [Fact]
    public void Opened_on_a_macro_it_shows_the_steps_and_can_switch_to_the_variables()
    {
        Ui.Run(() =>
        {
            var window = new RunWindow(Steps("probe"));
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (RunViewModel)window.DataContext!;

            Assert.True(viewModel.IsDebugPage);
            Assert.True(viewModel.HasSteps);
            Assert.True(Find<Border>(window, "RunToolbar").IsVisible);
            Assert.False(Find<TextBlock>(window, "DebugFromCardNote").IsVisible);

            viewModel.SelectPageCommand.Execute("Variables");
            Dispatcher.UIThread.RunJobs();

            Assert.True(viewModel.IsVariablesPage);
            Assert.False(viewModel.IsDebugPage);

            viewModel.SelectPageCommand.Execute("Debug");
            Assert.True(viewModel.IsDebugPage);

            window.Close();
        });
    }

    /// <summary>
    /// The same window debugs the second macro: there is one of these at a time, and the run that
    /// was going is stopped to make room for the other macro's steps. Why it stopped is written into
    /// the log, because a run that ends early by itself looks like a failure.
    /// </summary>
    [Fact]
    public void Debugging_another_macro_stops_the_run_and_says_why()
    {
        Ui.RunAsync(async () =>
        {
            var first = new MacroItem { Name = "first" };
            first.Steps.Add(SlowStep(5000));

            var second = new MacroItem { Name = "second" };
            second.Steps.Add(SlowStep(5000));

            var viewModel = new RunViewModel([], NullDeviceLayer.Instance, 1, null, [first, second]);

            await viewModel.DebugAsync(first);
            Assert.True(viewModel.IsBusy);
            Assert.Equal("first", viewModel.Debugged);
            Assert.Equal("first", first.Name);
            Assert.True(viewModel.HasSteps);
            Assert.Contains("first", viewModel.WindowTitle, System.StringComparison.Ordinal);

            await viewModel.DebugAsync(second);
            Assert.Equal("second", viewModel.Debugged);

            // What the log opens with is the reason the run before it ended.
            Assert.Contains(viewModel.Lines,
                line => line.Text == WhaleGenie.Localization.Strings.Format(
                    "Run.DebugSwitched", "first", "second"));

            // Asking for the macro that is already running leaves it alone rather than starting it
            // again, which would throw away the run in progress.
            var going = viewModel.Lines.Count;
            await viewModel.DebugAsync(second);
            Assert.Equal(going, viewModel.Lines.Count);

            viewModel.StopCommand.Execute(null);
            while (viewModel.IsBusy)
            {
                await Task.Delay(10);
            }

            return true;
        });
    }

    /// <summary>A macro of one step that waits long enough for a check to catch it running.</summary>
    private static MacroStep SlowStep(int milliseconds) => new()
    {
        Type = "control.delay",
        Parameters =
        [
            new StepParameter { Name = "ms", Kind = ActionParameterKind.Number, Value = $"{milliseconds}" },
        ],
    };

    private static IEnumerable<MacroStep> Steps(string name)
    {
        var step = SlowStep(5);
        StepIds.Settle([step]);
        return [step];
    }

    private static T Find<T>(Window window, string name)
        where T : Control
        => window.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);
}
