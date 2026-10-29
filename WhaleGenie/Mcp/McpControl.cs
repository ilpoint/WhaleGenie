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
    private static bool _allowed;

    private static bool _allowsRun;

    /// <summary>
    /// Where a run reads the permissions the MCP page stored, asked afresh each time. Null in a run
    /// that was given no such file — a check, the self check — which then has only the flags it was
    /// started with, and can be counted on to answer the same way on every machine.
    /// </summary>
    internal static Func<(bool Control, bool Run)>? Stored { get; set; }

    /// <summary>
    /// True while the person has allowed an MCP client to send keys, clicks and window commands —
    /// either by switching it on in the MCP page, which a running server reads back, or by starting
    /// this program with <c>--allow-control</c>, which overrides the page for that run. Reading the
    /// screen, the windows and the elements works whatever this says; only the tools that change
    /// something on the machine ask.
    /// </summary>
    public static bool Allowed
    {
        get => _allowed || Stored?.Invoke().Control == true;
        set => _allowed = value;
    }

    /// <summary>
    /// True while the person has allowed an MCP client to run one macro at a time. It is a second
    /// switch rather than part of the first: sending a click and running a macro that writes files
    /// and starts programs are not the same favour to ask, and a person may want one without the
    /// other.
    /// </summary>
    public static bool AllowsRun
    {
        get => _allowsRun || Stored?.Invoke().Run == true;
        set => _allowsRun = value;
    }
}
