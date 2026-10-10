using System.Runtime.CompilerServices;
using Avalonia.Headless;
using WhaleGenie.Models;

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
    internal static void Start() => Ui.Run(() => ActionCatalog.Definitions.Count);
}
