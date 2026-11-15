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
using WhaleGenie.GraphicsCapture;


namespace WhaleGenie.Mcp;

// The windows on the machine: the list, the element at a point, and what one says.
public sealed partial class McpControlTools
{
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
}
