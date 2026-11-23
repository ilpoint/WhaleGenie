using System;
using WhaleGenie.Contracts;

namespace WhaleGenie.Runtime;

/// <summary>
/// One interface group's panel as an overlay window: placed by C# next to its target, drawn by the
/// page, and moved with the system move loop so it can travel past its own bounds.
/// </summary>
public interface IPanelOverlay
{
    bool IsVisible { get; }

    /// <summary>Builds the window and loads the page before the first real show.</summary>
    void Warm();

    /// <summary>Shows the panel at a screen position and size, with the frame it should draw.</summary>
    void Show(SurfacePanelSnapshot snapshot, OverlayPoint position, int width, int height,
        long aboveHandle);

    void Hide();

    void BeginDrag();

    /// <summary>Raised after the user moved the window; the caller stores the new offset.</summary>
    event Action<OverlayPoint>? Moved;
}
