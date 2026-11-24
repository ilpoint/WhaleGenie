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

// The screen: a capture, a pixel and where the pointer is.
public sealed partial class McpControlTools
{
    /// <summary>
    /// The tool class over the device layer and the window aside the run assembled. The pair is
    /// handed in rather than read from a static, so a check drives the tools over a pretend machine
    /// and a run that never asks for a device never builds one. It is public because the server
    /// builds a tool class the container was not given from the constructors it finds.
    /// </summary>
    public McpControlTools(Func<IDeviceLayer> devices, IWindowAside aside)
    {
        _devices = devices;
        _aside = aside;
    }

    [McpAudited(Handled = true)]
    [McpServerTool(Name = "whalegenie_capture_screen", Title = "Look at the screen"),
     Description("Takes a picture of the desktop, or of one window, and returns it as a PNG image. Coordinates are screen pixels; a region left empty is the whole desktop, or the whole window when a window is named.")]
    public Task<CallToolResult> CaptureScreen(
        [Description("Window handle from whalegenie_list_windows; 0 means the desktop.")] long windowHandle = 0,
        [Description("Left edge of the region, in screen pixels; empty means the whole area.")] int? x = null,
        [Description("Top edge of the region, in screen pixels; empty means the whole area.")] int? y = null,
        [Description("Width of the region in pixels; empty or not above zero means the whole area.")] int? width = null,
        [Description("Height of the region in pixels; empty or not above zero means the whole area.")] int? height = null,
        [Description("Scales the picture down so its longest side is at most this many pixels; 0 keeps the captured size. 1920 by default.")] int maxSide = 1920,
        [Description("Hides WhaleGenie's own windows while the picture is taken, so they do not cover what is being looked at. Ignored when a window is named, because a window is read through itself.")] bool hideWhaleGenie = true)
        => McpErrors.RunAsync(async () =>
        {
            var devices = _devices();
            var request = await RegionAsync(devices, windowHandle, x, y, width, height);

            // WhaleGenie's own windows usually stand in front of exactly what is being looked at,
            // the way they do for every picker: they step aside for the length of one capture and
            // come back as they were. That is window work, so it happens on the interface thread.
            var shot = windowHandle == 0 && hideWhaleGenie
                ? await _aside.WithoutOwnWindows(() => devices.Screen.Capture(request))
                : Use(() => devices.Screen.Capture(request));

            var frame = maxSide > 0 ? McpPictures.Scale(shot.Frame, maxSide) : shot.Frame;
            var scaled = frame.Width != shot.Frame.Width || frame.Height != shot.Frame.Height;

            // Looking is not changing, but it is still worth a line: "what did it see" is the other
            // half of the question this log answers, and a capture is the one look that takes a
            // whole picture away.
            McpAudit.Note("whalegenie_capture_screen",
                windowHandle == 0 ? $"desktop {frame.Width}x{frame.Height}" : $"window {windowHandle}");

            return new CallToolResult
            {
                Content =
                [
                    ImageContentBlock.FromBytes(PngWriter.Encode(frame), "image/png"),
                    new TextContentBlock
                    {
                        Text = Encode(new
                        {
                            window = windowHandle,
                            x = shot.Origin.X,
                            y = shot.Origin.Y,
                            width = frame.Width,
                            height = frame.Height,
                            way = shot.Method.ToString(),
                            scaledFrom = scaled
                                ? new { width = shot.Frame.Width, height = shot.Frame.Height }
                                : null,
                        }),
                    },
                ],
            };
        });

    [McpReadOnly]
    [McpServerTool(Name = "whalegenie_pixel_at", ReadOnly = true, Title = "Colour of one pixel"),
     Description("Reads the colour of one pixel of the desktop as #RRGGBB, in screen pixels.")]
    public Task<CallToolResult> PixelAt(
        [Description("Horizontal position in screen pixels.")] int x,
        [Description("Vertical position in screen pixels.")] int y)
        => Task.FromResult(McpErrors.Run(() => McpErrors.Ok(Use(() =>
        {
            var colour = _devices().Screen.PixelAt(x, y);
            return new { x, y, color = colour.ToHex() };
        }))));

    [McpReadOnly]
    [McpServerTool(Name = "whalegenie_cursor", ReadOnly = true, Title = "Where the pointer is"),
     Description("Where the pointer is right now, in screen pixels.")]
    public Task<CallToolResult> Cursor()
        => Task.FromResult(McpErrors.Run(() => McpErrors.Ok(Use(() =>
        {
            var at = _devices().Screen.Cursor;
            return new { x = at.X, y = at.Y };
        }))));
}
