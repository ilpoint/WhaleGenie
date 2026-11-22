using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using WhaleGenie.Assets;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;


namespace WhaleGenie.Views;

// Opening it for a drag or for a picture, and what it does once it is up.
public partial class RegionPickerWindow : Window
{
    /// <summary>
    /// The picker that draws a direction rather than a rectangle, reporting the start and end in the
    /// order the pointer visited them.
    /// </summary>
    public static async Task<DragSpan?> PickDragAsync(Window owner, IScreenDevice screen,
        IWindowDevice windows, Region? bounds = null, IInputHook? hook = null)
    {
        using var aside = AppWindows.StepAside(owner);
        await AppWindows.SettleAsync();
        var picker = new RegionPickerWindow(screen, windows, null, bounds, arrow: true, hook: hook);
        _ = await picker.TakeAsync();
        return picker.ConfirmedDrag;
    }

    /// <summary>
    /// The same, and the picture inside the rectangle is kept. What a rectangle is usually dragged
    /// for is a reference picture, and the windows stay away until that picture has been taken: a
    /// window put back in front of where the rectangle was drawn would be in the picture.
    /// </summary>
    public static async Task<string?> CaptureAsync(Window owner, IScreenDevice screen,
        IWindowDevice windows, string folder, Region? bounds = null, IInputHook? hook = null)
    {
        using var aside = AppWindows.StepAside(owner);
        await AppWindows.SettleAsync();

        if (await new RegionPickerWindow(screen, windows, null, bounds, hook: hook).TakeAsync()
            is not { } region)
        {
            return null;
        }

        // The screen is asked what it is showing, and it answers with the last frame it drew — the
        // one this picker was drawn on. Waiting for the desktop to be drawn again is what keeps the
        // picker's own overlay out of the picture.
        await AppWindows.SettleAsync();

        return BitmapImages.Capture(screen, region.X, region.Y, region.Width, region.Height,
            string.IsNullOrWhiteSpace(folder) ? ImageAssets.DefaultFolder() : folder);
    }

    /// <summary>
    /// Stands the picker on its own — nothing else of this program is on the screen to own it — and
    /// reports the rectangle the user answered with, or null when it was left without one.
    /// </summary>
    internal async Task<Region?> TakeAsync()
    {
        TakeDesk();
        if (ClampRegion(_initial) is { } initial)
        {
            _start = new PixelPoint(initial.X, initial.Y);
            _end = new PixelPoint(initial.X + initial.Width, initial.Y + initial.Height);
        }
        await AppWindows.ShowAndWaitAsync(this);
        return Confirmed;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        Cover();
        Activate();
        Focus();
        AttachHook();
        var at = _screen.Cursor;
        _pointer = PointIn(new PixelPoint(at.X, at.Y));
        Update();
    }
}
