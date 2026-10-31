using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.Storage;

/// <summary>What a package file holds: the macros and the shared variables.</summary>
public sealed class PackageContents
{
    public required IReadOnlyList<MacroItem> Macros { get; init; }

    public required IReadOnlyList<VariableDefinition> Globals { get; init; }

    public required ProjectTriggers Triggers { get; init; }
}

/// <summary>
/// Reads and writes the <c>.wgmacro</c> macro package: a zip holding a manifest, a readme,
/// one JSON document per macro and the images those macros reference.
/// </summary>
public static class MacroPackage
{
    /// <summary>
    /// The extension is the program's name, so a package is recognisable as this program's even
    /// where nothing else says so. The old <c>.vkm</c> comes from a name the project no longer
    /// uses, and no file in the wild carries it, so there is nothing that reads it back.
    /// </summary>
    public const string Extension = ".wgmacro";
    public const string FormatId = "whalegenie.macro.package";
    /// <summary>
    /// Bumped when what a field means changes, not only when a field is added. 20 is the version
    /// where a call writes what it passes as an argument row list rather than one parameter per
    /// argument: a 19 package's <c>arg.*</c> keys would be read as fields the action does not have,
    /// so it is refused rather than half-read.
    /// </summary>
    public const int FormatVersion = 20;

    private const string ManifestEntry = "manifest.json";
    private const string ReadmeEntry = "README.md";
    private const string TriggersEntry = "triggers.json";
    private const string NodesFolder = "nodes/";
    private const string AssetsFolder = "assets/";
    private const string ImageParameter = "image";

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>Where a package's images are unpacked when it is opened.</summary>
    public static string AssetFolderFor(string packagePath)
        => Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(packagePath)) ?? ".",
            Path.GetFileNameWithoutExtension(packagePath) + ".assets");

    /// <summary>Writes the macros and shared variables into a package file.</summary>
    public static void Save(string path, IReadOnlyList<MacroItem> macros,
        IReadOnlyList<VariableDefinition> globals, string appVersion)
        => Save(path, macros, ProjectTriggers.FromMacros(macros), globals, appVersion);

    /// <summary>Writes the macro bodies and the project-wide trigger entries into one package.</summary>
    public static void Save(string path, IReadOnlyList<MacroItem> macros,
        ProjectTriggers triggers, IReadOnlyList<VariableDefinition> globals, string appVersion)
    {
        triggers.Attach(macros);

        // A macro with a fatal declaration problem cannot be called, so writing it into a package
        // would only carry a broken interface forward. The editor blocks the same faults; this is
        // the guard for every other way a package can be written.
        foreach (var macro in macros)
        {
            var problems = macro.Callable.Problems();
            if (problems.Count > 0)
            {
                throw new InvalidDataException(Strings.Format("Package.BadDeclaration",
                    macro.Name, problems[0].Name));
            }
        }

        var menuProblems = triggers.MenuProblems(macros);
        if (menuProblems.Count > 0)
        {
            throw new InvalidDataException(menuProblems[0]);
        }

        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? ".");

        // Build in a temporary file first, so a failure cannot damage an existing package.
        var temporary = fullPath + ".tmp";
        try
        {
            using (var stream = File.Create(temporary))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                SaveInto(archive, fullPath, macros, triggers, globals, appVersion);
            }

            // The package being replaced is kept beside it as .bak. A save that turns out to be wrong
            // is the one mistake nobody can undo from inside the program, and the copy costs a single
            // file write. Every path that saves — the window's own Ctrl+S and the MCP tool alike —
            // comes through here, so none of them can forget it.
            if (File.Exists(fullPath))
            {
                File.Copy(fullPath, fullPath + ".bak", true);
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
    /// Gives every macro after the first one of its name a suffix saying when it was read, so a
    /// package that carries two macros of one name still arrives as two macros. The first one keeps
    /// the name, because that is the one the file’s call sites and triggers were written against.
    /// </summary>
    internal static void Disambiguate(IReadOnlyList<MacroItem> macros, DateTime when)
    {
        // Taken names start as every name the file has, not just the ones seen so far: a macro whose
        // own name happens to be the suffixed form must not be pushed aside by the suffix.
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var macro in macros)
        {
            if (macro.Name.Trim() is { Length: > 0 } name)
            {
                taken.Add(name);
            }
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var macro in macros)
        {
            var name = macro.Name.Trim();
            if (name.Length == 0)
            {
                // An unnamed macro is the declaration editor's problem; there is nothing to make
                // unique about a name nobody has.
                continue;
            }

            macro.Name = name;
            if (seen.Add(name))
            {
                continue;
            }

            macro.Name = MacroNames.Free(name, taken, when);
            taken.Add(macro.Name);
        }
    }

    /// <summary>Reads a package file back into macros and shared variables.</summary>
    public static PackageContents Load(string path) => Load(path, AssetFolderFor(path), reuseExisting: true);

    /// <summary>
    /// Reads a package, putting the pictures it carries into <paramref name="assetFolder"/>.
    /// </summary>
    /// <remarks>
    /// An import is not the same reader as an open. Opening a project finds a folder that belongs to
    /// that project, where a picture already there is the one the macro means. A file that arrives
    /// from elsewhere is unpacked into somebody else's folder, so a name already taken is not
    /// evidence of the same picture: the bytes are compared, and a different picture is kept beside
    /// the old one rather than quietly replacing what another macro looks for.
    /// </remarks>
    public static PackageContents Load(string path, string assetFolder)
        => Load(path, assetFolder, reuseExisting: false);

    private static PackageContents Load(string path, string assetFolder, bool reuseExisting)
    {
        var fullPath = Path.GetFullPath(path);
        using var stream = File.OpenRead(fullPath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var manifest = ReadObject(archive, ManifestEntry)
            ?? throw new InvalidDataException("The package has no manifest.json.");

        var format = manifest["format"]?.GetValue<string>();
        if (!string.Equals(format, FormatId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"'{format}' is not a WhaleGenie macro package.");
        }

        var version = manifest["version"]?.GetValue<int>() ?? 0;
        if (version != FormatVersion)
        {
            throw new InvalidDataException(
                $"The package was written in format {version}; this WhaleGenie reads format "
                + $"{FormatVersion}.");
        }

        var assets = new AssetExtractor(archive, assetFolder, reuseExisting);
        var macros = new List<MacroItem>();

        foreach (var item in manifest["macros"] as JsonArray ?? [])
        {
            if (item is not JsonObject entry || ReadString(entry, "document") is not { Length: > 0 } document)
            {
                continue;
            }

            if (ReadObject(archive, document) is not { } node)
            {
                continue;
            }

            RewriteAssets(node, assets.Expand);
            var macro = MacroItem.FromJson(node);

            if (macro.Name.Length == 0)
            {
                macro.Name = ReadString(entry, "name");
            }

            macro.IsEnabled = ReadBool(entry, "enabled", true);
            macros.Add(macro);
        }

        // Two macros with one name can only come from a file somebody edited by hand: the editor
        // refuses to save a second macro under a name that is taken. They are still two macros with
        // their own steps, so the later one is given the date it was read on and both are kept —
        // dropping one, which is what a name-keyed table would do, loses work and leaves every call
        // to that name meaning whichever happened to be read first.
        Disambiguate(macros, DateTime.Now);

        var triggers = ProjectTriggers.FromJson(ReadObject(archive, TriggersEntry));
        ExpandMenuIcons(triggers, assets.Expand);
        triggers.Attach(macros);

        var globals = new List<VariableDefinition>();
        foreach (var item in manifest["variables"] as JsonArray ?? [])
        {
            if (item is not JsonObject variable || ReadString(variable, "name") is not { Length: > 0 } name)
            {
                continue;
            }

            globals.Add(new VariableDefinition
            {
                Name = name,
                Scope = VariableScope.Global,
                Type = DefaultIfEmpty(ReadString(variable, "type"), "text"),
                DefaultValue = ReadString(variable, "default"),
                Description = ReadString(variable, "description"),
            });
        }

        return new PackageContents { Macros = macros, Globals = globals, Triggers = triggers };
    }

    private static void SaveInto(ZipArchive archive, string packagePath,
        IReadOnlyList<MacroItem> macros, ProjectTriggers triggers,
        IReadOnlyList<VariableDefinition> globals, string appVersion)
    {
        var assets = new AssetCollector(archive, AssetFolderFor(packagePath));
        var manifestMacros = new JsonArray();
        var files = new List<(string Name, string Trigger, string Loop, int Steps)>();
        var index = 0;

        foreach (var macro in macros)
        {
            index++;
            var entryName = $"{NodesFolder}{index:00}-{Slug(macro.Name)}.json";
            var document = macro.ToJson();
            RewriteAssets(document, assets.Import);
            WriteEntry(archive, entryName, document.ToJsonString(Indented));

            manifestMacros.Add(new JsonObject
            {
                ["name"] = macro.Name,
                ["enabled"] = macro.IsEnabled,
                ["document"] = entryName,
                ["steps"] = macro.Steps.Count,
            });

            files.Add((macro.Name, string.Join(" / ", macro.Triggers.Select(entry => entry.KindLabel)),
                macro.Triggers.Count.ToString(CultureInfo.InvariantCulture), macro.Steps.Count));
        }

        var manifestVariables = new JsonArray();
        foreach (var variable in globals)
        {
            manifestVariables.Add(new JsonObject
            {
                ["name"] = variable.Name,
                ["type"] = variable.Type,
                ["default"] = variable.DefaultValue,
                ["description"] = variable.Description,
            });
        }

        var triggersNode = triggers.ToJson();
        RewriteMenuIcons(triggersNode, assets.Import);
        var manifest = new JsonObject
        {
            ["format"] = FormatId,
            ["version"] = FormatVersion,
            ["app"] = "WhaleGenie",
            ["appVersion"] = appVersion,
            ["savedUtc"] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            ["macros"] = manifestMacros,
            ["variables"] = manifestVariables,
            ["assets"] = new JsonArray([.. assets.Stored.Select(name => JsonValue.Create(name))]),
        };

        WriteEntry(archive, ManifestEntry, manifest.ToJsonString(Indented));
        WriteEntry(archive, TriggersEntry, triggersNode.ToJsonString(Indented));
        WriteEntry(archive, ReadmeEntry, BuildReadme(appVersion, files, globals, assets.Stored));
    }

    /// <summary>Human readable summary stored next to the JSON, for people opening the zip.</summary>
    private static string BuildReadme(string appVersion,
        IReadOnlyList<(string Name, string Trigger, string Loop, int Steps)> macros,
        IReadOnlyList<VariableDefinition> globals, IReadOnlyList<string> assets)
    {
        var text = new StringBuilder();
        text.AppendLine("# WhaleGenie macro package");
        text.AppendLine();
        text.AppendLine("This file is a WhaleGenie macro project. It is a zip archive; rename it to")
            .AppendLine("`.zip` if you want to look inside without WhaleGenie.");
        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture,
            $"- Format: `{FormatId}` version {FormatVersion}");
        text.AppendLine(CultureInfo.InvariantCulture, $"- Written by WhaleGenie {appVersion}");
        text.AppendLine(CultureInfo.InvariantCulture,
            $"- Saved: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        text.AppendLine();

        text.AppendLine("## Contents");
        text.AppendLine();
        text.AppendLine("| Entry | What it holds |");
        text.AppendLine("| --- | --- |");
        text.AppendLine("| `manifest.json` | Format version and the list of everything in the package. |");
        text.AppendLine("| `README.md` | This file. |");
        text.AppendLine("| `triggers.json` | Trigger entries, floating-group positions, menu pages, blocked programs and the menu launcher. |");
        text.AppendLine("| `nodes/` | One JSON document per macro, holding its steps. |");
        text.AppendLine("| `assets/` | Images the macros use, referenced as `assets/<file>`. |");
        text.AppendLine();

        text.AppendLine("## Macros");
        text.AppendLine();
        if (macros.Count == 0)
        {
            text.AppendLine("_None._");
        }
        else
        {
            text.AppendLine("| Macro | Trigger | Loop | Steps |");
            text.AppendLine("| --- | --- | --- | --- |");
            foreach (var (name, trigger, loop, steps) in macros)
            {
                text.AppendLine(CultureInfo.InvariantCulture,
                    $"| {name} | {trigger} | {loop} | {steps} |");
            }
        }

        text.AppendLine();
        text.AppendLine("## Global variables");
        text.AppendLine();
        if (globals.Count == 0)
        {
            text.AppendLine("_None._");
        }
        else
        {
            text.AppendLine("| Name | Type | Default | Description |");
            text.AppendLine("| --- | --- | --- | --- |");
            foreach (var variable in globals)
            {
                text.AppendLine(CultureInfo.InvariantCulture,
                    $"| {variable.Name} | {variable.Type} | {variable.DefaultValue} | {variable.Description} |");
            }
        }

        text.AppendLine();
        text.AppendLine("## Images");
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

    /// <summary>Turns a macro name into something safe to use as a file name.</summary>
    private static string Slug(string name)
    {
        var text = new StringBuilder();
        foreach (var character in name)
        {
            text.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '-');
        }

        var slug = text.ToString().Trim('-');
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        return slug.Length == 0 ? "macro" : slug[..Math.Min(slug.Length, 40)];
    }

    /// <summary>
    /// Rewrites every <c>image</c> value through <paramref name="map"/>. Shared with the export,
    /// which writes a package of its own out of a few chosen macros.
    /// </summary>
    internal static void RewriteAssets(JsonNode? node, Func<string, string> map)
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
                        RewriteAssets(child, map);
                    }
                }

                break;
            case JsonArray array:
                foreach (var child in array)
                {
                    RewriteAssets(child, map);
                }

                break;
        }
    }

    private static void RewriteMenuIcons(JsonNode? node, Func<string, string> map)
    {
        switch (node)
        {
            case JsonObject container:
                if (container["kind"]?.GetValue<string>() is { } kind
                    && string.Equals(kind, "image", StringComparison.OrdinalIgnoreCase)
                    && container["value"] is JsonValue value
                    && value.TryGetValue<string>(out var text)
                    && text.Length > 0)
                {
                    container["value"] = map(text);
                    return;
                }

                foreach (var child in container.Select(pair => pair.Value))
                {
                    RewriteMenuIcons(child, map);
                }
                break;
            case JsonArray array:
                foreach (var child in array)
                {
                    RewriteMenuIcons(child, map);
                }
                break;
        }
    }

    private static void ExpandMenuIcons(ProjectTriggers triggers, Func<string, string> map)
    {
        foreach (var entry in triggers.Entries)
        {
            if (entry.MenuIcon is { Kind: MenuIconKind.Image } icon)
            {
                entry.MenuIcon = icon with { Value = map(icon.Value) };
            }
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
        private readonly string _sourceFolder;
        private readonly Dictionary<string, string> _mapped = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _taken = new(StringComparer.OrdinalIgnoreCase);

        public AssetCollector(ZipArchive archive, string sourceFolder)
        {
            _archive = archive;
            _sourceFolder = sourceFolder;
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

            // A value already stored as assets/x.png lives beside the package.
            var relative = value.Replace('/', Path.DirectorySeparatorChar);
            return ResolveFile(Path.Combine(_sourceFolder, relative))
                ?? ResolveFile(value);
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

    /// <summary>Unpacks referenced images next to the package as it is read.</summary>
    private sealed class AssetExtractor
    {
        private readonly ZipArchive _archive;
        private readonly string _folder;
        private readonly bool _reuseExisting;
        private readonly Dictionary<string, string> _mapped = new(StringComparer.OrdinalIgnoreCase);

        public AssetExtractor(ZipArchive archive, string folder, bool reuseExisting)
        {
            _archive = archive;
            _folder = folder;
            _reuseExisting = reuseExisting;
        }

        /// <summary>
        /// A picture that belongs to this folder already: what is there is the one the macro means,
        /// so the file is only written when the folder has nothing under that name.
        /// </summary>
        private string Keep(string name, ZipArchiveEntry entry)
        {
            var target = Path.Combine(_folder, name);
            if (!File.Exists(target))
            {
                Directory.CreateDirectory(_folder);
                entry.ExtractToFile(target, true);
            }

            return target;
        }

        /// <summary>
        /// A picture arriving from somewhere else: the same bytes under the name are shared, and
        /// different ones are written beside it under a number of their own.
        /// </summary>
        private string KeepApart(string name, ZipArchiveEntry entry)
        {
            byte[] arriving;
            using (var source = entry.Open())
            using (var buffer = new MemoryStream())
            {
                source.CopyTo(buffer);
                arriving = buffer.ToArray();
            }

            Directory.CreateDirectory(_folder);
            var stem = Path.GetFileNameWithoutExtension(name);
            var extension = Path.GetExtension(name);
            for (var counter = 1; ; counter++)
            {
                var candidate = counter == 1 ? name : $"{stem}-{counter}{extension}";
                var target = Path.Combine(_folder, candidate);
                if (!File.Exists(target))
                {
                    File.WriteAllBytes(target, arriving);
                    return target;
                }

                if (File.ReadAllBytes(target).SequenceEqual(arriving))
                {
                    return target;
                }
            }
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

            // Only the file name is used, so a crafted package cannot escape the folder.
            var name = Path.GetFileName(value);
            var resolved = Path.GetFullPath(_reuseExisting
                ? Keep(name, entry)
                : KeepApart(name, entry));
            _mapped[value] = resolved;
            return resolved;
        }
    }
}
