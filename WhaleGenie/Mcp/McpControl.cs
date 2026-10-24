using System;

namespace WhaleGenie.Mcp;

/// <summary>
/// Whether the MCP tools that drive this machine may be used right now. It is a switch in the
/// flyout rather than a file setting: the server itself starts off, and so does control, so a
/// restart of the program leaves both off until the person turns them on. The flyout is the only
/// writer; the tools are the only readers.
/// </summary>
public static class McpControl
{
    /// <summary>
    /// True while the person has allowed an MCP client to send keys, clicks and window commands.
    /// Reading the screen, the windows and the elements works whatever this says; only the tools
    /// that change something on the machine ask.
    /// </summary>
    public static bool Allowed { get; set; }
}
