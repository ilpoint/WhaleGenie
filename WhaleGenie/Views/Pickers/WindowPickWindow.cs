using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;
using WhaleGenie.Runtime;

namespace WhaleGenie.Views;

/// <summary>
/// 跟着指针走，把指针底下的整个窗口框出来，点一下取走它。框住的是窗口，不是窗口里的控件：
/// 宏要认的是"这个窗口"，后面那些步骤都在它里面做事。
/// </summary>
/// <remarks>
/// 框的中间是挖空的，所以指针和点击都落到目标窗口上；窗框本身只有几个像素，读数另开一扇小窗。
/// 这与元素拾取器是同一套做法，区别只在量的是窗口而不是控件：一次只看指针底下那一扇，
/// 由 Windows 自己回答（含没有标题的窗口），比逐层问 UI Automation 便宜得多。
/// </remarks>
public sealed class WindowPickWindow : Window
{
    /// <summary>How thick the outline is, in screen pixels.</summary>
    private const int Frame = 3;

    /// <summary>How far the readout keeps out of the pointer's way, in screen pixels.</summary>
    private const int ReadoutGap = 16;

    /// <summary>
    /// The key that leaves the picker, looked up once rather than compared by name on every press.
    /// A name that does not resolve leaves a key nothing can match, which is the safe way round.
    /// </summary>
    private static readonly HookKey Escape = KeyNames.Resolve("Esc") ?? default;
    private static readonly HookKey F1 = KeyNames.Resolve("F1") ?? default;

    private readonly IWindowDevice _windows;
    private readonly IScreenDevice _screen;

    /// <summary>The hook the click that takes a window is read from, or null for a probe-driven picker.</summary>
    private readonly IInputHook? _hookSource;

    private readonly bool _listen;

    // 50ms 一次：跟手够用，又把枚举窗口的开销压到看不见。指针位置没变时整个 tick 什么都不做。
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly TextBlock _readoutText = new();

    private Window? _readout;
    private IInputHook? _hook;
    private bool _done;
    private bool _watching;

    /// <summary>The window the pointer is over, which a click would take.</summary>
    internal WindowInfo? Hovered { get; private set; }

    /// <summary>What the picker was closed with, or null when it was cancelled.</summary>
    internal WindowInfo? Chosen { get; private set; }

    /// <summary>Where the frame is, written while drawing and read by the hook's own thread.</summary>
    private volatile Box? _frameBox;

    /// <summary>Where the readout is, kept the same way and for the same reason.</summary>
    private volatile Box? _readoutBox;

    /// <summary>The last place the pointer was looked at, so a still pointer costs nothing.</summary>
    private ScreenPoint _lookedAt;

    /// <summary>This program's own windows, which are never what is being picked.</summary>
    private readonly HashSet<long> _ours = [];

    /// <summary>
    /// 别的鲸灵窗口也算自己的：拾取器让位时它们已经藏起来了，但主窗口这类不进让位名单的窗口
    /// 仍可能露着，框住它再拿去当目标没有意义。按句柄记，问过一次就不再问。
    /// </summary>
    private readonly Dictionary<long, bool> _ownByHandle = [];

    private static string OwnProgram
        => System.IO.Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? string.Empty);

    public WindowPickWindow()
        : this(SharedDevices.Current.Windows, SharedDevices.Current.Screen, SharedDevices.Current.Hook)
    {
    }

    /// <summary>
    /// 用交给它的三样东西拾取：桌面上的窗口、指针所在的位置、点击从哪儿读。传这三样而不是
    /// 整个设备层，说的是它真正要什么，也让检查里能用替身。
    /// </summary>
    internal WindowPickWindow(IWindowDevice windows, IScreenDevice screen, IInputHook? hook)
    {
        _windows = windows;
        _screen = screen;
        _hookSource = hook;
        _listen = hook is not null;
        Build();
    }

    /// <summary>Shows the picker and reports the window that was clicked, or null when cancelled.</summary>
    public static async Task<WindowInfo?> PickAsync(IWindowDevice windows, IScreenDevice screen,
        IInputHook hook)
    {
        return await new WindowPickWindow(windows, screen, hook).TakeAsync();
    }

    internal async Task<WindowInfo?> TakeAsync()
    {
        await AppWindows.ShowAndWaitAsync(this);
        return Chosen;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        ShowReadout();

        // 窗口每换一扇就重开一次（框从一扇窗挪到另一扇），钩子与定时器却只该挂一次。
        if (!_listen || _watching)
        {
            return;
        }

        _watching = true;
        AttachHook();
        FollowPointer(force: true);
        _timer.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        _done = true;
        _timer.Stop();
        DetachHook();
        _readout?.Close();
        _readout = null;
        _readoutBox = null;
    }

    /// <summary>The frame itself, which is the same whichever window is under the pointer.</summary>
    private void Build()
    {
        Title = Strings.Get("WindowPick.Title");

        // 拾取器可见性色：要在任意截图底色上可见，故不随主题（判定为字面，不进令牌）。
        Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x84, 0xFF));
        WindowDecorations = WindowDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Width = 200;
        Height = 120;

        _timer.Tick += (_, _) => FollowPointer(force: false);
    }

    /// <summary>Looks at the pointer and redraws the outline around the window under it.</summary>
    private void FollowPointer(bool force)
    {
        if (_done)
        {
            return;
        }

        var point = _screen.Cursor;

        // 指针没动就什么都不用做：枚举窗口与量窗口都不是免费的，静着的时候不该花这份钱。
        if (!force && point.X == _lookedAt.X && point.Y == _lookedAt.Y && Hovered is not null)
        {
            return;
        }

        _lookedAt = point;
        ProbeAt(point.X, point.Y);
    }

    /// <summary>Looks at one place on the screen and keeps whatever window is there.</summary>
    internal void ProbeAt(int x, int y)
    {
        // 指针压在框的那几像素上时，底下的窗口还是刚才那扇：不要把框本身当成目标。
        if (_frameBox is { } frame && OnFrame(x, y, frame, Frame))
        {
            PlaceReadout(new ScreenPoint(x, y));
            return;
        }

        WindowInfo? found;
        try
        {
            found = _windows.At(x, y);
        }
        catch (Exception)
        {
            // 问不到桌面时和"这里什么都没有"一样，指针动一下就会再问。
            found = null;
        }

        if (found is not null && IsOurs(found.Handle))
        {
            found = null;
        }

        Hovered = found;
        _readoutText.Text = found is null
            ? Strings.Get("WindowPick.Empty")
            : Describe(found) + Environment.NewLine + Strings.Get("WindowPick.Confirm");
        Outline(found);
        PlaceReadout(new ScreenPoint(x, y));
    }

    /// <summary>Takes the window under the pointer, when there is one to take.</summary>
    internal void Confirm()
    {
        if (Hovered is not { } found)
        {
            return;
        }

        // 一扇没有标题的窗口只能按句柄认，而那正是这一步最不愿写死的东西：让它继续挑，
        // 换一扇有名字的，或者按 Esc 走开。
        if (found.Title.Trim().Length == 0)
        {
            _readoutText.Text = Strings.Get("WindowPick.NoTitle");
            return;
        }

        Finish(found);
    }

    /// <summary>Leaves without taking anything.</summary>
    internal void Cancel() => Finish(null);

    /// <summary>
    /// True when a place on the screen is the picker itself: the few pixels of the frame's border
    /// or the readout. The hole left in the middle is not ours, which is what lets the pointer
    /// reach the window that is being picked.
    /// </summary>
    internal bool Handles(ScreenPoint point)
    {
        if (_frameBox is { } frame && OnFrame(point.X, point.Y, frame, Frame))
        {
            return true;
        }

        return _readoutBox is { } readout && readout.Holds(point.X, point.Y);
    }

    /// <summary>
    /// True when a place falls on the frame of a box: inside it, but not inside the hole left in
    /// the middle, which is where the window underneath goes on seeing the pointer.
    /// </summary>
    internal static bool OnFrame(int x, int y, int left, int top, int width, int height, int frame)
        => width > frame * 2
           && height > frame * 2
           && x >= left && x < left + width
           && y >= top && y < top + height
           && (x < left + frame || x >= left + width - frame
                                 || y < top + frame || y >= top + height - frame);

    private bool OnFrame(int x, int y, Box frame, int thickness)
        => OnFrame(x, y, frame.Left, frame.Top, frame.Width, frame.Height, thickness);

    /// <summary>
    /// A rectangle on the screen kept as plain numbers, so the hook's thread can ask where the
    /// picker is without touching a window, which only the interface thread may read.
    /// </summary>
    private sealed record Box(int Left, int Top, int Width, int Height)
    {
        public bool Holds(int x, int y)
            => Width > 0 && Height > 0
               && x >= Left && x < Left + Width
               && y >= Top && y < Top + Height;
    }

    /// <summary>Draws the outline around a window, or takes it off the screen when there is none.</summary>
    private void Outline(WindowInfo? window)
    {
        if (window is null || window.Size.Width <= 0 || window.Size.Height <= 0)
        {
            _frameBox = null;
            IsVisible = false;
            return;
        }

        var scaling = RenderScaling <= 0 ? 1 : RenderScaling;
        var width = window.Size.Width + (Frame * 2);
        var height = window.Size.Height + (Frame * 2);
        var left = window.Location.X - Frame;
        var top = window.Location.Y - Frame;

        Position = new PixelPoint(left, top);
        Width = width / scaling;
        Height = height / scaling;

        // 记录的是画出来的那个框，挖掉的洞与点击时量的框因此永远是同一组数。
        _frameBox = new Box(left, top, width, height);

        IsVisible = true;
        WindowHole.Cut(this, width, height, Frame);
        RememberOurs();
    }

    /// <summary>
    /// The strip that says which window is under the pointer. It is a window of its own rather than
    /// a part of the frame, because a window can be twenty pixels wide and the words still have to
    /// fit — and it must not decorate what is being picked.
    /// </summary>
    private void ShowReadout()
    {
        if (_readout is null)
        {
            _readoutText.Foreground = Brushes.White;
            _readoutText.FontSize = 12;
            _readoutText.MaxWidth = 460;
            _readoutText.TextWrapping = TextWrapping.Wrap;
            _readoutText.TextTrimming = TextTrimming.CharacterEllipsis;

            _readout = new Window
            {
                Content = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x11, 0x25, 0x38)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x0A, 0x84, 0xFF)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(9, 5),
                    Child = _readoutText,
                },
                SizeToContent = SizeToContent.WidthAndHeight,
                WindowDecorations = WindowDecorations.None,
                CanResize = false,
                ShowInTaskbar = false,
                ShowActivated = false,
                Topmost = true,
                Title = Strings.Get("WindowPick.Title"),
            };
        }

        if (!_readout.IsVisible)
        {
            _readout.Show(this);
        }

        RememberOurs();
    }

    /// <summary>Puts the readout beside the pointer, kept inside the screen.</summary>
    private void PlaceReadout(ScreenPoint point)
    {
        if (_readout is not { IsVisible: true } readout)
        {
            _readoutBox = null;
            return;
        }

        var scaling = readout.RenderScaling <= 0 ? 1 : readout.RenderScaling;
        var width = (int)Math.Ceiling(readout.Bounds.Width * scaling);
        var height = (int)Math.Ceiling(readout.Bounds.Height * scaling);
        var screen = ScreenBounds();

        var x = point.X + ReadoutGap;
        var y = point.Y + ReadoutGap;

        if (width > 0 && x + width > screen.X + screen.Width)
        {
            x = point.X - width - ReadoutGap;
        }

        if (height > 0 && y + height > screen.Y + screen.Height)
        {
            y = point.Y - height - ReadoutGap;
        }

        x = Math.Clamp(x, screen.X, Math.Max(screen.X, screen.X + screen.Width - width));
        y = Math.Clamp(y, screen.Y, Math.Max(screen.Y, screen.Y + screen.Height - height));

        readout.Position = new PixelPoint(x, y);

        // 还没排版时量到的会是"能有多大"，那样的框会盖住整张桌面、把所有点击都算成自己的。
        _readoutBox = readout.IsMeasureValid && readout.IsArrangeValid && width > 0 && height > 0
            ? new Box(x, y, width, height)
            : null;
    }

    /// <summary>
    /// Where the whole desk is, which is the space the picker works in. A desk of several displays
    /// is covered corner to corner: the picker may be asked about any of them.
    /// </summary>
    private (int X, int Y, int Width, int Height) ScreenBounds()
    {
        var screens = Screens.All;
        if (screens.Count > 0)
        {
            var left = screens.Min(screen => screen.Bounds.X);
            var top = screens.Min(screen => screen.Bounds.Y);
            var right = screens.Max(screen => screen.Bounds.X + screen.Bounds.Width);
            var bottom = screens.Max(screen => screen.Bounds.Y + screen.Bounds.Height);
            return (left, top, right - left, bottom - top);
        }

        var desk = _screen.Desktop.Bounds;
        return (desk.Left, desk.Top, desk.Width, desk.Height);
    }

    /// <summary>What the readout says about the window under the pointer.</summary>
    private string Describe(WindowInfo window)
    {
        var title = window.Title.Trim();
        if (title.Length == 0)
        {
            title = Strings.Get("WindowPick.Untitled");
        }

        var program = string.Empty;
        try
        {
            program = _windows.ProcessOf(window.Handle);
        }
        catch (Exception)
        {
            // 拿不到程序名时只写标题：这句话是给人看的，没必要因此不显示。
        }

        return program.Length == 0
            ? title
            : $"{title}   ·   {program}";
    }

    /// <summary>True for one of this program's own windows, which is never what is being picked.</summary>
    private bool IsOurs(long handle)
    {
        if (_ours.Contains(handle))
        {
            return true;
        }

        if (_ownByHandle.TryGetValue(handle, out var known))
        {
            return known;
        }

        var mine = false;
        try
        {
            mine = string.Equals(_windows.ProcessOf(handle), OwnProgram, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            // 问不到程序名时按"不是自己的"处理：取错了还能再取一次，取不到就没法继续。
        }

        _ownByHandle[handle] = mine;
        return mine;
    }

    /// <summary>Writes down the handles of the picker's own two windows as they are made.</summary>
    private void RememberOurs()
    {
        foreach (var window in new[] { this, _readout })
        {
            if (window?.TryGetPlatformHandle() is { } platform && platform.Handle != IntPtr.Zero)
            {
                _ours.Add(platform.Handle.ToInt64());
            }
        }
    }

    private void Finish(WindowInfo? pick)
    {
        if (_done)
        {
            return;
        }

        _done = true;
        _timer.Stop();
        DetachHook();
        Chosen = pick;
        Close();
    }

    /// <summary>
    /// The click and the key that end the picking are taken from the shared hook rather than from
    /// the window, because the pointer is over somebody else's window and the picker never takes
    /// the focus. The click is swallowed, so the window that was picked is not also used.
    /// </summary>
    private void AttachHook()
    {
        try
        {
            if (_hookSource is not { } hook)
            {
                _hook = null;
                return;
            }

            hook.MouseDown += OnMouseDown;
            hook.KeyDown += OnKeyDown;
            _hook = hook;
        }
        catch (Exception)
        {
            // 没有钩子就取不了窗口，但拾取器仍旧打得开、也走得掉，比从 OnOpened 抛出去好。
            _hook = null;
        }
    }

    private void DetachHook()
    {
        if (_hook is not { } hook)
        {
            return;
        }

        hook.MouseDown -= OnMouseDown;
        hook.KeyDown -= OnKeyDown;
        _hook = null;
    }

    private void OnMouseDown(MouseHookEvent e)
    {
        if (e.Button == HookMouseButton.Right)
        {
            e.Suppress = true;
            Dispatcher.UIThread.Post(Cancel);
            return;
        }

        if (e.Button != HookMouseButton.Left)
        {
            return;
        }

        // 左键不拦：拾取器开着的时候，人还要去点别的窗口、点任务栏启动一个应用，把要取的那扇
        // 摆到前面来，再来按 F1。框的中间本来就是空的，点击落到目标上；取走只由 F1 决定。
        var at = new ScreenPoint(e.X, e.Y);
        Dispatcher.UIThread.Post(() => ProbeAt(at.X, at.Y));
    }

    private void OnKeyDown(KeyHookEvent e)
    {
        if (e.Key == F1)
        {
            e.Suppress = true;
            Dispatcher.UIThread.Post(Confirm);
            return;
        }

        if (e.Key != Escape)
        {
            return;
        }

        e.Suppress = true;
        Dispatcher.UIThread.Post(Cancel);
    }
}
