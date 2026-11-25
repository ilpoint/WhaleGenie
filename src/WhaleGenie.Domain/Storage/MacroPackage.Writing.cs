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
        var manifestMacros = new JsonArray();
        var libraryEntries = new JsonArray();
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
            libraryEntries.Add(new JsonObject
            {
                ["id"] = entry.Id,
                ["version"] = Math.Max(1, entry.Version),
                ["name"] = entry.Macro.Name.Trim(),
                ["description"] = entry.Macro.Description,
                ["document"] = entryName,
            });
        }

        var manifestVariables = new JsonArray();
        foreach (var variable in globals)
        {
            manifestVariables.Add(new JsonObject
            {
                ["name"] = variable.Name,
                ["type"] = variable.Type.ToString(),
                ["default"] = FormulaJson.Write(variable.Default),
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
            WriteEntry(archive, LibraryEntriesEntry, new JsonObject
            {
                ["version"] = LibraryEntriesVersion,
                ["entries"] = libraryEntries,
            }.ToJsonString(Indented));
        }

        WriteEntry(archive, ReadmeEntry,
            BuildReadme(appVersion, files, globals, assets.Stored, projectSection,
                library is not null));
    }

    /// <summary>Human readable summary stored next to the JSON, for people opening the zip.</summary>
    private static string BuildReadme(string appVersion,
        IReadOnlyList<(string Name, string Trigger, string Loop, int Steps)> macros,
        IReadOnlyList<VariableDefinition> globals, IReadOnlyList<string> assets,
        bool projectSection, bool librarySection)
    {
        var text = new StringBuilder();
        text.AppendLine(projectSection ? "# WhaleGenie macro package" : "# WhaleGenie public macro library");
        text.AppendLine();
        text.AppendLine(projectSection
                ? "This file is a WhaleGenie macro project. It is a zip archive; rename it to"
                : "This file is a WhaleGenie public macro library. It is a package with callable")
            .AppendLine(projectSection
                ? "`.zip` if you want to look inside without WhaleGenie."
                : "macros under `library/entries.json`, not a project.");
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
        if (projectSection)
        {
            text.AppendLine("| `triggers.json` | Trigger entries, floating-group positions, menu pages, blocked programs and the menu launcher. |");
        }
        if (librarySection)
        {
            text.AppendLine("| `library/entries.json` | Callable entries and their public versions. |");
        }
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
}
