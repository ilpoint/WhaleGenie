using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
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
        var resolver = new AssetSourceResolver(
            [AssetFolderFor(packagePath), Path.GetDirectoryName(packagePath), assetFolder]);
        var plan = BuildPlan(appVersion, DateTime.UtcNow, DateTime.Now,
            macros, triggers, globals, projectSection, library, resolver.Resolve);

        foreach (var asset in plan.Assets)
        {
            var entry = archive.CreateEntry(asset.EntryName, CompressionLevel.Optimal);
            using var target = entry.Open();
            using var origin = File.OpenRead(asset.SourcePath);
            origin.CopyTo(target);
        }

        foreach (var entry in plan.Entries)
        {
            WriteEntry(archive, entry.Name, entry.Content);
        }
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
