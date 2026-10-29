using System;
using System.Threading;
using WhaleGenie.Runtime;
using Xunit;

namespace WhaleGenie.Tests;

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

        Assert.True(heard.Wait(TimeSpan.FromSeconds(5)), "the running copy was never told to come back");
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
