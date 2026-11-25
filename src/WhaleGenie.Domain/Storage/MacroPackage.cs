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

/// <summary>What a package file holds: the macros and the shared variables.</summary>
public sealed class PackageContents
{
    public required IReadOnlyList<MacroItem> Macros { get; init; }

    public required IReadOnlyList<VariableDefinition> Globals { get; init; }

    public required ProjectTriggers Triggers { get; init; }

    /// <summary>Whether the package carries a project section; false for a library-only file.</summary>
    public bool HasProject { get; init; } = true;

    /// <summary>The manifest facts a preview shows; a loaded project always has them.</summary>
    public int FormatVersion { get; init; } = MacroPackage.FormatVersion;

    public string AppVersion { get; init; } = string.Empty;

    public string SavedUtc { get; init; } = string.Empty;

    /// <summary>The project's embedded public macros, when the package carries a library section.</summary>
    public IReadOnlyList<PublicMacroEntry> Library { get; init; } = [];
}

/// <summary>A package whose <c>library/</c> section holds versioned callable macros.</summary>
public sealed class LibraryContents
{
    public required IReadOnlyList<PublicMacroEntry> Entries { get; init; }
}

/// <summary>Which of the one package format's two sections a file carries.</summary>
public enum PackageKind
{
    Project,
    Library,
}

/// <summary>A package read through the wrong entry point; the caller maps this to its own words.</summary>
public sealed class PackageKindMismatchException(PackageKind actual)
    : IOException(actual == PackageKind.Library
        ? Strings.Get("Package.LibraryNotProject")
        : Strings.Get("Package.ProjectNotLibrary"))
{
    public PackageKind Actual { get; } = actual;
}

/// <summary>
/// Reads and writes the <c>.wgmacro</c> macro package: a zip holding a manifest, a readme,
/// one JSON document per macro and the images those macros reference. A library is the same
/// package with <c>library/entries.json</c>; it has no project triggers or project variables.
/// </summary>
public static partial class MacroPackage
{
    /// <summary>
    /// The extension is the program's name, so a package is recognisable as this program's even
    /// where nothing else says so.
    /// </summary>
    public const string Extension = ".wgmacro";
    public const string FormatId = "whalegenie.macro.package";

    /// <summary>
    /// Bumped whenever a field's meaning changes, not only when a field is added. A package written
    /// for another version is refused rather than half-read: no field is guessed at from a spelling
    /// that once meant something else.
    /// </summary>
    public const int FormatVersion = 33;

    private const string ManifestEntry = "manifest.json";
    private const string ReadmeEntry = "README.md";
    private const string TriggersEntry = "triggers.json";
    private const string NodesFolder = "nodes/";
    private const string AssetsFolder = "assets/";
    private const string ImageParameter = "image";
    private const string LibraryFolder = "library/";
    private const string LibraryEntriesEntry = "library/entries.json";

    /// <summary>The shape of <c>library/entries.json</c>, independent of the outer package version.</summary>
    public const int LibraryEntriesVersion = 1;

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };
}
