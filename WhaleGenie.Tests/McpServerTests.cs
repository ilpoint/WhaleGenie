using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Mcp;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// The in-process MCP endpoint. It is started on a loopback port, listed and called through the
/// official client; no window, no device and no external service is involved.
/// </summary>
[RestoreSharedState]
public partial class McpServerTests : IDisposable
{
    /// <summary>
    /// The two switches, open for this class: what these checks are about is the project's tools,
    /// not the door in front of them. The door has checks of its own.
    /// </summary>
    private readonly McpGate _gate = new() { Control = true };

    public void Dispose() => _gate.Dispose();

    private static async Task<string> Call(McpClient client, string name,
        Dictionary<string, object?> arguments)
    {
        var result = await client.CallToolAsync(name, arguments);
        return result.Content.OfType<TextContentBlock>().First().Text;
    }
}
