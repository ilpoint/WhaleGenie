using System.Diagnostics;
using WhaleGenie.Core.Devices.Platform;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// Asking this machine whether something is answering where the VIIPER server should be. Nothing
/// here starts a server: the question is handed in, so how often the machine is really asked — and
/// what the callers in between read — can be read without one being there.
/// </summary>
public class DriverInputTests : IDisposable
{
    private readonly Func<bool> _probe = DriverInput.Probe;

    /// <summary>
    /// How long a check here waits for the machine's answer. The engine's own patience is a fifth
    /// of a second, which is what a run can afford; a check reading the answer back is not in that
    /// hurry, and a thread pool that is busy elsewhere must not be read as "nothing is there".
    /// </summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    /// <summary>Puts back what was there, so no other check runs against a made-up machine.</summary>
    public void Dispose()
    {
        DriverInput.Probe = _probe;
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task A_question_that_is_still_out_is_not_asked_again()
    {
        // A window opening, a warm-up and a run all ask the same thing, and a machine with nothing
        // listening spends about two seconds per connection before it refuses: a thread per caller
        // left holding one of those is what emptied the thread pool and put unrelated work in a
        // queue behind it.
        var asked = 0;
        using var out_ = new ManualResetEventSlim();
        using var held = new ManualResetEventSlim();
        DriverInput.Probe = () =>
        {
            Interlocked.Increment(ref asked);
            out_.Set();

            // Long enough that the question is still out while both callers come and go, which is
            // the whole of what this check is about.
            held.Wait(TimeSpan.FromSeconds(30));
            return false;
        };

        // Five seconds of patience, against the fifth of a second the engine gives itself: room for
        // a busy machine to get to the question, while still short enough that both callers have
        // given up on it rather than being handed the answer above.
        var givingUp = TimeSpan.FromSeconds(5);
        var first = Task.Run(() => DriverInput.IsServerAnswering(givingUp));
        Assert.True(out_.Wait(TimeSpan.FromSeconds(30)), "the question never went out");

        var second = Task.Run(() => DriverInput.IsServerAnswering(givingUp));

        // Both of them give up on the wait they were given, and neither one asks again.
        Assert.False(await first);
        Assert.False(await second);
        Assert.Equal(1, asked);

        // Nothing is left waiting on the question above.
        held.Set();
    }

    [Fact]
    public void What_comes_back_from_the_machine_is_what_is_reported()
    {
        DriverInput.Probe = () => true;

        Assert.True(DriverInput.IsServerAnswering(Patience));
    }

    [Fact]
    public void Asking_about_this_machine_gives_up_rather_than_waiting_the_system_out()
    {
        // Windows spends about two seconds on a connection to a port nothing is listening on, which
        // is longer than whoever asked is willing to wait. What the machine answers is another
        // matter; this is about not holding the thread that asked for those two seconds.
        var asking = Stopwatch.StartNew();
        DriverInput.IsServerAnswering();
        asking.Stop();

        Assert.True(asking.Elapsed < TimeSpan.FromSeconds(1.5), $"asking took {asking.Elapsed}");
    }
}
