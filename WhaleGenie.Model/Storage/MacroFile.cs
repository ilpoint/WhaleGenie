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

/// <summary>What a single macro file was written for: a whole macro, or one that lives inside another.</summary>
public enum MacroFileKind
{
    /// <summary>A macro of its own, with its own triggers and its own place in the list.</summary>
    Macro,

    /// <summary>A macro that is called by another one rather than started on its own.</summary>
    SubMacro,
}

/// <summary>What a macro file holds: the macro, and what it was written as.</summary>
public sealed record MacroFileContents(MacroItem Macro, MacroFileKind Kind);

/// <summary>
/// Reads and writes <c>.wgm</c>, one macro in one file.
/// </summary>
/// <remarks>
/// A project package holds every macro and the variables they share; this holds one macro and the
/// pictures it looks for, so a macro that works can be handed to somebody without handing them the
/// whole project. It is the same zip shape as a package — a manifest, one document, the images
/// under <c>assets/</c> — so a person who unzips one finds the same things in the same places.
///
/// Two shapes travel in it: the macro's own steps and its submacros, which is everything a call
/// needs. Triggers are not in the file: they belong to the project that runs the macro, and the
/// import gives the arriving macro a place of its own to be started from.
/// </remarks>
public static class MacroFile
{
    public const string Extension = ".wgm";

    public const string FormatId = "whalegenie.macro";

    public const int FormatVersion = 1;

    private const string ManifestEntry = "manifest.json";
    private const string MacroEntry = "macro.json";
    private const string ReadmeEntry = "README.md";
    private const string AssetsFolder = "assets/";

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>
    /// Writes one macro into a file of its own. <paramref name="imagesFrom"/> is where a relative
    /// <c>assets/…</c> value is looked for — a project's asset folder, or a public library's.
    /// </summary>
    public static void Save(string path, MacroItem macro, MacroFileKind kind, string appVersion,
        string? imagesFrom = null)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? ".");
        var temporary = fullPath + ".tmp";

        try
        {
            using (var stream = File.Create(temporary))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                var assets = new MacroPackage.AssetCollector(archive,
                    imagesFrom ?? Path.GetDirectoryName(fullPath) ?? ".");
                var document = macro.ToSubMacroJson();
                MacroPackage.RewriteAssets(document, assets.Import);
                WriteEntry(archive, MacroEntry, document.ToJsonString(Indented));

                var manifest = new JsonObject
                {
                    ["format"] = FormatId,
                    ["version"] = FormatVersion,
                    ["app"] = "WhaleGenie",
                    ["appVersion"] = appVersion,
                    ["savedUtc"] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                    ["kind"] = kind == MacroFileKind.SubMacro ? "submacro" : "macro",
                    ["name"] = macro.Name.Trim(),
                    ["description"] = macro.Description,
                    ["steps"] = macro.Steps.Count,
                    ["subMacros"] = macro.SubMacros.Count,
                    ["assets"] = new JsonArray([.. assets.Stored.Select(name => JsonValue.Create(name))]),
                };

                WriteEntry(archive, ManifestEntry, manifest.ToJsonString(Indented));
                WriteEntry(archive, ReadmeEntry, BuildReadme(macro, kind, appVersion, assets.Stored));
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

    /// <summary>
    /// Reads a macro file, putting the pictures it carries into <paramref name="imageFolder"/>.
    /// </summary>
    /// <remarks>
    /// The pictures are written under a name made from their own content, so a file that arrives
    /// twice does not overwrite the first one's picture with a different one of the same name, and
    /// two macros that look for the same picture share the one file.
    /// </remarks>
    public static MacroFileContents Load(string path, string imageFolder)
    {
        var fullPath = Path.GetFullPath(path);
        using var stream = File.OpenRead(fullPath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var manifest = ReadObject(archive, ManifestEntry)
            ?? throw new InvalidDataException(Strings.Get("MacroFile.NoManifest"));

        var format = manifest["format"]?.GetValue<string>();
        if (!string.Equals(format, FormatId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(Strings.Format("MacroFile.NotAMacroFile", format ?? string.Empty));
        }

        var version = manifest["version"]?.GetValue<int>() ?? 0;
        if (version != FormatVersion)
        {
            throw new InvalidDataException(
                Strings.Format("MacroFile.WrongVersion", version, FormatVersion));
        }

        var node = ReadObject(archive, MacroEntry)
            ?? throw new InvalidDataException(Strings.Get("MacroFile.NoDocument"));

        var pictures = new PictureTrail(archive, imageFolder);
        MacroPackage.RewriteAssets(node, pictures.Expand);

        var macro = MacroItem.FromJson(node);
        if (macro.Name.Trim().Length == 0)
        {
            macro.Name = manifest["name"]?.GetValue<string>()?.Trim() ?? string.Empty;
        }

        var kind = string.Equals(manifest["kind"]?.GetValue<string>(), "submacro",
            StringComparison.OrdinalIgnoreCase)
            ? MacroFileKind.SubMacro
            : MacroFileKind.Macro;

        return new MacroFileContents(macro, kind);
    }

    /// <summary>Human readable summary stored next to the JSON, for people opening the zip.</summary>
    private static string BuildReadme(MacroItem macro, MacroFileKind kind, string appVersion,
        IReadOnlyList<string> assets)
    {
        var text = new StringBuilder();
        text.AppendLine("# WhaleGenie macro");
        text.AppendLine();
        text.AppendLine("This file holds one WhaleGenie macro. It is a zip archive; rename it to")
            .AppendLine("`.zip` if you want to look inside without WhaleGenie.");
        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"- Format: `{FormatId}` version {FormatVersion}");
        text.AppendLine(CultureInfo.InvariantCulture, $"- Written by WhaleGenie {appVersion}");
        text.AppendLine(CultureInfo.InvariantCulture, $"- Saved: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        text.AppendLine(CultureInfo.InvariantCulture, $"- Macro: `{macro.Name}`");
        text.AppendLine(CultureInfo.InvariantCulture,
            $"- Kind: {(kind == MacroFileKind.SubMacro ? "submacro (called by another macro)" : "macro")}");
        text.AppendLine(CultureInfo.InvariantCulture, $"- Steps: {macro.Steps.Count}");
        text.AppendLine();
        text.AppendLine("## Contents");
        text.AppendLine();
        text.AppendLine("| Entry | What it holds |");
        text.AppendLine("| --- | --- |");
        text.AppendLine("| `manifest.json` | Format version, the macro's name and what it holds. |");
        text.AppendLine("| `README.md` | This file. |");
        text.AppendLine("| `macro.json` | The macro: its steps, arguments, results and submacros. |");
        text.AppendLine("| `assets/` | Pictures the macro looks for, referenced as `assets/<file>`. |");
        text.AppendLine();
        text.AppendLine("Triggers are not in this file. They belong to the project that runs the macro, and");
        text.AppendLine("the project gives an imported macro a way to be started of its own.");
        text.AppendLine();
        text.AppendLine("## Pictures");
        text.AppendLine();
        if (assets.Count == 0)
        {
            text.AppendLine("_None._");
        }
        else
        {
            foreach (var asset in assets)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"- `{asset}`");
            }
        }

        return text.ToString();
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

    /// <summary>Writes the pictures a file carries into a folder, under names made from their content.</summary>
    private sealed class PictureTrail
    {
        private readonly ZipArchive _archive;
        private readonly string _folder;
        private readonly Dictionary<string, string> _mapped = new(StringComparer.OrdinalIgnoreCase);

        public PictureTrail(ZipArchive archive, string folder)
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

            if (_mapped.TryGetValue(value, out var known))
            {
                return known;
            }

            var entry = _archive.GetEntry(value);
            if (entry is null)
            {
                return value;
            }

            byte[] bytes;
            using (var source = entry.Open())
            using (var buffer = new MemoryStream())
            {
                source.CopyTo(buffer);
                bytes = buffer.ToArray();
            }

            // Only the extension is taken from the entry name: a crafted file cannot choose where
            // its picture lands, and the name it does land under is the content's own.
            var extension = Path.GetExtension(value);
            if (extension.Length == 0)
            {
                extension = ".png";
            }

            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()[..16];
            var target = Path.GetFullPath(Path.Combine(_folder,
                $"macro-{hash}{extension.ToLowerInvariant()}"));

            if (!File.Exists(target))
            {
                Directory.CreateDirectory(_folder);
                File.WriteAllBytes(target, bytes);
            }

            _mapped[value] = target;
            return target;
        }
    }
}
