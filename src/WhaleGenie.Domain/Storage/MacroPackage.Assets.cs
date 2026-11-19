using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WhaleGenie.Localization;
using WhaleGenie.Models;


namespace WhaleGenie.Storage;

// Collecting the assets a project uses into the package.
public static partial class MacroPackage
{

    private static string ReadString(JsonObject node, string name)
        => node[name]?.GetValue<string>() ?? string.Empty;

    private static bool ReadBool(JsonObject node, string name, bool fallback)
        => node[name] is JsonValue value && value.TryGetValue<bool>(out var result) ? result : fallback;

    private static string DefaultIfEmpty(string value, string fallback)
        => value.Length == 0 ? fallback : value;

    /// <summary>Copies referenced images into the package as it is written.</summary>
    internal sealed class AssetCollector
    {
        private readonly ZipArchive _archive;
        private readonly IReadOnlyList<string> _sourceFolders;
        private readonly Dictionary<string, string> _mapped = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _taken = new(StringComparer.OrdinalIgnoreCase);

        /// <param name="sourceFolders">
        /// Where to look for a picture, in order: beside the file being written first, then wherever
        /// the project it came from kept them. More than one is the normal case for an export.
        /// </param>
        public AssetCollector(ZipArchive archive, IEnumerable<string?> sourceFolders)
        {
            _archive = archive;
            _sourceFolders = [.. sourceFolders
                .Where(folder => !string.IsNullOrWhiteSpace(folder))
                .Select(folder => Path.GetFullPath(folder!))
                .Distinct(StringComparer.OrdinalIgnoreCase)];
        }

        /// <summary>Entry names written so far, in the order they were added.</summary>
        public List<string> Stored { get; } = [];

        public string Import(string value)
        {
            var source = Resolve(value);
            if (source is null)
            {
                return value;
            }

            if (_mapped.TryGetValue(source, out var existing))
            {
                return existing;
            }

            var name = UniqueName(Path.GetFileName(source));
            var entryName = AssetsFolder + name;
            var entry = _archive.CreateEntry(entryName, CompressionLevel.Optimal);
            using (var target = entry.Open())
            using (var origin = File.OpenRead(source))
            {
                origin.CopyTo(target);
            }

            _mapped[source] = entryName;
            Stored.Add(entryName);
            return entryName;
        }

        private static string? ResolveFile(string path)
            => File.Exists(path) ? Path.GetFullPath(path) : null;

        private string? Resolve(string value)
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

        private string UniqueName(string name)
        {
            if (name.Length == 0)
            {
                name = "image.png";
            }

            var candidate = name;
            var counter = 1;
            while (!_taken.Add(candidate))
            {
                counter++;
                candidate = $"{Path.GetFileNameWithoutExtension(name)}-{counter}{Path.GetExtension(name)}";
            }

            return candidate;
        }
    }
}
