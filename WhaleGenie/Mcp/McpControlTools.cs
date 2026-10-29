using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WhaleGenie.Core.Devices;
using WhaleGenie.GraphicsCapture;
using WhaleGenie.Runtime;
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

        // Chinese window titles and window rules come back as themselves rather than as escapes.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
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
                ? await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    using var aside = AppWindows.StepAside();
                    return devices.Screen.Capture(request);
                })
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

    [McpServerTool(Name = "whalegenie_cursor", ReadOnly = true, Title = "Where the pointer is"),
     Description("Where the pointer is right now, in screen pixels.")]
    public Task<CallToolResult> Cursor()
        => Task.FromResult(McpErrors.Run(() => McpErrors.Ok(Use(() =>
        {
            var at = _devices().Screen.Cursor;
            return new { x = at.X, y = at.Y };
        }))));

    [McpServerTool(Name = "whalegenie_list_windows", ReadOnly = true, Title = "List the windows"),
     Description("Lists the top-level windows on the desktop, frontmost first, with the handle, title, rectangle and state of each. An empty value lists every window. With details on, each window also carries its program name and window class — what a filter on process or class matched, which the plain listing cannot show.")]
    public Task<CallToolResult> ListWindows(
        [Description("Text to look for in a window; empty lists every window.")] string? value = null,
        [Description("Which part of the window the text is held against; title by default.")] WindowPart part = WindowPart.Title,
        [Description("How the text is compared; contains by default.")] MatchMode compare = MatchMode.Contains,
        [Description("Also read each window's program name and class. Off by default because it costs a look-up per window.")] bool details = false)
        => Task.FromResult(McpErrors.Run(() => McpErrors.Ok(Use(() =>
        {
            var device = _devices();
            var windows = device.Windows
                .Filter(value ?? string.Empty, Mode(part), Compare(compare), everyWhenEmpty: true)
                .Select(window => new
                {
                    handle = window.Handle,
                    title = window.Title,
                    x = window.Location.X,
                    y = window.Location.Y,
                    width = window.Size.Width,
                    height = window.Size.Height,
                    minimized = window.Minimized,
                    maximized = window.Maximized,
                    process = details ? device.Windows.ProcessOf(window.Handle) : string.Empty,
                    className = details ? device.Windows.ClassOf(window.Handle) : string.Empty,
                })
                .ToList();
            return new { total = windows.Count, windows };
        }))));

    [McpServerTool(Name = "whalegenie_element_at", ReadOnly = true, Title = "Name the control at a point"),
     Description("Reads the control at one point of the screen through UI Automation: its name, automation id, control type, class, rectangle, window title, what it is holding and whether it can be used, and the selector a macro could use for it. Answers found=false when nothing there can be named.")]
    public Task<CallToolResult> ElementAt(
        [Description("Horizontal position in screen pixels.")] int x,
        [Description("Vertical position in screen pixels.")] int y)
        => Task.FromResult(McpErrors.Run(() => McpErrors.Ok(Use(() =>
        {
            var device = _devices();
            var element = device.Ui.ElementAt(x, y);
            if (element is null)
            {
                return (object)new { found = false, x, y };
            }

            // What the control is holding is a second question from what it is: a text box's name
            // says which box it is, and its value says whether the typing arrived.
            var facts = Facts(device, element);
            return new
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
                value = facts?.Value ?? string.Empty,
                enabled = facts?.Enabled ?? false,
                visible = facts?.Visible ?? false,
                hasValue = facts is not null,
            };
        }))));

    [McpServerTool(Name = "whalegenie_find_elements", ReadOnly = true, Title = "Find controls by selector"),
     Description("Finds the controls a selector names, in the order a person counts them, up to a limit. A selector is what whalegenie_element_at answers with: a control type and the parts of it to match, such as Button[name='Save'].")]
    public Task<CallToolResult> FindElements(
        [Description("The selector, such as Button[name='Save'] or Edit[automationId='txtName'].")] string selector,
        [Description("How many to return at most; 20 by default.")] int limit = 20)
        => Task.FromResult(McpErrors.Run(() => McpErrors.Ok(Use(() =>
        {
            var query = UiQuery.Parse(selector ?? string.Empty);
            var found = _devices().Ui.FindAll(query, Math.Clamp(limit, 1, 200));
            return new
            {
                count = found.Count,
                elements = found.Select(element => new
                {
                    name = element.Name,
                    automationId = element.AutomationId,
                    controlType = element.ControlType,
                    className = element.ClassName,
                    windowTitle = element.WindowTitle,
                    x = element.Location.X,
                    y = element.Location.Y,
                    width = element.Size.Width,
                    height = element.Size.Height,
                    selector = element.Selector,
                }),
            };
        }))));

    [McpServerTool(Name = "whalegenie_window_info", ReadOnly = true, Title = "What one window is"),
     Description("Reads one window by handle: its title, rectangle, state, program name, class and the size of the part it draws in. This is how a client checks that a window it moved or activated is the window it meant.")]
    public Task<CallToolResult> WindowInfo(
        [Description("The handle from whalegenie_list_windows.")] long handle)
        => Task.FromResult(McpErrors.Run(() => McpErrors.Ok(Use(() =>
        {
            var windows = _devices().Windows;
            var window = windows.List().FirstOrDefault(candidate => candidate.Handle == handle);
            if (window is null)
            {
                return (object)new { found = false, handle };
            }

            var client = windows.ClientSize(handle);
            var origin = windows.ClientOrigin(handle);
            return new
            {
                found = true,
                handle,
                title = window.Title,
                x = window.Location.X,
                y = window.Location.Y,
                width = window.Size.Width,
                height = window.Size.Height,
                minimized = window.Minimized,
                maximized = window.Maximized,
                process = windows.ProcessOf(handle),
                className = windows.ClassOf(handle),
                clientX = origin.X,
                clientY = origin.Y,
                clientWidth = client.Width,
                clientHeight = client.Height,
            };
        }))));

    [McpServerTool(Name = "whalegenie_clipboard_read", Title = "Read the clipboard"),
     Description("Returns what is on the clipboard as text, and how many files it holds when files were copied. Reading a page's text after selecting and copying it is steadier than reading the screen. Needs \"Allow direct control\": what is on the clipboard is whatever the person last copied, which can be a password.")]
    public Task<CallToolResult> ClipboardRead()
        => Task.FromResult(McpErrors.Run(() => McpErrors.Ok(Drive("clipboard_read", () =>
        {
            var clipboard = _devices().Clipboard;
            var files = clipboard.ReadFiles();
            return new
            {
                text = clipboard.ReadText(),
                usesText = clipboard.HasText,
                files,
            };
        }, "read"))));

    [McpServerTool(Name = "whalegenie_clipboard_write", Destructive = true, Title = "Write the clipboard"),
     Description("Puts text on the clipboard, so a step that pastes can be tried without typing. Needs \"Allow direct control\".")]
    public Task<CallToolResult> ClipboardWrite(
        [Description("The text to put on the clipboard.")] string text)
        => Task.FromResult(McpErrors.Run(() => McpErrors.Ok(Drive("clipboard_write", () =>
        {
            var clipboard = _devices().Clipboard;
            clipboard.WriteText(text ?? string.Empty);
            return new { characters = (text ?? string.Empty).Length };
        }))));

    [McpServerTool(Name = "whalegenie_process_list", ReadOnly = true, Title = "List the running programs"),
     Description("Lists the programs running on this machine by name, so a client can tell whether the program it wants to drive is already up.")]
    public Task<CallToolResult> ProcessList()
        => Task.FromResult(McpErrors.Run(() => McpErrors.Ok(Use(() =>
        {
            var running = _devices().Processes.List();
            return new { total = running.Count, programs = running };
        }))));

    [McpServerTool(Name = "whalegenie_mouse_move", Destructive = true, Title = "Move the pointer"),
     Description("Moves the pointer to a point of the screen, the way a person's hand does. Input goes to whatever window has the focus.")]
    public Task<CallToolResult> MouseMove(
        [Description("Horizontal position in screen pixels.")] int x,
        [Description("Vertical position in screen pixels.")] int y,
        [Description("How long the move takes in milliseconds; 0 jumps straight there.")] int durationMs = 0)
        => Task.FromResult(McpErrors.Run(() => McpErrors.Ok(Drive("whalegenie_mouse_move", () =>
        {
            Front().MoveMouse(x, y, Span(durationMs));
            return new { x, y, durationMs = Span(durationMs), cursor = Where() };
        }))));

    [McpServerTool(Name = "whalegenie_mouse_click", Destructive = true, Title = "Click"),
     Description("Moves the pointer to a point and presses a mouse button there.")]
    public Task<CallToolResult> MouseClick(
        [Description("Horizontal position in screen pixels.")] int x,
        [Description("Vertical position in screen pixels.")] int y,
        [Description("Which button; left by default.")] MouseButtonName button = MouseButtonName.Left,
        [Description("How many clicks, 1 to 10; 2 is a double click.")] int clicks = 1,
        [Description("Milliseconds between two clicks; 0 uses the usual gap.")] int intervalMs = 0)
        => Task.FromResult(McpErrors.Run(() => McpErrors.Ok(Drive("whalegenie_mouse_click", () =>
        {
            // The point is not optional the way a scroll's is: a click that was given no point
            // would land in the screen's own corner, which is a place nobody asked for.
            var pressed = Button(button);
            var times = Math.Clamp(clicks, 1, 10);
            Front().Click(pressed, x, y, times, Span(intervalMs));
            return new { button = pressed, x, y, clicks = times, cursor = Where() };
        }))));

    [McpServerTool(Name = "whalegenie_mouse_scroll", Destructive = true, Title = "Turn the wheel"),
     Description("Turns the wheel at a point of the screen, or where the pointer already is.")]
    public Task<CallToolResult> MouseScroll(
        [Description("Which way the wheel turns; down by default.")] ScrollDirection direction = ScrollDirection.Down,
        [Description("How far, in wheel units, where 120 is one notch.")] int delta = 120,
        [Description("Horizontal position in screen pixels; empty means where the pointer is.")] int? x = null,
        [Description("Vertical position in screen pixels; empty means where the pointer is.")] int? y = null)
        => Task.FromResult(McpErrors.Run(() =>
        {
            // Where the wheel turns is read before the call rather than inside it, so the answer
            // reports the point the wheel actually turned at, whether it was given or not.
            var at = At(x, y);
            return McpErrors.Ok(Drive("whalegenie_mouse_scroll", () =>
            {
                var way = Way(direction);
                Front().Scroll(way, delta, at.X, at.Y);
                return new { direction = way, delta, at = new { at.X, at.Y }, cursor = Where() };
            }));
        }));

    [McpServerTool(Name = "whalegenie_mouse_drag", Destructive = true, Title = "Drag"),
     Description("Presses a mouse button at one point, walks the pointer to another with it held, and lets go there.")]
    public Task<CallToolResult> MouseDrag(
        [Description("Horizontal position the drag starts at, in screen pixels.")] int fromX,
        [Description("Vertical position the drag starts at, in screen pixels.")] int fromY,
        [Description("Horizontal position the drag ends at, in screen pixels.")] int toX,
        [Description("Vertical position the drag ends at, in screen pixels.")] int toY,
        [Description("Which button; left by default.")] MouseButtonName button = MouseButtonName.Left,
        [Description("How long the drag takes in milliseconds; 0 goes as fast as it can.")] int durationMs = 0,
        [Description("How many moves along the way, 1 to 200; 0 is a straight line.")] int steps = 0)
        => Task.FromResult(McpErrors.Run(() => McpErrors.Ok(Drive("whalegenie_mouse_drag", () =>
        {
            var pressed = Button(button);
            Front().Drag(pressed, fromX, fromY, toX, toY, Span(durationMs),
                Math.Clamp(steps, 0, 200));
            return new
            {
                button = pressed,
                from = new { x = fromX, y = fromY },
                to = new { x = toX, y = toY },
                cursor = Where(),
            };
        }))));

    [McpServerTool(Name = "whalegenie_key", Destructive = true, Title = "Press keys"),
     Description("Presses one key, or a chord of keys held together such as Ctrl+Shift+S, the way a person's hands do. Input goes to whatever window has the focus.")]
    public Task<CallToolResult> Key(
        [Description("The key name, or a chord of them joined by '+'. The names are the ones the input.* actions use.")] string key,
        [Description("How long the key is held in milliseconds; 0 uses the usual short press.")] int holdMs = 0)
        => Task.FromResult(McpErrors.Run(() => McpErrors.Ok(Drive("whalegenie_key", () =>
        {
            var name = (key ?? string.Empty).Trim();
            if (name.Contains('+'))
            {
                var keys = KeyNames.ResolveChord(name, out var unknown);
                if (unknown is { Length: > 0 })
                {
                    throw new McpFailure(McpErrors.BadArgument, $"There is no key named \"{unknown}\".",
                        "Key names are the ones the input.* actions use, for example Ctrl, Shift, F5, Enter.");
                }

                // A lone "+" splits into no parts at all, so the call would be reported as sent
                // while no key was pressed.
                if (keys.Count == 0)
                {
                    throw new McpFailure(McpErrors.BadArgument, $"\"{name}\" names no key.");
                }

                var names = keys.Select(KeyNames.Name).ToArray();
                Front().Hotkey(names, Span(holdMs));
                return (object)new { keys = names, holdMs = Span(holdMs) };
            }

            if (KeyNames.Resolve(name) is null)
            {
                throw new McpFailure(McpErrors.BadArgument, $"There is no key named \"{name}\".",
                    "Key names are the ones the input.* actions use, for example Ctrl, Shift, F5, Enter.");
            }

            Front().KeyPress(name, Span(holdMs));
            return new { key = name, holdMs = Span(holdMs) };
        }))));

    [McpServerTool(Name = "whalegenie_type_text", Destructive = true, Title = "Type text"),
     Description("Types text where the keyboard is pointing, one character at a time when an interval is asked for. The text itself is not echoed back.")]
    public Task<CallToolResult> TypeText(
        [Description("The text to type.")] string text,
        [Description("Milliseconds between two characters; 0 types as fast as it can.")] int intervalMs = 0)
        => Task.FromResult(McpErrors.Run(() => McpErrors.Ok(Drive("whalegenie_type_text", () =>
        {
            Front().TypeText(text ?? string.Empty, Span(intervalMs));
            return new { characters = (text ?? string.Empty).Length, intervalMs = Span(intervalMs) };
        }, $"{(text ?? string.Empty).Length} characters"))));

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
                "Ask the person to turn on \"Allow direct control\" in the WhaleGenie MCP flyout, then call again." +
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
