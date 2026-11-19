using Avalonia.Threading;
using Xunit;

namespace WhaleGenie.Tests;

/// <summary>
/// A call that leaves work posting its next turn makes the session's teardown spend its five-second
/// budget and give up. The call has already run and has its result; the leftover queue belongs to
/// the application being thrown away. The failure used to land on whichever check ran next, as
/// "You've caused dispatcher loop"; now the call returns and the next one starts a new dispatcher.
/// </summary>
public sealed class UiIsolationTests
{
    [Fact]
    public void A_job_that_keeps_re_posting_itself_does_not_reach_the_next_check()
    {
        Ui.Run(() =>
        {
            void Turn() => Dispatcher.UIThread.Post(Turn);
            Turn();
        });

        Ui.Run(() => { });
    }
}
