using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

// The headless session is one thread carrying one Avalonia application. Two test classes that
// start at the same time race to set that up, and the one that loses is told its own dispatcher
// belongs to another thread — a failure that says nothing about what the check was looking at.
// So the checks that need the session take their turn instead of starting together. The engine's
// own checks live in another assembly and still run side by side.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace WhaleGenie.Tests;

/// <summary>
/// Everything that touches a control runs here, on the one thread the headless session owns.
/// The session is started once for the whole test assembly and hands each call the user-interface
/// thread, which is what Avalonia demands for windows and dispatcher work.
/// </summary>
internal static class Ui
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(Ui).Assembly);

    /// <summary>
    /// Runs <paramref name="body"/> on the user-interface thread. It goes through the overload
    /// that carries an exception back out rather than the one that drops it, so a failed
    /// assertion inside still fails the test — a case that cannot fail is worse than no case.
    /// </summary>
    public static void Run(Action body)
        => Run(() =>
        {
            body();
            return true;
        });

    public static T Run<T>(Func<T> body)
        => Session.Dispatch(() => Task.FromResult(body()), CancellationToken.None)
            .GetAwaiter()
            .GetResult();

    /// <summary>
    /// The same, for a check that has to wait. A callback that has to come back to this thread
    /// cannot be waited on by a check that is holding it, so the waiting itself has to be done
    /// here rather than inside the body.
    /// </summary>
    public static T RunAsync<T>(Func<Task<T>> body)
        => Session.Dispatch(body, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

    /// <summary>
    /// Waits for something the interface does on its own schedule — a dialog that is about to open,
    /// a window that is about to close — by pumping the dispatcher until it is true.
    /// </summary>
    /// <remarks>
    /// The alternative, "sleep 20ms and assert", turns a slow machine into a failing check: the whole
    /// suite takes minutes, and the same wait that is generous on its own is not generous at the end
    /// of a long run. This waits for the thing itself and gives up with the reason it was waiting,
    /// so a real failure reads as one.
    /// </remarks>
    public static async Task Until(Func<bool> condition, string because, int timeoutMs = 3000)
    {
        for (var waited = 0; waited < timeoutMs && !condition(); waited += 10)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.True(condition(), because);
    }

    /// <summary>
    /// Closes a window and answers the "unsaved work" question it asks on the way out.
    /// </summary>
    /// <remarks>
    /// The question is modal, so it runs a nested dispatcher loop inside the dispatch the check is
    /// standing in. A check that walks away from that loop leaves it behind, and the next dispatch
    /// on this thread dies with "You've caused dispatcher loop" — on whichever check happens to be
    /// next rather than the one that opened it. The window that asks owns its prompt, so what is
    /// answered here is that window's own owned windows.
    /// </remarks>
    public static void CloseAndAnswer(Window window)
    {
        window.Close();
        AnswerUnsavedWork(window);
    }

    /// <summary>Dismisses the question <paramref name="window"/> is showing about unsaved work.</summary>
    public static void AnswerUnsavedWork(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        foreach (var prompt in window.OwnedWindows.OfType<ConfirmDialog>().ToList())
        {
            ((ConfirmDialogViewModel)prompt.DataContext!).CancelCommand.Execute(null);
        }

        Dispatcher.UIThread.RunJobs();
    }
}

/// <summary>
/// The session is started as the assembly is loaded rather than by whichever check happens to run
/// first, and the action catalogue is read once here as well. Reading the catalogue parses its icons
/// through Avalonia's drawing platform, and only the session's own thread has one: a check that
/// reads the catalogue first from its own thread is told the platform is missing, and that failure
/// then sticks to the type for every check after it — a hundred of them at once. Which order the
/// checks run in should not decide whether they can see the catalogue at all.
/// </summary>
internal static class SessionStartsFirst
{
    [ModuleInitializer]
    internal static void Start() => Ui.Run(() =>
    {
        // The checks below read the Chinese wording unless they switch to English themselves.
        // Pinning it here keeps a runner with an English system language from changing what they
        // are looking at.
        Strings.Current.Language = Language.Chinese;
        _ = ActionCatalog.Definitions.Count;
        _ = AppIcons.Workflow;
    });
}
