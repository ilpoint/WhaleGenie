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
    public void Register(BridgeServer server)
    {
        server.Register("surface.menu.state", _ => Task.FromResult<object?>(menu.Snapshot()));
        server.Register("surface.menu.activate", Activate);
    }

    private Task<object?> Activate(System.Text.Json.JsonElement parameters)
    {
        var entryId = parameters.TryGetProperty("entryId", out var named) ? named.GetString() : null;
        if (entryId is not { Length: > 0 })
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.BadArgument"));
        }

        return Task.FromResult<object?>(new { started = menu.Activate(entryId) });
    }
}
