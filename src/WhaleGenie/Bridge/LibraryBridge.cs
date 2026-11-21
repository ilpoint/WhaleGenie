using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Storage;

namespace WhaleGenie.Bridge;

/// <summary>
/// The public macro library as the page sees it: the shared callable macros, listed by name,
/// version and description. The manager window is still native, but the page needs its list so
/// the library is not invisible once the editor moves.
/// </summary>
internal sealed class LibraryBridge : IBridgeHandler
{
    public void Register(BridgeServer server)
    {
        server.Register("library.state", _ => Task.FromResult<object?>(State()));
        server.Register("library.delete", Delete);
    }

    private object State()
    {
        try
        {
            return new
            {
                entries = PublicMacroLibrary.Load(AppPaths.PublicMacroLibrary)
                    .Select(entry => new
                    {
                        id = entry.Id,
                        name = entry.Macro.Name,
                        version = entry.Version,
                        description = entry.Macro.Description,
                    }),
                error = (string?)null,
            };
        }
        catch (Exception error) when (error is IOException or InvalidDataException)
        {
            return new { entries = Array.Empty<object>(), error = error.Message };
        }
    }

    private Task<object?> Delete(JsonElement parameters)
    {
        var id = parameters.TryGetProperty("id", out var named) ? named.GetString() : null;
        if (id is not { Length: > 0 })
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("PublicMacroManager.Delete"));
        }

        try
        {
            var entries = PublicMacroLibrary.Load(AppPaths.PublicMacroLibrary)
                .Where(entry => !string.Equals(entry.Id, id, StringComparison.Ordinal))
                .ToList();
            PublicMacroLibrary.Save(AppPaths.PublicMacroLibrary, entries, AppVersion.Text);
            return Task.FromResult<object?>(State());
        }
        catch (Exception error) when (error is IOException or InvalidDataException)
        {
            throw new BridgeFailureException(BridgeErrors.Internal, error.Message);
        }
    }
}
