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

    /// <summary>Pushes one frame of a wheel the caller resolved, aimed where the pointer is.</summary>
    internal void PushLayout(WhaleGenie.Models.WheelRuntimeLayout layout,
        WhaleGenie.Models.WheelOptions options, SurfaceWheelAim? aim = null,
        double dx = 0, double dy = 0)
        => _server?.PushAsync("surface.wheel.changed", WheelSurface.Of(layout, options, aim, dx, dy),
            BridgePushMode.Latest, BridgeSurface.Wheel);
}