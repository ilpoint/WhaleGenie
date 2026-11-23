using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

// The run itself and what the engine calls back: before a step, at a breakpoint, and on failure.
public partial class RunViewModel
{
    private async Task RunAsync()
    {
        Lines.Clear();

        // Read again when the run starts rather than when the window opened: the line about the door
        // is about whichever run holds it right now.
        OnPropertyChanged(nameof(ShowsInputOwner));
        OnPropertyChanged(nameof(InputOwnerLine));
        OnPropertyChanged(nameof(InputRateOwner));

        // A run asked for after another one was stopped says why the first one ended: a run that
        // stopped by itself and one that was stopped on the way to this read the same otherwise.
        if (SwapNote.Length > 0)
        {
            var note = SwapNote;
            SwapNote = string.Empty;
            Lines.Add(new RunLineViewModel(
                DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                LogLevel.Warn, note));
        }

        IsBusy = true;
        Status = Strings.Get("Run.Running");
        StatusKindValue = RunStatusKind.Running;
        IsAsking = false;
        IsAskingUser = false;
        CurrentId = null;
        _reached.Clear();

        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        _capture = RunLogStore.Begin(Debugged, "debug");
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ended = ended;
        RunResult? result = null;

        try
        {
            var runner = RunFactory.Create(new RunRequest
            {
                Variables = _variables,
                Host = new PostedRunHost(this, _ui),
                Devices = _devices,
                Library = Library,
                Name = Debugged,
                Scale = _delayScale,
                InputRate = _inputRate,
                Looks = _capture is { } capture ? new RunLooks(capture, this) : this,
                StartSignature = _callable?.Signature,
                StartArguments = StartArguments(),
            });

            // Off the interface thread: a step that talks to a device waits for it right there in
            // the same call, and a page that has not drawn the element yet is waited on for the
            // browser's own half-minute. Doing that on the interface thread is half a minute of a
            // window that will not repaint — and of a stop button that cannot be pressed.
            result = await Task.Run(() => runner.RunAsync(_steps, cancellation.Token),
                cancellation.Token);
        }
        finally
        {
            _cancellation = null;
            _ended = null;
            ended.TrySetResult();
            IsBusy = false;
            _stopAtNextStep = false;
            CurrentId = null;

            // The run is over, so nothing is being read any more.
            WindowRead?.Invoke(null);

            RefreshVariables();
            RefreshStepOutcomes();
            Status = Lines.Count > 0 ? Lines[^1].Text : Strings.Get("Run.Ready");
            // 跑完了：成功与失败要分开，否则这一行永远一个颜色。
            StatusKindValue = result is { Succeeded: false } ? RunStatusKind.Failed : RunStatusKind.Done;
            _capture?.Finish(result, result?.Status.ToString() ?? Status);
            _capture = null;
        }
    }

    /// <summary>
    /// Highlights the step that is about to run and stops when asked to: single stepping, a
    /// breakpoint, or a breakpoint whose condition holds. The answer is whether the step is to be
    /// carried out at all, which is what the pause's "skip this step once" says no to.
    /// </summary>
    public async Task<bool> BeforeStep(ExecutableStep step, int depth, CancellationToken token)
    {
        // Whatever the last step was reading, this one is not reading it yet: the frame comes down
        // and goes up again when this step's own reading is reported.
        WindowRead?.Invoke(null);

        CurrentId = step.Id;
        if (_ownIds.Contains(step.Id))
        {
            _reached.Add(step.Id);
        }

        RefreshVariables();

        if (!_stopAtNextStep && !StopsHere(step.Id))
        {
            return true;
        }

        _stopAtNextStep = false;
        Status = Strings.Get("Run.Paused");
        StatusKindValue = RunStatusKind.Paused;

        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _gate = gate;
        OnPropertyChanged(nameof(IsPaused));
        SkipStepCommand.NotifyCanExecuteChanged();
        using var registration = token.Register(() => gate.TrySetResult(false));

        var go = await gate.Task;
        _gate = null;
        OnPropertyChanged(nameof(IsPaused));
        SkipStepCommand.NotifyCanExecuteChanged();
        Status = Strings.Get("Run.Running");
        StatusKindValue = RunStatusKind.Running;

        if (!go)
        {
            throw new OperationCanceledException(token);
        }

        var skip = _skipThisStep;
        _skipThisStep = false;
        return !skip;
    }

    /// <summary>
    /// Whether the run stops in front of this step: a breakpoint is a promise to stop, and one that
    /// carries a condition keeps that promise only while the condition holds. A condition that
    /// cannot be worked out at all stops the run too and says so in the log, because a breakpoint
    /// nobody can read is not the same thing as a breakpoint that has not been reached.
    /// </summary>
    internal bool StopsHere(string stepId)
    {
        if (!_breakpoints.Contains(stepId))
        {
            return false;
        }

        var rule = BreakpointRuleOf(stepId);
        var arrived = _breakpointHits[stepId] = _breakpointHits.GetValueOrDefault(stepId) + 1;
        if (rule is null)
        {
            return true;
        }

        // The count is kept whether or not the expression holds: "the third time the run passes
        // here and the value is 5" means the third arrival, not the third match.
        if (rule.WaitsForHits && arrived < rule.HitTimes)
        {
            return false;
        }

        if (!rule.WaitsForExpression)
        {
            return true;
        }

        var answer = MacroRunner.AskCondition(ConditionStep(rule.Expression), _variables,
            _devices, _macros);
        if (answer.Key.Length == 0)
        {
            return answer.Holds;
        }

        // A condition nobody can work out is not a reason to stop the run: a breakpoint that stops
        // at random is worse than one that says it could not read its own question and stays out of
        // the way.
        Write(Strings.Format("Run.BreakpointUnreadable",
            answer.Detail.Length == 0 ? Strings.Get(answer.Key) : Strings.Format(answer.Key, answer.Detail)));
        return false;
    }

    /// <summary>Writes one line into the log that did not come from the run itself.</summary>
    private void Write(string text)
        => Lines.Add(new RunLineViewModel(
            DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            LogLevel.Warn, text));

    /// <summary>Hands a failed step to the user, who picks what happens next.</summary>
    public async Task<StepErrorChoice> Ask(string step, string reason, string detail, string comment,
        CancellationToken token)
    {
        Question = FailedStepPrompt.Question(step, reason, detail, comment);
        IsAsking = true;

        var answer = new TaskCompletionSource<StepErrorChoice>(TaskCreationOptions.RunContinuationsAsynchronously);
        _answer = answer;
        using var registration = token.Register(() => answer.TrySetResult(StepErrorChoice.Stop));

        var choice = await answer.Task;
        _answer = null;
        IsAsking = false;
        return choice;
    }

    /// <summary>
    /// Puts the macro's own question on the run window and waits for the answer. The window is the
    /// one place a run under the debugger can be answered, and a run that is stopped while the
    /// question is up reads as unanswered, the same way a failure question reads as "stop".
    /// </summary>
    public async Task<AskAnswer> AskUser(AskQuestion question, CancellationToken token)
    {
        Question = question.Message;
        WantsTypedAnswer = question.Kind is AskKind.Text;
        AnswerText = question.Default;
        IsAskingUser = true;

        var answer = new TaskCompletionSource<AskAnswer>(TaskCreationOptions.RunContinuationsAsynchronously);
        _asked = answer;
        using var registration = token.Register(() => answer.TrySetResult(AskAnswer.Cancel));

        var given = await answer.Task;
        _asked = null;
        IsAskingUser = false;
        return given;
    }

    /// <summary>
    /// Puts the "this cannot be undone" question on the same panel the macro's own questions use,
    /// because that is the one place a run under the debugger can be answered. A run stopped while
    /// the question is up reads as no, the way an unanswered question of the macro's own reads as
    /// cancelled.
    /// </summary>
    public async Task<bool> ConfirmIrreversible(ExecutableStep step, CancellationToken token)
    {
        var answer = await AskUser(
            new AskQuestion(AskKind.Confirm,
                IrreversiblePrompt.Question(step.Type, step.Meta.Comment), string.Empty),
            token);
        return answer.Confirmed;
    }

    /// <summary>Writes one translated line into the log.</summary>
    public void Log(LogEntry entry)
    {
        var text = RunLogText.Of(entry);
        Lines.Add(new RunLineViewModel(
            entry.Time.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            entry.Level,
            text));
        _capture?.Add(entry, text);
    }

    /// <summary>
    /// The same wording the window shows, for the engine to put in the file a failed run leaves
    /// behind. It reads the wording table and touches nothing of the window, so the thread the run
    /// is on may ask for it while the window is drawing.
    /// </summary>
    public string Text(LogEntry entry) => RunLogText.Of(entry);
}
