using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using WhaleGenie.Contracts;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Storage;

namespace WhaleGenie.Bridge;

/// <summary>
/// The public macro library as the page sees it. Entries are callable macros with no trigger of
/// their own; a project gets a copy, and how that copy is called is the editor's business. The
/// page lists and removes entries and publishes the macro that is open in the editor; nothing here
/// puts a project macro into the library as an editable second copy of the macro list.
/// </summary>
internal sealed class LibraryBridge(ProjectWorkspace project, DialogBridge? dialogs = null,
    string? libraryPath = null)
    : IBridgeHandler
{
    private readonly string _library = libraryPath ?? AppPaths.PublicMacroLibrary;

    public void Register(BridgeServer server)
    {
        server.Register("library.state", _ => Task.FromResult<object?>(State()));
        server.Register("library.create", Create);
        server.Register("library.import", Import);
        server.Register("library.delete", Delete);
        server.Register("library.publish", Publish);
    }

    private LibraryState State()
    {
        try
        {
            return new LibraryState(
                [.. Entries().Select(entry => new LibraryEntry(
                    entry.Id, entry.Macro.Name, entry.Version, entry.Macro.Description))],
                null);
        }
        catch (Exception error) when (error is IOException or InvalidDataException)
        {
            return new LibraryState([], error.Message);
        }
    }

    /// <summary>
    /// Adds a blank entry. The library is a place to start from as well as a shelf of finished
    /// macros; a blank callable is valid and can be filled in by copying it into a project.
    /// </summary>
    private Task<object?> Create(JsonElement parameters)
    {
        var wanted = parameters.TryGetProperty("name", out var named) ? named.GetString() : null;
        return Change(entries =>
        {
            var name = FreeName(entries, wanted);
            return [.. entries, new PublicMacroEntry(Guid.NewGuid().ToString("N"), 1,
                new MacroItem { Name = name })];
        });
    }

    /// <summary>Adds every macro a package holds to the library, each under a free name.</summary>
    private Task<object?> Import(JsonElement parameters)
    {
        var path = parameters.TryGetProperty("path", out var named) ? named.GetString() : null;
        if (path is not { Length: > 0 })
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsFile"));
        }

        try
        {
            var contents = MacroPackage.Load(path);
            MacroImport.IntoLibrary(contents.Macros, _library);
            return Task.FromResult<object?>(State());
        }
        catch (Exception error) when (error is IOException or InvalidDataException)
        {
            throw new BridgeFailureException(BridgeErrors.Internal, error.Message);
        }
    }

    private static string FreeName(IReadOnlyList<PublicMacroEntry> entries, string? wanted)
    {
        var taken = entries.Select(entry => entry.Macro.Name.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var stem = wanted?.Trim() is { Length: > 0 } named ? named : Strings.Get("Library.NewName");
        var name = stem;
        for (var index = 2; taken.Contains(name); index++)
        {
            name = stem + " " + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return name;
    }

    /// <summary>
    /// Removes one entry. Deleting is not something the page does by itself: the library is shared
    /// by every project, and a mistaken click would take the definition away from all of them.
    /// </summary>
    private async Task<object?> Delete(JsonElement parameters)
    {
        var id = RequiredId(parameters);
        var entry = Entries().FirstOrDefault(candidate =>
            string.Equals(candidate.Id, id, StringComparison.Ordinal));
        if (entry is null)
        {
            return State();
        }

        if (dialogs is not null)
        {
            var accepted = await dialogs.ConfirmAsync(
                Strings.Get("PublicMacroManager.DeleteTitle"),
                Strings.Format("PublicMacroManager.DeleteMessage", entry.Macro.Name),
                Strings.Get("PublicMacroManager.Delete"),
                Strings.Get("Common.Cancel"));
            if (!accepted)
            {
                return State();
            }
        }

        return await Change(entries => entries
            .Where(candidate => !string.Equals(candidate.Id, id, StringComparison.Ordinal))
            .ToList());
    }

    /// <summary>
    /// Copies the named project macro into the library in the callable shape: a sub-macro document
    /// has no trigger, recorder or interface of its own, because those belong to the project that
    /// starts it. Publishing the same name again replaces the entry and raises its version only
    /// when the definition really changed.
    /// </summary>
    private Task<object?> Publish(JsonElement parameters)
    {
        var macro = ProjectMacro(parameters);
        var copy = MacroItem.FromJson(macro.ToSubMacroJson());
        copy.Name = macro.Name.Trim();
        copy.Description = macro.Description;

        return Change(entries =>
        {
            var existing = entries.FirstOrDefault(entry => string.Equals(
                entry.Macro.Name.Trim(), copy.Name, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                return [.. entries, new PublicMacroEntry(Guid.NewGuid().ToString("N"), 1, copy)];
            }

            var version = string.Equals(Stored(existing.Macro), Stored(copy), StringComparison.Ordinal)
                ? existing.Version
                : existing.Version + 1;
            return [.. entries.Select(entry => ReferenceEquals(entry, existing)
                ? new PublicMacroEntry(entry.Id, version, copy)
                : entry)];
        });
    }

    private static string Stored(MacroItem macro) => macro.ToSubMacroJson().ToJsonString();

    private Task<object?> Change(Func<IReadOnlyList<PublicMacroEntry>, List<PublicMacroEntry>> change)
    {
        try
        {
            var entries = change(Entries());
            PublicMacroLibrary.Save(_library, entries, AppVersion.Text);
            return Task.FromResult<object?>(State());
        }
        catch (Exception error) when (error is IOException or InvalidDataException)
        {
            throw new BridgeFailureException(BridgeErrors.Internal, error.Message);
        }
    }

    private MacroItem ProjectMacro(JsonElement parameters)
    {
        var name = parameters.TryGetProperty("name", out var named) ? named.GetString() : null;
        var macro = project.Macros.FirstOrDefault(candidate => string.Equals(
            candidate.Name.Trim(), name?.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new BridgeFailureException(BridgeErrors.NoSuchMacro,
                Strings.Format("Bridge.NoSuchMacro", name ?? string.Empty));
        return macro;
    }

    private static string RequiredId(JsonElement parameters)
        => parameters.TryGetProperty("id", out var named) && named.GetString() is { Length: > 0 } id
            ? id
            : throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("PublicMacroManager.Delete"));

    private IReadOnlyList<PublicMacroEntry> Entries()
        => PublicMacroLibrary.Load(_library);
}
