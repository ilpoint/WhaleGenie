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

// What is written into the archive: the files, the readme and the slug a name becomes.
public static partial class MacroPackage
{
    private static void SaveInto(ZipArchive archive, string packagePath,
        IReadOnlyList<MacroItem> macros, ProjectTriggers triggers,
        IReadOnlyList<VariableDefinition> globals, string appVersion, string? assetFolder)
    {
        var assets = new AssetCollector(archive,
            [AssetFolderFor(packagePath), Path.GetDirectoryName(packagePath), assetFolder]);
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
}
