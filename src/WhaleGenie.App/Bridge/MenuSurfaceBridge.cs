using System.Threading.Tasks;
using WhaleGenie.Contracts;
using WhaleGenie.Runtime;

namespace WhaleGenie.Bridge;

/// <summary>
/// The menu's side of the bridge: the pages the project has and the cells they put macros in. The
/// overlay asks once when it opens; paging and search are the page's own state, so nothing here
/// pushes.
/// </summary>
public sealed class MenuSurfaceBridge(MenuSurface menu) : IBridgeHandler
{
    public void Register(BridgeServer server)
        => server.Register("surface.menu.state", _ => Task.FromResult<object?>(menu.Snapshot()));
}
