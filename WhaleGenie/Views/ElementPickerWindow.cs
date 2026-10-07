using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using SharpHook;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Devices.Platform;
using WhaleGenie.Localization;

namespace WhaleGenie.Views;

/// <summary>
/// Follows the pointer and outlines the control under it through UI Automation, so a selector is
/// taken off the screen instead of written out by hand.
/// </summary>
/// <remarks>
/// The frame is hollow: its middle is cut away with a window region, so the control underneath
/// still sees the pointer, both for UI Automation's own hit test and for the click that takes it.
/// Nothing the pointer lands on is ever the picker itself, except the few pixels of the border and
/// the strip that describes what was found — and those are recognised and left alone.
/// </remarks>
public sealed class ElementPickerWindow : Window
{
    /// <summary>How thick the outline is, in screen pixels.</summary>
    private const int Frame = 3;

    /// <summary>How far the readout keeps out of the pointer's way, in screen pixels.</summary>
    private const int ReadoutGap = 18;

    private readonly Func<int, int, UiElementInfo?> _look;
    private readonly FlaUiDevice? _device;
    private readonly bool _listen;
    private readonly IScreenDevice _screen = new WindowsScreenDevice();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(70) };
    private readonly TextBlock _readoutText = new();

    private Window? _readout;
    private IGlobalHook? _hook;
    private bool _done;

    /// <summary>True once the hook and the timer are watching, which is only worth doing once.</summary>
    private bool _watching;

    /// <summary>Where the frame is, written while drawing and read by the hook's own thread.</summary>
    private volatile Box? _frameBox;

    /// <summary>Where the readout is, kept the same way and for the same reason.</summary>
    private volatile Box? _readoutBox;

    public ElementPickerWindow()
        : this(null)
    {
    }

    /// <summary>
    /// Creates the picker over the desktop's UI Automation, or over a reader handed in by a test,
    /// which is what lets the picking be driven without a real screen.
    /// </summary>
    internal ElementPickerWindow(Func<int, int, UiElementInfo?>? look)
    {
        Title = Strings.Get("Element.Title");
        Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x84, 0xFF));
        WindowDecorations = WindowDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Width = 240;
        Height = 90;

        if (look is null)
        {
            _device = new FlaUiDevice();
            _look = _device.ElementAt;
            _listen = true;
        }
        else
        {
            // A reader handed in by a test drives the picker a probe at a time, so there is no
            // pointer to follow and no hook to take.
            _look = look;
            _listen = false;
        }

        _timer.Tick += (_, _) => FollowPointer();
    }

    /// <summary>What the picker took: the selector to write, and the window the control sits in.</summary>
    public sealed record Pick(string Selector, string Window);

    /// <summary>The control the pointer is over, which a click would take.</summary>
    internal UiElementInfo? Hovered { get; private set; }

    /// <summary>What the picker was closed with, or null when it was cancelled.</summary>
    internal Pick? Chosen { get; private set; }

    /// <summary>
    /// Shows the picker over <paramref name="owner"/> and reports the control that was clicked.
    /// The owner steps out of the way, because the control being picked is usually behind it.
    /// </summary>
    public static async Task<Pick?> PickAsync(Window owner)
    {
        var previous = owner.WindowState;
        owner.WindowState = WindowState.Minimized;

        try
        {
            return await new ElementPickerWindow(null).ShowDialogOver<Pick?>(owner);
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

        ShowReadout();

        // The frame comes off the screen and goes back on as the pointer moves between a control
        // and the desktop, and each time it comes back the window opens again. The hook and the
        // timer are already watching, so setting them up a second time would only answer every
        // click twice.
        if (!_listen || _watching)
        {
            return;
        }

        _watching = true;
        AttachHook();
        FollowPointer();
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
        _device?.Dispose();
    }

    /// <summary>Looks at the pointer and redraws the outline around whatever is under it.</summary>
    private void FollowPointer()
    {
        var point = WindowsScreenDevice.CursorPosition();
        ProbeAt(point.X, point.Y);
    }

    /// <summary>Looks at one place on the screen and keeps whatever is there.</summary>
    internal void ProbeAt(int x, int y)
    {
        var point = new ScreenPoint(x, y);
        if (Handles(point))
        {
            return;
        }

        UiElementInfo? info;
        try
        {
            info = _look(x, y);
        }
        catch (Exception)
        {
            // A provider that will not answer is the same as a place with nothing on it.
            info = null;
        }

        Hovered = info;
        _readoutText.Text = info is null ? Strings.Get("Element.Empty") : Describe(info);

        Outline(info);
        PlaceReadout(point);
    }

    /// <summary>Takes the control under the pointer, when there is one to take.</summary>
    internal void Confirm()
    {
        if (Hovered is not { } info || info.Selector.Length == 0)
        {
            // Nothing worth writing here, so the picker stays up and the pointer can be moved on.
            return;
        }

        Finish(new Pick(info.Selector, info.WindowTitle));
    }

    /// <summary>Leaves without taking anything.</summary>
    internal void Cancel() => Finish(null);

    /// <summary>
    /// True when a place on the screen is the picker itself: the few pixels of the frame's border
    /// or the readout. The hole left in the middle is not ours, which is what lets the pointer
    /// reach the control that is being picked.
    /// </summary>
    /// <remarks>
    /// The click arrives on the hook's own thread, and a window may only be read from the thread
    /// that made it. So this answers from the rectangles kept while drawing rather than from the
    /// windows themselves. Reading a window here threw, and an exception out of a hook has
    /// nowhere to go but out of the native callback, which ended the whole process.
    /// </remarks>
    internal bool Handles(ScreenPoint point)
    {
        if (_frameBox is { } frame
            && OnFrame(point.X, point.Y, frame.Left, frame.Top, frame.Width, frame.Height, Frame))
        {
            return true;
        }

        return _readoutBox is { } readout && readout.Holds(point.X, point.Y);
    }

    /// <summary>
    /// True when a place falls on the frame of a box: inside it, but not inside the hole left in
    /// the middle, which is where the control underneath goes on seeing the pointer.
    /// </summary>
    internal static bool OnFrame(int x, int y, int left, int top, int width, int height, int frame)
        => width > frame * 2
           && height > frame * 2
           && x >= left && x < left + width
           && y >= top && y < top + height
           && (x < left + frame || x >= left + width - frame
                                 || y < top + frame || y >= top + height - frame);

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

    /// <summary>Draws the outline around a control, or takes it off the screen when there is none.</summary>
    private void Outline(UiElementInfo? info)
    {
        if (info is null || info.Size.Width <= 0 || info.Size.Height <= 0)
        {
            _frameBox = null;
            IsVisible = false;
            return;
        }

        var scaling = RenderScaling <= 0 ? 1 : RenderScaling;
        var width = info.Size.Width + (Frame * 2);
        var height = info.Size.Height + (Frame * 2);
        var left = info.Location.X - Frame;
        var top = info.Location.Y - Frame;

        Position = new PixelPoint(left, top);
        Width = width / scaling;
        Height = height / scaling;

        // Recorded as the frame is drawn, so the region that is cut and the rectangle a click is
        // measured against are always the same numbers.
        _frameBox = new Box(left, top, width, height);

        IsVisible = true;
        WindowHole.Cut(this, width, height, Frame);
    }

    /// <summary>
    /// The strip that says what the pointer is over. It is a window of its own rather than a part
    /// of the frame, because a control can be twenty pixels wide and the words still have to fit.
    /// </summary>
    private void ShowReadout()
    {
        if (_readout is null)
        {
            _readoutText.Foreground = Brushes.White;
            _readoutText.FontSize = 12;
            _readoutText.MaxWidth = 460;
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
                Title = Strings.Get("Element.Title"),
            };
        }

        // The readout is made once. Making a second one would put the same text block inside two
        // borders, which is an error rather than a second readout.
        if (!_readout.IsVisible)
        {
            _readout.Show(this);
        }
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
        _readoutBox = new Box(x, y, width, height);
    }

    /// <summary>Where the main screen is, which is the screen the picker works on.</summary>
    private (int X, int Y, int Width, int Height) ScreenBounds()
    {
        if (Screens.Primary is { } primary)
        {
            return (primary.Bounds.X, primary.Bounds.Y, primary.Bounds.Width, primary.Bounds.Height);
        }

        var size = _screen.PrimarySize;
        return (0, 0, size.Width, size.Height);
    }

    /// <summary>What the readout says about the control under the pointer.</summary>
    private static string Describe(UiElementInfo info)
    {
        var parts = new List<string>
        {
            info.ControlType.Length > 0 ? info.ControlType : Strings.Get("Element.Unknown"),
        };

        if (info.Name.Length > 0)
        {
            parts.Add(Strings.Format("Element.Named", info.Name));
        }

        if (info.AutomationId.Length > 0)
        {
            parts.Add(Strings.Format("Element.Id", info.AutomationId));
        }

        if (info.WindowTitle.Length > 0)
        {
            parts.Add(Strings.Format("Element.InWindow", info.WindowTitle));
        }

        return string.Join("   ", parts);
    }

    private void Finish(Pick? pick)
    {
        if (_done)
        {
            return;
        }

        _done = true;
        _timer.Stop();
        DetachHook();
        Chosen = pick;
        Close(pick);
    }

    /// <summary>
    /// The click and the two keys that end the picking are taken from the shared hook rather than
    /// from the window, because the pointer is over somebody else's window and the picker never
    /// takes the focus. The click is swallowed, so the control that was picked is not also used.
    /// </summary>
    private void AttachHook()
    {
        try
        {
            var hook = GlobalInputHook.Shared.Hook;
            hook.MousePressed += OnMousePressed;
            hook.KeyPressed += OnKeyPressed;
            _hook = hook;
        }
        catch (Exception)
        {
            // Without a hook there is no way to take a control, but the picker still opens and
            // can be left again, which is better than an exception out of OnOpened.
            _hook = null;
        }
    }

    private void DetachHook()
    {
        if (_hook is not { } hook)
        {
            return;
        }

        hook.MousePressed -= OnMousePressed;
        hook.KeyPressed -= OnKeyPressed;
        _hook = null;
    }

    private void OnMousePressed(object? sender, MouseHookEventArgs e)
    {
        if (e.Data.Button == SharpHook.Data.MouseButton.Button2)
        {
            e.SuppressEvent = true;
            Dispatcher.UIThread.Post(Cancel);
            return;
        }

        if (e.Data.Button != SharpHook.Data.MouseButton.Button1)
        {
            return;
        }

        var x = e.Data.X;
        var y = e.Data.Y;
        if (Handles(new ScreenPoint(x, y)))
        {
            return;   // a click on the picker's own border is none of its business
        }

        e.SuppressEvent = true;
        Dispatcher.UIThread.Post(() =>
        {
            if (_done)
            {
                // Something else finished the picker between the click and this running, and a
                // window that is on its way out is not worth looking at again.
                return;
            }

            // The pointer has stopped moving, so looking again here is what keeps the answer
            // exact rather than a step behind.
            ProbeAt(x, y);
            Confirm();
        });
    }

    private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        if (e.Data.KeyCode != SharpHook.Data.KeyCode.VcEscape)
        {
            return;
        }

        e.SuppressEvent = true;
        Dispatcher.UIThread.Post(Cancel);
    }

}
