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
/// The public macro library as the page sees it. The page lists and removes entries and can put
/// a project macro into the library or replace one with the current version of a project macro;
/// the native manager window is no longer needed for the library to stay usable.
/// </summary>
internal sealed class LibraryBridge(ProjectWorkspace project) : IBridgeHandler
{
    public void Register(BridgeServer server)
    {
        server.Register("library.state", _ => Task.FromResult<object?>(State()));
        server.Register("library.delete", Delete);
        server.Register("library.add", Add);
        server.Register("library.replace", Replace);
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

    private Task<object?> Delete(JsonElement parameters)
    {
        var id = RequiredId(parameters);
        return Change(entries => entries.Where(entry =>
            !string.Equals(entry.Id, id, StringComparison.Ordinal)).ToList());
    }

    private Task<object?> Add(JsonElement parameters)
    {
        var macro = ProjectMacro(parameters);
        return Change(entries =>
        {
            var name = UniqueName(entries, macro.Name.Trim().Length > 0 ? macro.Name : "macro");
            var copy = MacroItem.FromJson(macro.ToJson());
            copy.Name = name;
            return [.. entries, new PublicMacroEntry(Guid.NewGuid().ToString("N"), 1, copy)];
        });
    }

    private Task<object?> Replace(JsonElement parameters)
    {
        var id = RequiredId(parameters);
        var macro = ProjectMacro(parameters);
        return Change(entries => [.. entries.Select(entry =>
        {
            if (!string.Equals(entry.Id, id, StringComparison.Ordinal))
            {
                return entry;
            }

            var copy = MacroItem.FromJson(macro.ToJson());
            copy.Name = UniqueName(entries, macro.Name, id);
            var version = string.Equals(entry.Macro.ToJson().ToJsonString(),
                copy.ToJson().ToJsonString(), StringComparison.Ordinal)
                ? entry.Version
                : entry.Version + 1;
            return new PublicMacroEntry(entry.Id, version, copy);
        })]);
    }

    private Task<object?> Change(Func<IReadOnlyList<PublicMacroEntry>, List<PublicMacroEntry>> change)
    {
        try
        {
            var entries = change(Entries());
            PublicMacroLibrary.Save(AppPaths.PublicMacroLibrary, entries, AppVersion.Text);
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

    private static IReadOnlyList<PublicMacroEntry> Entries()
        => PublicMacroLibrary.Load(AppPaths.PublicMacroLibrary);

    private static string UniqueName(IReadOnlyList<PublicMacroEntry> entries, string wanted,
        string? exceptId = null)
    {
        var name = wanted.Trim();
        if (name.Length == 0)
        {
            name = "macro";
        }

        for (var index = 2; entries.Any(entry =>
            !string.Equals(entry.Id, exceptId, StringComparison.Ordinal)
            && string.Equals(entry.Macro.Name, name, StringComparison.OrdinalIgnoreCase)); index++)
        {
            name = wanted.Trim() + " " + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return name;
    }
}
