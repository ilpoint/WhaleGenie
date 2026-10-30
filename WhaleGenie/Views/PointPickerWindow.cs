using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Runtime;

namespace WhaleGenie.Views;

/// <summary>
/// One frozen-desktop overlay with the crosshair, readout, UIA outline and shortcut hint drawn in
/// the same native window. Window-based picking only shows the crosshair inside the target client.
/// </summary>
public sealed class PointPickerWindow : Window
{
    private static readonly HookKey F1 = KeyNames.Resolve("F1") ?? default;
    private static readonly HookKey Escape = KeyNames.Resolve("Esc") ?? default;

    private readonly IScreenDevice _screen;
    private readonly IWindowDevice _windows;
    private readonly IUiDevice _ui;
    private readonly WindowRule? _target;
    private readonly IInputHook? _hookSource;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };

    private readonly Canvas _overlay = new() { Name = "Overlay", IsHitTestVisible = false };
    private readonly Canvas _crosshair = new() { Name = "Crosshair", IsVisible = false };
    private readonly Border _vertical = new() { Width = 1, Height = 34 };
    private readonly Border _horizontal = new() { Width = 34, Height = 1 };
    private readonly Border _outline = new()
    {
        Name = "ElementOutline",
        BorderThickness = new Thickness(2),
        Background = Brushes.Transparent,
        IsHitTestVisible = false,
        IsVisible = false,
    };
    private readonly Border _readout;
    private readonly TextBlock _readoutText = new()
    {
        Name = "ReadoutText",
        Foreground = Brushes.White,
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
    };
    private readonly Image _desk = new() { Stretch = Stretch.Fill, IsHitTestVisible = false };
    private WriteableBitmap? _picture;
    private IInputHook? _hook;
    private WindowInfo? _window;
    private PixelPoint _origin;
    private ScreenSize _deskSize;
    private double _scaling = 1;
    private bool _insideClient;
    private bool _targetLost;
    private bool _opened;
    private bool _done;
    private bool _swallowF1Up;
    private bool _swallowEscapeUp;
    private ScreenPoint? _pendingPoint;

    /// <summary>
    /// The crosshair over just what it reads: the screen it draws from, the windows it measures
    /// against and the element under the pointer. Taking those four rather than the whole device
    /// layer says what this picker needs.
    /// </summary>
    internal PointPickerWindow(IScreenDevice screen, IWindowDevice windows, IUiDevice ui,
        IInputHook hook, WindowRule? target)
    {
        _screen = screen;
        _windows = windows;
        _ui = ui;
        _target = target;
        _hookSource = hook;

        Title = Strings.Get("PointPicker.Title");
        WindowDecorations = WindowDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Background = Brushes.Transparent;

        _vertical.Background = Brushes.OrangeRed;
        _horizontal.Background = Brushes.OrangeRed;
        _crosshair.Width = 34;
        _crosshair.Height = 34;
        _crosshair.Children.Add(_vertical);
        _crosshair.Children.Add(_horizontal);

        // 拾取器可见性色：要在任意截图底色上可见，故不随主题（判定为字面，不进令牌）。
        _outline.BorderBrush = new SolidColorBrush(Color.FromRgb(0x0A, 0x84, 0xFF));
        _readout = new Border
        {
            Name = "Readout",
            Background = new SolidColorBrush(Color.FromRgb(0x11, 0x25, 0x38)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x2F, 0x92, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(9, 6),
            MaxWidth = 420,
            IsHitTestVisible = false,
            Child = _readoutText,
        };

        _overlay.Children.Add(_outline);
        _overlay.Children.Add(_crosshair);
        _overlay.Children.Add(_readout);

        var root = new Grid();
        root.Children.Add(_desk);
        root.Children.Add(_overlay);
        Content = root;

        _timer.Tick += (_, _) => Follow();
    }

    internal ScreenPoint? Chosen { get; private set; }

    public static async Task<ScreenPoint?> PickAsync(Window owner, IScreenDevice screen,
        IWindowDevice windows, IUiDevice ui, IInputHook hook, WindowRule? target = null)
    {
        using var aside = AppWindows.StepAside(owner);
        return await new PointPickerWindow(screen, windows, ui, hook, target).TakeAsync();
    }

    internal async Task<ScreenPoint?> TakeAsync()
    {
        TakeDesk();
        await AppWindows.ShowAndWaitAsync(this);
        return Chosen;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _opened = true;
        Cover();
        PassThrough(this);
        MacroTriggerGate.Enter();
        AttachHook();
        Follow();
        _timer.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _done = true;
        _timer.Stop();
        DetachHook();
        _picture?.Dispose();
        MacroTriggerGate.Exit();
    }

    /// <summary>
    /// Freezes the desktop before this overlay is shown. The overlay remains full-desktop in both
    /// modes; the window-client rectangle decides where the crosshair and UIA outline are drawn.
    /// </summary>
    private void TakeDesk()
    {
        var (origin, size) = DeskBounds();
        _origin = origin;
        _deskSize = size;
        if (size.Width <= 0 || size.Height <= 0)
        {
            return;
        }

        ScreenComposed.Wait();

        ImageFrame frame;
        try
        {
            frame = _screen.Capture(origin.X, origin.Y, size.Width, size.Height);
        }
        catch (Exception)
        {
            return;
        }

        if (frame.IsEmpty || frame.Width != size.Width || frame.Height != size.Height)
        {
            return;
        }

        var picture = new WriteableBitmap(new PixelSize(frame.Width, frame.Height),
            new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using (var buffer = picture.Lock())
        {
            for (var row = 0; row < frame.Height; row++)
            {
                Marshal.Copy(frame.Bgra, row * frame.Width * 4,
                    IntPtr.Add(buffer.Address, row * buffer.RowBytes), frame.Width * 4);
            }
        }

        _picture?.Dispose();
        _picture = picture;
        _desk.Source = picture;
    }

    private void Cover()
    {
        var (origin, size) = DeskBounds();
        _origin = origin;
        _deskSize = size;
        _scaling = RenderScaling <= 0 ? 1 : RenderScaling;

        Width = size.Width / _scaling;
        Height = size.Height / _scaling;
        Position = origin;
        _desk.Width = Width;
        _desk.Height = Height;
        _overlay.Width = Width;
        _overlay.Height = Height;
        _crosshair.Width = Width;
        _crosshair.Height = Height;
    }

    private (PixelPoint Origin, ScreenSize Size) DeskBounds()
    {
        if (Screens is { All.Count: > 0 } screens)
        {
            var left = screens.All.Min(screen => screen.Bounds.X);
            var top = screens.All.Min(screen => screen.Bounds.Y);
            var right = screens.All.Max(screen => screen.Bounds.X + screen.Bounds.Width);
            var bottom = screens.All.Max(screen => screen.Bounds.Y + screen.Bounds.Height);
            return (new PixelPoint(left, top), new ScreenSize(right - left, bottom - top));
        }

        var desk = _screen.Desktop.Bounds;
        return (new PixelPoint(desk.Left, desk.Top), new ScreenSize(desk.Width, desk.Height));
    }

    /// <summary>
    /// Input, UIA hit-testing and element lookup all pass through the frozen picture. Only the
    /// crosshair is a reading; it must not make the target window lose focus.
    /// </summary>
    private static void PassThrough(Window window)
    {
        if (!OperatingSystem.IsWindows() || window.TryGetPlatformHandle() is not { } handle)
        {
            return;
        }

        var style = GetWindowLongPtr(handle.Handle, GwlExStyle).ToInt64();
        SetWindowLongPtr(handle.Handle, GwlExStyle,
            new IntPtr(style | WsExTransparent | WsExNoActivate | WsExToolWindow));
    }

    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x00000020;
    private const long WsExNoActivate = 0x08000000;
    private const long WsExToolWindow = 0x00000080;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);

    private void Follow()
    {
        if (_done)
        {
            return;
        }

        if (_opened && !IsVisible)
        {
            Finish(null);
            return;
        }

        var at = _screen.Cursor;
        _insideClient = false;
        _targetLost = false;
        _window = null;

        ClientArea? client = null;
        if (_target is { HasTarget: true } rule)
        {
            try
            {
                _window = _windows.Find(rule.MatchValue, rule.Reading.Match,
                    rule.Reading.Compare, rule.Options);
            }
            catch (Exception)
            {
                _window = null;
            }

            if (_window is { } window)
            {
                try
                {
                    client = new ClientArea(_windows.ClientOrigin(window.Handle),
                        _windows.ClientSize(window.Handle));
                    _insideClient = client.Value.Contains(at);
                }
                catch (Exception)
                {
                    _window = null;
                }
            }

            _targetLost = _window is null;
        }

        var showCrosshair = _target is null || _insideClient;
        DrawCrosshair(at, client, showCrosshair);
        var element = DrawOutline(at, client, showCrosshair);
        UpdateReadout(at, client, _insideClient, element);
        PlaceReadout(at);
    }

    private void DrawCrosshair(ScreenPoint at, ClientArea? client, bool show)
    {
        if (!show)
        {
            _crosshair.IsVisible = false;
            return;
        }

        _crosshair.Clip = client is { } area
            ? new RectangleGeometry(LocalRect(area))
            : null;
        var local = ToLocal(at);
        Canvas.SetLeft(_vertical, local.X - 0.5);
        Canvas.SetTop(_vertical, local.Y - 17);
        Canvas.SetLeft(_horizontal, local.X - 17);
        Canvas.SetTop(_horizontal, local.Y - 0.5);
        _crosshair.IsVisible = true;
    }

    private string? DrawOutline(ScreenPoint at, ClientArea? client, bool show)
    {
        if (!show)
        {
            _outline.IsVisible = false;
            return null;
        }

        UiElementInfo? info;
        try
        {
            info = _ui.ElementAt(at.X, at.Y);
        }
        catch (Exception)
        {
            info = null;
        }

        if (info is null || info.Size.Width <= 0 || info.Size.Height <= 0)
        {
            _outline.IsVisible = false;
            return null;
        }

        var bounds = client is { } area
            ? area.Intersect(info.Location, info.Size)
            : Intersect(new ScreenPoint(_origin.X, _origin.Y), _deskSize,
                info.Location, info.Size);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            _outline.IsVisible = false;
            return null;
        }

        var local = ScreenRectToLocal(bounds);
        Canvas.SetLeft(_outline, local.X);
        Canvas.SetTop(_outline, local.Y);
        _outline.Width = local.Width;
        _outline.Height = local.Height;
        _outline.IsVisible = true;

        var name = info.Name.Length > 0
            ? Strings.Format("PointPicker.Element", info.ControlType, info.Name)
            : info.ControlType;
        return name.Length > 0 ? name : null;
    }

    private void UpdateReadout(ScreenPoint at, ClientArea? client, bool inside, string? element)
    {
        if (_target is null)
        {
            _readoutText.Text = Strings.Format("PointPicker.ReadoutScreen", at.X, at.Y);
            _readoutText.Text += Environment.NewLine + Strings.Get("PointPicker.ShortcutScreen");
            return;
        }

        if (_targetLost)
        {
            _readoutText.Text = Strings.Get("PointPicker.TargetLost");
            return;
        }

        if (!inside || client is not { } area)
        {
            _readoutText.Text = Strings.Format("PointPicker.OutsideWindow", at.X, at.Y);
            _readoutText.Text += Environment.NewLine + Strings.Get("PointPicker.ShortcutWindow");
            return;
        }

        var point = new ScreenPoint(at.X - area.Origin.X, at.Y - area.Origin.Y);
        var lines = new List<string>
        {
            Strings.Format("PointPicker.ReadoutWindow", point.X, point.Y, at.X, at.Y),
        };
        if (element is { Length: > 0 })
        {
            lines.Add(element);
        }

        lines.Add(Strings.Get("PointPicker.ShortcutWindow"));
        _readoutText.Text = string.Join(Environment.NewLine, lines);
    }

    private void PlaceReadout(ScreenPoint at)
    {
        var local = ToLocal(at);
        var width = _readout.Bounds.Width > 0 ? _readout.Bounds.Width : 420;
        var height = _readout.Bounds.Height > 0 ? _readout.Bounds.Height : 86;
        var x = (double)local.X + 20;
        var y = (double)local.Y + 20;
        if (x + width > Width)
        {
            x = local.X - width - 20;
        }

        if (y + height > Height)
        {
            y = local.Y - height - 20;
        }

        Canvas.SetLeft(_readout, Math.Max(0, Math.Min(x, Math.Max(0, Width - width))));
        Canvas.SetTop(_readout, Math.Max(0, Math.Min(y, Math.Max(0, Height - height))));
    }

    private ScreenPoint ToLocal(ScreenPoint point)
        => new((int)Math.Round((point.X - _origin.X) / _scaling),
            (int)Math.Round((point.Y - _origin.Y) / _scaling));

    private Rect LocalRect(ClientArea area)
    {
        var local = ToLocal(area.Origin);
        return new Rect(local.X, local.Y, area.Size.Width / _scaling, area.Size.Height / _scaling);
    }

    private Rect ScreenRectToLocal(ScreenRect rect)
        => new((rect.Left - _origin.X) / _scaling, (rect.Top - _origin.Y) / _scaling,
            rect.Width / _scaling, rect.Height / _scaling);

    private static ScreenRect Intersect(ScreenPoint origin, ScreenSize area, ScreenPoint location,
        ScreenSize size)
    {
        var left = Math.Max(origin.X, location.X);
        var top = Math.Max(origin.Y, location.Y);
        var right = Math.Min(origin.X + area.Width, location.X + size.Width);
        var bottom = Math.Min(origin.Y + area.Height, location.Y + size.Height);
        return right > left && bottom > top
            ? new ScreenRect(left, top, right - left, bottom - top)
            : default;
    }

    private void Finish(ScreenPoint? point)
    {
        if (_done)
        {
            return;
        }

        _done = true;
        Chosen = point;
        Close();
    }

    private void AttachHook()
    {
        if (_hookSource is not { } hook)
        {
            return;
        }

        hook.KeyDown += OnKeyDown;
        hook.KeyUp += OnKeyUp;
        _hook = hook;
    }

    private void DetachHook()
    {
        if (_hook is not { } hook)
        {
            return;
        }

        hook.KeyDown -= OnKeyDown;
        hook.KeyUp -= OnKeyUp;
        _hook = null;
    }

    private void OnKeyDown(KeyHookEvent e)
    {
        if (e.Key == F1)
        {
            e.Suppress = true;
            _swallowF1Up = true;
            _pendingPoint = _target is null || _insideClient
                ? _screen.Cursor
                : null;
        }
        else if (e.Key == Escape)
        {
            e.Suppress = true;
            _swallowEscapeUp = true;
        }
    }

    private void OnKeyUp(KeyHookEvent e)
    {
        if ((e.Key == F1 && _swallowF1Up) || (e.Key == Escape && _swallowEscapeUp))
        {
            e.Suppress = true;
        }

        if (e.Key == F1 && _swallowF1Up)
        {
            _swallowF1Up = false;
            var at = _pendingPoint;
            _pendingPoint = null;
            if (at is not null)
            {
                Dispatcher.UIThread.Post(() => Finish(at));
            }
        }
        else if (e.Key == Escape && _swallowEscapeUp)
        {
            _swallowEscapeUp = false;
            Dispatcher.UIThread.Post(() => Finish(null));
        }
    }

    private readonly record struct ClientArea(ScreenPoint Origin, ScreenSize Size)
    {
        public bool Contains(ScreenPoint point)
            => point.X >= Origin.X && point.Y >= Origin.Y
               && point.X < Origin.X + Size.Width && point.Y < Origin.Y + Size.Height;

        public ScreenRect Intersect(ScreenPoint location, ScreenSize size)
            => PointPickerWindow.Intersect(Origin, Size, location, size);
    }

    private readonly record struct ScreenRect(int Left, int Top, int Width, int Height);
}
