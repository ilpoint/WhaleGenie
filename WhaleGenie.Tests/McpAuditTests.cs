using System;
using System.IO;
using System.Linq;
using WhaleGenie.Mcp;

namespace WhaleGenie.Tests;

/// <summary>
/// The record of what an outside client did. It exists because the person at the machine asks
/// "what did it change" afterwards, and the answer has to be readable without a terminal: one line
/// per call, the tool, what it was pointed at, and how it ended.
/// </summary>
public class McpAuditTests
{
    /// <summary>A folder of its own, so the checks do not write into the program's own log folder.</summary>
    private static string Fresh()
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    [Fact]
    public void A_call_that_reached_the_machine_leaves_a_line()
    {
        var folder = Fresh();
        var original = McpAudit.Folder;
        McpAudit.Folder = folder;

        try
        {
            McpAudit.Note("whalegenie_mouse_click", "sent");
            McpAudit.Note("whalegenie_type_text", "12 characters");

            var lines = File.ReadAllLines(McpAudit.File);
            Assert.Equal(2, lines.Length);
            Assert.Contains("whalegenie_mouse_click", lines[0], StringComparison.Ordinal);
            Assert.Contains("sent", lines[0], StringComparison.Ordinal);

            // The text itself is never written: the log says how much was typed, not what.
            Assert.Contains("whalegenie_type_text", lines[1], StringComparison.Ordinal);
            Assert.Contains("12 characters", lines[1], StringComparison.Ordinal);
        }
        finally
        {
            McpAudit.Folder = original;
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void A_refusal_is_written_with_the_code_it_was_refused_by()
    {
        var folder = Fresh();
        var original = McpAudit.Folder;
        McpAudit.Folder = folder;

        try
        {
            McpAudit.Note("whalegenie_mouse_click", "refused", McpErrors.DirectControlOff);

            var line = Assert.Single(File.ReadAllLines(McpAudit.File));
            Assert.Contains("refused", line, StringComparison.Ordinal);
            Assert.Contains(McpErrors.DirectControlOff, line, StringComparison.Ordinal);
        }
        finally
        {
            McpAudit.Folder = original;
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void A_caller_cannot_break_the_one_line_per_call_shape()
    {
        var folder = Fresh();
        var original = McpAudit.Folder;
        McpAudit.Folder = folder;

        try
        {
            // A window title or a macro name may hold a line break, and a log whose lines are not
            // lines cannot be read back a line at a time.
            McpAudit.Note("whalegenie_window_activate", "title\nwith a break\tand a tab");

            var line = Assert.Single(File.ReadAllLines(McpAudit.File));
            Assert.Equal(3, line.Split('\t').Length);
            Assert.Contains("with a break", line, StringComparison.Ordinal);
        }
        finally
        {
            McpAudit.Folder = original;
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void A_folder_that_cannot_be_written_does_not_stop_the_call()
    {
        var folder = Fresh();
        var original = McpAudit.Folder;

        // A file where the folder should be: creating the folder fails, and that has to stay the
        // log's problem rather than the call's — writing the record is a side effect of the call,
        // and a side effect may not turn a call that worked into one that failed.
        var blocked = Path.Combine(folder, "not-a-folder");
        File.WriteAllText(blocked, "x");
        McpAudit.Folder = blocked;

        try
        {
            McpAudit.Note("whalegenie_mouse_click", "sent");
        }
        finally
        {
            McpAudit.Folder = original;
            Directory.Delete(folder, true);
        }
    }
}
