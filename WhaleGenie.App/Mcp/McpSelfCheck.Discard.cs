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

// Discarding a draft, and the call every check goes through.
internal sealed partial class McpSelfCheck
{
    private async Task DiscardAsync(CancellationToken token)
    {
        foreach (var draft in _drafts.ToArray())
        {
            var answer = await InvokeAsync("whalegenie_discard",
                new Dictionary<string, object?>(StringComparer.Ordinal) { ["draftId"] = draft }, token)
                .ConfigureAwait(false);
            Add($"cleanup.{draft}", answer.Failed ? McpCheckResult.Missing : McpCheckResult.Pass,
                answer.Failed ? answer.Text : "discarded");
        }

        _drafts.Clear();
    }

    private async Task<Answer> InvokeAsync(string tool, Dictionary<string, object?> arguments,
        CancellationToken token)
    {
        var requestBytes = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(arguments));
        var watch = Stopwatch.StartNew();
        if (!_tools.Contains(tool))
        {
            _report.Calls.Add(new McpCall(tool, requestBytes, 0, 0, true));
            return new Answer(true, $"\"{tool}\" is not among the tools this server lists");
        }

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
        limit.CancelAfter(_options.CallTimeout);
        try
        {
            var result = await client.CallToolAsync(tool, arguments, cancellationToken: limit.Token)
                .ConfigureAwait(false);
            var text = string.Concat((result.Content ?? [])
                .OfType<TextContentBlock>()
                .Select(block => block.Text));
            var failed = result.IsError == true;
            _report.Calls.Add(new McpCall(tool, requestBytes, Encoding.UTF8.GetByteCount(text),
                watch.ElapsedMilliseconds, failed));
            return new Answer(failed, text);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            _report.Calls.Add(new McpCall(tool, requestBytes, 0, watch.ElapsedMilliseconds, true));
            return new Answer(true, Describe(error));
        }
    }
}
