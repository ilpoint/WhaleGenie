using WhaleGenie.Mcp;

namespace WhaleGenie.Tests;

/// <summary>
/// What <c>--mcp</c> was asked for. The two permissions are not here: they belong to the MCP flyout,
/// which a served run reads while it runs, so a command line that mentions one is refused rather
/// than quietly ignored.
/// </summary>
public class McpCommandLineTests
{
    [Fact]
    public void A_windowless_run_is_told_only_what_to_serve()
    {
        var options = McpCommandLine.ParseHost(["--mcp"], out var error);

        Assert.Empty(error);
        Assert.NotNull(options);
        Assert.Null(options!.PackagePath);
        Assert.Null(options.NewFolder);
    }

    [Fact]
    public void The_old_permission_flags_are_refused()
    {
        Assert.Null(McpCommandLine.ParseHost(["--mcp", "--allow-control"], out var control));
        Assert.Contains("--allow-control", control, StringComparison.Ordinal);

        Assert.Null(McpCommandLine.ParseHost(["--mcp", "--allow-run"], out var run));
        Assert.Contains("--allow-run", run, StringComparison.Ordinal);
    }
}
