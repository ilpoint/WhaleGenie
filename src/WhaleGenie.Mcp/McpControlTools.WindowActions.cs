using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WhaleGenie.Core.Devices;


namespace WhaleGenie.Mcp;

// Acting on a window: bringing it up, moving it and closing it.
public sealed partial class McpControlTools
{
    [McpAudited(Handled = true)]
    [McpServerTool(Name = "whalegenie_window_activate", Destructive = true, Title = "Bring a window forward"),
     Description("Brings one window to the front, restoring it first when it was shrunk. This is how a window is chosen before keys and clicks are sent to it.")]
    public Task<CallToolResult> WindowActivate(
        [Description("The window handle from whalegenie_list_windows.")] long handle)
        => Task.FromResult(McpErrors.Run(() => McpErrors.Ok(Drive("whalegenie_window_activate", () =>
        {
            var windows = _devices().Windows;
            var asked = windows.Activate(handle);

            // Windows answers the request, not the result: it can refuse to raise a window and
            // still say the call went through, which leaves a client believing it is typing into
            // the window it raised. The window is looked for in front before that is reported.
            var raised = false;
            for (var look = 0; look < 6; look++)
            {
                if (windows.Foreground()?.Handle == handle)
                {
                    raised = true;
                    break;
                }

                Thread.Sleep(30);
            }

            return new { handle, activated = raised, requested = asked };
        }))));

    [McpAudited(Handled = true)]
    [McpServerTool(Name = "whalegenie_window_move", Destructive = true, Title = "Move or resize a window"),
     Description("Moves and resizes one window, in screen pixels.")]
    public Task<CallToolResult> WindowMove(
        [Description("The window handle from whalegenie_list_windows.")] long handle,
        [Description("Left edge of the window in screen pixels.")] int x,
        [Description("Top edge of the window in screen pixels.")] int y,
        [Description("Width in pixels; empty keeps the width it has. A window is moved far more often than it is resized.")] int? width = null,
        [Description("Height in pixels; empty keeps the height it has.")] int? height = null)
        => Task.FromResult(McpErrors.Run(() => McpErrors.Ok(Drive("whalegenie_window_move", () =>
        {
            if (width is <= 0 || height is <= 0)
            {
                throw new McpFailure(McpErrors.BadArgument,
                    "A window's width and height have to be above zero.",
                    "Screen pixels; leave them out to keep the size the window has now.");
            }

            // Moving a window used to need its size as well, so a client that only wanted it
            // somewhere else had to read the size first and pass it back unchanged.
            var windows = _devices().Windows;
            var current = windows.List().FirstOrDefault(candidate => candidate.Handle == handle);
            var moved = windows.Move(handle, x, y,
                width ?? current?.Size.Width ?? 0, height ?? current?.Size.Height ?? 0);
            return new
            {
                handle,
                x,
                y,
                width = width ?? current?.Size.Width ?? 0,
                height = height ?? current?.Size.Height ?? 0,
                moved,
            };
        }))));

    [McpAudited(Handled = true)]
    [McpServerTool(Name = "whalegenie_window_close", Destructive = true, Title = "Close a window"),
     Description("Asks one window to close, the same as clicking its close button.")]
    public Task<CallToolResult> WindowClose(
        [Description("The window handle from whalegenie_list_windows.")] long handle)
        => Task.FromResult(McpErrors.Run(() => McpErrors.Ok(Drive("whalegenie_window_close", () =>
        {
            var closed = _devices().Windows.Close(handle);
            return new { handle, closed };
        }))));

    /// <summary>The device that sends input the way a person's own input goes.</summary>
    private IInputDevice Front() => _devices().Inputs.For(InputRoute.Front);
}
