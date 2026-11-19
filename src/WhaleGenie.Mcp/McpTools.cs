using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WhaleGenie.Core.Execution;
using WhaleGenie.Models;
using WhaleGenie.Storage;

namespace WhaleGenie.Mcp;

/// <summary>
/// The tools an MCP client sees. They read and write a draft of the open project; the project
/// itself is only touched by apply, and its package only by save. Running is deliberately absent:
/// the person at the machine still presses the run button.
///
/// Every answer is a JSON document and every refusal carries a code from <see cref="McpErrors"/>,
/// so a client can branch on what went wrong instead of reading English back to itself.
/// </summary>
[McpServerToolType]
public sealed partial class McpTools(McpDraftStore drafts, IProjectGateway project)
{
    /// <summary>
    /// The largest picture a client may hand over. A template a step matches against is a crop of
    /// the screen, not a photograph, so anything past this is a client sending the wrong file.
    /// </summary>
    private const int MaxImageBytes = 8 * 1024 * 1024;

    // The drafts and the catalogue are plain objects; nothing here belongs to a window, so a call
    // runs on the calling thread. The name stays because it says why a tool reads as one turn.
    private static Task<T> Ui<T>(Func<T> work) => Task.FromResult(work());
}
