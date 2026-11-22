using System.Threading.Tasks;
using WhaleGenie.Contracts;
using WhaleGenie.Localization;
using WhaleGenie.Runtime;

namespace WhaleGenie.Bridge;

/// <summary>
/// The menu's side of the bridge: the pages the project has and the cells they put macros in. The
/// overlay asks once when it opens; paging and search are the page's own state, so nothing here
/// pushes.
/// </summary>
internal sealed class MenuSurfaceBridge(MenuSurface menu) : IBridgeHandler
{
    /// <summary>The window the page speaks through, once the shell has built it.</summary>
    internal IMenuOverlay? Overlay { get; set; }

    public void Register(BridgeServer server)
    {
        server.Register("surface.menu.state", _ => Task.FromResult<object?>(menu.Snapshot()));
        server.Register("surface.menu.activate", Activate);
        server.Register("surface.menu.close", _ =>
        {
            Overlay?.Hide();
            return Task.FromResult<object?>(null);
        });
    }

    private Task<object?> Activate(System.Text.Json.JsonElement parameters)
    {
        var entryId = parameters.TryGetProperty("entryId", out var named) ? named.GetString() : null;
        if (entryId is not { Length: > 0 })
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.BadArgument"));
        }

        // The menu goes away before the macro runs, so the window the macro is about gets the
        // foreground back instead of the overlay sitting on top of it.
        Overlay?.Hide();
        return Task.FromResult<object?>(new { started = menu.Activate(entryId) });
    }
}
