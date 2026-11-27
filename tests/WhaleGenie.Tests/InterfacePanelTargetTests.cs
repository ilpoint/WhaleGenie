using System.Linq;
using WhaleGenie.Core.Devices;
using WhaleGenie.Runtime;
using WhaleGenie.Tests.Support;
using Xunit;

namespace WhaleGenie.Tests;

/// <summary>
/// Which window an interface group's buttons belong to. A rule that names a window keeps them in that
/// window while it is open — the buttons are in the target's layer, so whatever covers the target
/// covers them — but a different window that answers the same rule in front is the one being used
/// now. Only a rule that names nothing follows whatever is in front regardless of the rule.
/// </summary>
public class InterfacePanelTargetTests
{
    [Fact]
    public void A_named_window_keeps_its_buttons_when_another_program_is_in_front()
    {
        var windows = new WindowsForChecks()
            .Add(10, "Untitled - Paint", "mspaint", "Mspaint")
            .Add(20, "note - text", "notepad", "Notepad");

        var target = NativeSurfaceHost.TargetFor(Named("note"), windows.List()[0], windows);

        Assert.Equal(20, target?.Handle);
    }

    [Fact]
    public void A_rule_that_names_nothing_follows_the_window_in_front()
    {
        var windows = new WindowsForChecks()
            .Add(10, "Untitled - Paint", "mspaint", "Mspaint")
            .Add(20, "note - text", "notepad", "Notepad");

        var target = NativeSurfaceHost.TargetFor(WindowRule.Empty, windows.List()[0], windows);

        Assert.Equal(10, target?.Handle);
    }

    [Fact]
    public void The_frontmost_window_that_answers_the_rule_is_the_one()
    {
        var windows = new WindowsForChecks()
            .Add(10, "note - first", "notepad", "Notepad")
            .Add(20, "note - second", "notepad", "Notepad");

        var target = NativeSurfaceHost.TargetFor(Named("note"), null, windows);

        Assert.Equal(10, target?.Handle);
    }

    /// <summary>
    /// Two documents of the same program answer the same rule. The one brought forward is the one
    /// the buttons belong to; the first window in the listing must not keep them behind it.
    /// </summary>
    [Fact]
    public void A_matching_window_in_front_takes_over_from_an_earlier_match()
    {
        var windows = new WindowsForChecks()
            .Add(10, "note - first", "notepad", "Notepad")
            .Add(20, "note - second", "notepad", "Notepad");
        var second = windows.List().Single(window => window.Handle == 20);

        var target = NativeSurfaceHost.TargetFor(Named("note"), second, windows);

        Assert.Equal(20, target?.Handle);
    }

    [Fact]
    public void A_minimized_target_takes_the_buttons_away()
    {
        var windows = new WindowsForChecks()
            .Add(10, "note - text", "notepad", "Notepad")
            .Shrink(10);

        Assert.Null(NativeSurfaceHost.TargetFor(Named("note"), null, windows));
    }

    [Fact]
    public void A_target_that_is_gone_takes_the_buttons_away()
    {
        var windows = new WindowsForChecks()
            .Add(10, "note - text", "notepad", "Notepad")
            .Remove(10);

        Assert.Null(NativeSurfaceHost.TargetFor(Named("note"), windows.List().FirstOrDefault(), windows));
    }

    /// <summary>
    /// Once a group has found its window it stays with it while that window is open: a title that
    /// changed after the rule was written must not make the group jump to another window that also
    /// answers the rule, which is what made the buttons leave the target on their own.
    /// </summary>
    [Fact]
    public void A_held_window_keeps_answering_after_its_title_changed()
    {
        var windows = new WindowsForChecks().Add(10, "Game", "game", "Game");
        var held = windows.List()[0];
        windows.Remove(10).Add(10, "Game - paused", "game", "Game");

        var kept = NativeSurfaceHost.HeldTarget(held, windows);

        Assert.Equal(10, kept?.Handle);
        Assert.Equal("Game - paused", kept?.Title);
    }

    [Fact]
    public void A_held_window_that_closed_is_no_longer_held()
    {
        var windows = new WindowsForChecks().Add(10, "Game", "game", "Game");
        var held = windows.List()[0];
        windows.Remove(10);

        Assert.Null(NativeSurfaceHost.HeldTarget(held, windows));
    }

    [Fact]
    public void A_minimized_held_window_is_reported_minimized()
    {
        var windows = new WindowsForChecks().Add(10, "Game", "game", "Game");
        var held = windows.List()[0];
        windows.Shrink(10);

        Assert.True(NativeSurfaceHost.HeldTarget(held, windows)?.Minimized);
    }

    /// <summary>A rule a person writes for one program: the title holds a word.</summary>
    private static WindowRule Named(string title) => new(Title: title);
}
