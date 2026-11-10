using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WhaleGenie.Core.Execution;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;


namespace WhaleGenie.Mcp;

// Opening the project a client works on, the drafts it has open, and reading a picture beside it.
public sealed partial class McpTools
{
    [McpServerTool(Name = "whalegenie_open_project",
            Title = "Open a draft"),
     Description("Takes a snapshot of the open project and returns a draft id for editing it. Everything is edited through that id; nothing reaches the window until apply. A session holds at most eight drafts: opening one more drops the oldest that was already applied, and says which in dropped; with none applied the call is refused with TooManyDrafts rather than losing work.")]
    public Task<CallToolResult> OpenProject()
        => McpErrors.RunAsync(async () =>
        {
            var basis = await project.SnapshotAsync();
            var (draft, dropped) = drafts.Create(basis);
            return McpErrors.Ok(new
            {
                draftId = draft.Id,
                path = basis.Path,
                revision = basis.Revision,
                macros = basis.Macros.Select(McpOutline.Summary),

                // A draft that was applied has already reached the project; saying which ones were
                // let go is what keeps the list a client holds from being quietly wrong.
                dropped,
            });
        });

    [McpServerTool(Name = "whalegenie_list_drafts", ReadOnly = true,
            Title = "List the drafts that are open"),
     Description("Lists the drafts this server is holding: the id, the project each was taken from, when it was taken, how many macros it holds and whether it has been applied. A draft is dropped when the server stops or when it is discarded, so this is the answer to \"what is still pending\".")]
    public Task<CallToolResult> ListDrafts()
        => McpErrors.RunAsync(async () =>
        {
            var open = drafts.All();
            return McpErrors.Ok(new
            {
                total = open.Count,
                drafts = await Ui(() => open.Select(draft => new
                {
                    draftId = draft.Id,
                    path = draft.Basis.Path,
                    revision = draft.Basis.Revision,
                    taken = draft.CreatedAt,
                    macros = draft.MacroSnapshot().Count,
                    applied = draft.Applied,
                }).ToList()),
            });
        });

    [McpServerTool(Name = "whalegenie_read_picture", ReadOnly = true,
            Title = "Read a picture a macro saved"),
     Description("Returns a PNG that a step saved, as an image. Only files inside the project folder or the log folder are read: this is for looking at what a capture step produced, not for reading any file on the machine.")]
    public Task<CallToolResult> ReadPicture(
        [Description("The path a capture step reported, such as the path result of vision.capture.")] string path)
        => McpErrors.RunAsync(async () =>
        {
            var status = await project.StatusAsync();
            var roots = new List<string> { AppPaths.Logs };
            if (Path.GetDirectoryName(status.Path) is { Length: > 0 } folder)
            {
                roots.Add(folder);
            }

            var full = Path.GetFullPath((path ?? string.Empty).Trim());
            var allowed = roots.Any(root => full.StartsWith(
                Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));

            if (!allowed)
            {
                return McpErrors.Fail(McpErrors.BadArgument,
                    "That file is outside the project folder and the log folder.",
                    "Pass the path a capture step reported; only what the project itself wrote is read.");
            }

            if (!full.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
            {
                return McpErrors.Fail(McpErrors.BadArgument, $"There is no PNG at \"{full}\".",
                    "A capture step reports the path it wrote; call it again if the file is gone.");
            }

            var bytes = await File.ReadAllBytesAsync(full);
            if (bytes.Length > 8 * 1024 * 1024)
            {
                return McpErrors.Fail(McpErrors.BadArgument, "That picture is larger than 8 MB.",
                    "Capture a smaller region, or read it on the machine instead.");
            }

            McpAudit.Note("whalegenie_read_picture", $"{Path.GetFileName(full)}, {bytes.Length} bytes");
            return new CallToolResult
            {
                Content =
                [
                    ImageContentBlock.FromBytes(bytes, "image/png"),
                    new TextContentBlock
                    {
                        Text = JsonSerializer.Serialize(new { path = full, bytes = bytes.Length }),
                    },
                ],
            };
        });
}
