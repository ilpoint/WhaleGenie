using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Threading;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WhaleGenie.Core.Devices;
using WhaleGenie.Execution;
using WhaleGenie.GraphicsCapture;
using WhaleGenie.Views;

namespace WhaleGenie.Mcp;

/// <summary>
/// The picture-taking and machine-driving tools an MCP client sees beside the project tools. They
/// go through the same device layer a macro run goes through — the composition root's, never a
/// Windows implementation of their own — so what the AI sees and does is exactly what a macro
/// would see and do. The reading tools answer whenever the server is on; the tools that send
/// input answer only while <see cref="McpControl.Allowed"/> is set, which is the flyout's own
/// switch.
/// </summary>
/// <remarks>
/// A debug loop used to run through the person: the AI read a log, asked for a screenshot, and
/// waited for both to be carried over. These tools close that loop — look at the screen, point at
/// a control, press what a person would press — while the machine stays the person's: nothing
/// here runs a macro, and every write still goes through the draft, apply and save the project
/// tools already had.
/// </remarks>
[McpServerToolType]
public sealed class McpControlTools
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// One input at a time. Two clients — or one client that does not wait — pressing keys into
    /// the same simulator would interleave them, and an interleaved chord is input nobody meant
    /// to send. The machine is driven the way one person drives it: one thing at a time.
    /// </summary>
    private static readonly object InputLock = new();

    private readonly Func<IDeviceLayer> _devices;

    /// <summary>The tools as the server makes them, over this program's own device layer.</summary>
    public McpControlTools() : this(() => SharedDevices.Current)
    {
    }

    /// <summary>The same tools over a device layer a check hands in.</summary>
    internal McpControlTools(Func<IDeviceLayer> devices) => _devices = devices;

    [McpServerTool(Name = "whalegenie_capture_screen", ReadOnly = true),
     Description("Takes a picture of the desktop, or of one window, and returns it as a PNG image. Coordinates are screen pixels; a region left empty is the whole desktop, or the whole window when a window is named.")]
    public async Task<CallToolResult> CaptureScreen(
        [Description("Window handle from whalegenie_list_windows; 0 means the desktop.")] long windowHandle = 0,
        [Description("Left edge of the region, in screen pixels; empty means the whole area.")] int? x = null,
        [Description("Top edge of the region, in screen pixels; empty means the whole area.")] int? y = null,
        [Description("Width of the region in pixels; empty or not above zero means the whole area.")] int? width = null,
        [Description("Height of the region in pixels; empty or not above zero means the whole area.")] int? height = null,
        [Description("Scales the picture down so its longest side is at most this many pixels; 0 keeps the captured size. 1920 by default.")] int maxSide = 1920,
        [Description("Hides WhaleGenie's own windows while the picture is taken, so they do not cover what is being looked at. Ignored when a window is named, because a window is read through itself.")] bool hideWhaleGenie = true)
    {
        var devices = _devices();
        var request = await RegionAsync(devices, windowHandle, x, y, width, height);

        // WhaleGenie's own windows usually stand in front of exactly what is being looked at, the
        // way they do for every picker: they step aside for the length of one capture and come
        // back as they were. That is window work, so it happens on the interface thread.
        var shot = windowHandle == 0 && hideWhaleGenie
            ? await Dispatcher.UIThread.InvokeAsync(() =>
            {
                using var aside = AppWindows.StepAside();
                return devices.Screen.Capture(request);
            })
            : Use(() => devices.Screen.Capture(request));

        var frame = maxSide > 0 ? McpPictures.Scale(shot.Frame, maxSide) : shot.Frame;
        var scaled = frame.Width != shot.Frame.Width || frame.Height != shot.Frame.Height;
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
                        scaledFrom = scaled ? new { width = shot.Frame.Width, height = shot.Frame.Height } : null,
                    }),
                },
            ],
        };
    }

    [McpServerTool(Name = "whalegenie_pixel_at", ReadOnly = true),
     Description("Reads the colour of one pixel of the desktop as #RRGGBB, in screen pixels.")]
    public Task<string> PixelAt(
        [Description("Horizontal position in screen pixels.")] int x,
        [Description("Vertical position in screen pixels.")] int y)
        => Task.FromResult(Encode(Use(() =>
        {
            var colour = _devices().Screen.PixelAt(x, y);
            return new { x, y, color = colour.ToHex() };
        })));

    [McpServerTool(Name = "whalegenie_cursor", ReadOnly = true),
     Description("Where the pointer is right now, in screen pixels.")]
    public Task<string> Cursor()
        => Task.FromResult(Encode(Use(() =>
        {
            var at = _devices().Screen.Cursor;
            return new { x = at.X, y = at.Y };
        })));

    [McpServerTool(Name = "whalegenie_list_windows", ReadOnly = true),
     Description("Lists the top-level windows on the desktop, frontmost first, with the handle, title, rectangle and state of each. An empty value lists every window.")]
    public Task<string> ListWindows(
        [Description("Text to look for in a window; empty lists every window.")] string? value = null,
        [Description("Which part of the window the text is held against: title (default), process, class or handle.")] string? match = null,
        [Description("How the text is compared: contains (default), startsWith, endsWith, equals or regex.")] string? compare = null)
        => Task.FromResult(Encode(Use(() =>
        {
            var windows = _devices().Windows.Filter(value ?? string.Empty, Mode(match),
                compare ?? TextCompare.Contains, everyWhenEmpty: true);
            return windows.Select(window => new
            {
                handle = window.Handle,
                title = window.Title,
                x = window.Location.X,
                y = window.Location.Y,
                width = window.Size.Width,
                height = window.Size.Height,
                minimized = window.Minimized,
                maximized = window.Maximized,
            }).ToArray();
        })));

    [McpServerTool(Name = "whalegenie_element_at", ReadOnly = true),
     Description("Reads the control at one point of the screen through UI Automation: its name, automation id, control type, class, rectangle, window title and the selector a macro could use for it. Answers found=false when nothing there can be named.")]
    public Task<string> ElementAt(
        [Description("Horizontal position in screen pixels.")] int x,
        [Description("Vertical position in screen pixels.")] int y)
        => Task.FromResult(Encode(Use(() =>
        {
            var element = _devices().Ui.ElementAt(x, y);
            return element is null
                ? (object)new { found = false, x, y }
                : new
                {
                    found = true,
                    x,
                    y,
                    name = element.Name,
                    automationId = element.AutomationId,
                    controlType = element.ControlType,
                    className = element.ClassName,
                    windowTitle = element.WindowTitle,
                    width = element.Size.Width,
                    height = element.Size.Height,
                    selector = element.Selector,
                };
        })));

    [McpServerTool(Name = "whalegenie_mouse_move", Destructive = true),
     Description("Moves the pointer to a point of the screen, the way a person's hand does. Input goes to whatever window has the focus.")]
    public Task<string> MouseMove(
        [Description("Horizontal position in screen pixels.")] int x,
        [Description("Vertical position in screen pixels.")] int y,
        [Description("How long the move takes in milliseconds; 0 jumps straight there.")] int durationMs = 0)
        => Task.FromResult(Encode(Drive(() =>
        {
            Front().MoveMouse(x, y, Span(durationMs));
            return new { x, y, durationMs = Span(durationMs), cursor = Where() };
        })));

    [McpServerTool(Name = "whalegenie_mouse_click", Destructive = true),
     Description("Moves the pointer to a point and presses a mouse button there.")]
    public Task<string> MouseClick(
        [Description("Which button: left (default), right, middle, back or forward.")] string button = "left",
        [Description("Horizontal position in screen pixels.")] int x = 0,
        [Description("Vertical position in screen pixels.")] int y = 0,
        [Description("How many clicks, 1 to 10; 2 is a double click.")] int clicks = 1,
        [Description("Milliseconds between two clicks; 0 uses the usual gap.")] int intervalMs = 0)
        => Task.FromResult(Encode(Drive(() =>
        {
            var times = Math.Clamp(clicks, 1, 10);
            Front().Click(Button(button), x, y, times, Span(intervalMs));
            return new { button = button.Trim().ToLowerInvariant(), x, y, clicks = times, cursor = Where() };
        })));

    [McpServerTool(Name = "whalegenie_mouse_scroll", Destructive = true),
     Description("Turns the wheel at a point of the screen, or where the pointer already is.")]
    public Task<string> MouseScroll(
        [Description("Which way: up, down (default), left or right.")] string direction = "down",
        [Description("How far, in wheel units, where 120 is one notch.")] int delta = 120,
        [Description("Horizontal position in screen pixels; empty means where the pointer is.")] int? x = null,
        [Description("Vertical position in screen pixels; empty means where the pointer is.")] int? y = null)
        => Task.FromResult(Encode(Drive(() =>
        {
            var at = At(x, y);
            var way = Way(direction);
            Front().Scroll(way, delta, at.X, at.Y);
            return new { direction = way, delta, at = new { at.X, at.Y }, cursor = Where() };
        })));

    [McpServerTool(Name = "whalegenie_mouse_drag", Destructive = true),
     Description("Presses a mouse button at one point, walks the pointer to another with it held, and lets go there.")]
    public Task<string> MouseDrag(
        [Description("Which button: left (default), right, middle, back or forward.")] string button = "left",
        [Description("Horizontal position the drag starts at, in screen pixels.")] int fromX = 0,
        [Description("Vertical position the drag starts at, in screen pixels.")] int fromY = 0,
        [Description("Horizontal position the drag ends at, in screen pixels.")] int toX = 0,
        [Description("Vertical position the drag ends at, in screen pixels.")] int toY = 0,
        [Description("How long the drag takes in milliseconds; 0 goes as fast as it can.")] int durationMs = 0,
        [Description("How many moves along the way, 1 to 200; 0 is a straight line.")] int steps = 0)
        => Task.FromResult(Encode(Drive(() =>
        {
            Front().Drag(Button(button), fromX, fromY, toX, toY, Span(durationMs),
                Math.Clamp(steps, 0, 200));
            return new
            {
                button = button.Trim().ToLowerInvariant(),
                from = new { x = fromX, y = fromY },
                to = new { x = toX, y = toY },
                cursor = Where(),
            };
        })));

    [McpServerTool(Name = "whalegenie_key", Destructive = true),
     Description("Presses one key, or a chord of keys held together such as Ctrl+Shift+S, the way a person's hands do. Input goes to whatever window has the focus.")]
    public Task<string> Key(
        [Description("The key name, or a chord of them joined by '+'. The names are the ones the input.* actions use.")] string key,
        [Description("How long the key is held in milliseconds; 0 uses the usual short press.")] int holdMs = 0)
        => Task.FromResult(Encode(Drive(() =>
        {
            var name = (key ?? string.Empty).Trim();
            if (name.Contains('+'))
            {
                var keys = KeyNames.ResolveChord(name, out var unknown);
                if (unknown is { Length: > 0 })
                {
                    throw new McpException($"There is no key named \"{unknown}\".");
                }

                var names = keys.Select(KeyNames.Name).ToArray();
                Front().Hotkey(names, Span(holdMs));
                return (object)new { keys = names, holdMs = Span(holdMs) };
            }

            if (KeyNames.Resolve(name) is null)
            {
                throw new McpException($"There is no key named \"{name}\".");
            }

            Front().KeyPress(name, Span(holdMs));
            return new { key = name, holdMs = Span(holdMs) };
        })));

    [McpServerTool(Name = "whalegenie_type_text", Destructive = true),
     Description("Types text where the keyboard is pointing, one character at a time when an interval is asked for. The text itself is not echoed back.")]
    public Task<string> TypeText(
        [Description("The text to type.")] string text,
        [Description("Milliseconds between two characters; 0 types as fast as it can.")] int intervalMs = 0)
        => Task.FromResult(Encode(Drive(() =>
        {
            Front().TypeText(text ?? string.Empty, Span(intervalMs));
            return new { characters = (text ?? string.Empty).Length, intervalMs = Span(intervalMs) };
        })));

    [McpServerTool(Name = "whalegenie_window_activate", Destructive = true),
     Description("Brings one window to the front, restoring it first when it was shrunk. This is how a window is chosen before keys and clicks are sent to it.")]
    public Task<string> WindowActivate(
        [Description("The window handle from whalegenie_list_windows.")] long handle)
        => Task.FromResult(Encode(Drive(() =>
        {
            var activated = _devices().Windows.Activate(handle);
            return new { handle, activated };
        })));

    [McpServerTool(Name = "whalegenie_window_move", Destructive = true),
     Description("Moves and resizes one window, in screen pixels.")]
    public Task<string> WindowMove(
        [Description("The window handle from whalegenie_list_windows.")] long handle,
        [Description("Left edge of the window in screen pixels.")] int x,
        [Description("Top edge of the window in screen pixels.")] int y,
        [Description("Width of the window in pixels.")] int width,
        [Description("Height of the window in pixels.")] int height)
        => Task.FromResult(Encode(Drive(() =>
        {
            if (width <= 0 || height <= 0)
            {
                throw new McpException("A window's width and height have to be above zero.");
            }

            var moved = _devices().Windows.Move(handle, x, y, width, height);
            return new { handle, x, y, width, height, moved };
        })));

    [McpServerTool(Name = "whalegenie_window_close", Destructive = true),
     Description("Asks one window to close, the same as clicking its close button.")]
    public Task<string> WindowClose(
        [Description("The window handle from whalegenie_list_windows.")] long handle)
        => Task.FromResult(Encode(Drive(() =>
        {
            var closed = _devices().Windows.Close(handle);
            return new { handle, closed };
        })));

    /// <summary>The device that sends input the way a person's own input goes.</summary>
    private IInputDevice Front() => _devices().Inputs.For(InputRoute.Front);

    /// <summary>Where the pointer is, read from the screen rather than from the input device.</summary>
    private object Where()
    {
        var at = _devices().Screen.Cursor;
        return new { x = at.X, y = at.Y };
    }

    /// <summary>The point a scroll happens at: the one named, or where the pointer already is.</summary>
    private WhaleGenie.Core.Devices.ScreenPoint At(int? x, int? y)
        => x is int left && y is int top
            ? new WhaleGenie.Core.Devices.ScreenPoint(left, top)
            : _devices().Screen.Cursor;

    /// <summary>
    /// Runs one machine-driving call: the switch has to be on, and the calls go one at a time.
    /// </summary>
    private static T Drive<T>(Func<T> work)
    {
        if (!McpControl.Allowed)
        {
            throw new McpException(
                "Direct control is off. Turn on \"Allow direct control\" in the WhaleGenie MCP flyout and ask again.");
        }

        lock (InputLock)
        {
            return Use(work);
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
            throw new McpException($"This machine has no {error.Capability}.");
        }
        catch (DeviceActionException error)
        {
            throw new McpException(error.Detail.Length > 0
                ? $"{error.Key}: {error.Detail}"
                : error.Key);
        }
        catch (ArgumentException error)
        {
            throw new McpException(error.Message, error);
        }
    }

    private static int Span(int milliseconds) => Math.Max(0, milliseconds);

    private static string Button(string button)
    {
        var name = (button ?? string.Empty).Trim().ToLowerInvariant();
        if (name is not ("left" or "right" or "middle" or "back" or "forward"))
        {
            throw new McpException(
                $"There is no mouse button \"{button}\". The buttons are left, right, middle, back and forward.");
        }

        return name;
    }

    private static string Way(string direction)
    {
        var name = (direction ?? string.Empty).Trim().ToLowerInvariant();
        if (name is not ("up" or "down" or "left" or "right"))
        {
            throw new McpException(
                $"There is no scroll direction \"{direction}\". The directions are up, down, left and right.");
        }

        return name;
    }

    private static WindowMatch Mode(string? match) => (match ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "process" => WindowMatch.Process,
        "class" or "classname" => WindowMatch.ClassName,
        "handle" => WindowMatch.Handle,
        "" or "title" => WindowMatch.Title,
        _ => throw new McpException(
            $"There is no window part \"{match}\". The parts are title, process, class and handle."),
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
                throw new McpException($"There is no window with the handle {windowHandle}.");
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

/// <summary>
/// A captured picture at a size an AI client will take: a whole 4K desktop as PNG is tens of
/// megabytes of base64, which most clients refuse outright, and the interesting part of a
/// debugging screenshot is usually readable at a twentieth of the size. The scaling averages
/// whole boxes of pixels rather than picking one out of each, so a line of text stays a line of
/// text instead of breaking up.
/// </summary>
internal static class McpPictures
{
    /// <summary>The same picture with its longest side at most <paramref name="maxSide"/> pixels.</summary>
    public static ImageFrame Scale(ImageFrame frame, int maxSide)
    {
        if (maxSide <= 0 || frame.IsEmpty)
        {
            return frame;
        }

        var longest = Math.Max(frame.Width, frame.Height);
        if (longest <= maxSide)
        {
            return frame;
        }

        var width = Math.Max(1, (int)Math.Round(frame.Width * (double)maxSide / longest));
        var height = Math.Max(1, (int)Math.Round(frame.Height * (double)maxSide / longest));
        if (width >= frame.Width || height >= frame.Height)
        {
            return frame;
        }

        var pixels = new byte[(long)width * height * 4];
        for (var y = 0; y < height; y++)
        {
            var top = (int)((long)y * frame.Height / height);
            var bottom = Math.Min(frame.Height, Math.Max(top + 1, (int)((long)(y + 1) * frame.Height / height)));
            for (var x = 0; x < width; x++)
            {
                var left = (int)((long)x * frame.Width / width);
                var right = Math.Min(frame.Width, Math.Max(left + 1, (int)((long)(x + 1) * frame.Width / width)));

                long blue = 0;
                long green = 0;
                long red = 0;
                long count = 0;
                for (var row = top; row < bottom; row++)
                {
                    var from = (long)row * frame.Width;
                    for (var column = left; column < right; column++)
                    {
                        var at = (from + column) * 4;
                        blue += frame.Bgra[at];
                        green += frame.Bgra[at + 1];
                        red += frame.Bgra[at + 2];
                        count++;
                    }
                }

                var to = ((long)y * width + x) * 4;
                pixels[to] = (byte)(count == 0 ? 0 : blue / count);
                pixels[to + 1] = (byte)(count == 0 ? 0 : green / count);
                pixels[to + 2] = (byte)(count == 0 ? 0 : red / count);
                pixels[to + 3] = 255;
            }
        }

        return new ImageFrame(width, height, pixels);
    }
}
