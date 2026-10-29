using System;

namespace WhaleGenie.Mcp;

/// <summary>
/// Whether the MCP tools that drive this machine may be used right now. The answer is asked for on
/// every call rather than settled when the server starts, because the person who decides is in the
/// other window: they flip a switch, and the client they are talking to should notice on its next
/// call instead of after a restart it does not know to do.
/// </summary>
public static class McpControl
{
    /// <summary>
    /// The two switches as the MCP flyout stored them, asked afresh each time. There is no second
    /// source: the page is the only writer, and a run that was given no settings file — a check, the
    /// self check — reads nothing and may only look.
    /// </summary>
    internal static Func<(bool Control, bool Run)>? Stored { get; set; }

    /// <summary>
    /// True while the person has allowed an MCP client to send keys, clicks and window commands.
    /// Reading the screen, the windows and the elements works whatever this says; only the tools that
    /// change something on the machine ask.
    /// </summary>
    public static bool Allowed => Stored?.Invoke().Control == true;

    /// <summary>
    /// True while the person has allowed an MCP client to run one macro at a time. It is a second
    /// switch rather than part of the first: sending a click and running a macro that writes files
    /// and starts programs are not the same favour to ask, and a person may want one without the
    /// other.
    /// </summary>
    public static bool AllowsRun => Stored?.Invoke().Run == true;
}
