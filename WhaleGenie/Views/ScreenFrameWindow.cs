using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using WhaleGenie.Core.Devices;

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
/// what the button beside a picker does, is the same frame with a clock on it. It is a drawing
/// rather than a window anybody means, and it carries no title so that no rule can recognise it —
/// see the constructor for what that is holding back.
/// </remarks>
internal sealed class ScreenFrameWindow : Window
{
    /// <summary>How thick the frame is, in screen pixels.</summary>
    private const int Frame = 3;

    private readonly DispatcherTimer? _life;
    private readonly DispatcherTimer? _layer;
    private readonly long _targetHandle;
    private readonly TaskCompletionSource _finished =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private ScreenPoint _location;
    private ScreenSize _size;

    private ScreenFrameWindow(ScreenPoint location, ScreenSize size, TimeSpan? living,
        long targetHandle)
    {
        _location = location;
        _size = size;
        _targetHandle = targetHandle;

        // 拾取器可见性色：要在任意截图底色上可见，故不随主题（判定为字面，不进令牌）。
        Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x84, 0xFF));
        WindowDecorations = WindowDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = targetHandle == 0;
        WindowStartupLocation = WindowStartupLocation.Manual;

        // An empty title, and that is the point: a macro names windows by their title, and the
        // window device lists the top-level windows that carry one, so a frame with an empty one is
        // a window no rule can recognise. Emptied rather than left alone, because Avalonia gives
        // every window the title "Window" otherwise. Without this a frame is a window in front — it
        // stands above every window that is not pinned — and an empty "match content", which means
        // the window in front, answers with the frame itself: the preview would frame the frame it
        // just drew, one ring stepping outwards from the last, and a run could read the ring instead
        // of the window it means.
        Title = string.Empty;

        if (living is { } span)
        {
            _life = new DispatcherTimer { Interval = span };
            _life.Tick += (_, _) => Close();
        }

        if (targetHandle != 0)
        {
            // A target may be activated or raised after the frame is placed. Reasserting the
            // relative layer keeps the frame on that target instead of above every window until
            // the next place update.
            _layer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _layer.Tick += (_, _) => ApplyLayer();
        }
    }

    /// <summary>Completes when the frame is off the screen, whether it timed out or was closed.</summary>
    public Task Finished => _finished.Task;

    /// <summary>
    /// Puts a frame around a place and leaves it there. Null when the place measures to nothing:
    /// there is no rectangle to draw around.
    /// </summary>
    public static ScreenFrameWindow? Show(ScreenPoint location, ScreenSize size,
        long targetHandle = 0)
        => Open(location, size, null, targetHandle);

    /// <summary>
    /// The same frame with a clock on it: it goes away by itself after <paramref name="living"/>,
    /// which is what an answer worth a moment and not an overlay to get rid of looks like.
    /// </summary>
    public static ScreenFrameWindow? Flash(ScreenPoint location, ScreenSize size, TimeSpan living)
        => Open(location, size, living, 0);

    /// <summary>The window this frame follows, or zero for an independent flash.</summary>
    internal long TargetHandle => _targetHandle;

    /// <summary>
    /// Moves a frame that is already up to another place, so a frame following something around does
    /// not blink out and back — which is noticeable, and reads as the thing it stands for going away.
    /// </summary>
    public void Place(ScreenPoint location, ScreenSize size)
    {
        // Following a window means being told where it is over and over, and most of those answers
        // are the one already drawn: cutting the region again for each of them would be work for
        // nothing, and on a window that is being read many times a second it is work done often.
        if (_location == location && _size == size)
        {
            ApplyLayer();
            return;
        }

        _location = location;
        _size = size;
        Fit();
    }

    private static ScreenFrameWindow? Open(ScreenPoint location, ScreenSize size, TimeSpan? living,
        long targetHandle)
    {
        if (size.Width <= 0 || size.Height <= 0)
        {
            return null;
        }

        var frame = new ScreenFrameWindow(location, size, living, targetHandle);
        frame.Show();
        return frame;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        Fit();
        _life?.Start();
        if (IsVisible)
        {
            _layer?.Start();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _life?.Stop();
        _layer?.Stop();
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
        ApplyLayer();
    }

    /// <summary>
    /// Keeps the frame in the target's own layer. The target is the window the frame is put
    /// after, which places the frame immediately below it. A topmost target needs the frame in
    /// the topmost band as well. The element flash has no target and stays an independent overlay.
    /// </summary>
    private void ApplyLayer()
    {
        if (!OperatingSystem.IsWindows() || TryGetPlatformHandle() is not { } own
            || own.Handle == IntPtr.Zero)
        {
            return;
        }

        var style = GetWindowLongPtr(own.Handle, GwlExStyle).ToInt64();
        SetWindowLongPtr(own.Handle, GwlExStyle,
            new IntPtr(style | WsExTransparent | WsExNoActivate | WsExToolWindow));

        if (_targetHandle == 0)
        {
            Topmost = true;
            return;
        }

        var target = new IntPtr(_targetHandle);
        if (!IsWindow(target))
        {
            Close();
            return;
        }

        var targetStyle = GetWindowLongPtr(target, GwlExStyle).ToInt64();
        Topmost = (targetStyle & WsExTopmost) != 0;
        SetWindowPos(own.Handle, target, 0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x00000020;
    private const long WsExNoActivate = 0x08000000;
    private const long WsExToolWindow = 0x00000080;
    private const long WsExTopmost = 0x00000008;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter,
        int x, int y, int width, int height, uint flags);
}
