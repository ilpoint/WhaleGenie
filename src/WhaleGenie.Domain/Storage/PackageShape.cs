using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.Storage;

/// <summary>What the package's manifest says about its sections, without touching a node or asset.</summary>
public sealed record PackageShape(
    int FormatVersion,
    string AppVersion,
    string SavedUtc,
    bool HasProject,
    bool HasLibrary);

/// <summary>
/// The pure manifest reader: format, version, app metadata and the section flags. The shell only
/// tells it whether <c>triggers.json</c> and a <c>library/</c> folder are present.
/// </summary>
public static class PackageShapeReader
{
    public static PackageShape Read(JsonObject manifest, bool hasTriggers,
        bool hasLibraryFolder, JsonObject? libraryNode)
    {
        var format = manifest["format"]?.GetValue<string>();
        if (!string.Equals(format, MacroPackage.FormatId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"'{format}' is not a WhaleGenie macro package.");
        }

        var version = manifest["version"]?.GetValue<int>() ?? 0;
        if (version != MacroPackage.FormatVersion)
        {
            throw new InvalidDataException(
                Strings.Format("Package.FormatUnreadable", version, MacroPackage.FormatVersion));
        }

        if (libraryNode is null && hasLibraryFolder)
        {
            throw new InvalidDataException(Strings.Get("Package.LibraryEntriesMissing"));
        }

        return new PackageShape(
            version,
            manifest["appVersion"]?.GetValue<string>() ?? string.Empty,
            manifest["savedUtc"]?.GetValue<string>() ?? string.Empty,
            hasTriggers,
            libraryNode is not null);
    }
}

/// <summary>One macro descriptor as it appears in the package manifest.</summary>
public sealed record PackageManifestMacro(
    string Name, bool Enabled, string Document, int Steps);

/// <summary>One global declaration as it appears in the package manifest.</summary>
public sealed record PackageManifestVariable(
    string Name, string Type, JsonNode? Default, string Description);

/// <summary>One public-library entry as it appears in <c>library/entries.json</c>.</summary>
public sealed record PackageManifestLibraryEntry(
    string Id, int Version, string Name, string Description, string Document);

/// <summary>
/// The pure manifest writer. The shell supplies already-collected document names and assets;
/// this assembles the JSON and decides nothing about files, clocks or zips.
/// </summary>
public static class PackageManifestBuilder
{
    public static JsonObject Build(string appVersion, DateTime savedAt,
        IReadOnlyList<PackageManifestMacro> macros,
        IReadOnlyList<PackageManifestVariable> variables,
        IReadOnlyList<string> assets)
        => new()
        {
            ["format"] = MacroPackage.FormatId,
            ["version"] = MacroPackage.FormatVersion,
            ["app"] = "WhaleGenie",
            ["appVersion"] = appVersion,
            ["savedUtc"] = savedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            ["macros"] = new JsonArray(
                [.. macros.Select(macro => (JsonNode)new JsonObject
                {
                    ["name"] = macro.Name,
                    ["enabled"] = macro.Enabled,
                    ["document"] = macro.Document,
                    ["steps"] = macro.Steps,
                })]),
            ["variables"] = new JsonArray(
                [.. variables.Select(variable => (JsonNode)new JsonObject
                {
                    ["name"] = variable.Name,
                    ["type"] = variable.Type,
                    ["default"] = variable.Default?.DeepClone(),
                    ["description"] = variable.Description,
                })]),
            ["assets"] = new JsonArray(
                [.. assets.Select(asset => (JsonNode)JsonValue.Create(asset))]),
        };

    public static JsonObject BuildLibrary(IReadOnlyList<PackageManifestLibraryEntry> entries)
        => new()
        {
            ["version"] = MacroPackage.LibraryEntriesVersion,
            ["entries"] = new JsonArray(
                [.. entries.Select(entry => (JsonNode)new JsonObject
                {
                    ["id"] = entry.Id,
                    ["version"] = Math.Max(1, entry.Version),
                    ["name"] = entry.Name,
                    ["description"] = entry.Description,
                    ["document"] = entry.Document,
                })]),
        };
}
public static class PackageReadme
{
    public static string Build(string appVersion,
        IReadOnlyList<(string Name, string Trigger, string Loop, int Steps)> macros,
        IReadOnlyList<VariableDefinition> globals, IReadOnlyList<string> assets,
        bool projectSection, bool librarySection, DateTime savedAt)
    {
        var text = new StringBuilder();
        text.AppendLine(projectSection
            ? "# WhaleGenie macro package"
            : "# WhaleGenie public macro library");
        text.AppendLine();
        text.AppendLine(projectSection
                ? "This file is a WhaleGenie macro project. It is a zip archive; rename it to"
                : "This file is a WhaleGenie public macro library. It is a package with callable")
            .AppendLine(projectSection
                ? "`.zip` if you want to look inside without WhaleGenie."
                : "macros under `library/entries.json`, not a project.");
        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture,
            $"- Format: `{MacroPackage.FormatId}` version {MacroPackage.FormatVersion}");
        text.AppendLine(CultureInfo.InvariantCulture, $"- Written by WhaleGenie {appVersion}");
        text.AppendLine(CultureInfo.InvariantCulture,
            $"- Saved: {savedAt:yyyy-MM-dd HH:mm:ss}");
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
}
