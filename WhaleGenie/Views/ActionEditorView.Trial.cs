using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

// Trying the step out: what it would run, the two ways it can be tried, and the windows put away first.
public partial class ActionEditorView : UserControl
{
    /// <summary>
    /// 试运行要跑的那一步：存的是它写下来的样子，但"沿用上一次"在单步里没有上一步可跟，换成它
    /// 会跟到的那条规则——试的才是运行时真正会碰的那个窗口。存的仍旧是原来的写法。
    /// </summary>
    private static MacroStep BuildStepForTrial(AddActionViewModel viewModel)
    {
        var built = viewModel.BuildStep();
        if (viewModel.WindowAbove.Length == 0)
        {
            return built;
        }

        foreach (var field in built.Parameters.Where(field =>
                     string.Equals(field.Name, "window", StringComparison.Ordinal)
                     && WindowRule.TryParse(field.Value, out var rule) && rule.Follow))
        {
            field.Written = Formula.Text(viewModel.WindowAbove);
        }

        return built;
    }

    /// <summary>Why a step could not be tried, in the interface's own words.</summary>
    private static string TrialFailure(RunResult outcome)
        => outcome.Detail.Length == 0
            ? Strings.Get(outcome.Key)
            : Strings.Format(outcome.Key, outcome.Detail);

    /// <summary>
    /// Runs the step being written, once, without closing the dialog or going into a run. What it
    /// does follows from what the step is: a step that looks at the screen only looks and shows what
    /// turned up — trying <c>vision.clickImage</c> for real would click on whatever the user has on
    /// screen — and every other step is sent for real, the same way the field's own test sends it.
    /// </summary>
    private async void OnRunStep(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AddActionViewModel viewModel || viewModel.IsRunning || !viewModel.CanRun)
        {
            return;
        }

        // The step as it would be saved, so what is run is what was written.
        var step = new[] { BuildStepForTrial(viewModel) }.ToExecutable()[0];

        // Some steps cannot be put back: a click of this button is one click, and one click should
        // not delete a folder or shut the machine down without being asked. Which steps those are
        // is said once, in the catalogue, and a whole run asks there too.
        if (ActionSemanticsCatalog.CannotBeUndone(step)
            && await ConfirmDialog.ShowAsync(HostWindow!, Strings.Get("Run.CannotBeUndoneTitle"),
                IrreversiblePrompt.Question(step.Type), Strings.Get("Common.Ok"), null, true,
                Strings.Get("Common.Cancel"), tone: ConfirmTone.Danger) is not ConfirmChoice.Primary)
        {
            return;
        }

        if (MacroRunner.CanLook(step.Type))
        {
            await RunLookingAsync(step);
            return;
        }

        await RunForRealAsync(viewModel, step);
    }

    /// <summary>
    /// Looks at the screen the way the step would and shows what turned up, without doing anything
    /// about it: no click, no key, no variable written.
    /// </summary>
    private async Task RunLookingAsync(ExecutableStep step)
    {
        // Off the thread that draws the window, and with this program's windows out of the picture:
        // reading the screen and matching a picture can take a moment, the dialog has to stay able
        // to come back when it is done, and a look that has the window asking in front of it reads
        // the window asking rather than what the user wanted to look at.
        var outcome = await AwayAsync(() => MacroRunner.LookOnce(step, Devices, new TrialFrames(this)));

        // Nothing is being read any more, so the frame comes down with the reading.
        _trialFrames.Clear();

        if (outcome.Look is null)
        {
            await ReportTestAsync(TrialFailure(
                new RunResult(RunStatus.Failed, outcome.Key, outcome.Detail, 0)));
            return;
        }

        // The step and the machine go along with the look, which is what lets this window look
        // again: what is being tried is this very step, and the threshold on it is usually the
        // thing being changed to get a better answer.
        LookWindow.Show(outcome.Look, HostWindow!, step, Devices);
    }

    /// <summary>
    /// Sends the step for real, the way a run would. Input steps are sent with every window of this
    /// program out of the way, because a key or a click lands on whatever is in front, and a window
    /// in front of the window the user meant would be one of ours.
    /// </summary>
    private async Task RunForRealAsync(AddActionViewModel viewModel, ExecutableStep step)
    {
        viewModel.IsRunning = true;
        RunResult outcome;
        try
        {
            // Off the thread that draws the window: a device call can wait on hardware, and the
            // dialog has to stay able to come back when it is done.
            outcome = await AwayForAsync(() => MacroRunner.TryAsync(step, Devices,
                variables: MacroVariables.Seed(new SystemVariableReader(Devices),
                    SharedVariables.Current),
                macros: Macros));
        }
        finally
        {
            viewModel.IsRunning = false;
        }

        if (outcome.Status is not RunStatus.Completed)
        {
            if (!_closed)
            {
                await ReportTestAsync(TrialFailure(outcome));
            }

            return;
        }

        Say(Strings.Format("Add.RanStep", viewModel.SelectedActionName));
    }

    /// <summary>
    /// Runs a trial with every window of this program off the screen, and brings them back when it
    /// is over — the same rule the pickers follow, and for the same reason: what a step looks at,
    /// clicks or types into is behind this window, and this window is the one thing that is always
    /// in the way. The screen is given a frame to be drawn again in first, because the picture read
    /// straight after hiding still has the hidden window in it.
    /// </summary>
    private async Task<T> AwayAsync<T>(Func<T> trial)
    {
        T outcome;
        using (var aside = AppWindows.StepAside(HostWindow))
        {
            await AppWindows.SettleAsync(Devices.Screen);
            outcome = await Task.Run(trial);
        }

        return outcome;
    }

    /// <summary>The same, for a trial that waits on devices rather than on nothing but the machine.</summary>
    private async Task<T> AwayForAsync<T>(Func<Task<T>> trial)
    {
        T outcome;
        using (var aside = AppWindows.StepAside(HostWindow))
        {
            await AppWindows.SettleAsync(Devices.Screen);
            outcome = await Task.Run(trial);
        }

        return outcome;
    }

    /// <summary>
    /// The same, for a trial that draws on the screen as it goes and so cannot leave this thread:
    /// what it puts up — a frame around a control, say — is not one of this program's windows to be
    /// hidden, it is the answer being shown.
    /// </summary>
    private async Task AwayWhileAsync(Func<Task> trial)
    {
        using (AppWindows.StepAside(HostWindow))
        {
            ScreenReady.Wait();
            await trial();
        }
    }

    /// <summary>Says what a run did, under the form, until the next thing the user does.</summary>
    private void Say(string message)
    {
        if (Form is not { } viewModel)
        {
            return;
        }

        viewModel.Say(message);

        _runTimer?.Stop();
        _runTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _runTimer.Tick += (_, _) =>
        {
            _runTimer?.Stop();
            _runTimer = null;
            viewModel.Say(string.Empty);
        };
        _runTimer.Start();
    }
}
