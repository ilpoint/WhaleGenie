using WhaleGenie.Mcp;

namespace WhaleGenie.Tests;

/// <summary>
/// What <c>--mcp</c> was asked for. The two permissions are read-only unless the command line says
/// otherwise: a windowless run has no flyout to flip, so this is the only place a person can say
/// "yes, this one may send input" — and saying nothing has to mean no.
/// </summary>
public class McpCommandLineTests
{
    [Fact]
    public void A_windowless_run_is_read_only_unless_it_was_told_otherwise()
    {
        var options = McpCommandLine.ParseHost(["--mcp"], out var error);

        Assert.Empty(error);
        Assert.NotNull(options);
        Assert.False(options!.AllowControl);
        Assert.False(options.AllowRun);
    }

    [Fact]
    public void The_two_permissions_are_separate_switches()
    {
        var both = McpCommandLine.ParseHost(["--mcp", "--allow-control", "--allow-run"], out _);

        Assert.True(both!.AllowControl);
        Assert.True(both.AllowRun);

        // Sending a click and running a macro that writes files are not the same favour to ask.
        var control = McpCommandLine.ParseHost(["--mcp", "--allow-control"], out _);

        Assert.True(control!.AllowControl);
        Assert.False(control.AllowRun);
    }
}
