using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;

namespace WhaleGenie.Views;

/// <summary>
/// Outlines a place on the screen for a moment and then goes away. It is what the "test" button
/// next to a picker puts up: the question after picking is "did that take the right thing?", and
/// seeing the frame land on it answers that without running the macro.
/// </summary>
/// <remarks>
/// Only the border is on screen — the middle is cut out — so the element inside stays readable
/// while it flashes, which is the whole point of showing it. It never takes the focus and never
/// stays: a frame left behind would be an overlay the user has to get rid of.
/// </remarks>
internal sealed class ElementFlashWindow : Window
{
    /// <summary>How thick the frame is, in screen pixels.</summary>
    private const int Frame = 3;

    /// <summary>How long the frame stays up.</summary>
    private static readonly TimeSpan Life = TimeSpan.FromMilliseconds(1500);

    private readonly ScreenPoint _location;
    private readonly ScreenSize _size;
    private readonly DispatcherTimer _timer = new() { Interval = Life };
    private readonly TaskCompletionSource _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private ElementFlashWindow(ScreenPoint location, ScreenSize size)
    {
        _location = location;
        _size = size;

        Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x84, 0xFF));
        WindowDecorations = WindowDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Title = Strings.Get("Main.Title");

        _timer.Tick += (_, _) => Close();
    }

    /// <summary>Completes when the frame is off the screen, whether it timed out or was closed.</summary>
    public Task Finished => _finished.Task;

    /// <summary>
    /// Finds what the selector names — through the desktop's UI Automation — and flashes where it
    /// is. Null when nothing is there, which is the answer the dialog reports rather than a frame
    /// over the wrong thing. The window filter narrows the search the same way the step's own
    /// window field does.
    /// </summary>
    public static UiElementInfo? Locate(IUiDevice? ui, string selector, string window)
    {
        var wanted = (selector ?? string.Empty).Trim();
        if (ui is null || wanted.Length == 0)
        {
            return null;
        }

        try
        {
            var query = UiQuery.Parse(wanted, string.IsNullOrWhiteSpace(window) ? null : window);
            return ui.FindAll(query, 1).FirstOrDefault();
        }
        catch (Exception)
        {
            // A control that cannot be looked up right now is an answer too: the dialog says
            // nothing matched rather than failing over it.
            return null;
        }
    }

    /// <summary>
    /// Flashes the rectangle an element occupies, or answers null when there is nothing there to
    /// show. The caller owns the window from then on and can wait on <see cref="Finished"/>.
    /// </summary>
    public static ElementFlashWindow? Show(UiElementInfo element)
    {
        if (element.Size.Width <= 0 || element.Size.Height <= 0)
        {
            return null;
        }

        var flash = new ElementFlashWindow(element.Location, element.Size);
        flash.Show();
        return flash;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        Fit();
        _timer.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _timer.Stop();
        _finished.TrySetResult();
    }

    /// <summary>
    /// Puts the frame around the element's rectangle. The rectangle comes back in screen pixels
    /// and a window is sized in ones of its own, so the scaling is what the two are translated
    /// through — the same conversion the element picker's own frame makes.
    /// </summary>
    private void Fit()
    {
        var scaling = RenderScaling <= 0 ? 1 : RenderScaling;
        var width = _size.Width + (Frame * 2);
        var height = _size.Height + (Frame * 2);

        Position = new PixelPoint(_location.X - Frame, _location.Y - Frame);
        Width = width / scaling;
        Height = height / scaling;

        WindowHole.Cut(this, width, height, Frame);
    }
}
