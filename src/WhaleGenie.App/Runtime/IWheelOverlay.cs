using System;
using WhaleGenie.Contracts;
using WhaleGenie.Models;

namespace WhaleGenie.Runtime;

/// <summary>A screen position without pulling Avalonia into the application layer.</summary>
public readonly record struct OverlayPoint(int X, int Y);

/// <summary>
/// The wheel as an overlay window: opened at the pointer when the gesture starts, sent one aim frame
/// per pointer sample, and closed when the gesture ends. The gesture and the hit test stay in C#;
/// the window only draws what it is told.
/// </summary>
public interface IWheelOverlay
{
    bool IsVisible { get; }

    /// <summary>The radii the open wheel was drawn with; the hit test reads them.</summary>
    WheelMetrics Metrics { get; }

    /// <summary>The visible wheel's centre after screen clamping; the hit test must use this.</summary>
    OverlayPoint Center { get; }

    /// <summary>
    /// Builds the window and its web control ahead of the first open; the page itself is loaded when
    /// the window first goes on screen, because a hidden one is refused by the control.
    /// </summary>
    void Warm();

    /// <summary>Shows the wheel centred on the pointer, with its own idle timeout.</summary>
    void Open(WheelRuntimeLayout layout, WheelOptions options, OverlayPoint origin, int timeoutMs);

    /// <summary>The wheel's scene changed while it is open; the page redraws, the window stays put.</summary>
    void SetLayout(WheelRuntimeLayout layout);

    /// <summary>The pointer moved to a position, or to the centre when the aim is null.</summary>
    void SetAim(SurfaceWheelAim? aim, OverlayPoint pointer);

    void Close();

    /// <summary>Raised when the wheel sat idle for its timeout; the gesture decides what that means.</summary>
    event Action? TimedOut;
}
