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
public sealed partial class McpControlTools
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
}
