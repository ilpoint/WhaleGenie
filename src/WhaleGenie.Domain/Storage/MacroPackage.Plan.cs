using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Models;

namespace WhaleGenie.Storage;

/// <summary>One entry a package plan will write, in the order the archive receives it.</summary>
internal sealed record PackageEntry(string Name, string Content);

/// <summary>One picture a package plan will copy: the archive name and the file the shell reads.</summary>
internal sealed record PackageAsset(string EntryName, string SourcePath);

/// <summary>The entries and assets a package will contain. Building it needs no file, zip or clock.</summary>
internal sealed record PackagePlanResult(
    IReadOnlyList<PackageEntry> Entries,
    IReadOnlyList<PackageAsset> Assets);

// What a package contains, decided before the archive is opened.
public static partial class MacroPackage
{
    /// <summary>
    /// Assembles the documents, manifest and README, and decides which pictures become package
    /// assets. The shell supplies the resolved source path of a picture; this method never opens
    /// one. Timestamps are arguments, so a test can fix them.
    /// </summary>
    internal static PackagePlanResult BuildPlan(
        string appVersion, DateTime savedUtc, DateTime readmeStamp,
        IReadOnlyList<MacroItem> macros, ProjectTriggers triggers,
        IReadOnlyList<VariableDefinition> globals, bool projectSection,
        IReadOnlyList<PublicMacroEntry>? library, Func<string, string?> resolveAsset)
    {
        var assets = new PlannedAssets(resolveAsset);
        var entries = new List<PackageEntry>();
        var manifestMacros = new List<PackageManifestMacro>();
        var libraryEntries = new List<PackageManifestLibraryEntry>();
        var files = new List<(string Name, string Trigger, string Loop, int Steps)>();
        var index = 0;

        foreach (var macro in macros)
        {
            index++;
            var entryName = $"{NodesFolder}{index:00}-{Slug(macro.Name)}.json";
            var document = macro.ToJson();
            RewriteAssets(document, assets.Import);
            entries.Add(new PackageEntry(entryName, document.ToJsonString(Indented)));

            manifestMacros.Add(new PackageManifestMacro(
                macro.Name, macro.IsEnabled, entryName, macro.Steps.Count));

            var triggerEntries = triggers.ForMacro(macro.Name);
            files.Add((macro.Name,
                string.Join(" / ", triggerEntries.Select(entry => entry.KindLabel)),
                triggerEntries.Count.ToString(CultureInfo.InvariantCulture), macro.Steps.Count));
        }

        foreach (var entry in library ?? [])
        {
            index++;
            var entryName = $"{NodesFolder}{index:00}-library-{Slug(entry.Macro.Name)}.json";
            var document = entry.Macro.ToJson();
            RewriteAssets(document, assets.Import);
            entries.Add(new PackageEntry(entryName, document.ToJsonString(Indented)));
            libraryEntries.Add(new PackageManifestLibraryEntry(
                entry.Id, Math.Max(1, entry.Version), entry.Macro.Name.Trim(),
                entry.Macro.Description, entryName));
        }

        var manifestVariables = globals.Select(variable => new PackageManifestVariable(
            variable.Name, variable.Type.ToString(), FormulaJson.Write(variable.Default),
            variable.Description)).ToList();

        var triggersNode = triggers.ToJson();
        RewriteMenuIcons(triggersNode, assets.Import);
        var manifest = PackageManifestBuilder.Build(appVersion, savedUtc,
            manifestMacros, manifestVariables, assets.Stored);

        entries.Add(new PackageEntry(ManifestEntry, manifest.ToJsonString(Indented)));
        if (projectSection)
        {
            entries.Add(new PackageEntry(TriggersEntry, triggersNode.ToJsonString(Indented)));
        }
        else if (library is null)
        {
            throw new InvalidOperationException("a package needs a project or library section");
        }

        if (library is not null)
        {
            entries.Add(new PackageEntry(LibraryEntriesEntry,
                PackageManifestBuilder.BuildLibrary(libraryEntries).ToJsonString(Indented)));
        }

        entries.Add(new PackageEntry(ReadmeEntry,
            PackageReadme.Build(appVersion, files, globals, assets.Stored, projectSection,
                library is not null, readmeStamp)));

        return new PackagePlanResult(entries, assets.Assets);
    }

    /// <summary>The pictures referenced while the plan is built, named once and kept in order.</summary>
    private sealed class PlannedAssets(Func<string, string?> resolve)
    {
        private readonly Dictionary<string, string> _mapped = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _taken = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<PackageAsset> _assets = [];

        public IReadOnlyList<PackageAsset> Assets => _assets;

        public List<string> Stored { get; } = [];

        public string Import(string value)
        {
            var source = resolve(value);
            if (string.IsNullOrWhiteSpace(source))
            {
                return value;
            }

            if (_mapped.TryGetValue(source, out var existing))
            {
                return existing;
            }

            var name = UniqueName(Path.GetFileName(source));
            var entryName = AssetsFolder + name;
            _mapped[source] = entryName;
            _assets.Add(new PackageAsset(entryName, source));
            Stored.Add(entryName);
            return entryName;
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
