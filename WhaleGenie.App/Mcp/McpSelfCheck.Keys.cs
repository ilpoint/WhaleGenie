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

// The failure keys the tools report, checked against the ones the engine uses.
internal sealed partial class McpSelfCheck
{
    /// <summary>
    /// The three action keys the scenario needs. A surface offering one batch call is allowed to
    /// take one; one that does not costs a search per key plus a describe per key, and that is the
    /// round trip the client really pays.
    /// </summary>
    private async Task DescribeKeysAsync(CancellationToken token)
    {
        var keys = new[] { "control.if", "control.repeat", "control.delay" };
        if (_tools.Contains("whalegenie_get_actions"))
        {
            var batch = await InvokeAsync("whalegenie_get_actions",
                new Dictionary<string, object?>(StringComparer.Ordinal) { ["keys"] = keys }, token)
                .ConfigureAwait(false);
            Add("discovery.batch", batch.Failed ? McpCheckResult.Missing : McpCheckResult.Pass,
                batch.Failed
                    ? batch.Text
                    : $"{keys.Length} actions in one call, {batch.Text.Length} characters");
            return;
        }

        foreach (var word in new[] { "if", "repeat", "delay" })
        {
            await InvokeAsync("whalegenie_list_actions",
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["query"] = word,
                    ["limit"] = 5,
                }, token).ConfigureAwait(false);
        }

        foreach (var key in keys)
        {
            await InvokeAsync("whalegenie_get_action",
                new Dictionary<string, object?>(StringComparer.Ordinal) { ["key"] = key }, token)
                .ConfigureAwait(false);
        }

        Add("discovery.batch", McpCheckResult.Missing,
            "no batch describe tool: one search and one describe per action");
    }
}
