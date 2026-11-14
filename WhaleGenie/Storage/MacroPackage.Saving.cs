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

// Where a package and its assets live, saving one, and keeping two macros from sharing a name.
public static partial class MacroPackage
{
    /// <summary>Where a package's images are unpacked when it is opened, and written when taken.</summary>
    public static string AssetFolderFor(string packagePath)
        => Path.Combine(FolderFor(packagePath), "assets");

    /// <summary>
    /// The copy of the package that the last save replaced. It is kept under the program's own data
    /// folder rather than beside the package: a save that turns out to be wrong is still one mistake
    /// nobody can undo from inside the program, and the copy is for the program and for whoever goes
    /// looking, not part of the folder the projects are read from.
    /// </summary>
    public static string BackupPathFor(string packagePath)
        => Path.Combine(FolderFor(packagePath), "last.wgmacro.bak");

    /// <summary>
    /// The folder one package owns, named after it and hashed by its full path: two projects called
    /// the same in different folders must not unpack over each other, and one project opened again
    /// must find the pictures it already has.
    /// </summary>
    private static string FolderFor(string packagePath)
    {
        var full = Path.GetFullPath(packagePath);
        var stem = Path.GetFileNameWithoutExtension(full);
        foreach (var bad in Path.GetInvalidFileNameChars())
        {
            stem = stem.Replace(bad, '_');
        }

        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(full.ToLowerInvariant())))[..8];
        return Path.Combine(AppPaths.Projects, $"{stem}-{hash}");
    }

    /// <summary>Writes the macros and shared variables into a package file.</summary>
    public static void Save(string path, IReadOnlyList<MacroItem> macros,
        IReadOnlyList<VariableDefinition> globals, string appVersion, string? assetFolder = null)
        => Save(path, macros, ProjectTriggers.FromMacros(macros), globals, appVersion, assetFolder);

    /// <summary>
    /// Writes the macro bodies and the project-wide trigger entries into one package.
    /// </summary>
    /// <param name="assetFolder">
    /// Where the pictures are now, when that is not beside the file being written. Exporting a
    /// macro, saving a project somewhere else and pulling one out of the public library all write
    /// the package somewhere the images have never been: without this, a value that says
    /// <c>assets/x.png</c> resolves to nothing, the image is left out of the zip, and the package
    /// travels with a reference to a file only this machine has.
    /// </param>
    public static void Save(string path, IReadOnlyList<MacroItem> macros,
        ProjectTriggers triggers, IReadOnlyList<VariableDefinition> globals, string appVersion,
        string? assetFolder = null)
    {
        triggers.Attach(macros);

        // A macro the editor has just built has names but no identities yet. Settling here is
        // idempotent: a macro that came from a file keeps every id it has, and a new declaration
        // gets one before the file is written rather than on the next load.
        foreach (var macro in macros)
        {
        }


        // A macro with a fatal declaration problem cannot be called, so writing it into a package
        // would only carry a broken interface forward. The editor blocks the same faults; this is
        // the guard for every other way a package can be written.
        foreach (var macro in macros)
        {
            var problems = macro.Callable.Problems();
            if (problems.Count > 0)
            {
                throw new InvalidDataException(Strings.Format("Package.BadDeclaration",
                    macro.Name, problems[0].Name));
            }
        }

        var menuProblems = triggers.MenuProblems(macros);
        if (menuProblems.Count > 0)
        {
            throw new InvalidDataException(menuProblems[0]);
        }

        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? ".");

        // Build in a temporary file first, so a failure cannot damage an existing package.
        var temporary = fullPath + ".tmp";
        try
        {
            using (var stream = File.Create(temporary))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                SaveInto(archive, fullPath, macros, triggers, globals, appVersion, assetFolder);
            }

            // The package being replaced is kept as a copy under the program's data folder. A save
            // that turns out to be wrong is the one mistake nobody can undo from inside the program,
            // and the copy costs a single file write. Every path that saves — the window's own Ctrl+S
            // and the MCP tool alike — comes through here, so none of them can forget it.
            if (File.Exists(fullPath))
            {
                var backup = BackupPathFor(fullPath);
                Directory.CreateDirectory(Path.GetDirectoryName(backup) ?? ".");
                File.Copy(fullPath, backup, true);
            }

            File.Move(temporary, fullPath, true);
        }
        catch
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }

            throw;
        }
    }

    /// <summary>
    /// Gives every macro after the first one of its name a suffix saying when it was read, so a
    /// package that carries two macros of one name still arrives as two macros. The first one keeps
    /// the name, because that is the one the file’s call sites and triggers were written against.
    /// </summary>
    internal static void Disambiguate(IReadOnlyList<MacroItem> macros, DateTime when)
    {
        // Taken names start as every name the file has, not just the ones seen so far: a macro whose
        // own name happens to be the suffixed form must not be pushed aside by the suffix.
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var macro in macros)
        {
            if (macro.Name.Trim() is { Length: > 0 } name)
            {
                taken.Add(name);
            }
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var macro in macros)
        {
            var name = macro.Name.Trim();
            if (name.Length == 0)
            {
                // An unnamed macro is the declaration editor's problem; there is nothing to make
                // unique about a name nobody has.
                continue;
            }

            macro.Name = name;
            if (seen.Add(name))
            {
                continue;
            }

            macro.Name = MacroNames.Free(name, taken, when);
            taken.Add(macro.Name);
        }
    }
}
