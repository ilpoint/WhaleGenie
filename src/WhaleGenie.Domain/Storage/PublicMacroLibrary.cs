using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using WhaleGenie.Models;

namespace WhaleGenie.Storage;

/// <summary>One versioned callable macro in the machine-wide public library.</summary>
public sealed record PublicMacroEntry(string Id, int Version, MacroItem Macro);

/// <summary>
/// The public library's view of the one package format. The archive is read and written by
/// <see cref="MacroPackage"/>; this class only maps the package's <c>library/entries.json</c> to
/// entries, copies an entry into a project, and merges another library's entries.
/// </summary>
public static class PublicMacroLibrary
{
    /// <summary>Where this library's unpacked images live; a library keeps them beside its file.</summary>
    public static string AssetFolderFor(string path)
        => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".",
            Path.GetFileNameWithoutExtension(path) + ".assets");

    /// <summary>Loads the library, answering an empty list when the file has not been made yet.</summary>
    public static IReadOnlyList<PublicMacroEntry> Load(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            return [];
        }

        return MacroPackage.LoadLibrary(fullPath, AssetFolderFor(fullPath), reuseExisting: true)
            .Entries;
    }

    /// <summary>Writes the whole library as the same package format, atomically.</summary>
    public static void Save(string path, IReadOnlyList<PublicMacroEntry> entries,
        string appVersion)
        => MacroPackage.SaveLibrary(path, entries, appVersion, AssetFolderFor(path));

    /// <summary>
    /// Copies one public macro into a project, rewriting every referenced image to a content-named
    /// file in the project's asset folder. The returned tree has no live dependency on the library.
    /// </summary>
    public static MacroItem CopyToProject(PublicMacroEntry entry, string assetFolder,
        string? sourceAssetFolder = null)
    {
        var node = entry.Macro.ToSubMacroJson();
        MacroPackage.RewriteAssets(node, value => CopyImage(value, assetFolder,
            sourceAssetFolder ?? AssetFolderFor(AppPaths.PublicMacroLibrary)));
        var copy = MacroItem.FromJson(node);
        copy.Name = entry.Macro.Name.Trim();
        copy.Description = entry.Macro.Description;
        copy.Origin = new MacroOrigin(entry.Id, entry.Macro.Name.Trim(),
            Math.Max(1, entry.Version));
        return copy;
    }

    /// <summary>
    /// Adds entries from another library package to this one. The arriving id and version are kept
    /// unless the id is already taken; a root name already in the library gets the same free-name
    /// rule as any other import. Pictures are copied into this library's own asset folder.
    /// </summary>
    public static IReadOnlyList<PublicMacroEntry> Merge(
        IReadOnlyList<PublicMacroEntry> arriving,
        IReadOnlyList<PublicMacroEntry> existing,
        string targetAssetFolder,
        string sourceAssetFolder)
    {
        var names = existing.Select(entry => entry.Macro.Name.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ids = existing.Select(entry => entry.Id.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = new List<PublicMacroEntry>();

        foreach (var entry in arriving)
        {
            var copy = CopyToProject(entry, targetAssetFolder, sourceAssetFolder);
            copy.Name = MacroNames.Free(copy.Name, names, DateTime.Now);
            copy.Origin = null;
            names.Add(copy.Name);

            var id = entry.Id.Trim();
            if (id.Length == 0 || !ids.Add(id))
            {
                id = Guid.NewGuid().ToString("N");
                ids.Add(id);
            }

            var merged = new PublicMacroEntry(id, Math.Max(1, entry.Version), copy);
            added.Add(merged);
        }

        return added;
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
}
