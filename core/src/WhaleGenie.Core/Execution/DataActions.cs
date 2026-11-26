using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// The data actions: Base64 both ways and a checksum. They work on what a step holds rather than on
/// the machine, except that a checksum may be asked of a file and is then read a piece at a time.
/// </summary>
internal static class DataActions
{
    /// <summary>
    /// The data actions, each answered by the method written for it. A step of another family is
    /// not in this table.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, RunActionHandler> Map =
        new Dictionary<string, RunActionHandler>(StringComparer.Ordinal)
        {
            ["data.base64Encode"] = (run, step, depth, _) =>
            {
                EncodeBase64(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["data.base64Decode"] = (run, step, depth, _) =>
            {
                DecodeBase64(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["data.hash"] = (run, step, depth, _) =>
            {
                HashText(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },
        };

    /// <summary>
    /// Encodes text as Base64. The text is read the way any other value is and then taken as
    /// UTF-8, which is the encoding every service a Base64 value is handed to expects.
    /// </summary>
    private static void EncodeBase64(IRunContext run, ExecutableStep step, int depth)
    {
        var text = run.Args(step).ValueOf("dataText").AsText();
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
        run.Store(step, "encoded", Value.FromText(encoded), depth);
    }

    /// <summary>
    /// Decodes Base64 back into text. Text that is not Base64 fails the step rather than turning
    /// into a value the macro cannot explain: a macro that decoded nothing should say so.
    /// </summary>
    private static void DecodeBase64(IRunContext run, ExecutableStep step, int depth)
    {
        var text = run.Args(step).ValueOf("dataText").AsText();
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(text.Trim());
        }
        catch (FormatException)
        {
            throw new StepFailure(RunKeys.BadBase64, text);
        }

        run.Store(step, "decoded", Value.FromText(Encoding.UTF8.GetString(bytes)), depth);
    }

    /// <summary>
    /// Works out the checksum of a piece of text or of a file. A file is walked over a piece at a
    /// time rather than read whole, because the files a checksum is asked about — an installer, a
    /// download, a backup — are exactly the ones that do not fit in memory.
    /// </summary>
    private static void HashText(IRunContext run, ExecutableStep step, int depth)
    {
        var algorithm = run.Args(step).Text("hashAlgorithm");
        byte[] digest;
        if (string.Equals(run.Args(step).Text("hashSource").Trim(), "file",
                StringComparison.OrdinalIgnoreCase))
        {
            var path = run.PathOf(step);
            using var file = run.Devices.Files.OpenRead(path);
            digest = ComputeHash(algorithm, file);
        }
        else
        {
            var text = run.Args(step).ValueOf("dataText").AsText();
            digest = ComputeHash(algorithm, new MemoryStream(Encoding.UTF8.GetBytes(text)));
        }

        run.Store(step, "digest", Value.FromText(Convert.ToHexString(digest).ToLowerInvariant()),
            depth);
    }

    /// <summary>
    /// The checksum a named algorithm works out. An unrecognized name falls back to SHA-256 rather
    /// than failing: a step that asked for a checksum should get one the macro can compare. MD5 and
    /// SHA-1 are offered because the file or site being compared with still names them; this is a
    /// checksum for a macro, not a security decision.
    /// </summary>
#pragma warning disable CA5350, CA5351
    private static byte[] ComputeHash(string algorithm, Stream bytes) =>
        algorithm.Trim().ToLowerInvariant() switch
        {
            "md5" => MD5.HashData(bytes),
            "sha1" => SHA1.HashData(bytes),
            "sha512" => SHA512.HashData(bytes),
            _ => SHA256.HashData(bytes),
        };
#pragma warning restore CA5350, CA5351
}
