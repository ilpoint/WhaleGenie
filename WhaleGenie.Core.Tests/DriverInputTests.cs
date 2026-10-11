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
            held.Wait(TimeSpan.FromSeconds(5));
            return false;
        };

        var first = Task.Run(DriverInput.IsServerAnswering);
        Assert.True(out_.Wait(TimeSpan.FromSeconds(5)), "the question never went out");

        var second = Task.Run(DriverInput.IsServerAnswering);

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

        Assert.True(DriverInput.IsServerAnswering());
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
