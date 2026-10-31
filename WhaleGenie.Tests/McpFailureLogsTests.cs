using System;
using System.IO;
using System.Text;
using WhaleGenie.Mcp;
using Xunit;

namespace WhaleGenie.Tests;

/// <summary>
/// Which file in the log folder counts as a failure log. The server keeps its own call log in the
/// same folder and writes one on any change, so "the newest .log" was usually the audit trail;
/// these checks pin down the rule that replaced it.
/// </summary>
public class McpFailureLogsTests
{
    /// <summary>The newest failure log wins, even when a call log is newer.</summary>
    [Fact]
    public void The_newest_failure_log_wins_over_a_newer_call_log()
    {
        var folder = Scratch();
        try
        {
            var older = Write(folder, "failure-2026-01-01.log", "first");
            var failure = Write(folder, "failure-2026-01-02.log", "second");
            var call = Write(folder, "mcp-2026-01-03.log", "audit");
            File.SetLastWriteTimeUtc(older, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(failure, new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(call, new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc));

            Assert.Equal(failure, McpFailureLogs.Named(folder, null));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>A folder with no failure in it answers nothing rather than the call log.</summary>
    [Fact]
    public void A_folder_with_only_a_call_log_answers_nothing()
    {
        var folder = Scratch();
        try
        {
            Write(folder, "mcp-2026-01-03.log", "audit");
            Assert.Null(McpFailureLogs.Named(folder, null));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    /// A path a client was handed is read back, and a path that is not a failure log — or is
    /// outside the folder — is refused instead of being read as any file at all.
    /// </summary>
    [Fact]
    public void Only_a_failure_log_inside_the_folder_can_be_named()
    {
        var folder = Scratch();
        try
        {
            var failure = Write(folder, "failure-run.log", "ready");
            var call = Write(folder, "mcp-run.log", "audit");
            var outside = Path.Combine(Path.GetTempPath(), "wg-outside-" + Guid.NewGuid().ToString("N") + ".log");
            File.WriteAllText(outside, "elsewhere");

            try
            {
                Assert.Equal(failure, McpFailureLogs.Named(folder, failure));
                Assert.Null(McpFailureLogs.Named(folder, call));
                Assert.Null(McpFailureLogs.Named(folder, outside));
                Assert.Null(McpFailureLogs.Named(folder, Path.Combine(folder, "failure-missing.log")));
            }
            finally
            {
                File.Delete(outside);
            }
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static string Scratch()
    {
        var folder = Path.Combine(Path.GetTempPath(), "wg-logs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static string Write(string folder, string name, string text)
    {
        var path = Path.Combine(folder, name);
        File.WriteAllText(path, text, new UTF8Encoding(false));
        return path;
    }
}
