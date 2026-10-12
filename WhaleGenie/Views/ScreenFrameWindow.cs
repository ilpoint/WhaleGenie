using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;

namespace WhaleGenie.Views;

/// <summary>
/// A frame drawn around a place on the screen, three pixels thick and hollow in the middle. It is
/// what answers "where is this?" without running anything: the frame lands on the thing and the
/// answer is plain to see.
/// </summary>
/// <remarks>
/// Several may stand at once and one stays until whoever put it up takes it down, because a place
/// worth showing once is usually worth watching for a while — the windows a rule recognises as it
/// is written, the window a run is reading right now. Only the border belongs to the frame: the
/// middle is cut away with a window region, so what is inside stays readable and stays clickable
/// while the frame is up, and the frame itself never takes the focus. A momentary flash, which is
/// what the button beside a picker does, is the same frame with a clock on it.
/// </remarks>
internal sealed class ScreenFrameWindow : Window
{
    /// <summary>How thick the frame is, in screen pixels.</summary>
    private const int Frame = 3;

    private readonly DispatcherTimer? _life;
    private readonly TaskCompletionSource _finished =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private ScreenPoint _location;
    private ScreenSize _size;

    private ScreenFrameWindow(ScreenPoint location, ScreenSize size, TimeSpan? living)
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

        if (living is { } span)
        {
            _life = new DispatcherTimer { Interval = span };
            _life.Tick += (_, _) => Close();
        }
    }

    /// <summary>Completes when the frame is off the screen, whether it timed out or was closed.</summary>
    public Task Finished => _finished.Task;

    /// <summary>
    /// Puts a frame around a place and leaves it there. Null when the place measures to nothing:
    /// there is no rectangle to draw around.
    /// </summary>
    public static ScreenFrameWindow? Show(ScreenPoint location, ScreenSize size)
        => Open(location, size, null);

    /// <summary>
    /// The same frame with a clock on it: it goes away by itself after <paramref name="living"/>,
    /// which is what an answer worth a moment and not an overlay to get rid of looks like.
    /// </summary>
    public static ScreenFrameWindow? Flash(ScreenPoint location, ScreenSize size, TimeSpan living)
        => Open(location, size, living);

    /// <summary>
    /// Moves a frame that is already up to another place, so a frame following something around does
    /// not blink out and back — which is noticeable, and reads as the thing it stands for going away.
    /// </summary>
    public void Place(ScreenPoint location, ScreenSize size)
    {
        _location = location;
        _size = size;
        Fit();
    }

    private static ScreenFrameWindow? Open(ScreenPoint location, ScreenSize size, TimeSpan? living)
    {
        if (size.Width <= 0 || size.Height <= 0)
        {
            return null;
        }

        var frame = new ScreenFrameWindow(location, size, living);
        frame.Show();
        return frame;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        Fit();
        _life?.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _life?.Stop();
        _finished.TrySetResult();
    }

    /// <summary>
    /// Puts the frame around the rectangle. The rectangle comes back in screen pixels and a window
    /// is sized in ones of its own, so the scaling is what the two are translated through — the
    /// same conversion the element picker's own frame makes.
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
