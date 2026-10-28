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
using WhaleGenie.Core.Devices;
using WhaleGenie.Execution;
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

    /// <summary>The screen the magnifier reads, the pointer it nudges, and the hook it listens on.</summary>
    private readonly IScreenDevice _screen;

    private readonly WhaleGenie.Core.Devices.IInputDevice _input;
    private readonly IInputHook _hookSource;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(40) };

    private Image? _preview;
    private Border? _target;
    private Border? _chip;
    private TextBlock? _colourText;
    private TextBlock? _rgbText;
    private TextBlock? _pointText;

    private IInputHook? _hook;
    private WriteableBitmap? _bitmap;
    private PixelColor _colour;
    private ScreenPoint _point;
    private bool _done;

    /// <summary>
    /// The colour the magnifier is reading now, which is what a check about a stand-in screen
    /// reads back: the window itself only ever shows it.
    /// </summary>
    internal PixelColor Colour => _colour;

    /// <summary>What the magnifier was left with, or null when it was left without a colour. The
    /// picker stands on its own, so there is no dialog result to carry this.</summary>
    internal Pick? Picked { get; private set; }

    /// <summary>
    /// What taking a colour leaves behind: the colour that was read and the place it was read from.
    /// The two travel together, because the magnifier is aimed at the pixel rather than typed at it
    /// — a step that watches one pixel wants both, and where it is was already answered by the aim.
    /// </summary>
    public readonly record struct Pick(PixelColor Colour, ScreenPoint At);

    public ColorPickerWindow()
        : this(SharedDevices.Current.Screen, SharedDevices.Current.Input, SharedDevices.Current.Hook)
    {
    }

    /// <summary>
    /// The magnifier over just what it reads: the screen it magnifies, the pointer it moves and the
    /// key hook it listens on. Taking those three rather than the whole device layer says what this
    /// window needs, and lets a stand-in be three things instead of twenty.
    /// </summary>
    internal ColorPickerWindow(IScreenDevice screen,
        WhaleGenie.Core.Devices.IInputDevice input, IInputHook hook)
    {
        _screen = screen;
        _input = input;
        _hookSource = hook;
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
        Place(_screen.Cursor);
    }

    /// <summary>
    /// Shows the magnifier over <paramref name="owner"/> and reports what was picked. The owner
    /// steps out of the way for as long as the magnifier is up, and so does everything else this
    /// program has on the screen: the colour being looked for is usually behind one of them.
    /// </summary>
    public static async Task<Pick?> PickAsync(Window owner, IScreenDevice screen,
        WhaleGenie.Core.Devices.IInputDevice input, IInputHook hook)
    {
        using var aside = AppWindows.StepAside(owner);
        return await new ColorPickerWindow(screen, input, hook).TakeAsync();
    }

    /// <summary>
    /// Stands the magnifier on its own — nothing else of this program is on the screen to own it —
    /// and reports what it was answered with, or null when it was left without a colour.
    /// </summary>
    internal async Task<Pick?> TakeAsync()
    {
        await AppWindows.ShowAndWaitAsync(this);
        return Picked;
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
        // The place is kept beside the colour rather than read again later: the two have to answer
        // for the same pixel, and the pointer may have moved on by then.
        _point = _screen.Cursor;
        _colour = ReadColour(_point);

        DrawRegion(_point);
        UpdateReadout(_point, _colour);
        Place(_point);
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

        var desk = _screen.Desktop.Bounds;
        var half = RegionSize / 2;
        var originX = Math.Clamp(point.X - half, desk.Left, Math.Max(desk.Left, desk.Right - RegionSize));
        var originY = Math.Clamp(point.Y - half, desk.Top, Math.Max(desk.Top, desk.Bottom - RegionSize));

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
        var desk = _screen.Desktop.Bounds;

        var x = point.X + Offset;
        var y = point.Y + Offset;

        if (width > 0 && x + width > desk.Right)
        {
            x = point.X - width - Offset;
        }

        if (height > 0 && y + height > desk.Bottom)
        {
            y = point.Y - height - Offset;
        }

        x = Math.Clamp(x, desk.Left, Math.Max(desk.Left, desk.Right - width));
        y = Math.Clamp(y, desk.Top, Math.Max(desk.Top, desk.Bottom - height));

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
                Finish(null, default);
                break;

            case Key.Enter:
            case Key.Space:
                e.Handled = true;
                Finish(_colour, _point);
                break;

            case Key.C when e.KeyModifiers.HasFlag(KeyModifiers.Alt):
                e.Handled = true;
                Finish(_colour, _point);
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
    internal void Nudge(Key key, int step)
    {
        var point = _screen.Cursor;
        var (dx, dy) = key switch
        {
            Key.Left => (-step, 0),
            Key.Right => (step, 0),
            Key.Up => (0, -step),
            _ => (0, step),
        };

        // The pointer is moved through the same device a macro's mouse step uses, so the picker
        // and a run move it the same way, and a layer handed in by a check writes it down rather
        // than moving anything.
        try
        {
            _input.MoveMouse(point.X + dx, point.Y + dy, 0);
        }
        catch (Exception)
        {
            return;
        }

        Refresh();
    }

    /// <summary>
    /// The click is swallowed, so the program the colour is being read from never sees it. The
    /// hook runs its handlers inline, which is what lets the event be suppressed.
    /// </summary>
    private void OnMouseDown(MouseHookEvent e)
    {
        if (e.Button != HookMouseButton.Left)
        {
            return;
        }

        var point = new ScreenPoint(e.X, e.Y);
        if (Covers(point))
        {
            return;   // the magnifier itself was clicked; leave that click alone
        }

        e.Suppress = true;
        var colour = ReadColour(point);
        Dispatcher.UIThread.Post(() => Finish(colour, point));
    }

    private void Finish(PixelColor? colour, ScreenPoint at)
    {
        if (_done)
        {
            return;
        }

        _done = true;
        Picked = colour is { } read ? new Pick(read, at) : null;
        _timer.Stop();
        DetachHook();
        Close();
    }

    private void AttachHook()
    {
        try
        {
            var hook = _hookSource;
            hook.MouseDown += OnMouseDown;
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
            hook.MouseDown -= OnMouseDown;
            _hook = null;
        }
    }
}
