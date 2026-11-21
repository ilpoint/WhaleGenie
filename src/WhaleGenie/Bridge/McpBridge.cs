using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;
using WhaleGenie.Mcp;
using WhaleGenie.Runtime;

namespace WhaleGenie.Bridge;

/// <summary>
/// The MCP flyout's side of the bridge: the two grants a client works under, the configuration
/// block a person pastes into that client once, and the audit trail. There is no server to switch
/// on here — a client starts this program with <c>--mcp</c> and reads the grants from the settings
/// file before every call, so flipping a switch takes effect for the client that is already
/// running.
/// </summary>
public sealed class McpBridge(Func<IClipboardDevice> clipboard) : IBridgeHandler
{
    /// <summary>The one clipboard the program holds; configuration text goes on it.</summary>
    private readonly Func<IClipboardDevice> _clipboard = clipboard;

    /// <summary>The block's shape, made once rather than every time the text is read.</summary>
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private BridgeServer? _server;

    public void Register(BridgeServer server)
    {
        _server = server;
        server.Register("mcp.state", _ => Task.FromResult<object?>(State()));
        server.Register("mcp.setGrants", SetGrants);
        server.Register("mcp.copyConfig", CopyConfig);
        server.Register("mcp.openAudit", OpenAudit);
    }

    private object State() => new
    {
        allowControl = LocalSettings.LoadMcpGrants().Control,
        allowRun = LocalSettings.LoadMcpGrants().Run,
        config = ConfigText(),
        auditFile = McpAudit.File,
    };

    private async Task<object?> SetGrants(JsonElement parameters)
    {
        var control = parameters.TryGetProperty("control", out var controlGiven)
            && controlGiven.ValueKind == JsonValueKind.True;
        var run = parameters.TryGetProperty("run", out var runGiven) && runGiven.ValueKind == JsonValueKind.True;

        LocalSettings.StoreMcpGrants(control, run);
        await _server!.PushAsync("mcp.changed", State());
        return null;
    }

    /// <summary>The block a person pastes into a client's configuration file, once.</summary>
    private static string ConfigText() => JsonSerializer.Serialize(new
    {
        mcpServers = new
        {
            whalegenie = new
            {
                command = McpProgram.Executable,
                args = new[] { "--mcp" },
            },
        },
    }, WriteOptions);

    /// <summary>Puts the configuration block on the clipboard the macros use.</summary>
    private Task<object?> CopyConfig(JsonElement parameters)
    {
        _clipboard().WriteText(ConfigText());
        return Task.FromResult<object?>(null);
    }

    /// <summary>
    /// Opens the file the calls are recorded in — the folder when nothing has been written yet,
    /// which is a different answer from an empty file.
    /// </summary>
    private Task<object?> OpenAudit(JsonElement parameters)
    {
        var file = McpAudit.File;
        var target = File.Exists(file) ? file : Models.AppPaths.Logs;
        Directory.CreateDirectory(Models.AppPaths.Logs);
        Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
        return Task.FromResult<object?>(null);
    }
}
