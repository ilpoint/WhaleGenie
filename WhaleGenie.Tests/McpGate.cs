using System;
using WhaleGenie.Mcp;

namespace WhaleGenie.Tests;

/// <summary>
/// Stands in for the MCP page's two switches, which the real program reads from its settings file.
/// A check sets them here instead of writing a file, so nothing a check does depends on, or disturbs,
/// the choices of whoever is using WhaleGenie on this machine.
/// </summary>
internal sealed class McpGate : IDisposable
{
    public McpGate() => McpControl.Stored = () => (Control, Run);

    public bool Control { get; set; }

    public bool Run { get; set; }

    /// <summary>Puts the gate back to "nothing has been written", which is what other checks expect.</summary>
    public void Dispose() => McpControl.Stored = null;
}
