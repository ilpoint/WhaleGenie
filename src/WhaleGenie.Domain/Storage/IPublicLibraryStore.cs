using System.Collections.Generic;
using WhaleGenie.Models;

namespace WhaleGenie.Storage;

/// <summary>
/// The public library's storage boundary. Editing asks it for the callable entries and App asks it
/// to load, save and copy one; the archive is the same <c>.wgmacro</c> package as a project, with
/// a <c>library/entries.json</c> section instead of project triggers.
/// </summary>
public interface IPublicLibraryStore
{
    /// <summary>The default file this store serves; tests may hand in a temporary path.</summary>
    string Path { get; }

    IReadOnlyList<PublicMacroEntry> Load(string? path = null);

    void Save(IReadOnlyList<PublicMacroEntry> entries, string appVersion, string? path = null);

    /// <summary>Copies one entry into a project's asset folder with its own image files.</summary>
    MacroItem CopyToProject(PublicMacroEntry entry, string assetFolder,
        string? sourceAssetFolder = null);
}

/// <summary>
/// The real public library file. It is the project package format with a library section, so the
/// codec, atomic replacement and backup are the same as for a project.
/// </summary>
public sealed class FilePublicLibraryStore(string? path = null) : IPublicLibraryStore
{
    public string Path => path ?? AppPaths.PublicMacroLibrary;

    public IReadOnlyList<PublicMacroEntry> Load(string? path = null)
        => PublicMacroLibrary.Load(path ?? Path);

    public void Save(IReadOnlyList<PublicMacroEntry> entries, string appVersion,
        string? path = null)
        => PublicMacroLibrary.Save(path ?? Path, entries, appVersion);

    public MacroItem CopyToProject(PublicMacroEntry entry, string assetFolder,
        string? sourceAssetFolder = null)
        => PublicMacroLibrary.CopyToProject(entry, assetFolder,
            sourceAssetFolder ?? PublicMacroLibrary.AssetFolderFor(Path));
}

/// <summary>A library with no entries, for tests and for a session that has no library attached.</summary>
public sealed class EmptyPublicLibraryStore : IPublicLibraryStore
{
    public static EmptyPublicLibraryStore Instance { get; } = new();

    public string Path => string.Empty;

    public IReadOnlyList<PublicMacroEntry> Load(string? path = null) => [];

    public void Save(IReadOnlyList<PublicMacroEntry> entries, string appVersion,
        string? path = null)
    {
    }

    public MacroItem CopyToProject(PublicMacroEntry entry, string assetFolder,
        string? sourceAssetFolder = null)
        => MacroItem.FromJson(entry.Macro.ToSubMacroJson());
}
