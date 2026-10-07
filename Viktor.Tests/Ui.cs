using Avalonia.Headless;

// The headless session is one thread carrying one Avalonia application. Two test classes that
// start at the same time race to set that up, and the one that loses is told its own dispatcher
// belongs to another thread — a failure that says nothing about what the check was looking at.
// So the checks that need the session take their turn instead of starting together. The engine's
// own checks live in another assembly and still run side by side.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Viktor.Tests;

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
}
