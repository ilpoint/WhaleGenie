using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace WhaleGenie.Views;

/// <summary>
/// Every window this program has on the screen, out of the way for a while.
/// </summary>
/// <remarks>
/// A rectangle is dragged on the screen, a colour is read off it, or a control is picked by
/// pointing at it, because something out there is wanted — and this program's own windows are
/// usually in front of exactly that, the window asking the question included. So they all go for as
/// long as the scope lives, and come back as they were once it ends.
/// </remarks>
internal sealed class AppWindows : IDisposable
{
    /// <summary>
    /// The windows this program has open. Where they are is the desktop's own list, read when a
    /// scope opens; a check that drives a picker puts its own windows here and puts this back
    /// afterwards.
    /// </summary>
    internal static Func<IEnumerable<Window>> Open { get; set; } = OnTheDesk;

    private readonly List<Away> _away = [];

    /// <summary>The window that is to be in front again once the scope ends — the one that asked.</summary>
    private readonly Window? _front;

    private AppWindows(Window? front) => _front = front;

    /// <summary>
    /// Puts every window this program has on the screen out of the way, and brings them back when
    /// the scope ends.
    /// </summary>
    public static AppWindows StepAside(Window? front = null) => StepAside(Open(), front);

    /// <summary>The same, over the windows handed in rather than over this machine's own list.</summary>
    internal static AppWindows StepAside(IEnumerable<Window> windows, Window? front = null)
    {
        var aside = new AppWindows(front);

        foreach (var window in windows)
        {
            // A window that is not on screen is left alone: the main window lives in the
            // notification area, has a way back of its own, and showing it here would be a window
            // appearing out of nowhere.
            if (!window.IsVisible)
            {
                continue;
            }

            var away = new Away(window, window.WindowState);
            window.Closed += away.Gone;
            aside._away.Add(away);
            window.Hide();
        }

        return aside;
    }

    /// <summary>
    /// Shows a window that stands on its own — nothing else of this program is on the screen to own
    /// it — and waits until it is closed, which is how a picker is put up while the rest are away.
    /// </summary>
    public static async Task ShowAndWaitAsync(Window window)
    {
        var closed = new TaskCompletionSource();

        void Done(object? sender, EventArgs e) => closed.TrySetResult();

        window.Closed += Done;
        window.Show();
        await closed.Task;
        window.Closed -= Done;
    }

    /// <summary>
    /// Brings the windows back as they were, in the state they were in, and the one that asked back
    /// in front — which is where its answer is going to be read.
    /// </summary>
    public void Dispose()
    {
        // Whoever asked comes back in front of what it had opened over, which is where the answer
        // is going to be read. Not if it was not on screen to begin with, and not if it went for
        // good while it was away.
        var front = _away.Find(away => away.Window == _front && !away.Closed);

        foreach (var away in _away)
        {
            away.BringBack();
        }

        _away.Clear();
        front?.Window.Activate();
    }

    /// <summary>Every window this program has open, which is the desktop's own list of them.</summary>
    private static IEnumerable<Window> OnTheDesk()
        => (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
            ?.Windows ?? [];

    /// <summary>
    /// One window put away, and what it takes to bring it back.
    /// </summary>
    private sealed class Away(Window window, WindowState state)
    {
        private bool _closed;

        public Window Window { get; } = window;

        /// <summary>True once the window went for good while it was away.</summary>
        public bool Closed => _closed;

        public void Gone(object? sender, EventArgs e) => _closed = true;

        /// <summary>
        /// Shows the window again in the state it was in. One that was closed while it was away is
        /// not shown: a run window closes itself when the run ends, and there is nothing left of it
        /// to show.
        /// </summary>
        public void BringBack()
        {
            Window.Closed -= Gone;

            if (_closed)
            {
                return;
            }

            Window.Show();
            if (Window.WindowState != state)
            {
                Window.WindowState = state;
            }
        }
    }
}
