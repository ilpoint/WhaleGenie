using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Contracts;
using WhaleGenie.Editing;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Projects;
using WhaleGenie.Storage;

namespace WhaleGenie.Bridge;

/// <summary>
/// A macro's callable children: making an empty one, copying one in, and removing one the macro no
/// longer calls. The command names the child, so the contract's two answers are "that macro does
/// not exist" and "that child is still called"; the first is the same code opening a macro uses.
/// </summary>
internal sealed partial class EditorSessionBridge
{
    private object? ApplySubMacro(EditorSessionCommand command)
    {
        var session = Required();
        switch (command)
        {
            case EditorSessionSubMacroCreateCommand create:
                _documents.CreateSubmacro(_documents.ActiveId, OptionalSubMacroName(create.Name));
                return Snapshot();
            case EditorSessionSubMacroCopyCommand copy:
                {
                    var source = RequiredSubMacroText(copy.From);
                    try
                    {
                        return SubMacroAnswer(session,
                            session.CopySubMacro(source, OptionalSubMacroName(copy.Name)));
                    }
                    catch (KeyNotFoundException)
                    {
                        throw new BridgeFailureException(BridgeErrors.NoSuchMacro,
                            Strings.Format("Bridge.NoSuchMacro", source));
                    }
                }

            case EditorSessionSubMacroDeleteCommand delete:
                {
                    var name = RequiredSubMacroText(delete.Name);
                    try
                    {
                        return SubMacroAnswer(session, session.DeleteSubMacro(name));
                    }
                    catch (KeyNotFoundException)
                    {
                        throw new BridgeFailureException(BridgeErrors.NoSuchMacro,
                            Strings.Format("Bridge.NoSuchMacro", name));
                    }
                    catch (InvalidOperationException)
                    {
                        throw new BridgeFailureException(BridgeErrors.BadArgument,
                            Strings.Format("Bridge.SubMacroInUse", name));
                    }
                }

            case EditorSessionSubMacroImportCommand import:
                _documents.ImportSubmacro(_documents.ActiveId, ImportedMacro(import),
                    OptionalSubMacroName(import.Name));
                return Snapshot();

            default:
                throw new BridgeFailureException(BridgeErrors.BadArgument,
                    Strings.Get("Bridge.EditorNeedsMacro"));
        }
    }

    /// <summary>
    /// The macro an import names, from the one of the three sources it says. A library entry
    /// keeps its callable shape; a package macro is read with its pictures already unpacked into
    /// the project's asset folder; a project macro is the one already in memory.
    /// </summary>
    private MacroItem ImportedMacro(EditorSessionSubMacroImportCommand command)
    {
        var reference = RequiredSubMacroText(command.Reference);
        var source = command.Source.Trim().ToLowerInvariant();
        return source switch
        {
            "public" => PublicLibraryMacro(reference, command.Version,
                command.LibrarySource ?? "local"),
            "project" => workspace.Macros.FirstOrDefault(macro => string.Equals(
                macro.Name.Trim(), reference, StringComparison.OrdinalIgnoreCase))
                ?? throw new BridgeFailureException(BridgeErrors.NoSuchMacro,
                    Strings.Format("Bridge.NoSuchMacro", reference)),
            "file" => PackageMacro(RequiredSubMacroText(command.Path), reference),
            _ => throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsMacro")),
        };
    }

    private MacroItem PublicLibraryMacro(string id, int version = 0, string source = "local")
    {
        var entry = source == "project"
            ? workspace.Library.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, id, StringComparison.Ordinal)
                && (version <= 0 || candidate.Version == version))
            : _publicLibrary.Load().FirstOrDefault(candidate =>
                string.Equals(candidate.Id, id, StringComparison.Ordinal)
                && (version <= 0 || candidate.Version == version));
        var found = entry ?? throw new BridgeFailureException(BridgeErrors.NoSuchMacro,
            Strings.Format("Bridge.NoSuchMacro", id));
        var assetFolder = source == "project"
            ? ImageAssets.FolderFor(workspace.CurrentPath)
            : PublicMacroLibrary.AssetFolderFor(_publicLibrary.Path);
        return _publicLibrary.CopyToProject(found, assetFolder, assetFolder);
    }

    private MacroItem PackageMacro(string path, string name)
    {
        var read = _packages.ReadAsync(path, ImageAssets.FolderFor(workspace.CurrentPath))
            .ConfigureAwait(false).GetAwaiter().GetResult();
        if (!read.Ok || read.Value is not { } contents)
        {
            if (read.Problem == PackageProblem.LibraryNotProject)
            {
                throw new BridgeFailureException(BridgeErrors.BadArgument,
                    Strings.Get("Package.LibraryNotProject"));
            }

            throw new BridgeFailureException(BridgeErrors.PackageOpenFailed,
                read.Message ?? Strings.Get("Bridge.PackageOpenFailed"));
        }

        return contents.Macros.FirstOrDefault(macro => string.Equals(
            macro.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new BridgeFailureException(BridgeErrors.NoSuchMacro,
                Strings.Format("Bridge.NoSuchMacro", name));
    }

    /// <summary>
    /// The answer the page reads for a session command: the snapshot the command changed, beside
    /// the tabs and the action being written, which the command itself did not touch.
    /// </summary>
    private object? SubMacroAnswer(HeadlessEditorSession document, EditorSnapshot snapshot)
    {
        // A child's command is answered by the macro that owns the children. The page is looking at
        // the child's document, so the strip moves to the macro the answer is about: otherwise the
        // root's steps would arrive under the child's tab.
        var root = document.RootDocument;
        _documents.Activate(root);
        return Snapshot();
    }

    /// <summary>A name the command may leave out, which asks the program to pick the next free one.</summary>
    private static string? OptionalSubMacroName(string? value)
    {
        if (value is null)
        {
            return null;
        }

        return value.Trim() is { Length: > 0 } name
            ? name
            : throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsField"));
    }

    /// <summary>A name or source the command has to carry before anything can be found by it.</summary>
    private static string RequiredSubMacroText(string? value)
        => OptionalSubMacroName(value)
            ?? throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsField"));
}
