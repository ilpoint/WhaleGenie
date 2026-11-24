using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WhaleGenie.Core.Execution;
using WhaleGenie.Models;
using WhaleGenie.Storage;


namespace WhaleGenie.Mcp;

// A picture a client stores beside the package.
public sealed partial class McpTools
{
    /// <summary>
    /// Puts a picture where the macros look for one and answers with the name a step points at. A
    /// picture is a file in the folder the package owns, and the package carries it when the project
    /// is saved — so it is written here, not inside a draft, and it is behind the same switch as
    /// anything else that puts something on this machine.
    /// </summary>
    [McpAudited(Handled = true)]
    [McpServerTool(Name = "whalegenie_put_image", Destructive = true,
            Title = "Store an image for a macro"),
     Description("Stores an image in the project's assets folder and answers with the name a step points at. Give the bytes (base64) or a file on this machine (path). A step uses it in a parameter called image, for example {\"image\":\"shot.png\"}; the file travels with the package when the project is saved.")]
    public Task<CallToolResult> PutImage(
        [Description("The image bytes, base64 encoded; a data: URL is accepted. Give this or path, not both.")] string? base64 = null,
        [Description("A file on this machine to copy in. Give this or base64, not both.")] string? path = null,
        [Description("The name to keep it under, without a folder. Left out, one is made from the time it arrived.")] string? name = null)
        => McpErrors.RunAsync(async () =>
        {
            if (RefuseWrite() is { } refused)
            {
                return refused;
            }

            var bytes = await ReadImage(base64, path);
            if (bytes.Length > MaxImageBytes)
            {
                return McpErrors.Fail(McpErrors.BadArgument,
                    $"The image is {bytes.Length} bytes; at most {MaxImageBytes} are accepted.",
                    "Scale it down before sending it: a picture a step matches against is a small part of the screen.");
            }

            if (Extension(bytes) is not { } extension)
            {
                return McpErrors.Fail(McpErrors.BadArgument,
                    "That is not a PNG, JPEG, BMP or GIF.",
                    "A screen picture is one of those four; anything else cannot be matched against the screen.");
            }

            var status = await project.StatusAsync();
            var folder = ImageAssets.FolderFor(status.Path);
            var file = FileName(name, extension);
            var written = Unique(folder, file);
            Directory.CreateDirectory(folder);
            await File.WriteAllBytesAsync(written, bytes);

            var size = ImageSize.Of(bytes);

            file = Path.GetFileName(written);
            McpAudit.Note("whalegenie_put_image", $"{file}, {bytes.Length} bytes");
            return McpErrors.Ok(new
            {
                file,
                folder,
                width = size.Width,
                height = size.Height,
                bytes = bytes.Length,
                parameter = new { image = file },
                hint = status.Path is null
                    ? "The project has no package yet, so this sits in WhaleGenie's own folder until the first save."
                    : "Saving the project copies it into the package's assets.",
            });
        });
}
