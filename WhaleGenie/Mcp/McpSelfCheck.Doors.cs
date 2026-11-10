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

// The two doors a client has to be given: control and run, and what each one refuses.
internal sealed partial class McpSelfCheck
{
    /// <summary>
    /// Whether the two permissions behave the way the server says they are set. The server reports
    /// both in its status, and the check reads that first: a run against one started with
    /// <c>--allow-control</c> has the door open, so holding it to the read-only rule would report a
    /// failure that is not one — and the read-only probe is a real mouse move, which an allowed
    /// server would carry out while the report called it broken.
    /// </summary>
    private async Task DoorsAsync(CancellationToken token)
    {
        var (controlAllowed, runAllowed) = await PermissionsAsync(token).ConfigureAwait(false);
        await ControlDoorAsync(controlAllowed, token).ConfigureAwait(false);
        await RunDoorAsync(runAllowed, token).ConfigureAwait(false);
    }

    /// <summary>Whether sending input goes through, or is refused, as the server said it would be.</summary>
    private async Task ControlDoorAsync(bool allowed, CancellationToken token)
    {
        if (!allowed)
        {
            var refused = await InvokeAsync("whalegenie_mouse_move", PointAt(1, 1), token)
                .ConfigureAwait(false);
            var off = Refused(refused, McpErrors.DirectControlOff);
            Add("gate.control_off", off ? McpCheckResult.Pass : McpCheckResult.Fail,
                off ? "sending input is refused until the person allows it" : refused.Text);
            return;
        }

        // To where the pointer already is: proving the door is open is worth a round trip but not a
        // pointer that jumps across the screen while somebody is working.
        var cursor = await InvokeAsync("whalegenie_cursor", [], token).ConfigureAwait(false);
        if (PointerIn(cursor.Text) is not { } at)
        {
            Add("gate.control_on", McpCheckResult.Fail,
                $"the pointer could not be read, so the open door was not tried: {cursor.Text}");
            return;
        }

        var moved = await InvokeAsync("whalegenie_mouse_move", PointAt(at.X, at.Y), token)
            .ConfigureAwait(false);
        Add("gate.control_on", moved.Failed ? McpCheckResult.Fail : McpCheckResult.Pass,
            moved.Failed ? moved.Text : "sending input goes through, as the server says it may");
    }

    /// <summary>
    /// Whether running a macro is let through or refused. The name asked for does not exist, so the
    /// answer when the door is open is "no such macro": reaching the lookup at all is the proof, and
    /// nothing runs either way.
    /// </summary>
    private async Task RunDoorAsync(bool allowed, CancellationToken token)
    {
        var run = await InvokeAsync("whalegenie_run_macro",
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["name"] = "nothing in particular",
            }, token).ConfigureAwait(false);

        if (!allowed)
        {
            var off = Refused(run, McpErrors.RunNotAllowed);
            Add("gate.run_off", off ? McpCheckResult.Pass : McpCheckResult.Fail,
                off ? "running a macro is refused until the person allows it" : run.Text);
            return;
        }

        var through = !Refused(run, McpErrors.RunNotAllowed);
        Add("gate.run_on", through ? McpCheckResult.Pass : McpCheckResult.Fail,
            through ? "running a macro is let through, and stops at the name that is not there" : run.Text);
    }

    /// <summary>Which of the two permissions the server says it has, read from its own status.</summary>
    private async Task<(bool Control, bool Run)> PermissionsAsync(CancellationToken token)
    {
        var status = await InvokeAsync("whalegenie_project_status", [], token).ConfigureAwait(false);
        if (status.Failed)
        {
            return (false, false);
        }

        try
        {
            using var document = JsonDocument.Parse(status.Text);
            if (!document.RootElement.TryGetProperty("control", out var doors))
            {
                return (false, false);
            }

            return (Flag(doors, "allowed"), Flag(doors, "runAllowed"));
        }
        catch (JsonException)
        {
            return (false, false);
        }
    }

    private static bool Flag(JsonElement doors, string name)
        => doors.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static (int X, int Y)? PointerIn(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return (document.RootElement.GetProperty("x").GetInt32(),
                document.RootElement.GetProperty("y").GetInt32());
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    private static Dictionary<string, object?> PointAt(int x, int y) =>
        new(StringComparer.Ordinal) { ["x"] = x, ["y"] = y };
}
