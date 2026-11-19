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
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;


namespace WhaleGenie.Views;

// Putting the desk back and handing over what was framed.
public partial class RegionPickerWindow : Window
{
    /// <summary>The picture belongs to this picker, so it goes when the picker does.</summary>
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        DetachHook();

        if (!_done)
        {
            RestoreWindow();
        }

        _picture?.Dispose();
        _picture = null;
        _magnifier.Release();
    }

    private void Finish(Region? region)
    {
        if (_done)
        {
            return;
        }

        _done = true;
        Confirmed = region;
        ConfirmedDrag = _arrow && region is not null
            ? new DragSpan(_start.X, _start.Y, _end.X, _end.Y)
            : null;
        if (region is null)
        {
            RestoreWindow();
        }

        Close();
    }

    /// <summary>Esc or closing the picker leaves the mover's window where it was before the drag.</summary>
    private void RestoreWindow()
    {
        if (_liveWindow == 0 || _liveOriginal is not { } original)
        {
            return;
        }

        try
        {
            _windows.Move(_liveWindow, original.X, original.Y, original.Width,
                original.Height);
        }
        catch (Exception)
        {
            // The window has gone; there is nothing left to put back.
        }
    }
}
