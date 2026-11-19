using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Devices;
using WhaleGenie.Models;

namespace WhaleGenie.Runtime;

/// <summary>
/// Everything the eight watches share: the plan being watched, the state that survives between
/// passes, the file watches, and the one place a key press is turned into a start or a stop.
/// </summary>
/// <remarks>
/// A watch is only ever asked about its own kind, and only the watches that are armed for a rhythm
/// run on it. One watch throwing does not stop the others: a refused screen read must not also cost
/// the clock and the windows their turn.
/// </remarks>
internal sealed class TriggerHub : IDisposable
{
    private readonly TriggerStateStore _state = new();
    private readonly WatchPool _watches = new();
    private readonly HeldKeys _held = new();
    private readonly ColourTrigger _colour;
    private readonly KeyTrigger _keys;
    private readonly HotstringRecognizer _recognizer = new();
    private readonly HotstringTrigger _hotstrings;
    private readonly TimerTrigger _timers = new();
    private readonly FileTrigger _files;
    private readonly IdleTrigger _idle = new();
    private readonly ProgramTrigger _programs;
    private readonly WindowTrigger _windows;
    private readonly IReadOnlyList<ITriggerPoll> _pass;
    private readonly IGamepadReading _pads;
    private readonly GamepadButtonWatch _padHeld = new();
    private readonly ITriggerRunner _runner;

    private TriggerPlan _plan = TriggerPlan.Empty;
    private DateTime _lastPolledAt;

    public TriggerHub(IDeviceLayer devices, IWindowEvents windowEvents, ITriggerRunner runner)
    {
        _runner = runner;
        _colour = new ColourTrigger(devices.Screen, devices.Input);
        _keys = new KeyTrigger(_held);
        _hotstrings = new HotstringTrigger(_recognizer, _held, devices.Input);
        _files = new FileTrigger(devices.Files, _watches);
        _programs = new ProgramTrigger(devices.Processes);
        _windows = new WindowTrigger(devices.Windows, windowEvents);
        _pads = devices.GamepadReading;
        // A window is watched on the shared clock as well as when the machine says one changed.
        // The straight-away path catches one that is only up for a moment; this one is the fallback
        // for a machine that says nothing. Poll() throttles itself, so having both is harmless.
        _pass = [_colour, _timers, _files, _windows, _idle];
    }

    public TriggerPlan Plan => _plan;

    internal TriggerStateStore State => _state;

    /// <summary>Whether the machine says when a window changes.</summary>
    public bool WindowsTold => _windows.Told;

    /// <summary>
    /// Takes a new plan, and lets go of what belonged to entries that are no longer watching the
    /// same thing. This runs when the project changes, not on a pass, so switching the system off
    /// does not keep a file watch alive on a path nobody is watching for.
    /// </summary>
    public void Publish(TriggerPlan plan)
    {
        _plan = plan;
        _state.Reconcile(plan);
        _watches.Reconcile([.. plan.OfKind(TriggerKind.File).Select(trigger => trigger.Id)]);
    }

    /// <summary>One pass of the shared clock: the pixels, the schedules, the files and the quiet.</summary>
    public void Look(DateTime now)
    {
        _lastPolledAt = now;
        var round = Round(now);
        foreach (var watch in _pass)
        {
            try
            {
                watch.Poll(round);
            }
            catch (Exception error)
            {
                AppDiagnostics.Record($"trigger.{watch.Kind}", error);
            }
        }
    }

    /// <summary>The programs, on a clock of their own.</summary>
    public void Programs(DateTime now) => Guarded(TriggerKind.Process, () => _programs.Poll(Round(now)));

    /// <summary>The windows, on the clock or because the machine said something changed.</summary>
    public void Windows(DateTime now, bool straightAway)
    {
        try
        {
            if (straightAway)
            {
                _windows.PollStraightAway(Round(now));
            }
            else
            {
                _windows.Poll(Round(now));
            }
        }
        catch (Exception error)
        {
            AppDiagnostics.Record("trigger.window-events", error);
        }
    }

    /// <summary>The controller, counting a button that was already down as not pressed now.</summary>
    public void TakeUpGamepad()
    {
        try
        {
            _padHeld.Take(_pads.Held());
        }
        catch (Exception)
        {
            _padHeld.Take(GamepadButtons.None);
        }
    }

    /// <summary>
    /// Reads the controller once. A button going down and coming back up is written down the way a
    /// key is, so a combination may hold a controller button and a key together.
    /// </summary>
    public void Pad(DateTime now)
    {
        GamepadButtons held;
        try
        {
            held = _pads.Held();
        }
        catch (Exception)
        {
            // A controller that cannot be read is a controller with nothing to say; the next round
            // tries again rather than the timer dying.
            return;
        }

        foreach (var (button, down) in _padHeld.Changed(held))
        {
            var name = GamepadNames.Binding(button);
            if (name.Length == 0)
            {
                continue;
            }

            if (down)
            {
                _held.Press(name);
            }
            else
            {
                _held.Release(name);
            }

            Key(now, name, down);
        }
    }

    /// <summary>A key, mouse button or wheel notch going down or coming up, from the hook thread.</summary>
    public void Key(DateTime now, string key, bool pressed)
    {
        var round = Round(now);
        if (pressed)
        {
            _keys.Pressed(round, key);
        }
        else
        {
            _keys.Released(round, key);
        }
    }

    /// <summary>
    /// One typed character, answered from the hook thread. True when a typed string matched and its
    /// macro was started, which is also when the ending key is suppressed.
    /// </summary>
    public bool Hotstring(DateTime now, KeyHookEvent e) => _hotstrings.Press(Round(now), e);

    /// <summary>A key or controller button going down, for the combinations answered later.</summary>
    public void Hold(string key) => _held.Press(key);

    /// <summary>A key or controller button coming up.</summary>
    public void Release(string key) => _held.Release(key);

    /// <summary>Whether a binding is the one that opens the project menu.</summary>
    public bool Menu(string binding)
        => _plan.MenuLauncher.Length > 0 && _held.Binds(_plan.MenuLauncher, binding);

    /// <summary>Drops what has been typed so far, so the next character starts a new word.</summary>
    public void ResetTyped() => _recognizer.Reset();

    /// <summary>What every armed trigger is waiting for, for the run window.</summary>
    public IReadOnlyList<TriggerWatch> Report(DateTime now)
    {
        var round = Round(now);
        var rows = new List<TriggerWatch>();
        foreach (var watch in _pass)
        {
            rows.AddRange(watch.Report(round));
        }

        rows.AddRange(_keys.Report(round));
        rows.AddRange(_programs.Report(round));
        foreach (var trigger in _plan.OfKind(TriggerKind.Hotstring))
        {
            rows.Add(new TriggerWatch(trigger.Id, trigger.Macro.Name, trigger.Looking, null,
                round.LastPolledAt, DateTime.MinValue, true));
        }

        return rows;
    }

    /// <summary>True when a colour trigger asked for its macro to stop after the current pass.</summary>
    public bool Stopping(string macroName) => _colour.Stopping(macroName);

    /// <summary>Forgets the colour bookkeeping of a run that has ended.</summary>
    public void RunEnded(string macroName) => _colour.Forget(macroName);

    /// <summary>Forgets everything kept between passes, as switching the system off does.</summary>
    public void Reset()
    {
        _state.Clear();
        _colour.Clear();
        _held.Clear();
        _recognizer.Reset();
    }

    /// <summary>Closes every file watch. The pool can be used again when the system is switched on.</summary>
    public void CloseAll() => _watches.Dispose();

    public void Dispose() => CloseAll();

    private TriggerRound Round(DateTime now) => new(_plan, _runner, _state, now, _lastPolledAt);

    private void Guarded(TriggerKind kind, Action work)
    {
        try
        {
            work();
        }
        catch (Exception error)
        {
            AppDiagnostics.Record($"trigger.{kind}", error);
        }
    }
}
