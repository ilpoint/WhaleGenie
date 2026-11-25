using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json.Nodes;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.Storage;

// Reading a package back, and what it holds.
public static partial class MacroPackage
{
    /// <summary>Reads a project package file back into macros and shared variables.</summary>
    public static PackageContents Load(string path) => Load(path, AssetFolderFor(path), reuseExisting: true);

    /// <summary>
    /// Reads a project package, putting the pictures it carries into <paramref name="assetFolder"/>.
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
        var read = Read(path, assetFolder, reuseExisting, PackageKind.Project,
            extractAssets: true);
        return ToContents(read);
    }

    /// <summary>
    /// Reads the package summary used before a person chooses open or import. It parses documents
    /// but never extracts a picture, so cancelling a preview leaves no asset folder behind.
    /// </summary>
    public static PackageContents Preview(string path)
    {
        try
        {
            var project = Read(path, AssetFolderFor(path), reuseExisting: true,
                PackageKind.Project, extractAssets: false);
            return ToContents(project);
        }
        catch (PackageKindMismatchException error) when (error.Actual == PackageKind.Library)
        {
            var library = Read(path, AssetFolderFor(path), reuseExisting: true,
                PackageKind.Library, extractAssets: false);
            return ToContents(library);
        }
    }

    private static PackageContents ToContents(RawPackage read)
        => new()
        {
            Macros = read.Macros,
            Globals = read.Globals,
            Triggers = read.Triggers,
            Library = read.Library ?? [],
            FormatVersion = read.FormatVersion,
            AppVersion = read.AppVersion,
            SavedUtc = read.SavedUtc,
            HasProject = read.HasProject,
        };

    public static LibraryContents LoadLibrary(string path, string? assetFolder = null,
        bool reuseExisting = true)
    {
        var read = Read(path, assetFolder ?? AssetFolderFor(path), reuseExisting,
            PackageKind.Library, extractAssets: true);
        return new LibraryContents { Entries = read.Library ?? [] };
    }

    private static RawPackage Read(string path, string assetFolder, bool reuseExisting,
        PackageKind expected, bool extractAssets)
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
                Strings.Format("Package.FormatUnreadable", version, FormatVersion));
        }

        var libraryNode = ReadObject(archive, LibraryEntriesEntry);
        var hasLibraryFolder = archive.Entries.Any(entry =>
            entry.FullName.StartsWith(LibraryFolder, StringComparison.OrdinalIgnoreCase));
        if (libraryNode is null && hasLibraryFolder)
        {
            throw new InvalidDataException(Strings.Get("Package.LibraryEntriesMissing"));
        }

        var hasProject = archive.GetEntry(TriggersEntry) is not null;
        var hasLibrary = libraryNode is not null;
        if (expected == PackageKind.Project && !hasProject)
        {
            throw new PackageKindMismatchException(PackageKind.Library);
        }

        if (expected == PackageKind.Library && !hasLibrary)
        {
            throw new PackageKindMismatchException(PackageKind.Project);
        }

        var assets = new AssetExtractor(archive, assetFolder, reuseExisting, extractAssets);
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

        var triggers = hasProject
            ? ProjectTriggers.FromJson(ReadObject(archive, TriggersEntry))
            : new ProjectTriggers();
        ExpandMenuIcons(triggers, assets.Expand);

        var globals = hasProject ? ReadGlobals(manifest) : [];

        IReadOnlyList<PublicMacroEntry>? library = null;
        if (libraryNode is not null)
        {
            library = ReadLibraryEntries(archive, libraryNode, assets);
        }

        return new RawPackage
        {
            Macros = macros,
            Globals = globals,
            Triggers = triggers,
            Library = library,
            FormatVersion = version,
            AppVersion = ReadString(manifest, "appVersion"),
            SavedUtc = ReadString(manifest, "savedUtc"),
            HasProject = hasProject,
        };
    }

    private static List<VariableDefinition> ReadGlobals(JsonObject manifest)
    {
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
                Type = VariableTypes.KindOf(ReadString(variable, "type")),
                Default = FormulaJson.Read(variable["default"]),
                Description = ReadString(variable, "description"),
            });
        }

        return globals;
    }

    private static List<PublicMacroEntry> ReadLibraryEntries(ZipArchive archive, JsonObject library,
        AssetExtractor assets)
    {
        var version = library["version"]?.GetValue<int>() ?? 0;
        if (version != LibraryEntriesVersion)
        {
            throw new InvalidDataException(
                Strings.Format("Package.LibraryVersionUnreadable", version, LibraryEntriesVersion));
        }

        var entries = new List<PublicMacroEntry>();
        foreach (var item in library["entries"] as JsonArray ?? [])
        {
            if (item is not JsonObject descriptor)
            {
                continue;
            }

            var id = ReadString(descriptor, "id").Trim();
            var document = ReadString(descriptor, "document").Trim();
            if (id.Length == 0 || document.Length == 0
                || ReadObject(archive, document) is not { } node)
            {
                continue;
            }

            RewriteAssets(node, assets.Expand);
            var macro = MacroItem.FromJson(node);
            macro.Name = ReadString(descriptor, "name").Trim() is { Length: > 0 } name
                ? name
                : macro.Name.Trim();
            macro.Description = ReadString(descriptor, "description") is { Length: > 0 } description
                ? description
                : macro.Description;

            var macroVersion = descriptor["version"] is JsonValue value
                && value.TryGetValue<int>(out var parsed)
                    ? parsed
                    : 1;
            entries.Add(new PublicMacroEntry(id, Math.Max(1, macroVersion), macro));
        }

        return entries;
    }

    private sealed class RawPackage
    {
        public required IReadOnlyList<MacroItem> Macros { get; init; }

        public required IReadOnlyList<VariableDefinition> Globals { get; init; }

        public required ProjectTriggers Triggers { get; init; }

        public IReadOnlyList<PublicMacroEntry>? Library { get; init; }

        public int FormatVersion { get; init; }

        public string AppVersion { get; init; } = string.Empty;

        public string SavedUtc { get; init; } = string.Empty;

        public bool HasProject { get; init; }
    }
}
