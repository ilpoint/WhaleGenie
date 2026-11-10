using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using WhaleGenie.Core.Execution;

namespace WhaleGenie.Mcp;

/// <summary>What a self-check run is allowed to do, and how long one tool call may take.</summary>
internal sealed class McpSelfCheckOptions
{
    /// <summary>What the report calls the thing that was checked: this transport is "stdio".</summary>
    public string Where { get; init; } = string.Empty;

    /// <summary>Whether the run may write a draft into the project. Off for a server someone is
    /// using; on when the check owns a throwaway copy of a package.</summary>
    public bool AllowApply { get; init; }

    public bool AllowSave { get; init; }

    public string ScratchMacro { get; init; } = "MCP self check";

    public TimeSpan CallTimeout { get; init; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// Asks a running MCP server the questions a first-time client would ask, and writes down what the
/// answers cost. It never throws and never asserts: a tool that fails becomes one line in the
/// report and the next question is still asked, because the report has to be printable on the run
/// that is half broken — that is the run that needs it.
///
/// It knows only <see cref="McpClient"/>. Who connected — a host started inside a check, a package
/// in another process — is none of its business, which is why one ruler can measure the code
/// before and after a change.
///
/// A field a newer surface does not send yet is read as absent rather than required: the ruler
/// tolerating a shape it has not met is not the product keeping a compatibility branch.
/// </summary>
internal sealed partial class McpSelfCheck(McpClient client, McpSelfCheckOptions? options = null)
{
    /// <summary>
    /// The macro the patch check writes a call to. It declares one required argument and one
    /// optional one, which is the smallest declaration a call can be written against.
    /// </summary>
    private const string Called = "Child (self-check)";

    private static readonly string[] RequiredTools =
    [
        "whalegenie_project_status", "whalegenie_open_project", "whalegenie_put_macro",
        "whalegenie_validate", "whalegenie_apply", "whalegenie_save_project",
        "whalegenie_list_actions", "whalegenie_get_action",
    ];

    /// <summary>Why a write was not tried: the switch that allows one is off.</summary>
    private const string WriteOff = "direct control is off, so the project may not be written";

    private static readonly string[] OptionalTools =
    [
        "whalegenie_instructions", "whalegenie_macro_template", "whalegenie_get_actions",
        "whalegenie_list_macros", "whalegenie_get_macro", "whalegenie_delete_macro",
        "whalegenie_set_globals", "whalegenie_discard", "whalegenie_list_runs",
        "whalegenie_get_run_log", "whalegenie_get_last_failure_log", "whalegenie_capture_screen",
        "whalegenie_pixel_at", "whalegenie_cursor", "whalegenie_list_windows",
        "whalegenie_element_at", "whalegenie_mouse_move", "whalegenie_mouse_click",
        "whalegenie_mouse_scroll", "whalegenie_mouse_drag", "whalegenie_key",
        "whalegenie_type_text", "whalegenie_window_activate", "whalegenie_window_move",
        "whalegenie_window_close", "whalegenie_get_step", "whalegenie_get_globals",
        "whalegenie_find_elements", "whalegenie_window_info", "whalegenie_clipboard_read",
        "whalegenie_clipboard_write", "whalegenie_process_list", "whalegenie_read_picture",
        "whalegenie_put_image",
        "whalegenie_run_macro", "whalegenie_stop_macro", "whalegenie_run_status",
    ];

    private readonly McpSelfCheckOptions _options = options ?? new McpSelfCheckOptions();
    private readonly McpSelfCheckReport _report = new() { Where = options?.Where ?? string.Empty };
    private readonly Dictionary<string, string[]> _parameters = new(StringComparer.Ordinal);
    private readonly List<string> _drafts = [];
    private HashSet<string> _tools = new(StringComparer.Ordinal);

    /// <summary>Whether the door that writing the project stands behind is open on this server.</summary>
    private bool _controlAllowed;

    /// <summary>
    /// Whether this run may write the project at all: it was asked to, and the switch that allows a
    /// client to change this machine is on. Reading is free; putting something in the person's file
    /// is not, so a check against a read-only server measures what it can and says what it skipped.
    /// </summary>
    private bool _writes;

    public async Task<McpSelfCheckReport> RunAsync(CancellationToken token = default)
    {
        try
        {
            await DescribeAsync(token).ConfigureAwait(false);

            // What the server says about its own doors decides whether a write is worth trying:
            // otherwise every write below is one refusal that looks like a failure.
            (_controlAllowed, _) = await PermissionsAsync(token).ConfigureAwait(false);
            _writes = _options.AllowApply && _controlAllowed;

            await LoopAsync(token).ConfigureAwait(false);
            await PatchAsync(token).ConfigureAwait(false);
            await RejectAsync(token).ConfigureAwait(false);
            await ResourcesAsync(token).ConfigureAwait(false);
            await ProjectPagesAsync(token).ConfigureAwait(false);
            await DoorsAsync(token).ConfigureAwait(false);
            await GateAsync(token).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            // A ruler that stops at the first surprise measures nothing; what it has collected up
            // to that point is still worth printing.
            Add("selfcheck.crash", McpCheckResult.Fail, Describe(error));
        }

        await DiscardAsync(token).ConfigureAwait(false);
        return _report;
    }
}
