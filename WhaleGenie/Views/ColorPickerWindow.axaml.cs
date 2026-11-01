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
using WhaleGenie.Localization;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;

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

    /// <summary>The magnifier's size until it has been measured, as written in its markup.</summary>
    private const double PanelWidth = 292;

    private const double PanelHeight = 452;

    /// <summary>The screen the magnifier reads, the pointer it nudges, and the hook it listens on.</summary>
    private readonly IScreenDevice _screen;

    private readonly WhaleGenie.Core.Devices.IInputDevice _input;
    private readonly IInputHook _hookSource;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(40) };

    private Image? _preview;
    private Image? _desk;
    private Canvas? _surface;
    private Border? _panel;
    private Border? _target;
    private Border? _chip;
    private TextBlock? _colourText;
    private TextBlock? _rgbText;
    private TextBlock? _pointText;

    private IInputHook? _hook;
    private WriteableBitmap? _bitmap;
    private WriteableBitmap? _deskPicture;
    private PixelPoint _origin;
    private double _scaling = 1;

    /// <summary>
    /// Where the magnifier sits on the screen, kept up to date as it is placed. The click that takes
    /// the colour arrives on the hook's own thread, and a control's bounds read from there is a
    /// window's visual tree touched by the wrong thread — the crash that made every click during a
    /// pick take the program with it. Numbers cached on the drawing thread are what that handler is
    /// allowed to see.
    /// </summary>
    private PixelRect _panelAt;
    private PixelColor _colour;
    private ScreenPoint _point;
    private bool _done;

    /// <summary>
    /// The desk as it was when the magnifier opened, when the screen is being held still, and where
    /// that frame begins. Read from instead of the live screen: a colour is taken from what was on
    /// screen at the moment the picker came up, and the things behind it are not read again — a
    /// moving window does not turn the magnifier into a different colour between two moves of the
    /// pointer, and the pixels stay where they were seen.
    /// </summary>
    private ImageFrame? _frozen;

    private int _frozenLeft;
    private int _frozenTop;

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
        _desk = this.FindControl<Image>("Desk");
        _surface = this.FindControl<Canvas>("Surface");
        _panel = this.FindControl<Border>("Panel");
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

        // Waited for before the screen is read: hiding a window is one call, but the desktop still
        // holds the frame that had it in it, and a picture taken then is the old screen.
        await AppWindows.SettleAsync();

        var window = new ColorPickerWindow(screen, input, hook);
        window.FreezeDesk(screen);
        return await window.TakeAsync();
    }

    /// <summary>
    /// Takes the desk as a picture and reads the magnifier from that instead of from the live
    /// screen: what the colour is taken from is then the screen as it was when the picker opened,
    /// and nothing that moves afterwards can change what is under the crosshair. A screen that
    /// cannot be captured leaves the magnifier reading live, which is what it did before.
    /// </summary>
    private void FreezeDesk(IScreenDevice screen)
    {
        try
        {
            var desk = screen.Desktop.Bounds;

            // Taken once the screen has stopped changing. The windows this program has went off it a
            // moment ago, and the duplicator behind the capture answers with the frame it still
            // holds until the new one arrives — which is the frame with this program in it.
            var frame = ScreenReady.Capture(screen, desk.Left, desk.Top, desk.Width, desk.Height);
            if (frame.IsEmpty)
            {
                return;
            }

            _frozen = frame;
            _frozenLeft = desk.Left;
            _frozenTop = desk.Top;

            // The picture stands behind the magnifier, so what the eye is aiming at is the same
            // pixels the readout is counting: a colour picked off a screen that has moved on is a
            // colour nobody chose.
            _deskPicture?.Dispose();
            _deskPicture = ImageAssets.ToBitmap(frame);
            if (_desk is not null)
            {
                _desk.Source = _deskPicture;
            }
        }
        catch (Exception)
        {
            _frozen = null;
        }
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

        Cover();
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
        // The pictures are still attached to what is on screen for one more frame. Letting go of
        // them here would hand the renderer a disposed bitmap, so they are dropped from the views
        // first and released once the frame that used them has been drawn.
        _preview = null;
        if (_desk is { } picture)
        {
            picture.Source = null;
        }

        var bitmap = _bitmap;
        var deskPicture = _deskPicture;
        _bitmap = null;
        _deskPicture = null;
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                bitmap?.Dispose();
                deskPicture?.Dispose();
            }
            catch (Exception)
            {
                // A picture the compositor was still holding is not worth taking the program down
                // for; the memory is reclaimed with the window either way.
            }
        }, DispatcherPriority.Background);
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
        if (_frozen is { } frozen)
        {
            var x = point.X - _frozenLeft;
            var y = point.Y - _frozenTop;
            if (x < 0 || y < 0 || x >= frozen.Width || y >= frozen.Height)
            {
                return default;
            }

            var at = ((y * frozen.Width) + x) * 4;
            return new PixelColor(frozen.Bgra[at + 2], frozen.Bgra[at + 1], frozen.Bgra[at]);
        }

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
            frame = _frozen is { } frozen
                ? FrozenRegion(frozen, originX, originY)
                : _screen.Capture(originX, originY, RegionSize, RegionSize);
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

    /// <summary>
    /// The patch around <paramref name="originX"/>,<paramref name="originY"/> taken out of the frozen
    /// frame. What falls outside it stays black, the way a live capture at the edge of the screen
    /// comes back short rather than being stretched.
    /// </summary>
    private ImageFrame FrozenRegion(ImageFrame frame, int originX, int originY)
    {
        var pixels = new byte[RegionSize * RegionSize * 4];

        for (var row = 0; row < RegionSize; row++)
        {
            var y = originY + row - _frozenTop;
            if (y < 0 || y >= frame.Height)
            {
                continue;
            }

            for (var column = 0; column < RegionSize; column++)
            {
                var x = originX + column - _frozenLeft;
                if (x < 0 || x >= frame.Width)
                {
                    continue;
                }

                var from = ((y * frame.Width) + x) * 4;
                var to = ((row * RegionSize) + column) * 4;
                pixels[to] = frame.Bgra[from];
                pixels[to + 1] = frame.Bgra[from + 1];
                pixels[to + 2] = frame.Bgra[from + 2];
                pixels[to + 3] = 0xFF;
            }
        }

        return new ImageFrame(RegionSize, RegionSize, pixels);
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

    /// <summary>
    /// Stretches the window over the whole desk, whatever its scaling is: the picture underneath is
    /// the desk, so a place on the picture and a place in the numbers are the same place. Every
    /// display makes one desk, so a colour on the second one is read as easily as one on the first.
    /// </summary>
    private void Cover()
    {
        var desk = _screen.Desktop.Bounds;
        _origin = new PixelPoint(desk.Left, desk.Top);
        _scaling = RenderScaling <= 0 ? 1 : RenderScaling;

        Width = desk.Width / _scaling;
        Height = desk.Height / _scaling;
        Position = _origin;

        if (_desk is { } picture)
        {
            picture.Width = Width;
            picture.Height = Height;
        }

        if (_surface is { } surface)
        {
            surface.Width = Width;
            surface.Height = Height;
        }
    }

    /// <summary>Puts the magnifier beside the pointer, kept inside the desk.</summary>
    private void Place(ScreenPoint point)
    {
        if (_panel is not { } panel)
        {
            return;
        }

        var local = ToLocal(point);
        var width = panel.Bounds.Width > 0 ? panel.Bounds.Width : PanelWidth;
        var height = panel.Bounds.Height > 0 ? panel.Bounds.Height : PanelHeight;

        var x = local.X + Offset;
        var y = local.Y + Offset;

        if (x + width > Width)
        {
            x = local.X - width - Offset;
        }

        if (y + height > Height)
        {
            y = local.Y - height - Offset;
        }

        var left = Math.Clamp(x, 0, Math.Max(0, Width - width));
        var top = Math.Clamp(y, 0, Math.Max(0, Height - height));
        Canvas.SetLeft(panel, left);
        Canvas.SetTop(panel, top);

        _panelAt = new PixelRect(
            _origin.X + (int)Math.Round(left * _scaling),
            _origin.Y + (int)Math.Round(top * _scaling),
            (int)Math.Round(width * _scaling),
            (int)Math.Round(height * _scaling));
    }

    /// <summary>
    /// Whether a screen point falls on the magnifier itself. Read from the numbers placed on the
    /// drawing thread, never from the control: this is called by the hook, on the hook's thread.
    /// </summary>
    private bool Covers(ScreenPoint point)
        => _panelAt.Contains(new PixelPoint(point.X, point.Y));

    /// <summary>A place on the screen in the window's own units, which is where the panel is drawn.</summary>
    private Point ToLocal(ScreenPoint point)
        => new((point.X - _origin.X) / _scaling, (point.Y - _origin.Y) / _scaling);

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
