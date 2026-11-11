using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;

namespace WhaleGenie.ViewModels;

// The buttons: run, step, continue, skip, stop, breakpoints and the answers to a question.
public partial class RunViewModel
{
    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Run()
    {
        // Run goes all the way through, only stopping where a breakpoint waits.
        _stopAtNextStep = false;
        _ = RunAsync();
    }

    [RelayCommand(CanExecute = nameof(CanStep))]
    private void Step()
    {
        _stopAtNextStep = true;

        if (IsBusy)
        {
            Release();
        }
        else
        {
            _ = RunAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Continue()
    {
        _stopAtNextStep = false;
        Release();
    }

    /// <summary>
    /// Carries on without carrying out the step the run is standing in front of. It is what a pause
    /// is for other than looking: a step that would do something the user does not want done — on
    /// this machine, with these values — is left out, and the run goes on to whatever comes next.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsPaused))]
    private void SkipStep()
    {
        if (_gate is null)
        {
            // Nothing is waiting in front of a step, so there is no step to leave out: setting the
            // flag now would leave out whichever step happened to run next.
            return;
        }

        _skipThisStep = true;
        Release();
    }

    /// <summary>True while a run is waiting in front of a step for the user to say what happens next.</summary>
    public bool IsPaused => _gate is not null;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop()
    {
        _stopAtNextStep = false;
        _cancellation?.Cancel();
        Release();
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke();

    [RelayCommand]
    private void ToggleBreakpoint(RunStepViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        row.HasBreakpoint = !row.HasBreakpoint;
        if (row.HasBreakpoint)
        {
            _breakpoints.Add(row.Id);
        }
        else
        {
            _breakpoints.Remove(row.Id);

            // A breakpoint that is gone has nothing left to wait for: a rule kept behind it would
            // turn up again on the next breakpoint put on the same step.
            _breakpointRules.Remove(row.Id);
            _breakpointHits.Remove(row.Id);
            row.Waits(null);
        }
    }

    /// <summary>
    /// Gives a step's breakpoint what to wait for, turning the breakpoint on with it: how many
    /// arrivals count and an expression that has to hold are both about the breakpoint, and setting
    /// either on a step that has no breakpoint means the step should stop when they say so.
    /// </summary>
    public void SetBreakpointRule(RunStepViewModel? row, BreakpointRule rule)
    {
        if (row is null)
        {
            return;
        }

        _breakpointHits.Remove(row.Id);
        if (rule.Always)
        {
            _breakpointRules.Remove(row.Id);
        }
        else
        {
            _breakpointRules[row.Id] = rule;
        }

        _breakpoints.Add(row.Id);
        row.HasBreakpoint = true;
        row.Waits(rule);
    }

    /// <summary>Puts a breakpoint back to stopping every time it is reached.</summary>
    public void ClearBreakpointRule(RunStepViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        _breakpointRules.Remove(row.Id);
        _breakpointHits.Remove(row.Id);
        row.Waits(null);
    }

    /// <summary>What a step's breakpoint waits for, or null while it stops every time.</summary>
    public BreakpointRule? BreakpointRuleOf(string stepId)
        => _breakpointRules.TryGetValue(stepId, out var rule) ? rule : null;

    /// <summary>
    /// A condition written in the panel, as the one step the engine asks its questions with. The
    /// `if` action is where a condition already lives, so a condition written here reads exactly the
    /// way one written in the macro does.
    /// </summary>
    private static ExecutableStep ConditionStep(string expression) => new()
    {
        Type = "control.if",
        Parameters =
        [
            new ExecutableParameter
            {
                Name = "condition",
                // A breakpoint asks a question, so its line is read as the formula it is.
                Written = Formula.FromFormula(expression.Trim()),
            },
        ],
    };

    /// <summary>
    /// The names a breakpoint's condition may use: the same ones the macro's own fields are offered,
    /// which is what makes a condition written here read the way the rest of the macro reads.
    /// </summary>
    public IReadOnlyList<VariableChoice> Choices => _choices;

    /// <summary>
    /// The devices this run is on, so a dialog opened from the window tries its work out on the same
    /// machine — and, in a check, on the same stand-in.
    /// </summary>
    internal IDeviceLayer Devices => _devices;

    /// <summary>Answers the question a failed step asked.</summary>
    [RelayCommand]
    private void Answer(string? choice)
    {
        _answer?.TrySetResult(choice switch
        {
            "retry" => StepErrorChoice.Retry,
            "skip" => StepErrorChoice.Skip,
            _ => StepErrorChoice.Stop,
        });
    }

    /// <summary>Answers the question the macro itself asked.</summary>
    [RelayCommand]
    private void AnswerUser(string? choice)
        => _asked?.TrySetResult(choice == "ok"
            ? new AskAnswer(true, AnswerText)
            : new AskAnswer(false, AnswerText));
}
