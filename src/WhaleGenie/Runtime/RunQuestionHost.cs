using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Runtime;

/// <summary>
/// The native questions a triggered run asks. They belong beside the trigger host rather than in
/// the main window: a run started by a key has no run window, and the shell may be hidden when the
/// question arrives. The dialogs are ordinary native windows; this class only chooses their owner
/// and hands the answer back to the engine.
/// </summary>
/// <remarks>
/// An answer of "stop" or "cancel" is what a closed box or no owner at all gives: it is the one
/// answer that cannot make things worse on its own. The debugger keeps its questions inside the run
/// window; this host is for the runs nobody is watching.
/// </remarks>
public sealed class RunQuestionHost : IRunQuestionHost
{
    private readonly Func<Window?>? _owner;

    public RunQuestionHost()
    {
    }

    /// <summary>A check may hand in a window of its own; the program leaves it to the active one.</summary>
    internal RunQuestionHost(Func<Window?> owner) => _owner = owner;

    public Task<StepErrorChoice> AskAboutFailedStep(string step, string reason, string detail,
        string comment)
        => OnUi(async () =>
        {
            if (Owner() is not { } owner)
            {
                return StepErrorChoice.Stop;
            }

            var choice = await ConfirmDialog.ShowAsync(owner, Strings.Get("Run.AskTitle"),
                FailedStepPrompt.Question(step, reason, detail, comment),
                Strings.Get("Run.AskRetry"), Strings.Get("Run.AskSkip"), true, Strings.Get("Run.AskStop"));

            return choice switch
            {
                ConfirmChoice.Primary => StepErrorChoice.Retry,
                ConfirmChoice.Secondary => StepErrorChoice.Skip,
                _ => StepErrorChoice.Stop,
            };
        });

    public Task<AskAnswer> AskUser(AskQuestion question)
        => OnUi(async () =>
        {
            if (Owner() is not { } owner)
            {
                return AskAnswer.Cancel;
            }

            if (question.Kind is AskKind.Text)
            {
                return await TextPromptDialog.ShowAsync(owner, Strings.Get("Main.Title"), question);
            }

            var choice = await ConfirmDialog.ShowAsync(owner, Strings.Get("Main.Title"),
                question.Message, Strings.Get("Run.AnswerOk"), null, true,
                Strings.Get("Run.AnswerCancel"));
            return choice is ConfirmChoice.Primary ? new AskAnswer(true, string.Empty) : AskAnswer.Cancel;
        });

    public Task<bool> ConfirmIrreversibleStep(ExecutableStep step)
        => OnUi(async () =>
        {
            if (Owner() is not { } owner)
            {
                return false;
            }

            return await ConfirmDialog.ShowAsync(owner, Strings.Get("Run.CannotBeUndoneTitle"),
                IrreversiblePrompt.Question(step.Type, step.Meta.Comment),
                Strings.Get("Common.Ok"), null, true, Strings.Get("Common.Cancel"),
                tone: ConfirmTone.Danger) is ConfirmChoice.Primary;
        });

    /// <summary>The engine asks on its own thread; a dialog belongs on the interface thread.</summary>
    private static Task<T> OnUi<T>(Func<Task<T>> ask) => Dispatcher.UIThread.InvokeAsync(ask);

    /// <summary>
    /// The window the question is put over: the active WhaleGenie window, or the main one when the
    /// shell is in the notification area. A question with no owner is answered by the caller's
    /// fallback rather than shown off-screen.
    /// </summary>
    private Window? Owner()
    {
        if (_owner?.Invoke() is { } given)
        {
            return given;
        }

        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return null;
        }

        var active = desktop.Windows.FirstOrDefault(window => window.IsActive);
        if (active is not null)
        {
            return active;
        }

        return desktop.MainWindow is { IsVisible: true } main ? main : null;
    }
}
