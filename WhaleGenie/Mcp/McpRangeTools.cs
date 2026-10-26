using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia.Threading;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WhaleGenie.Range;

namespace WhaleGenie.Mcp;

/// <summary>
/// The range window a client is allowed to try things on: what is on it, and putting it back the way
/// it started. Without a range run there is nothing to read and nothing to reset, and that is said
/// rather than answered with an empty window.
/// </summary>
[McpServerToolType]
public sealed class McpRangeTools
{
    [McpServerTool(Name = "whalegenie_range_state", ReadOnly = true,
            Title = "What the range window shows"),
     Description("Reports the range window: where it is on the screen, where each control sits inside it, and what the window currently shows — how many times the counting button was pressed, what the text box holds, whether the box is ticked. Answers RangeNotAvailable when this run has no range window.")]
    public Task<CallToolResult> RangeState()
        => McpErrors.RunAsync(async () =>
        {
            // The window belongs to the interface thread, so even reading where it is has to be
            // asked for there.
            var facts = await Dispatcher.UIThread.InvokeAsync(() => RangeWindow.Current?.Facts());
            return facts is null
                ? McpErrors.Fail(McpErrors.RangeNotAvailable, "This run has no range window.",
                    "Start the program with --range to open one; the ordinary window and --mcp are not places to try things on.")
                : McpErrors.Ok(facts);
        });

    [McpServerTool(Name = "whalegenie_range_reset", Destructive = true,
            Title = "Put the range window back"),
     Description("Puts the range window back the way it started: no presses, an empty text box, an unticked box, the swatch in its own colour. Two runs can then be compared without the first one's presses counting towards the second.")]
    public Task<CallToolResult> RangeReset()
        => McpErrors.RunAsync(async () =>
        {
            var facts = await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (RangeWindow.Current is not { } window)
                {
                    return null;
                }

                window.Reset();
                return window.Facts();
            });

            if (facts is null)
            {
                return McpErrors.Fail(McpErrors.RangeNotAvailable,
                    "This run has no range window.", "Start the program with --range to open one.");
            }

            McpAudit.Note("range_reset", "reset");
            return McpErrors.Ok(facts);
        });
}
