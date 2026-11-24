using System.ComponentModel;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace WhaleGenie.Mcp;

// Dropping a draft that will not be applied.
public sealed partial class McpTools
{
    [McpAudited("drop draft {draftId}")]
    [McpServerTool(Name = "whalegenie_discard", Destructive = true,
            Title = "Discard a draft"),
     Description("Drops a draft. Applied changes already in the project stay there and can still be saved by the person.")]
    public Task<CallToolResult> Discard(
        [Description("The draft id.")] string draftId)
    {
        if (!drafts.Remove(draftId))
        {
            return Task.FromResult(McpErrors.Fail(McpErrors.DraftNotFound,
                $"There is no draft \"{draftId}\" to discard.",
                "Call whalegenie_open_project for a new one; a draft is also dropped when the server stops.",
                retryable: true, details: new { draftId }));
        }

        return Task.FromResult(McpErrors.Ok(new { discarded = draftId }));
    }
}
