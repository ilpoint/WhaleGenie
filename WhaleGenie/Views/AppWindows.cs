using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using WhaleGenie.ViewModels;

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

    /// <summary>
    /// The handles of the windows this program has, which is what a preview keeps its frames off.
    /// The window asking the question stands in front of the very thing being looked for, so a
    /// frame around it says nothing and covers the window the answer is written in. A check hands
    /// its own numbers over here, because a window in a test session has no handle to read.
    /// </summary>
    internal static Func<IReadOnlyList<long>> Handles { get; set; } = OwnHandles;

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

        // Who is on the screen is settled before anyone goes: taking a window off the screen takes
        // the windows it owns with it, so by the time this loop reached such a window it would
        // already be gone, would not be written down, and nothing would bring it back — the window
        // a step is written in is owned by the editor and would vanish exactly that way.
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
        }

        foreach (var away in aside._away)
        {
            away.Take();
        }

        return aside;
    }

    /// <summary>
    /// Waits until what is on the screen is the screen *without* these windows.
    /// </summary>
    /// <remarks>
    /// Taking a window off the screen is one call, but the desktop still holds the frame that had it
    /// in it: a picture taken then is the old screen, with the window asking still standing in front
    /// of what was to be looked at. Pumping the queue applies the hide, and waiting for composition
    /// is what leaves that frame behind — which is the part a sleep cannot stand in for.
    /// </remarks>
    public async Task SettleAsync()
    {
        Dispatcher.UIThread.RunJobs();
        await Task.Yield();
        ScreenComposed.Wait();
        Dispatcher.UIThread.RunJobs();
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
    /// The places that are worth a frame: the windows out there, rather than the ones standing here
    /// asking. The list of windows a rule recognises keeps them either way — what it says is what a
    /// run would do, and an empty match text really does mean the window in front, this one
    /// included. It is only the frame that is left off: a window somebody is already looking at
    /// needs no ring around it, and the ring is drawn at its very edge, where it reads as the
    /// window's own border blinking.
    /// </summary>
    public static IReadOnlyList<FramePlace> Outside(IReadOnlyList<FramePlace> places)
    {
        var ours = Handles();
        return [.. places.Where(place => !ours.Contains(place.Handle))];
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
    /// The handles of those windows, read off the platform the same way the frame is placed with.
    /// A window that has not been shown yet has no handle to read and is nothing to keep a frame
    /// off either, so a zero is dropped rather than matched against the desktop's own numbering.
    /// </summary>
    private static IReadOnlyList<long> OwnHandles()
        => [.. Open().Select(window => window.TryGetPlatformHandle()?.Handle.ToInt64() ?? 0)
            .Where(handle => handle != 0)];

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
        /// Takes the window off the screen. One that came off with its owner is hidden again here,
        /// which costs nothing and is what leaves Avalonia agreeing that it is hidden — it is the
        /// showing afterwards that has to find it that way.
        /// </summary>
        public void Take() => Window.Hide();

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
