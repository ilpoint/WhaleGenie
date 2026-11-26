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

// What the calls share: the window in front, a point, a failure and a capture region.
public sealed partial class McpControlTools
{
    /// <summary>Where the pointer is, read from the screen rather than from the input device.</summary>
    /// <remarks>
    /// The input has already been sent by the time this runs, so a screen that cannot be read says
    /// nothing here rather than failing the call: a click that happened must not come back as an
    /// error, because the client would send it a second time.
    /// </remarks>
    private object? Where()
    {
        try
        {
            var at = _devices().Screen.Cursor;
            return new { x = at.X, y = at.Y };
        }
        catch (Exception error)
            when (error is DeviceUnavailableException or DeviceActionException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>The point a scroll happens at: the one named, or where the pointer already is.</summary>
    /// <remarks>
    /// Half a point is a mistake rather than a wish to use the pointer: a client that sent one
    /// number meant to turn the wheel there, and quietly using the pointer instead would turn the
    /// wrong part of the screen.
    /// </remarks>
    private ScreenPoint At(int? x, int? y)
    {
        if (x is null != y is null)
        {
            throw new McpFailure(McpErrors.BadArgument,
                "A scroll point is both x and y or neither.",
                "Send both coordinates to turn the wheel at a point, or neither to turn it where the pointer already is.");
        }

        return x is int left && y is int top
            ? new ScreenPoint(left, top)
            : _devices().Screen.Cursor;
    }

    /// <summary>
    /// What one control says about itself right now, or null when it cannot be asked. The selector
    /// the element reports is what the question is put with, so the answer is about the same control
    /// the caller was looking at.
    /// </summary>
    private static UiElementFacts? Facts(IDeviceLayer device, UiElementInfo element)
    {
        try
        {
            return device.Ui.Facts(UiQuery.Parse(element.Selector, element.WindowTitle));
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>
    /// Runs one machine-driving call: the switch has to be on, and the calls go one at a time.
    /// </summary>
    /// <param name="summary">
    /// What to write in the audit line, for a call whose size is the thing worth remembering. The
    /// typed text itself is never written: the log says how much was typed, not what.
    /// </param>
    private static T Drive<T>(string tool, Func<T> work, string? summary = null)
    {
        if (!McpControl.Allowed)
        {
            McpAudit.Note(tool, "refused", McpErrors.DirectControlOff);
            throw new McpFailure(McpErrors.DirectControlOff,
                "Direct control is off, so nothing was done to the machine.",
                "Ask the person to turn on \"Allow direct control\"（允许直接控制） in the WhaleGenie MCP flyout, then call again." +
                " Looking at the screen needs no switch.");
        }

        lock (InputLock)
        {
            try
            {
                var answer = Use(work);
                McpAudit.Note(tool, summary ?? "sent");
                return answer;
            }
            catch (McpFailure failure)
            {
                // A call that a device refused is worth a line too: the person reading the log wants
                // to know the AI tried, not only what went through.
                McpAudit.Note(tool, "refused", failure.Code);
                throw;
            }
        }
    }

    /// <summary>
    /// Says a device's own refusal the way an MCP client can read it: what was missing, or the
    /// failure key the run would have reported, rather than a stack trace.
    /// </summary>
    private static T Use<T>(Func<T> work)
    {
        try
        {
            return work();
        }
        catch (DeviceUnavailableException error)
        {
            throw new McpFailure(McpErrors.DeviceUnavailable,
                $"This machine has no {error.Capability}.",
                "Ask the person to use a machine that has it, or use a step that does not need it.");
        }
        catch (DeviceActionException error)
        {
            throw new McpFailure(McpErrors.DeviceFailed,
                error.Detail.Length > 0 ? $"{error.Key}: {error.Detail}" : error.Key,
                "The device refused the call; check the window or the input it was aimed at.");
        }
        catch (ArgumentException error)
        {
            throw new McpFailure(McpErrors.BadArgument, error.Message);
        }
    }

    private static int Span(int milliseconds) => Math.Max(0, milliseconds);

    private static string Button(MouseButtonName button) => button switch
    {
        MouseButtonName.Right => "right",
        MouseButtonName.Middle => "middle",
        MouseButtonName.Back => "back",
        MouseButtonName.Forward => "forward",
        _ => "left",
    };

    private static string Way(ScrollDirection direction) => direction switch
    {
        ScrollDirection.Up => "up",
        ScrollDirection.Left => "left",
        ScrollDirection.Right => "right",
        _ => "down",
    };

    private static WindowMatch Mode(WindowPart part) => part switch
    {
        WindowPart.Process => WindowMatch.Process,
        WindowPart.Class => WindowMatch.ClassName,
        WindowPart.Handle => WindowMatch.Handle,
        _ => WindowMatch.Title,
    };

    private static string Compare(MatchMode mode) => mode switch
    {
        MatchMode.StartsWith => TextCompare.StartsWith,
        MatchMode.EndsWith => TextCompare.EndsWith,
        MatchMode.Equals => TextCompare.Exactly,
        MatchMode.Regex => TextCompare.Regex,
        _ => TextCompare.Contains,
    };

    /// <summary>
    /// The rectangle a capture is read from: the one named, or the whole window, or the whole
    /// desktop. A window is found by its handle the way every window step finds one, so a window
    /// that has gone says so rather than being quietly read as the desktop.
    /// </summary>
    private static async Task<ScreenCaptureRequest> RegionAsync(IDeviceLayer devices, long windowHandle,
        int? x, int? y, int? width, int? height)
    {
        if (windowHandle != 0)
        {
            var window = Use(() => devices.Windows.Find(
                windowHandle.ToString(CultureInfo.InvariantCulture), WindowMatch.Handle));
            if (window is null)
            {
                throw new McpFailure(McpErrors.BadArgument,
                    $"There is no window with the handle {windowHandle}.",
                    "Handles change when a window closes; call whalegenie_list_windows for the current ones.",
                    retryable: true);
            }

            return new ScreenCaptureRequest(
                x ?? window.Location.X,
                y ?? window.Location.Y,
                Wide(width, window.Size.Width),
                Wide(height, window.Size.Height),
                CaptureWay.Auto,
                windowHandle);
        }

        var desktop = Use(() => devices.Screen.Desktop);
        var bounds = desktop.Bounds;
        return new ScreenCaptureRequest(
            x ?? bounds.Left,
            y ?? bounds.Top,
            Wide(width, bounds.Width),
            Wide(height, bounds.Height));
    }

    /// <summary>A size that was not really given counts as the whole area, the way an empty one does.</summary>
    private static int Wide(int? width, int whole) => width is int value && value > 0 ? value : whole;

    private static string Encode(object value) => JsonSerializer.Serialize(value, Json);
}
