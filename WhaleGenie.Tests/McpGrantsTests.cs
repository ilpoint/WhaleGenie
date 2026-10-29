using System;
using System.IO;
using WhaleGenie.Localization;
using WhaleGenie.Mcp;
using Xunit;

namespace WhaleGenie.Tests;

/// <summary>
/// The two permissions a client works under, and the point of them: the page and the server are two
/// processes, so a switch has to reach the running server without a restart of anything. These run
/// against a settings file of their own, never the one whoever is using WhaleGenie keeps.
/// </summary>
public class McpGrantsTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"whale-genie-grants-{Guid.NewGuid():N}.json");

    private readonly string _was;

    public McpGrantsTests()
    {
        _was = LocalSettings.FilePath;
        LocalSettings.FilePath = _path;
        McpControl.Stored = McpGrants.Current;
    }

    /// <summary>Whatever a check did, the rest of the run reads the real file and answers as before.</summary>
    public void Dispose()
    {
        McpControl.Stored = null;
        LocalSettings.FilePath = _was;
        try
        {
            File.Delete(_path);
        }
        catch
        {
            // A file left behind in the temporary folder is not worth failing a check over.
        }
    }

    [Fact]
    public void A_switch_flipped_beside_a_running_server_is_obeyed_on_its_next_question()
    {
        LocalSettings.StoreMcpGrants(false, false);
        Assert.False(McpControl.Allowed);
        Assert.False(McpControl.AllowsRun);

        // The page writes the file; nothing restarts, and nothing is told.
        LocalSettings.StoreMcpGrants(true, true);

        Assert.True(McpControl.Allowed);
        Assert.True(McpControl.AllowsRun);

        // And back off again, one at a time: a permission is not the other one.
        LocalSettings.StoreMcpGrants(false, true);

        Assert.False(McpControl.Allowed);
        Assert.True(McpControl.AllowsRun);
    }

    [Fact]
    public void A_run_with_nothing_stored_may_only_look()
    {
        // No settings file at all, and nothing to read it from: the state of a check, and of a
        // machine that has never opened the MCP flyout.
        File.Delete(_path);
        McpControl.Stored = null;

        Assert.False(McpControl.Allowed);
        Assert.False(McpControl.AllowsRun);

        // A run that has a file but no grants in it is the same: the page writes a no as readily as
        // a yes, and a missing answer is a no either way.
        McpControl.Stored = McpGrants.Current;
        Assert.False(McpControl.Allowed);
        Assert.False(McpControl.AllowsRun);
    }
}
