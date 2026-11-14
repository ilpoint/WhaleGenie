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
}

/// <summary>
/// Reads and writes the <c>.wgmacro</c> macro package: a zip holding a manifest, a readme,
/// one JSON document per macro and the images those macros reference.
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
    public const int FormatVersion = 31;

    private const string ManifestEntry = "manifest.json";
    private const string ReadmeEntry = "README.md";
    private const string TriggersEntry = "triggers.json";
    private const string NodesFolder = "nodes/";
    private const string AssetsFolder = "assets/";
    private const string ImageParameter = "image";

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };
}
