using System;
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
    private double _centerX = -1;
    private double _centerY = -1;

    /// <summary>The page has painted the frame it was last sent and can be revealed.</summary>
    internal event Action? Ready;

    public void Register(BridgeServer server)
    {
        _server = server;
        server.Register("surface.wheel.state", _ => Task.FromResult<object?>(State()));
        server.Register("surface.wheel.ready", _ =>
        {
            Ready?.Invoke();
            return Task.FromResult<object?>(null);
        });
    }

    private SurfaceWheelSnapshot State()
    {
        var snapshot = wheel.Snapshot();
        return _centerX >= 0 && _centerY >= 0
            ? snapshot with { CenterX = _centerX, CenterY = _centerY }
            : snapshot;
    }

    /// <summary>Pushes only the pointer and the rewarded ring; the wheel itself stays static.</summary>
    internal void PushAim(SurfaceWheelAim? aim, double x, double y)
        => _server?.PushAsync("surface.wheel.aim",
            new { x, y, ring = aim?.Ring, index = aim?.Index ?? -1, hasAim = aim is not null },
            BridgePushMode.Latest, BridgeSurface.Wheel);

    /// <summary>Pushes one frame of a wheel the caller resolved, aimed where the pointer is.</summary>
    internal void PushLayout(WhaleGenie.Models.WheelRuntimeLayout layout,
        WhaleGenie.Models.WheelOptions options, SurfaceWheelAim? aim = null,
        double dx = 0, double dy = 0, double centerX = -1, double centerY = -1,
        bool opening = false)
    {
        var snapshot = WheelSurface.Of(layout, options, aim, dx, dy);
        if (centerX >= 0 && centerY >= 0)
        {
            _centerX = centerX;
            _centerY = centerY;
            snapshot = snapshot with { CenterX = centerX, CenterY = centerY };
        }

        if (opening)
        {
            snapshot = snapshot with { Opening = true };
        }

        _server?.PushAsync("surface.wheel.changed", snapshot, BridgePushMode.Latest,
            BridgeSurface.Wheel);
    }
}