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

// Driving the machine: the mouse, keys and typing.
public sealed partial class McpControlTools
{
    [McpAudited(Handled = true)]
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

    [McpAudited(Handled = true)]
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

    [McpAudited(Handled = true)]
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

    [McpAudited(Handled = true)]
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

    [McpAudited(Handled = true)]
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

    [McpAudited(Handled = true)]
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
}
