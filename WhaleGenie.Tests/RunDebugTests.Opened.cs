using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;


namespace WhaleGenie.Tests;

// What the window shows when it opens: the debugger with a macro, a direct run, or nothing to run.
public partial class RunDebugTests
{
    /// <summary>
    /// A retried step is more than one attempt, and the debugger says so: each try's time and the
    /// budget left after it are what make a timeout and a retry readable after the run.
    /// </summary>
    [Fact]
    public void The_debugger_shows_each_try_and_what_was_left_of_the_budget()
    {
        var row = new RunStepViewModel
        {
            Id = "k3f9",
            Label = "Wait",
            Detail = string.Empty,
            Depth = 0,
            CanBreak = true,
        };

        row.Took(new StepRun
        {
            StepId = "k3f9",
            StepType = "control.delay",
            Outcome = "failed",
            ElapsedMs = 150,
            FailureKey = "Run.Timeout",
            FailureDetail = "300",
            TimeoutMs = 300,
            Attempts =
            [
                new StepAttempt
                {
                    Number = 1,
                    ElapsedMs = 100,
                    Outcome = "failed",
                    FailureKey = "Run.NoDevice",
                    RemainingMs = 200,
                },
                new StepAttempt
                {
                    Number = 2,
                    ElapsedMs = 50,
                    Outcome = "failed",
                    FailureKey = "Run.Timeout",
                    RemainingMs = 0,
                    Overran = true,
                },
            ],
        });

        Assert.True(row.HasAttempts);
        Assert.Equal(2, row.Attempts.Count);
        Assert.Equal(Strings.Format("Run.StepAttempts", 2), row.AttemptsHeader);
        Assert.True(row.Attempts[0].HasFailure);
        Assert.Contains("Run.NoDevice", row.Attempts[0].Failure, StringComparison.Ordinal);
        Assert.Contains(Strings.Get("Run.AttemptOverran"), row.Attempts[1].Failure,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_direct_run_asks_for_the_declared_parameters()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "有参数" };
            macro.Parameters.Add(new MacroParameter
            {
                Name = "数量",
                Required = true,
                Type = ActionReturnType.Number,
                Description = "要做多少次。",
            });
            macro.Parameters.Add(new MacroParameter
            {
                Name = "倍数",
                Required = false,
                Default = "3",
                Type = ActionReturnType.Number,
            });
            macro.Steps.Add(new MacroStep
            {
                Type = "control.delay",
                Meta = StepMeta.New,
                Parameters =
                [
                    new StepParameter
                    {
                        Name = "ms",
                        Kind = ActionParameterKind.Number,
                        Value = "0",
                    },
                ],
            });

            var window = new RunWindow(macro.Steps, callable: macro.Callable);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (RunViewModel)window.DataContext!;
            Assert.True(viewModel.HasArguments);
            Assert.Equal(["数量", "倍数"], viewModel.Arguments.Select(row => row.Name));
            Assert.True(window.FindControl<Border>("RunArgumentsPanel") is { IsVisible: true });

            var required = viewModel.Arguments[0];
            var optional = viewModel.Arguments[1];
            Assert.True(required.IsMissing);
            Assert.False(viewModel.CanStart);

            // The default belongs to the declaration: the row starts empty and says what it is.
            Assert.Equal(string.Empty, optional.Value);
            Assert.Contains("3", optional.Hint, StringComparison.Ordinal);
            Assert.False(optional.IsMissing);

            required.Value = "5";
            Dispatcher.UIThread.RunJobs();
            Assert.True(viewModel.CanStart);

            // A required field can still say "I mean an empty value".
            required.Value = string.Empty;
            required.Given = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(viewModel.CanStart);
        });
    }

    [Fact]
    public void Opened_on_its_own_it_shows_the_variables_and_no_run_controls()
    {
        Ui.Run(() =>
        {
            var window = new RunWindow([]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (RunViewModel)window.DataContext!;

            Assert.False(viewModel.HasSteps);
            Assert.False(viewModel.CanStart);
            Assert.Empty(viewModel.Variables);

            Assert.False(Find<Border>(window, "RunToolbar").IsVisible);
            Assert.True(Find<TextBlock>(window, "DebugFromCardNote").IsEffectivelyVisible);

            window.Close();
        });
    }

    [Fact]
    public void Opened_on_a_macro_it_shows_the_steps_and_the_watch_list()
    {
        Ui.Run(() =>
        {
            var window = new RunWindow(Steps("probe"));
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (RunViewModel)window.DataContext!;

            Assert.True(viewModel.HasSteps);

            // 装着步骤开着也不跑：谁想先看一眼、先下个断点，窗口不该已经动手了。
            Assert.False(viewModel.IsBusy);
            Assert.Equal(Strings.Get("Run.Ready"), viewModel.Status);
            Assert.True(viewModel.CanStart);
            Assert.True(viewModel.CanStep);

            Assert.True(Find<Border>(window, "RunToolbar").IsVisible);
            Assert.False(Find<TextBlock>(window, "DebugFromCardNote").IsEffectivelyVisible);

            window.Close();
        });
    }
}
