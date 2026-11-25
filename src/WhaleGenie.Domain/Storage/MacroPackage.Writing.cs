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
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;


namespace WhaleGenie.Storage;

// What is written into the archive: the files, the readme and the slug a name becomes.
public static partial class MacroPackage
{
    private static void SaveInto(ZipArchive archive, string packagePath,
        IReadOnlyList<MacroItem> macros, ProjectTriggers triggers,
        IReadOnlyList<VariableDefinition> globals, string appVersion, string? assetFolder,
        bool projectSection, IReadOnlyList<PublicMacroEntry>? library = null)
    {
        var assets = new AssetCollector(archive,
            [AssetFolderFor(packagePath), Path.GetDirectoryName(packagePath), assetFolder]);
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
            WriteEntry(archive, entryName, document.ToJsonString(Indented));

            manifestMacros.Add(new PackageManifestMacro(
                macro.Name, macro.IsEnabled, entryName, macro.Steps.Count));

            var entries = triggers.ForMacro(macro.Name);
            files.Add((macro.Name, string.Join(" / ", entries.Select(entry => entry.KindLabel)),
                entries.Count.ToString(CultureInfo.InvariantCulture), macro.Steps.Count));
        }

        foreach (var entry in library ?? [])
        {
            index++;
            var entryName =
                $"{NodesFolder}{index:00}-library-{Slug(entry.Macro.Name)}.json";
            var document = entry.Macro.ToJson();
            RewriteAssets(document, assets.Import);
            WriteEntry(archive, entryName, document.ToJsonString(Indented));
            libraryEntries.Add(new PackageManifestLibraryEntry(
                entry.Id, Math.Max(1, entry.Version), entry.Macro.Name.Trim(),
                entry.Macro.Description, entryName));
        }

        var manifestVariables = new List<PackageManifestVariable>();
        foreach (var variable in globals)
        {
            manifestVariables.Add(new PackageManifestVariable(
                variable.Name, variable.Type.ToString(), FormulaJson.Write(variable.Default),
                variable.Description));
        }

        var triggersNode = triggers.ToJson();
        RewriteMenuIcons(triggersNode, assets.Import);
        var manifest = PackageManifestBuilder.Build(appVersion, DateTime.UtcNow,
            manifestMacros, manifestVariables, assets.Stored);

        WriteEntry(archive, ManifestEntry, manifest.ToJsonString(Indented));
        if (projectSection)
        {
            WriteEntry(archive, TriggersEntry, triggersNode.ToJsonString(Indented));
        }
        else if (library is null)
        {
            throw new InvalidOperationException("a package needs a project or library section");
        }

        if (library is not null)
        {
            WriteEntry(archive, LibraryEntriesEntry,
                PackageManifestBuilder.BuildLibrary(libraryEntries).ToJsonString(Indented));
        }

        WriteEntry(archive, ReadmeEntry,
            PackageReadme.Build(appVersion, files, globals, assets.Stored, projectSection,
                library is not null, DateTime.Now));
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
}
