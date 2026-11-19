using System;
using System.Threading;
using WhaleGenie.Core.Devices;

namespace WhaleGenie.Runtime;

// Hooking the machine and letting it go, including the notice that a window changed.
public sealed partial class MacroTriggerService
{
    /// <summary>
    /// Puts the hook, the timers and the window notices in place. The hook is recorded before it is
    /// subscribed to, so a subscription that throws half way can still be undone: a listener left
    /// behind would go on answering for a system that is switched off.
    /// </summary>
    private void Attach()
    {
        var hook = _hookSource ?? _devices.Hook;
        _hook = hook;
        try
        {
            hook.KeyDown += OnKeyDown;
            hook.KeyUp += OnKeyUp;
            hook.MouseDown += OnMouseDown;
            hook.MouseUp += OnMouseUp;
            hook.Wheel += OnWheel;
        }
        catch (Exception error)
        {
            // A hook that will not go in means no key and no mouse button can be answered to. The
            // pixels, the windows, the programs and the clock are all watched by looking rather
            // than by being told, so those keep working: turning the whole system off because one
            // part of it is missing would stop a macro whose trigger was never about a key.
            AppDiagnostics.Record("trigger.attach", error);
            ReleaseHook();
        }

        _hub.Reset();

        _poll.Change(TriggerPace.Poll, TriggerPace.Poll);
        _programs.Change(TriggerPace.Program, TriggerPace.Program);

        // What the controller is holding is taken up here rather than counted as pressed: a button
        // that was down before the switch went on is not somebody asking for something now.
        _hub.TakeUpGamepad();
        _pad.Change(TriggerPace.Pad, TriggerPace.Pad);

        // The machine is asked to say when a window changes, which is what makes a window that is
        // only up for a moment worth waiting for. It keeps saying so while the switch is on, and
        // the reading on the timer goes on underneath: a machine that stops saying, or never said,
        // leaves everything working, only later.
        _windowEvents.Changed += OnWindowsChanged;
        _windowEvents.ForegroundChanged += OnForegroundChanged;
        _windowEvents.Start();
    }

    private void Detach()
    {
        _windowEvents.Changed -= OnWindowsChanged;
        _windowEvents.ForegroundChanged -= OnForegroundChanged;

        ReleaseHook();

        _poll.Change(Timeout.Infinite, Timeout.Infinite);
        _programs.Change(Timeout.Infinite, Timeout.Infinite);
        _pad.Change(Timeout.Infinite, Timeout.Infinite);

        // A file watch holds an operating-system handle, and a colour trigger holds an edge. Both
        // belong to a system that is on, so switching it off closes and forgets them.
        StopAll();
        _hub.CloseAll();

        // Nothing the switch was running is left to look at the screen. The sessions are kept for
        // reuse while the system is on, so this is the moment to give them back rather than hold
        // the graphics device until the program goes away.
        try
        {
            _devices.Screen.ReleaseCaptures();
        }
        catch (Exception)
        {
            // Switching the system off is not a macro step. A session that will not close now is
            // still closed by the program's own exit, and it must not leave the switch half off.
        }
    }

    private void ReleaseHook()
    {
        if (_hook is not { } hook)
        {
            return;
        }

        _hook = null;
        try
        {
            hook.KeyDown -= OnKeyDown;
            hook.KeyUp -= OnKeyUp;
            hook.MouseDown -= OnMouseDown;
            hook.MouseUp -= OnMouseUp;
            hook.Wheel -= OnWheel;
        }
        catch (Exception error)
        {
            AppDiagnostics.Record("trigger.detach", error);
        }
    }

    /// <summary>
    /// The machine saying a window has appeared or gone away. The window list is read there and
    /// then rather than on its next round, because the window worth catching is one that is only up
    /// for a moment, and by the next round it is gone.
    /// </summary>
    private void OnWindowsChanged()
    {
        if (!Listening || MacroTriggerGate.IsOpen)
        {
            return;
        }

        // A burst of events — one window being made says so several times over — rides along with
        // the pass that is already under way instead of queueing up behind it, and is answered by
        // one reading at the end.
        if (Interlocked.Exchange(ref _windowPass, 1) == 1)
        {
            Interlocked.Exchange(ref _windowAgain, 1);
            return;
        }

        try
        {
            do
            {
                Interlocked.Exchange(ref _windowAgain, 0);
                _hub.Windows(DateTime.Now, straightAway: true);
            }
            while (Interlocked.Exchange(ref _windowAgain, 0) == 1);
        }
        catch (Exception error)
        {
            // Being told about a window must never take the program down; the timer goes on.
            AppDiagnostics.Record("trigger.window-events", error);
        }
        finally
        {
            Interlocked.Exchange(ref _windowPass, 0);
        }
    }

    private void OnForegroundChanged() => _hub.ResetTyped();
}
