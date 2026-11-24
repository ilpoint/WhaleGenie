using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace WhaleGenie.Mcp;

/// <summary>
/// Writes the line a tool declared its calls are worth, and nothing for a tool that only looks or
/// one that writes the line itself. It sits outside <see cref="McpTolerantTool"/>, so a call refused
/// for the shape of its arguments is recorded too: what was attempted is the thing a person reads
/// the log for.
/// </summary>
internal sealed class McpAuditedTool(McpServerTool inner, McpToolDeclaration declaration)
    : McpServerTool
{
    public override Tool ProtocolTool => inner.ProtocolTool;

    public override IReadOnlyList<object> Metadata => inner.Metadata;

    public override ValueTask<CallToolResult> InvokeAsync(
        RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
        => new(AuditedAsync(() => inner.InvokeAsync(request, cancellationToken),
            request.Params?.Arguments));

    /// <summary>
    /// Runs one call and records how it ended. A failed result carries the code it was refused by,
    /// and an exception is recorded before it is thrown on: either way the line says why nothing
    /// happened, not only that a call was made.
    /// </summary>
    internal async Task<CallToolResult> AuditedAsync(
        Func<ValueTask<CallToolResult>> call, IDictionary<string, JsonElement>? arguments)
    {
        // A tool that looks writes nothing, and a tool that writes its own line must not be given a
        // second one: one call is one line, and the declaration is what says which of the two it is.
        if (declaration.Handled || declaration.ReadOnly)
        {
            return await call().ConfigureAwait(false);
        }

        CallToolResult result;
        try
        {
            result = await call().ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Note(McpAuditSummary.Outcome(error));
            throw;
        }

        Note(McpAuditSummary.Outcome(result));
        return result;

        void Note(string outcome)
            => McpAudit.Note(ProtocolTool.Name,
                McpAuditSummary.Render(declaration.Summary!, arguments), outcome);
    }
}
