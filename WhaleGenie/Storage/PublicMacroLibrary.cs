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
using WhaleGenie.Core.Execution;
using WhaleGenie.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.Storage;

/// <summary>One versioned callable macro in the machine-wide public library.</summary>
public sealed record PublicMacroEntry(string Id, int Version, MacroItem Macro);

/// <summary>
/// Reads and writes the public macro library. The library is deliberately separate from projects:
/// a project receives a copy, so changing or deleting the source cannot alter a project already
/// written.
/// </summary>
public static class PublicMacroLibrary
{
    public const string FormatId = "whalegenie.public-macro.library";
    public const int FormatVersion = 1;

    private const string ManifestEntry = "manifest.json";
    private const string MacrosFolder = "macros/";
    private const string AssetsFolder = "assets/";
    private const string ImageParameter = "image";

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>Loads the library, answering an empty list when the file has not been made yet.</summary>
    public static IReadOnlyList<PublicMacroEntry> Load(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            return [];
        }

        using var stream = File.OpenRead(fullPath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var manifest = ReadObject(archive, ManifestEntry)
            ?? throw new InvalidDataException(
                Strings.Get("PublicMacroManager.NoManifest"));
        if (!string.Equals(manifest["format"]?.GetValue<string>(), FormatId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                Strings.Get("PublicMacroManager.NotALibrary"));
        }

        var version = manifest["version"]?.GetValue<int>() ?? 0;
        if (version != FormatVersion)
        {
            throw new InvalidDataException(Strings.Format(
                "PublicMacroManager.WrongVersion", version, FormatVersion));
        }

        var assets = new AssetReader(archive, AssetFolderFor(fullPath));
        var entries = new List<PublicMacroEntry>();
        foreach (var item in manifest["macros"] as JsonArray ?? [])
        {
            if (item is not JsonObject descriptor)
            {
                continue;
            }

            var id = descriptor[FieldNames.Id]?.GetValue<string>()?.Trim() ?? string.Empty;
            var document = descriptor["document"]?.GetValue<string>()?.Trim() ?? string.Empty;
            if (id.Length == 0 || document.Length == 0
                || ReadObject(archive, document) is not { } node)
            {
                continue;
            }

            RewriteImages(node, assets.Expand);
            var macro = MacroItem.FromJson(node);
            macro.Name = descriptor[FieldNames.Name]?.GetValue<string>()?.Trim()
                ?? macro.Name.Trim();
            macro.Description = descriptor[FieldNames.Description]?.GetValue<string>()
                ?? macro.Description;
            var macroVersion = descriptor[FieldNames.Version] is JsonValue number
                && number.TryGetValue<int>(out var parsed)
                    ? parsed
                    : 1;
            entries.Add(new PublicMacroEntry(id, Math.Max(1, macroVersion), macro));
        }

        return entries;
    }

    /// <summary>Writes the whole library atomically, preserving the old file on failure.</summary>
    public static void Save(string path, IReadOnlyList<PublicMacroEntry> entries,
        string appVersion)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            var name = entry.Macro.Name.Trim();
            if (name.Length == 0 || !names.Add(name) || !ids.Add(entry.Id.Trim()))
            {
                throw new InvalidDataException(Strings.Get("PublicMacroManager.DuplicateName"));
            }

            var problems = entry.Macro.Callable.Problems();
            if (problems.Count > 0)
            {
                throw new InvalidDataException(Strings.Format(
                    "PublicMacroManager.BadDeclaration", name, problems[0].Name));
            }
        }

        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? ".");
        var temporary = fullPath + ".tmp";

        try
        {
            using (var stream = File.Create(temporary))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                var assets = new AssetWriter(archive, AssetFolderFor(fullPath));
                var macros = new JsonArray();
                foreach (var entry in entries)
                {
                    var document = entry.Macro.ToSubMacroJson();
                    RewriteImages(document, assets.Import);
                    var fileName = $"{MacrosFolder}{SafeId(entry.Id)}.json";
                    WriteEntry(archive, fileName, document.ToJsonString(Indented));
                    macros.Add(new JsonObject
                    {
                        [FieldNames.Id] = entry.Id,
                        [FieldNames.Name] = entry.Macro.Name.Trim(),
                        [FieldNames.Description] = entry.Macro.Description,
                        [FieldNames.Version] = Math.Max(1, entry.Version),
                        ["document"] = fileName,
                    });
                }

                var manifest = new JsonObject
                {
                    ["format"] = FormatId,
                    ["version"] = FormatVersion,
                    ["app"] = "WhaleGenie",
                    ["appVersion"] = appVersion,
                    ["savedUtc"] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                    ["macros"] = macros,
                    ["assets"] = new JsonArray(
                        [.. assets.Stored.Select(name => JsonValue.Create(name))]),
                };
                WriteEntry(archive, ManifestEntry, manifest.ToJsonString(Indented));
            }

            File.Move(temporary, fullPath, true);
        }
        catch
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }

            throw;
        }
    }

    /// <summary>Where this library's unpacked images live.</summary>
    public static string AssetFolderFor(string path)
        => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".",
            Path.GetFileNameWithoutExtension(path) + ".assets");

    /// <summary>
    /// Copies one public macro into a project, rewriting every referenced image to a content-named
    /// file in the project's asset folder. The returned tree has no live dependency on the library.
    /// </summary>
    public static MacroItem CopyToProject(PublicMacroEntry entry, string assetFolder,
        string? sourceAssetFolder = null)
    {
        var node = entry.Macro.ToSubMacroJson();
        RewriteImages(node, value => CopyImage(value, assetFolder,
            sourceAssetFolder ?? AssetFolderFor(AppPaths.PublicMacroLibrary)));
        var copy = MacroItem.FromJson(node);
        copy.Name = entry.Macro.Name.Trim();
        copy.Description = entry.Macro.Description;
        copy.Origin = new MacroOrigin(entry.Id, entry.Macro.Name.Trim(),
            Math.Max(1, entry.Version));
        return copy;
    }

    private static string CopyImage(string value, string folder, string sourceFolder)
    {
        var source = ImageAssets.Resolve(value, sourceFolder);
        if (source is null)
        {
            return value;
        }

        Directory.CreateDirectory(folder);
        using var stream = File.OpenRead(source);
        var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant()[..16];
        var extension = Path.GetExtension(source);
        if (extension.Length == 0)
        {
            extension = ".png";
        }

        var target = Path.Combine(folder, $"public-{hash}{extension.ToLowerInvariant()}");
        if (!File.Exists(target))
        {
            File.Copy(source, target);
        }

        return Path.GetFullPath(target);
    }

    private static string SafeId(string id)
    {
        var safe = new string([.. id.Where(char.IsLetterOrDigit)]);
        return safe.Length == 0 ? "macro" : safe;
    }

    private static void RewriteImages(JsonNode? node, Func<string, string> map)
    {
        switch (node)
        {
            case JsonObject container:
                foreach (var name in container.Select(pair => pair.Key).ToList())
                {
                    var child = container[name];
                    if (name == ImageParameter && child is JsonValue value
                        && value.TryGetValue<string>(out var text) && text.Length > 0)
                    {
                        container[name] = map(text);
                    }
                    else
                    {
                        RewriteImages(child, map);
                    }
                }

                break;
            case JsonArray array:
                foreach (var child in array)
                {
                    RewriteImages(child, map);
                }

                break;
        }
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static JsonObject? ReadObject(ZipArchive archive, string name)
    {
        var entry = archive.GetEntry(name);
        if (entry is null)
        {
            return null;
        }

        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        return JsonNode.Parse(reader.ReadToEnd()) as JsonObject;
    }

    private sealed class AssetWriter
    {
        private readonly ZipArchive _archive;
        private readonly string _sourceFolder;
        private readonly Dictionary<string, string> _mapped = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _taken = new(StringComparer.OrdinalIgnoreCase);

        public AssetWriter(ZipArchive archive, string sourceFolder)
        {
            _archive = archive;
            _sourceFolder = sourceFolder;
        }

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

        private string? Resolve(string value)
        {
            if (Path.IsPathRooted(value))
            {
                return File.Exists(value) ? Path.GetFullPath(value) : null;
            }

            var relative = value.Replace('/', Path.DirectorySeparatorChar);
            var stored = Path.Combine(_sourceFolder, relative);
            if (File.Exists(stored))
            {
                return Path.GetFullPath(stored);
            }

            return File.Exists(value) ? Path.GetFullPath(value) : null;
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
                candidate =
                    $"{Path.GetFileNameWithoutExtension(name)}-{counter}{Path.GetExtension(name)}";
            }

            return candidate;
        }
    }

    private sealed class AssetReader
    {
        private readonly ZipArchive _archive;
        private readonly string _folder;
        private readonly Dictionary<string, string> _mapped = new(StringComparer.OrdinalIgnoreCase);

        public AssetReader(ZipArchive archive, string folder)
        {
            _archive = archive;
            _folder = folder;
        }

        public string Expand(string value)
        {
            if (!value.StartsWith(AssetsFolder, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }

            if (_mapped.TryGetValue(value, out var existing))
            {
                return existing;
            }

            var entry = _archive.GetEntry(value);
            if (entry is null)
            {
                return value;
            }

            var target = Path.Combine(_folder, Path.GetFileName(value));
            if (!File.Exists(target))
            {
                Directory.CreateDirectory(_folder);
                entry.ExtractToFile(target, true);
            }

            var resolved = Path.GetFullPath(target);
            _mapped[value] = resolved;
            return resolved;
        }
    }
}
