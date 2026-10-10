using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Devices.Platform;
using WhaleGenie.Localization;

namespace WhaleGenie.Execution;

/// <summary>
/// Keeps the program in the notification area.
///
/// A macro is set off by a key, a mouse button or a pixel, not by its window being open, so closing
/// or minimizing the window is almost never meant as "stop working" — and stopping by accident
/// leaves the user with nothing listening and no sign of why. So the window goes out of the way
/// instead, the icon is where it comes back from, and stopping on purpose is the icon's menu.
/// </summary>
public sealed class AppTray : IDisposable
{
    /// <summary>
    /// The tray this program has, or null when it has none. A window that asks about work which is
    /// not saved needs it to say that the question was dismissed: that answer belongs to the exit
    /// which asked the question, and an exit that did not hear it would still be waiting — and
    /// would take the program down the next time that window closed for a reason of the user's own.
    /// </summary>
    public static AppTray? Current { get; private set; }

    private readonly Window _window;

    private readonly INotificationArea _area;

    private readonly Action _exit;

    /// <summary>Whether the user has been told where the window went; once is enough in a session.</summary>
    private bool _told;

    private bool _exiting;

    private bool _disposed;

    /// <summary>
    /// The way out as far as it has got: the windows still to be asked, and what runs once they are
    /// all gone. It is held while a window is asking about work that is not saved, because the
    /// answer — not the asking — is what decides whether the program stops.
    /// </summary>
    private IReadOnlyList<Window>? _waiting;

    private Action? _whenGone;

    public AppTray(Window window, INotificationArea area, Action exit)
    {
        _window = window;
        _area = area;
        _exit = exit;

        Current = this;
        _area.Activated += Restore;
        _area.ExitRequested += Stop;
        _window.PropertyChanged += OnWindowPropertyChanged;
        _window.Closing += OnClosing;
    }

    /// <summary>True while the icon's menu is what is closing the program.</summary>
    public bool IsLeaving => _exiting;

    /// <summary>
    /// Puts the icon in the notification area for this program, or answers null when there is no
    /// notification area to put it in. A program that cannot keep an icon still runs; it just
    /// closes the ordinary way instead.
    /// </summary>
    public static AppTray? Attach(Window window, IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            var area = new WindowsTray(
                Strings.Get("Tray.Tooltip"), Strings.Get("Tray.Show"), Strings.Get("Tray.Exit"));
            AppTray? tray = null;
            tray = new AppTray(window, area, () => tray!.Leave(desktop));
            return tray;
        }
        catch (Exception)
        {
            // A machine whose shell refuses the icon — or one where it is being replaced while the
            // program starts — must not turn into a program that will not start at all.
            return null;
        }
    }

    /// <summary>Leaves the program, letting whatever has unsaved work have its say first.</summary>
    private void Leave(IClassicDesktopStyleApplicationLifetime desktop)
        => Walk([.. desktop.Windows], () => desktop.Shutdown());

    /// <summary>
    /// Asks the windows to close, newest first, and runs <paramref name="finish"/> once they are
    /// all gone.
    /// </summary>
    /// <remarks>
    /// A window that calls the close off is one that is asking the person something — the macro
    /// editor holding unsaved steps, or the main window holding a project that was changed. Going
    /// ahead anyway would throw away the changes the prompt was asking about and make the prompt a
    /// lie, so the walk waits there instead: the answer closes the window and carries on from where
    /// it stopped, and a question that is dismissed calls the whole way out off (see
    /// <see cref="CalledOff"/>). The newest windows are asked first, because the ones opened over
    /// the main window are the ones with something to say.
    /// </remarks>
    internal void Walk(IReadOnlyList<Window> windows, Action finish)
    {
        foreach (var window in windows.Reverse())
        {
            if (Refused(window))
            {
                _waiting = windows;
                _whenGone = finish;
                window.Closed += OnAnswered;
                return;
            }
        }

        _waiting = null;
        _whenGone = null;
        finish();
    }

    /// <summary>
    /// Asks a window to close and answers whether it said no.
    /// </summary>
    /// <remarks>
    /// Whether the window is on screen is not the question: the main window spends most of its life
    /// hidden in the notification area, so a window asking about unsaved work would look like a
    /// window that had gone — and the program would stop on top of the question it was asking. What
    /// says a window is still there is that one of its handlers called the close off, and those
    /// have all had their say by the time this returns.
    /// </remarks>
    private static bool Refused(Window window)
    {
        var refused = false;

        void Watch(object? sender, WindowClosingEventArgs e) => refused |= e.Cancel;

        window.Closing += Watch;
        try
        {
            window.Close();
        }
        finally
        {
            window.Closing -= Watch;
        }

        return refused;
    }

    /// <summary>Carries the way out on from the window that was asking, now that it has gone.</summary>
    private void OnAnswered(object? sender, EventArgs e)
    {
        if (sender is Window window)
        {
            window.Closed -= OnAnswered;
        }

        // One turn later, and below everything already waiting to run: a window that closes on the
        // way out hands something over as it goes — the editor hands its macro to the list, which
        // arrives on the next turn and is what the question after it is about — so carrying on
        // straight away would ask about the project as it was before the user finished writing it.
        Dispatcher.UIThread.Post(Resume, DispatcherPriority.Background);
    }

    /// <summary>Picks the way out up again, unless the question that stopped it was dismissed.</summary>
    private void Resume()
    {
        if (_waiting is not { } windows || _whenGone is not { } finish)
        {
            return;
        }

        _waiting = null;
        _whenGone = null;
        Walk(windows, finish);
    }

    /// <summary>
    /// The question a window was asking has been dismissed, so the way out it belonged to is off:
    /// the program stays up, and closing that window later is an ordinary close again.
    /// </summary>
    public void CalledOff()
    {
        if (_waiting is { } windows)
        {
            foreach (var window in windows)
            {
                window.Closed -= OnAnswered;
            }
        }

        _waiting = null;
        _whenGone = null;
        _exiting = false;
    }

    /// <summary>
    /// Minimizing puts the window out of the way as well: the window and the icon would otherwise
    /// both be asking to be clicked, and the one with the macros in it is the icon.
    /// </summary>
    private void OnWindowPropertyChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty && _window.WindowState == WindowState.Minimized)
        {
            Hide();
        }
    }

    /// <summary>
    /// Closing hides the window rather than letting it go. The one way out is the icon's menu,
    /// which sets <see cref="_exiting"/> first — that is what lets this handler tell the two apart.
    /// </summary>
    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_exiting)
        {
            return;
        }

        e.Cancel = true;
        Hide();
    }

    private void Hide()
    {
        // Cleared before hiding, so bringing it back shows an ordinary window rather than a
        // minimized one that only appears as a sliver in the taskbar.
        _window.WindowState = WindowState.Normal;
        _window.Hide();

        if (_told)
        {
            return;
        }

        _told = true;
        _area.Balloon(
            Strings.Get("Tray.HiddenTitle"), Strings.Get("Tray.HiddenText"), NotificationKind.Information);
    }

    /// <summary>
    /// Puts the window out of the way for a while, and says whether it went. It is the same thing
    /// the icon does, so the way back is the one the user already knows; whoever asked for the room
    /// brings it back with <see cref="ComeBack"/> once that is over.
    /// </summary>
    public bool StepAside()
    {
        if (!_window.IsVisible)
        {
            return false;
        }

        Hide();
        return true;
    }

    /// <summary>Brings back a window that stepped aside, unless it is already back.</summary>
    public void ComeBack()
    {
        if (!_window.IsVisible)
        {
            Restore();
        }
    }

    private void Restore()
    {
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private void Stop()
    {
        _exiting = true;
        _exit();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (ReferenceEquals(Current, this))
        {
            Current = null;
        }

        _window.PropertyChanged -= OnWindowPropertyChanged;
        _window.Closing -= OnClosing;
        _area.Activated -= Restore;
        _area.ExitRequested -= Stop;
        _area.Dispose();
    }
}
