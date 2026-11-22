using System.Threading.Tasks;
using WhaleGenie.Contracts;
using WhaleGenie.Runtime;

namespace WhaleGenie.Bridge;

/// <summary>The wheel's frame to the page: one call when it opens, one when its scene changes.</summary>
internal sealed class WheelSurfaceBridge(WheelSurface wheel) : IBridgeHandler
{
    public void Register(BridgeServer server)
        => server.Register("surface.wheel.state", _ => Task.FromResult<object?>(wheel.Snapshot()));
}
