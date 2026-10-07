using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using SharpHook;
using SharpHook.Data;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Devices.Platform;
using WhaleGenie.Localization;

namespace WhaleGenie.Views;

/// <summary>
/// A magnifier that follows the pointer and reads the colour of the pixel in the middle of it.
/// Clicking anywhere, or pressing Enter, takes that colour; Esc leaves the colour alone.
/// </summary>
public partial class ColorPickerWindow : Window
{
    /// <summary>How many pixels around the pointer are enlarged, and by how much.</summary>
    private const int RegionSize = 21;
    private const int Zoom = 12;

    /// <summary>How far from the pointer the magnifier sits, so it never covers its own target.</summary>
    private const int Offset = 28;

    private readonly IScreenDevice _screen = new WindowsScreenDevice();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(40) };

    private Image? _preview;
    private Border? _target;
    private Border? _chip;
    private TextBlock? _colourText;
    private TextBlock? _rgbText;
    private TextBlock? _pointText;

    private IGlobalHook? _hook;
    private WriteableBitmap? _bitmap;
    private PixelColor _colour;
    private bool _done;

    public ColorPickerWindow()
    {
        InitializeComponent();

        Title = Strings.Get("Picker.Title");
        _preview = this.FindControl<Image>("Preview");
        _target = this.FindControl<Border>("Target");
        _chip = this.FindControl<Border>("ColourChip");
        _colourText = this.FindControl<TextBlock>("ColourText");
        _rgbText = this.FindControl<TextBlock>("RgbText");
        _pointText = this.FindControl<TextBlock>("PointText");

        _timer.Tick += (_, _) => Refresh();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);

        // Placed before the window appears, so it never sits under the pointer for a frame.
        Place(WindowsScreenDevice.CursorPosition());
    }

    /// <summary>
    /// Shows the magnifier over <paramref name="owner"/> and reports the colour picked. The owner
    /// is put out of the way for as long as the picker is up, because the colour being looked for
    /// is usually behind the window that asked for it.
    /// </summary>
    public static async Task<PixelColor?> PickAsync(Window owner)
    {
        var previous = owner.WindowState;
        owner.WindowState = WindowState.Minimized;

        try
        {
            return await new ColorPickerWindow().ShowDialogOver<PixelColor?>(owner);
        }
        finally
        {
            owner.WindowState = previous == WindowState.Maximized
                ? WindowState.Maximized
                : WindowState.Normal;
            owner.Activate();
        }
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        Activate();
        Refresh();
        _timer.Start();
        AttachHook();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        _done = true;
        _timer.Stop();
        DetachHook();
        _preview = null;
        _bitmap?.Dispose();
        _bitmap = null;
    }

    /// <summary>Reads the pointer, redraws the magnifier and moves it along with the pointer.</summary>
    private void Refresh()
    {
        var point = WindowsScreenDevice.CursorPosition();
        _colour = ReadColour(point);

        DrawRegion(point);
        UpdateReadout(point, _colour);
        Place(point);
    }

    private PixelColor ReadColour(ScreenPoint point)
    {
        try
        {
            return _screen.PixelAt(point.X, point.Y);
        }
        catch (Exception)
        {
            // The pointer can leave the primary screen, which the screen device cannot read.
            return default;
        }
    }

    /// <summary>Copies the pixels around the pointer and draws them enlarged.</summary>
    private void DrawRegion(ScreenPoint point)
    {
        if (_preview is null)
        {
            return;
        }

        var size = _screen.PrimarySize;
        var half = RegionSize / 2;
        var originX = Math.Clamp(point.X - half, 0, Math.Max(0, size.Width - RegionSize));
        var originY = Math.Clamp(point.Y - half, 0, Math.Max(0, size.Height - RegionSize));

        ImageFrame frame;
        try
        {
            frame = _screen.Capture(originX, originY, RegionSize, RegionSize);
        }
        catch (Exception)
        {
            return;
        }

        if (frame.IsEmpty)
        {
            return;
        }

        var bitmap = new WriteableBitmap(new PixelSize(RegionSize, RegionSize),
            new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);

        var rows = Math.Min(RegionSize, frame.Height);
        var width = Math.Min(RegionSize, frame.Width);
        using (var buffer = bitmap.Lock())
        {
            for (var row = 0; row < rows; row++)
            {
                Marshal.Copy(frame.Bgra, row * frame.Width * 4,
                    IntPtr.Add(buffer.Address, row * buffer.RowBytes), width * 4);
            }
        }

        var previous = _bitmap;
        _bitmap = bitmap;
        _preview.Source = bitmap;
        previous?.Dispose();

        // The crosshair marks the pointer's own pixel, which is not the middle when the patch
        // had to be pushed back onto the screen at an edge.
        if (_target is not null)
        {
            Canvas.SetLeft(_target, Math.Clamp(point.X - originX, 0, RegionSize - 1) * Zoom);
            Canvas.SetTop(_target, Math.Clamp(point.Y - originY, 0, RegionSize - 1) * Zoom);
        }
    }

    private void UpdateReadout(ScreenPoint point, PixelColor colour)
    {
        if (_chip is not null)
        {
            _chip.Background = new SolidColorBrush(Color.FromRgb(colour.R, colour.G, colour.B));
        }

        if (_colourText is not null)
        {
            _colourText.Text = colour.ToHex();
        }

        if (_rgbText is not null)
        {
            _rgbText.Text = $"R {colour.R}   G {colour.G}   B {colour.B}";
        }

        if (_pointText is not null)
        {
            _pointText.Text = Strings.Format("Picker.Position", point.X, point.Y);
        }
    }

    /// <summary>Puts the magnifier beside the pointer, kept inside the screen.</summary>
    private void Place(ScreenPoint point)
    {
        var scale = RenderScaling <= 0 ? 1 : RenderScaling;
        var width = (int)Math.Ceiling(Bounds.Width * scale);
        var height = (int)Math.Ceiling(Bounds.Height * scale);
        var size = _screen.PrimarySize;

        var x = point.X + Offset;
        var y = point.Y + Offset;

        if (width > 0 && x + width > size.Width)
        {
            x = point.X - width - Offset;
        }

        if (height > 0 && y + height > size.Height)
        {
            y = point.Y - height - Offset;
        }

        x = Math.Clamp(x, 0, Math.Max(0, size.Width - width));
        y = Math.Clamp(y, 0, Math.Max(0, size.Height - height));

        Position = new PixelPoint(x, y);
    }

    /// <summary>Whether a screen point falls on the magnifier window itself.</summary>
    private bool Covers(ScreenPoint point)
    {
        var scale = RenderScaling <= 0 ? 1 : RenderScaling;
        var width = (int)Math.Ceiling(Bounds.Width * scale);
        var height = (int)Math.Ceiling(Bounds.Height * scale);

        return point.X >= Position.X && point.X < Position.X + width
            && point.Y >= Position.Y && point.Y < Position.Y + height;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                Finish(null);
                break;

            case Key.Enter:
            case Key.Space:
                e.Handled = true;
                Finish(_colour);
                break;

            case Key.C when e.KeyModifiers.HasFlag(KeyModifiers.Alt):
                e.Handled = true;
                Finish(_colour);
                break;

            case Key.Left:
            case Key.Right:
            case Key.Up:
            case Key.Down:
                e.Handled = true;
                Nudge(e.Key, e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1);
                break;
        }
    }

    /// <summary>
    /// Moves the pointer a pixel at a time, so a colour can be taken from a spot a mouse cannot
    /// be settled on exactly. Holding Shift moves ten pixels at a time for the longer trips.
    /// </summary>
    private void Nudge(Key key, int step)
    {
        var point = WindowsScreenDevice.CursorPosition();
        var (dx, dy) = key switch
        {
            Key.Left => (-step, 0),
            Key.Right => (step, 0),
            Key.Up => (0, -step),
            _ => (0, step),
        };

        if (!OperatingSystem.IsWindows() || !SetCursorPos(point.X + dx, point.Y + dy))
        {
            return;
        }

        Refresh();
    }

    /// <summary>
    /// The click is swallowed, so the program the colour is being read from never sees it. The
    /// hook runs its handlers inline, which is what lets the event be suppressed.
    /// </summary>
    private void OnMousePressed(object? sender, MouseHookEventArgs e)
    {
        if (e.Data.Button != SharpHook.Data.MouseButton.Button1)
        {
            return;
        }

        var point = new ScreenPoint(e.Data.X, e.Data.Y);
        if (Covers(point))
        {
            return;   // the magnifier itself was clicked; leave that click alone
        }

        e.SuppressEvent = true;
        var colour = ReadColour(point);
        Dispatcher.UIThread.Post(() => Finish(colour));
    }

    private void Finish(PixelColor? colour)
    {
        if (_done)
        {
            return;
        }

        _done = true;
        _timer.Stop();
        DetachHook();
        Close(colour);
    }

    private void AttachHook()
    {
        try
        {
            var hook = GlobalInputHook.Shared.Hook;
            hook.MousePressed += OnMousePressed;
            _hook = hook;
        }
        catch (Exception)
        {
            // Without a hook the keyboard still takes the colour.
            _hook = null;
        }
    }

    private void DetachHook()
    {
        if (_hook is { } hook)
        {
            hook.MousePressed -= OnMousePressed;
            _hook = null;
        }
    }

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);
}
