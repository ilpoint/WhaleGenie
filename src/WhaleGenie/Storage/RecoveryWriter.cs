using System;
using Avalonia.Threading;

namespace WhaleGenie.Storage;

/// <summary>
/// When the recovery snapshot is written: when the work it stands for changes, and — while it
/// keeps changing — no more often than once an interval. Nothing is written over work that is
/// sitting still. A snapshot rewritten on a timer whether anything moved or not is a disk write
/// and a serialising with nothing to show for it, and it leaves the file claiming to be seconds
/// old about work that may not have been touched for hours.
/// </summary>
/// <remarks>
/// A change is not written the moment it arrives: while somebody is typing, every keystroke is a
/// change, and a write for each one turns a sentence into a hundred writes. Waiting for the
/// changes to pause is what makes "written as it changes" mean one write per burst of work rather
/// than one per keystroke. What is being written is the state of the windows, so the clock is the
/// user-interface thread's.
/// </remarks>
internal sealed class RecoveryWriter
{
    /// <summary>
    /// How long the newest change may sit unwritten. Long enough that a burst of changes is one
    /// write, short enough that a stop without notice costs a moment of work at most.
    /// </summary>
    internal static TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(3);

    private readonly Action _write;
    private DispatcherTimer? _timer;

    /// <summary>Whether a change has arrived that has not been written yet.</summary>
    private bool _waiting;

    public RecoveryWriter(Action write) => _write = write;

    /// <summary>Something changed: write it once the changes pause.</summary>
    public void Changed()
    {
        _waiting = true;
        if (_timer is null)
        {
            _timer = new DispatcherTimer { Interval = Interval };
            _timer.Tick += (_, _) => Flush();
        }

        // Left running when it is already running, rather than restarted: restarting would push
        // the write further away with every keystroke, and somebody who types without ever pausing
        // would never be written at all.
        if (!_timer.IsEnabled)
        {
            _timer.Start();
        }
    }

    /// <summary>
    /// Writes now instead of waiting. For the change that first makes work unsaved: that one turns
    /// "nothing to lose" into "something to lose", and work pasted in and then lost to a crash a
    /// moment later must not be waiting on a timer to be kept.
    /// </summary>
    public void Now()
    {
        _timer?.Stop();
        _waiting = false;
        _write();
    }

    /// <summary>Stops watching. Whatever was written already stays where it is.</summary>
    public void Stop()
    {
        _timer?.Stop();
        _waiting = false;
    }

    private void Flush()
    {
        _timer?.Stop();
        if (!_waiting)
        {
            return;
        }

        _waiting = false;
        _write();
    }
}
