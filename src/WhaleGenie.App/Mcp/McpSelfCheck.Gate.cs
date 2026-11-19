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

// The gate in front of a tool call, the paging of long answers and what a call costs.
internal sealed partial class McpSelfCheck
{
    private async Task GateAsync(CancellationToken token)
    {
        if (!_writes)
        {
            Add("gate.apply_invalid", McpCheckResult.Skip,
                _controlAllowed ? "the run may not write the project" : WriteOff);
            return;
        }

        var opened = await InvokeAsync("whalegenie_open_project", [], token).ConfigureAwait(false);
        var draft = Field(opened.Text, "draftId");
        if (string.IsNullOrWhiteSpace(draft))
        {
            Add("gate.apply_invalid", McpCheckResult.Fail, opened.Text);
            return;
        }

        _drafts.Add(draft!);
        var wrong = new JsonObject
        {
            ["name"] = "Gate probe",
            ["steps"] = new JsonArray(new JsonObject
            {
                ["type"] = "control.delai",
                ["params"] = new JsonObject { ["ms"] = "1" },
            }),
        };
        await InvokeAsync("whalegenie_put_macro", MacroArguments(draft!, wrong), token).ConfigureAwait(false);

        var applied = await InvokeAsync("whalegenie_apply",
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["draftId"] = draft }, token)
            .ConfigureAwait(false);
        Add("gate.apply_invalid", applied.Failed ? McpCheckResult.Pass : McpCheckResult.Missing,
            applied.Failed
                ? applied.Text
                : "apply accepted a draft the validator calls wrong");
    }

    /// <summary>
    /// Whether the list tools say how much they did not show. Read from real answers, because the
    /// shape is the question; nothing here is inspection of the schema.
    /// </summary>
    private async Task MeasurePagingAsync(string draft, CancellationToken token)
    {
        var actions = await InvokeAsync("whalegenie_list_actions",
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["limit"] = 5 }, token)
            .ConfigureAwait(false);
        var macros = await InvokeAsync("whalegenie_list_macros",
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["draftId"] = draft }, token)
            .ConfigureAwait(false);
        var runs = await InvokeAsync("whalegenie_list_runs",
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["limit"] = 5 }, token)
            .ConfigureAwait(false);
        var log = await InvokeAsync("whalegenie_get_run_log",
            new Dictionary<string, object?>(StringComparer.Ordinal), token).ConfigureAwait(false);

        _report.Facts["paging.whalegenie_list_actions"] = Shape(actions);
        _report.Facts["paging.whalegenie_list_macros"] = Shape(macros);
        _report.Facts["paging.whalegenie_list_runs"] = Shape(runs);
        _report.Facts["paging.whalegenie_get_run_log"] = Shape(log);

        _report.Metrics["paging.withTotal"] = _report.Facts
            .Count(pair => pair.Key.StartsWith("paging.", StringComparison.Ordinal)
                && pair.Value.Contains("total", StringComparison.Ordinal));
        _report.Metrics["paging.measured"] = _report.Facts
            .Count(pair => pair.Key.StartsWith("paging.", StringComparison.Ordinal));
    }

    private void MeasureCost(int start)
    {
        var window = _report.Calls.Skip(start).ToList();
        _report.Metrics["scenario.calls"] = window.Count;
        _report.Metrics["scenario.requestBytes"] = window.Sum(call => call.RequestBytes);
        _report.Metrics["scenario.responseBytes"] = window.Sum(call => call.ResponseBytes);
        _report.Metrics["scenario.longestCall"] = window.Count == 0 ? 0 : window.Max(call => call.ResponseBytes);
        _report.Facts["scenario.policy"] = _tools.Contains("whalegenie_get_actions")
            ? "status, open, one batch describe, template, put, validate, apply, save"
            : "status, open, a search and a describe per key, template, put, validate, apply, save";
    }
}
