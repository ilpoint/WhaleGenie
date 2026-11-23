using System;
using WhaleGenie.Bridge;
using WhaleGenie.Contracts;
using WhaleGenie.Runtime;

namespace WhaleGenie.Web;

/// <summary>One wheel window, built once and shown at the pointer when the gesture opens it.</summary>
internal sealed class WheelOverlayHost(BridgeServer bridge, WheelSurfaceBridge wheel) : IWheelOverlay
{
    private WebOverlayWindow? _window;

    public event Action? Hidden;

    public bool IsVisible => _window?.IsVisible == true;

    public void Show()
    {
        _window ??= new WebOverlayWindow(bridge, BridgeSurface.Wheel, "overlay/wheel", 440, 440);
        _window.Show();
        _window.FocusPage();
    }

    public void SetAim(SurfaceWheelAim? aim) => wheel.PushAim(aim);

    public void Hide()
    {
        if (_window is not { IsVisible: true } window)
        {
            return;
        }

        window.Hide();
        Hidden?.Invoke();
    }
}
