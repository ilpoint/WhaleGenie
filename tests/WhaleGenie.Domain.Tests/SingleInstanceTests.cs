using System;
using System.Threading;
using WhaleGenie.Runtime;
using Xunit;

namespace WhaleGenie.Domain.Tests;

/// <summary>
/// One window at a time. Two copies of the program are two writers of the same settings, package
/// and virtual devices, so the second launch asks the running one to come back and then ends.
/// </summary>
public class SingleInstanceTests
{
    [Fact]
    public void A_second_launch_asks_the_running_one_to_come_back()
    {
        // A place of this check's own: the person's copy of the program may be running.
        SingleInstance.Place = "Local\\WhaleGenie-test-" + Guid.NewGuid().ToString("N");

        Assert.True(SingleInstance.TryAcquire(), "the first launch takes the place");
        Assert.False(SingleInstance.TryAcquire(), "the second launch does not");

        using var heard = new ManualResetEventSlim();
        SingleInstance.Listen(() => heard.Set());

        SingleInstance.AskToComeBack();

        // Generous, because the listener is a thread of its own and a machine with every suite
        // running at once can be slow to give it a turn.
        Assert.True(heard.Wait(TimeSpan.FromSeconds(20)), "the running copy was never told to come back");
    }

    [Fact]
    public void A_listener_stays_with_the_place_it_was_started_for()
    {
        // A check takes a place of its own twice in one process, and a listener from the first one
        // is still alive. It must keep listening to the place it was started for: a listener that
        // reads the place again on each turn drifts to the newer one, and the two then race for
        // the same ask — which is why the ask once went unanswered on a busy runner.
        var first = "Local\\WhaleGenie-test-" + Guid.NewGuid().ToString("N");
        var second = "Local\\WhaleGenie-test-" + Guid.NewGuid().ToString("N");

        SingleInstance.Place = first;
        Assert.True(SingleInstance.TryAcquire());
        var firstCalls = 0;
        SingleInstance.Listen(() => Interlocked.Increment(ref firstCalls));

        SingleInstance.Place = second;
        Assert.True(SingleInstance.TryAcquire());
        var secondCalls = 0;
        SingleInstance.Listen(() => Interlocked.Increment(ref secondCalls));

        // Wake the first listener so it goes round its loop, where the old code read the field
        // again and landed on the second place.
        SingleInstance.Place = first;
        SingleInstance.AskToComeBack();
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref firstCalls) == 1,
            TimeSpan.FromSeconds(20)), "the first listener was not told");

        // Two asks to the second place must both reach its own listener, not the older one.
        SingleInstance.Place = second;
        for (var asked = 1; asked <= 2; asked++)
        {
            SingleInstance.AskToComeBack();
            Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref secondCalls) >= asked,
                TimeSpan.FromSeconds(20)), "the second listener missed an ask");
        }

        Assert.Equal(1, Volatile.Read(ref firstCalls));
    }

    /// <summary>
    /// The ask does not end the moment it is heard: it ends when the window says it is back. The
    /// launch that asks is the copy holding the foreground, and the right to take it lasts only as
    /// long as that copy does — leaving before the window used it is how the taskbar button was all
    /// that ever happened.
    /// </summary>
    [Fact]
    public void A_second_launch_waits_until_the_window_is_back()
    {
        SingleInstance.Place = "Local\\WhaleGenie-test-" + Guid.NewGuid().ToString("N");

        Assert.True(SingleInstance.TryAcquire());
        Assert.False(SingleInstance.TryAcquire());

        var raised = false;
        SingleInstance.Listen(() =>
        {
            Thread.Sleep(300);
            raised = true;
        });

        SingleInstance.AskToComeBack();

        Assert.True(raised, "the second launch left before the window was up");
    }
}
