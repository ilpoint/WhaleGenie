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

// Reading a package back, and what it holds.
public static partial class MacroPackage
{

    /// <summary>Reads a package file back into macros and shared variables.</summary>
    public static PackageContents Load(string path) => Load(path, AssetFolderFor(path), reuseExisting: true);

    /// <summary>
    /// Reads a package, putting the pictures it carries into <paramref name="assetFolder"/>.
    /// </summary>
    /// <remarks>
    /// An import is not the same reader as an open. Opening a project finds a folder that belongs to
    /// that project, where a picture already there is the one the macro means. A file that arrives
    /// from elsewhere is unpacked into somebody else's folder, so a name already taken is not
    /// evidence of the same picture: the bytes are compared, and a different picture is kept beside
    /// the old one rather than quietly replacing what another macro looks for.
    /// </remarks>
    public static PackageContents Load(string path, string assetFolder)
        => Load(path, assetFolder, reuseExisting: false);

    private static PackageContents Load(string path, string assetFolder, bool reuseExisting)
    {
        var fullPath = Path.GetFullPath(path);
        using var stream = File.OpenRead(fullPath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var manifest = ReadObject(archive, ManifestEntry)
            ?? throw new InvalidDataException("The package has no manifest.json.");

        var format = manifest["format"]?.GetValue<string>();
        if (!string.Equals(format, FormatId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"'{format}' is not a WhaleGenie macro package.");
        }

        var version = manifest["version"]?.GetValue<int>() ?? 0;
        if (version != FormatVersion)
        {
            throw new InvalidDataException(
                $"The package was written in format {version}; this WhaleGenie reads format "
                + $"{FormatVersion}.");
        }

        var assets = new AssetExtractor(archive, assetFolder, reuseExisting);
        var macros = new List<MacroItem>();

        foreach (var item in manifest["macros"] as JsonArray ?? [])
        {
            if (item is not JsonObject entry || ReadString(entry, "document") is not { Length: > 0 } document)
            {
                continue;
            }

            if (ReadObject(archive, document) is not { } node)
            {
                continue;
            }

            RewriteAssets(node, assets.Expand);
            var macro = MacroItem.FromJson(node);

            if (macro.Name.Length == 0)
            {
                macro.Name = ReadString(entry, "name");
            }

            macro.IsEnabled = ReadBool(entry, "enabled", true);
            macros.Add(macro);
        }

        // Two macros with one name can only come from a file somebody edited by hand: the editor
        // refuses to save a second macro under a name that is taken. They are still two macros with
        // their own steps, so the later one is given the date it was read on and both are kept —
        // dropping one, which is what a name-keyed table would do, loses work and leaves every call
        // to that name meaning whichever happened to be read first.
        Disambiguate(macros, DateTime.Now);

        var triggers = ProjectTriggers.FromJson(ReadObject(archive, TriggersEntry));
        ExpandMenuIcons(triggers, assets.Expand);
        triggers.Attach(macros);

        var globals = new List<VariableDefinition>();
        foreach (var item in manifest["variables"] as JsonArray ?? [])
        {
            if (item is not JsonObject variable || ReadString(variable, "name") is not { Length: > 0 } name)
            {
                continue;
            }

            globals.Add(new VariableDefinition
            {
                Id = ReadString(variable, "id").Trim(),
                Name = name,
                Scope = VariableScope.Global,
                Type = VariableTypes.KindOf(ReadString(variable, "type")),
                Default = FormulaJson.Read(variable["default"]),
                Description = ReadString(variable, "description"),
            });
        }

        VariableIds.SettleGlobals(globals);
        foreach (var macro in macros)
        {
            VariableIds.Bind(macro, globals);
        }

        return new PackageContents { Macros = macros, Globals = globals, Triggers = triggers };
    }
}
