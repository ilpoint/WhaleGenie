using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
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
    private readonly Window _window;

    private readonly INotificationArea _area;

    private readonly Action _exit;

    /// <summary>Whether the user has been told where the window went; once is enough in a session.</summary>
    private bool _told;

    private bool _exiting;

    private bool _disposed;

    public AppTray(Window window, INotificationArea area, Action exit)
    {
        _window = window;
        _area = area;
        _exit = exit;

        _area.Activated += Restore;
        _area.ExitRequested += Stop;
        _window.PropertyChanged += OnWindowPropertyChanged;
        _window.Closing += OnClosing;
    }

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
            return new AppTray(window, area, () => Leave(window, desktop));
        }
        catch (Exception)
        {
            // A machine whose shell refuses the icon — or one where it is being replaced while the
            // program starts — must not turn into a program that will not start at all.
            return null;
        }
    }

    /// <summary>Leaves the program, letting whatever has unsaved work have its say first.</summary>
    private static void Leave(Window window, IClassicDesktopStyleApplicationLifetime desktop)
        => Insist(
            [.. desktop.Windows.Where(open => !ReferenceEquals(open, window))],
            () =>
            {
                window.Hide();
                desktop.Shutdown();
            });

    /// <summary>
    /// Asks the windows to close, and finishes only once they are gone.
    /// </summary>
    /// <remarks>
    /// A window that stays is one that is asking the person something — the macro editor holding
    /// unsaved steps is the one that does this. Going ahead anyway would throw away the changes the
    /// prompt was asking about and make the prompt a lie, so instead the answer decides it: save or
    /// discard and the window goes and the rest of the way out runs, dismiss the question and the
    /// window stays and the program stays with it. They are asked newest first, because the windows
    /// opened over the main one are the ones with something to say.
    /// </remarks>
    internal static void Insist(IReadOnlyList<Window> windows, Action finish)
    {
        foreach (var window in windows.Reverse())
        {
            window.Close();
            if (window.IsVisible)
            {
                window.Closed += (_, _) => Insist(windows, finish);
                return;
            }
        }

        finish();
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
        _window.PropertyChanged -= OnWindowPropertyChanged;
        _window.Closing -= OnClosing;
        _area.Activated -= Restore;
        _area.ExitRequested -= Stop;
        _area.Dispose();
    }
}
