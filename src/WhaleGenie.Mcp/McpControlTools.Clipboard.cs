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

// The clipboard and the programs running.
public sealed partial class McpControlTools
{
    [McpServerTool(Name = "whalegenie_clipboard_read", Title = "Read the clipboard"),
     Description("Returns what is on the clipboard as text, and how many files it holds when files were copied. Reading a page's text after selecting and copying it is steadier than reading the screen. Needs \"Allow direct control\"（允许直接控制）: what is on the clipboard is whatever the person last copied, which can be a password.")]
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
     Description("Puts text on the clipboard, so a step that pastes can be tried without typing. Needs \"Allow direct control\"（允许直接控制）.")]
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
}
