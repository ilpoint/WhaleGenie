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

// The rest of the answers: the documents of a project, the ids a rename touched, refusals, and file names.
public sealed partial class McpTools
{
    /// <summary>
    /// Every submacro below a macro, each with the path that reaches it: <c>sub:a</c>, then
    /// <c>sub:a/sub:b</c> for one inside that.
    /// </summary>
    private static IEnumerable<(MacroItem Sub, string Label)> Documents(
        IReadOnlyList<MacroItem> subs, string scope)
    {
        foreach (var sub in subs)
        {
            var label = scope == "main" ? "sub:" + sub.Name.Trim() : scope + "/sub:" + sub.Name.Trim();
            yield return (sub, label);
            foreach (var nested in Documents(sub.SubMacros, label))
            {
                yield return nested;
            }
        }
    }

    /// <summary>
    /// The ids a write had to move because two steps arrived sharing one, said as the pairs they
    /// are. A client that wrote expressions against its own ids can see what was kept instead.
    /// </summary>
    private static IReadOnlyList<object> Renamed(IReadOnlyList<StepIds.Rename> renamed)
        => [.. renamed.Select(rename => (object)new { from = rename.Old, to = rename.New })];

    /// <summary>
    /// The two tools that write the project stand behind one door: the switch that allows this
    /// machine to be changed. Editing a draft is not behind it — a draft is a copy, and looking at
    /// what it would do costs nothing.
    /// </summary>
    private static CallToolResult? RefuseWrite()
        => McpControl.Allowed
            ? null
            : McpErrors.Fail(McpErrors.ProjectWriteOff,
                "Direct control is off, so the project was not written.",
                "Ask the person to turn on \"Allow direct control\"（允许直接控制） in the WhaleGenie MCP flyout, then call again." +
                " Opening, editing and validating a draft needs no switch.",
                retryable: true);

    /// <summary>The image the call carried, from the bytes or from a file, or a refusal.</summary>
    private static async Task<byte[]> ReadImage(string? base64, string? path)
    {
        var hasBytes = !string.IsNullOrWhiteSpace(base64);
        var hasFile = !string.IsNullOrWhiteSpace(path);
        if (hasBytes == hasFile)
        {
            throw new McpFailure(McpErrors.BadArgument,
                hasBytes ? "Give either base64 or path, not both." : "Give the image as base64 or as a path.",
                "A file on this machine is cheaper to send; the bytes are for a picture that is not on disk yet.");
        }

        if (hasFile)
        {
            var file = path!.Trim();
            if (!File.Exists(file))
            {
                throw new McpFailure(McpErrors.BadArgument, $"There is no file at \"{file}\".",
                    "The path is read on this machine, where the server runs, not where the client does.");
            }

            return await File.ReadAllBytesAsync(Path.GetFullPath(file));
        }

        var text = base64!.Trim();

        // A data URL is what a model tends to have when the picture came from a chat; the part
        // before the comma is a description of the bytes, not part of them.
        var comma = text.IndexOf(',');
        if (text.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma > 0)
        {
            text = text[(comma + 1)..];
        }

        try
        {
            return Convert.FromBase64String(text);
        }
        catch (FormatException)
        {
            throw new McpFailure(McpErrors.BadJson, "The base64 was not base64.",
                "Send the bytes without line breaks; a data URL is accepted as it is.");
        }
    }

    /// <summary>What kind of picture the bytes are, from the signature they start with.</summary>
    private static string? Extension(byte[] bytes)
    {
        if (bytes.Length > 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
        {
            return ".png";
        }

        if (bytes.Length > 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return ".jpg";
        }

        if (bytes.Length > 2 && bytes[0] == 0x42 && bytes[1] == 0x4D)
        {
            return ".bmp";
        }

        return bytes.Length > 6 && bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46
            ? ".gif"
            : null;
    }

    /// <summary>
    /// The name to store the picture under: what was asked for, with the extension the bytes
    /// actually are, and without a folder — this writes into one folder and nowhere else.
    /// </summary>
    private static string FileName(string? name, string extension)
    {
        var asked = (name ?? string.Empty).Trim();
        asked = Path.GetFileName(asked.Replace('\\', '/'));
        if (asked.Length == 0)
        {
            return "image-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + extension;
        }

        var stem = Path.GetFileNameWithoutExtension(asked);
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string([.. stem.Where(character => !invalid.Contains(character))]).Trim();
        return (clean.Length == 0 ? "image" : clean) + extension;
    }

    /// <summary>
    /// A path in that folder which nothing else is using. A second picture under the same name is
    /// kept rather than overwriting the one a step already points at.
    /// </summary>
    private static string Unique(string folder, string file)
    {
        var candidate = Path.Combine(folder, file);
        var stem = Path.GetFileNameWithoutExtension(file);
        var extension = Path.GetExtension(file);
        var counter = 1;
        while (File.Exists(candidate))
        {
            counter++;
            candidate = Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"{stem}-{counter}{extension}"));
        }

        return candidate;
    }

    /// <summary>The macros this draft moved, named against the snapshot it came from. Saying what changed is
    /// cheaper for a reader than diffing two macro dumps, and it is what "what did you do" means.
    /// </summary>
    private static IReadOnlyList<string> Changed(McpDraft draft)
    {
        var before = draft.Basis.Macros.ToDictionary(
            macro => macro.Name.Trim(), macro => macro.ToJson().ToJsonString(), StringComparer.OrdinalIgnoreCase);
        var after = draft.MacroSnapshot().ToDictionary(
            macro => macro.Name.Trim(), macro => macro.ToJson().ToJsonString(), StringComparer.OrdinalIgnoreCase);

        var changed = new List<string>();
        foreach (var (name, json) in after)
        {
            if (!before.TryGetValue(name, out var was) || !string.Equals(was, json, StringComparison.Ordinal))
            {
                changed.Add(name);
            }
        }

        changed.AddRange(before.Keys.Where(name => !after.ContainsKey(name)).Select(name => "-" + name));
        return changed;
    }

    private static object RunSummary(RunLogSnapshot run) => new
    {
        run.Id,
        run.Macro,
        run.Source,
        run.Started,
        run.Finished,
        run.Status,
        run.Key,
        run.Detail,
        run.Steps,
        run.FailureLogPath,
        lines = run.Lines.Count,
    };
}
