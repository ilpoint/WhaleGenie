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

    /// <summary>
    /// True while the person is moving the panel. A refresh must keep its hands off a window the
    /// system move loop is moving, or the two would set the place on every frame of the drag.
    /// </summary>
    bool IsMoving { get; }

    /// <summary>
    /// Shows the panel at a screen position and size, with the frame it should draw. The window is
    /// built on the first show: a group has no target to sit beside until a window answers to its
    /// rule, so warming every group at startup would build windows nobody may ever see.
    /// </summary>
    void Show(SurfacePanelSnapshot snapshot, OverlayPoint position, int width, int height,
        long aboveHandle);

    /// <summary>Takes the panel off screen; the window and its loaded page are kept for next time.</summary>
    void Hide();

    /// <summary>
    /// Gives the window up for good, with its page. A group that leaves the project must not leave
    /// an invisible window behind: one per switch of the system would add up over a session.
    /// </summary>
    void Close();

    void BeginDrag();

    /// <summary>Raised after the user moved the window; the caller stores the new offset.</summary>
    event Action<OverlayPoint>? Moved;
}
