using System;

namespace WhaleGenie.Runtime;

/// <summary>
/// The wheel as an overlay window: shown at the pointer while the gesture is live, hidden when it
/// ends. The gesture and the hit test stay in C#; the window only draws what it is told.
/// </summary>
public interface IWheelOverlay
{
    bool IsVisible { get; }

    void Show();

    void Hide();

    /// <summary>Raised whenever the overlay goes away, whichever path closed it.</summary>
    event Action? Hidden;
}
