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
    /// where nothing else says so. The old <c>.vkm</c> comes from a name the project no longer
    /// uses, and no file in the wild carries it, so there is nothing that reads it back.
    /// </summary>
    public const string Extension = ".wgmacro";
    public const string FormatId = "whalegenie.macro.package";
    /// <summary>
    /// Bumped when what a field means changes, not only when a field is added. 20 is the version
    /// where a call writes what it passes as an argument row list rather than one parameter per
    /// argument: a 19 package's <c>arg.*</c> keys would be read as fields the action does not have,
    /// so it is refused rather than half-read. 25 is the version where how often the screen is read
    /// moved from each step to the settings window: a 24 package's <c>intervalMs</c> and
    /// <c>frameMaxAgeMs</c> would be read as fields no action has any more, and the macro would run
    /// at a different pace than the one it was written at without saying so. The wheel's positions
    /// joining <c>triggers.json</c> needs no bump: a package without the node opens with an empty
    /// wheel, and a package that has one is read by a build that knows the node. 26 is the version
    /// where a macro declares the local values it starts every run with: a 25 build would drop the
    /// <c>locals</c> node and run the same macro without its initial values, which is a different
    /// macro than the one that was saved.
    /// </summary>
    public const int FormatVersion = 26;

    private const string ManifestEntry = "manifest.json";
    private const string ReadmeEntry = "README.md";
    private const string TriggersEntry = "triggers.json";
    private const string NodesFolder = "nodes/";
    private const string AssetsFolder = "assets/";
    private const string ImageParameter = "image";

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };
}
