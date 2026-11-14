using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Devices.Platform;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.GraphicsCapture;


namespace WhaleGenie.Core.Execution;

// Everything else a file step does: saving variables, base64, and hashing text.
public sealed partial class MacroRunner
{
    private void SaveVariables(ExecutableStep step, int depth)
    {
        var path = PathOf(step);
        var chosen = ReadText(step, "names").Trim();
        var names = chosen.Length > 0
            ? chosen.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [.. Variables.Local.Values.Keys
                .Concat(Variables.Global.Values.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)];

        var document = new JsonObject();
        foreach (var name in names)
        {
            if (Variables.TryGet(name, out var value))
            {
                document[name] = ToJson(value);
            }
        }

        var text = document.ToJsonString(Writable);
        _devices.Files.WriteText(path, text, false, EncodingOf(step));
        Log(LogLevel.Info, depth, step.Type, LogKeys.SavedVariables, path, document.Count);
    }

    private void LoadVariables(ExecutableStep step, int depth)
    {
        var path = PathOf(step);
        var document = Json(path, _devices.Files.ReadText(path, EncodingOf(step)));
        if (document is not JsonObject saved)
        {
            throw new StepFailure(RunKeys.BadJson, path);
        }

        foreach (var (name, node) in saved)
        {
            // Shared variables are only written to when the Variable Center already made one,
            // the same rule the "set variable" step follows.
            var target = Variables.Global.Contains(name) ? Variables.Global : Variables.Local;
            target.Set(name, FromJson(node));
        }

        Log(LogLevel.Info, depth, step.Type, LogKeys.LoadedVariables, path, saved.Count);
    }

    /// <summary>
    /// Encodes text as Base64. The text is read the way any other value is and then taken as
    /// UTF-8, which is the encoding every service a Base64 value is handed to expects.
    /// </summary>
    private void EncodeBase64(ExecutableStep step, int depth)
    {
        var text = ReadValue(step, "dataText").AsText();
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
        Store(step, "encoded", Value.FromText(encoded), depth);
    }

    /// <summary>
    /// Decodes Base64 back into text. Text that is not Base64 fails the step rather than turning
    /// into a value the macro cannot explain: a macro that decoded nothing should say so.
    /// </summary>
    private void DecodeBase64(ExecutableStep step, int depth)
    {
        var text = ReadValue(step, "dataText").AsText();
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(text.Trim());
        }
        catch (FormatException)
        {
            throw new StepFailure(RunKeys.BadBase64, text);
        }

        Store(step, "decoded", Value.FromText(Encoding.UTF8.GetString(bytes)), depth);
    }

    /// <summary>
    /// Works out the checksum of a piece of text or of a file. A file is walked over a piece at a
    /// time rather than read whole, because the files a checksum is asked about — an installer, a
    /// download, a backup — are exactly the ones that do not fit in memory.
    /// </summary>
    private void HashText(ExecutableStep step, int depth)
    {
        var algorithm = ReadText(step, "hashAlgorithm");
        byte[] digest;
        if (string.Equals(ReadText(step, "hashSource").Trim(), "file", StringComparison.OrdinalIgnoreCase))
        {
            var path = PathOf(step);
            using var file = _devices.Files.OpenRead(path);
            digest = ComputeHash(algorithm, file);
        }
        else
        {
            var text = ReadValue(step, "dataText").AsText();
            digest = ComputeHash(algorithm, new MemoryStream(Encoding.UTF8.GetBytes(text)));
        }

        Store(step, "digest", Value.FromText(Convert.ToHexString(digest).ToLowerInvariant()), depth);
    }

    /// <summary>
    /// The checksum a named algorithm works out. An unrecognized name falls back to SHA-256 rather
    /// than failing: a step that asked for a checksum should get one the macro can compare.
    /// </summary>
    private static byte[] ComputeHash(string algorithm, Stream bytes) =>
        algorithm.Trim().ToLowerInvariant() switch
        {
            "md5" => MD5.HashData(bytes),
            "sha1" => SHA1.HashData(bytes),
            "sha512" => SHA512.HashData(bytes),
            _ => SHA256.HashData(bytes),
        };
}
