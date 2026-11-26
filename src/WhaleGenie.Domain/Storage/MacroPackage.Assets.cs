using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;


namespace WhaleGenie.Storage;

// Finding the files a project's asset values name; the plan decides what the package calls them.
public static partial class MacroPackage
{

    private static string ReadString(JsonObject node, string name)
        => node[name]?.GetValue<string>() ?? string.Empty;

    private static bool ReadBool(JsonObject node, string name, bool fallback)
        => node[name] is JsonValue value && value.TryGetValue<bool>(out var result) ? result : fallback;

    /// <summary>
    /// Finds the file a picture value names. This is the file-system edge of packaging: it reads
    /// no bytes and writes nothing, so the plan can be tested with a dictionary of paths.
    /// </summary>
    internal sealed class AssetSourceResolver
    {
        private readonly IReadOnlyList<string> _sourceFolders;

        /// <param name="sourceFolders">
        /// Where to look for a picture, in order: beside the file being written first, then wherever
        /// the project it came from kept them. More than one is the normal case for an export.
        /// </param>
        public AssetSourceResolver(IEnumerable<string?> sourceFolders)
        {
            _sourceFolders = [.. sourceFolders
                .Where(folder => !string.IsNullOrWhiteSpace(folder))
                .Select(folder => Path.GetFullPath(folder!))
                .Distinct(StringComparer.OrdinalIgnoreCase)];
        }

        public string? Resolve(string value)
        {
            if (Path.IsPathRooted(value))
            {
                return ResolveFile(value);
            }

            // A value written as assets/x.png names an entry of the package, and on disk that is one
            // of two places: the picture sits beside the file (`<folder>/assets/x.png`), or the
            // folder being searched is itself the unpacked assets folder (`<folder>/x.png`). Which
            // one it is depends on where the package came from, so both are tried.
            var relative = value.Replace('/', Path.DirectorySeparatorChar);
            var name = Path.GetFileName(relative);
            foreach (var folder in _sourceFolders)
            {
                if (ResolveFile(Path.Combine(folder, relative)) is { } beside)
                {
                    return beside;
                }

                if (ResolveFile(Path.Combine(folder, name)) is { } unpacked)
                {
                    return unpacked;
                }
            }

            return ResolveFile(value);
        }

        private static string? ResolveFile(string path)
            => File.Exists(path) ? Path.GetFullPath(path) : null;
    }
}
