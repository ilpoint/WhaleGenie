using System.Threading.Tasks;
using WhaleGenie.Contracts;
using WhaleGenie.Runtime;

namespace WhaleGenie.Bridge;

/// <summary>
/// The wheel's frame and its aim: one answer when the page opens, and a pushed frame when the
/// pointer moves. The aim is coalesced — a fast pointer writes many positions and the page only
/// needs the last one.
/// </summary>
internal sealed class WheelSurfaceBridge(WheelSurface wheel) : IBridgeHandler
{
    private BridgeServer? _server;

    public void Register(BridgeServer server)
    {
        _server = server;
        server.Register("surface.wheel.state", _ => Task.FromResult<object?>(wheel.Snapshot()));
    }

    /// <summary>Sends one aim frame; a null aim means the pointer is over the centre.</summary>
    internal void PushAim(SurfaceWheelAim? aim)
        => _server?.PushAsync("surface.wheel.changed", wheel.Snapshot(aim),
            BridgePushMode.Latest, BridgeSurface.Wheel);
}