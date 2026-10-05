using Avalonia.Headless;

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

    public static void Run(Action body) => Session.Dispatch(body, CancellationToken.None);

    public static T Run<T>(Func<T> body)
        => Session.Dispatch(() => Task.FromResult(body()), CancellationToken.None)
            .GetAwaiter()
            .GetResult();
}
