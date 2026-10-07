using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SharpHook;
using Viktor.Core.Devices;
using Viktor.Core.Devices.Platform;
using Viktor.Core.Execution;
using Viktor.Localization;
using Viktor.Models;

namespace Viktor.Execution;

/// <summary>
/// Watches the keyboard, the watched pixels and the clock for what the macros in the list are
/// waiting for, and runs them the way their trigger settings ask. The main window's system switch
/// turns the whole thing on and off.
/// </summary>
public sealed class MacroTriggerService : IDisposable
{
    /// <summary>How often a watched pixel is looked at, and the clock is asked the time.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(60);

    /// <summary>
    /// How often the open windows are looked at. A title comes free with the listing, but a
    /// program name or a window class costs a look-up per window, so the window list is not read
    /// as often as a pixel: half a second late is soon enough to notice a window opening.
    /// </summary>
    private static readonly TimeSpan WindowPollInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>A breather between the passes of a macro that repeats, so it cannot spin.</summary>
    private const int RepeatGapMs = 10;

    private readonly Func<IReadOnlyList<MacroItem>> _macros;
    private readonly IDeviceLayer _devices;
    private readonly bool _ownsDevices;
    private readonly object _gate = new();
    private readonly object _keys = new();
    private readonly Dictionary<MacroItem, CancellationTokenSource> _running = [];
    private readonly HashSet<MacroItem> _colourTriggered = [];
    private readonly HashSet<MacroItem> _colourHeld = [];
    private readonly HashSet<MacroItem> _colourStopping = [];
    private readonly HashSet<MacroItem> _spent = [];
    private readonly Dictionary<MacroItem, DateTime> _scheduleDue = [];
    private readonly Dictionary<MacroItem, FileWatch> _watches = [];
    private readonly Dictionary<MacroItem, HashSet<int>> _programsRunning = [];
    private readonly Dictionary<MacroItem, bool> _windowsThere = [];
    private readonly HashSet<MacroItem> _idleDone = [];
    private readonly HashSet<string> _heldKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Timer _poll;

    private IGlobalHook? _hook;
    private DateTime _windowsDueAt;
    private bool _enabled;
    private bool _disposed;

    /// <summary>
    /// Raised when a macro starts or stops, so the window can show which ones are running. It is
    /// raised on whatever thread noticed, which is never the interface thread.
    /// </summary>
    public event Action? StatusChanged;

    public MacroTriggerService(Func<IReadOnlyList<MacroItem>> macros, IDeviceLayer? devices = null)
    {
        _macros = macros;
        _ownsDevices = devices is null;
        _devices = devices ?? new WindowsDeviceLayer();
        _poll = new Timer(_ => Poll(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>
    /// Asked when a step of a triggered macro has a failure rule of "ask me". Set by the window
    /// that can ask; without it a run that wants to ask stops, which says more than guessing.
    /// </summary>
    public Func<string, string, string, string, Task<StepErrorChoice>>? Ask { get; set; }

    /// <summary>The system switch on the main window.</summary>
    public bool IsEnabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value || _disposed)
            {
                return;
            }

            _enabled = value;
            if (value)
            {
                Attach();
            }
            else
            {
                Detach();
            }
        }
    }

    /// <summary>The macros whose bodies are running right now.</summary>
    public IReadOnlyList<MacroItem> Running
    {
        get
        {
            lock (_gate)
            {
                return [.. _running.Keys];
            }
        }
    }

    public bool IsRunning(MacroItem macro)
    {
        lock (_gate)
        {
            return _running.ContainsKey(macro);
        }
    }

    /// <summary>Stops a macro the trigger started.</summary>
    public void Stop(MacroItem macro)
    {
        CancellationTokenSource? cancellation;
        lock (_gate)
        {
            _colourHeld.Remove(macro);
            if (!_running.Remove(macro, out cancellation))
            {
                _colourStopping.Remove(macro);
                return;
            }

            _colourStopping.Remove(macro);
        }

        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The run finished on its own between the two lines; there is nothing to stop.
        }

        StatusChanged?.Invoke();
    }

    /// <summary>Stops everything the trigger started, which is what switching the system off does.</summary>
    public void StopAll()
    {
        List<CancellationTokenSource> cancellations;
        lock (_gate)
        {
            cancellations = [.. _running.Values];
            _running.Clear();
            _spent.Clear();
            _colourTriggered.Clear();
            _colourHeld.Clear();
            _colourStopping.Clear();
            _scheduleDue.Clear();
            _programsRunning.Clear();
            _windowsThere.Clear();
            _idleDone.Clear();
        }

        foreach (var cancellation in cancellations)
        {
            try
            {
                cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already finished.
            }
        }

        StatusChanged?.Invoke();
    }

    /// <summary>
    /// Arms a macro again after it was disarmed and re-armed, so a "trigger once" macro can be
    /// used a second time without reloading the project.
    /// </summary>
    public void Rearm(MacroItem macro)
    {
        lock (_gate)
        {
            _spent.Remove(macro);
            _colourTriggered.Remove(macro);
            _colourHeld.Remove(macro);
            _colourStopping.Remove(macro);
            _scheduleDue.Remove(macro);
            _programsRunning.Remove(macro);
            _windowsThere.Remove(macro);
            _idleDone.Remove(macro);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Detach();
        _poll.Dispose();

        if (_ownsDevices && _devices is IDisposable owned)
        {
            owned.Dispose();
        }
    }

    // ---------------------------------------------------------------- hooking

    private void Attach()
    {
        try
        {
            var hook = GlobalInputHook.Shared.Hook;
            hook.KeyPressed += OnKeyPressed;
            hook.KeyReleased += OnKeyReleased;
            hook.MousePressed += OnMousePressed;
            hook.MouseReleased += OnMouseReleased;
            hook.MouseWheel += OnMouseWheel;
            _hook = hook;
        }
        catch (Exception)
        {
            // Without a hook there is nothing to answer to; the switch simply does nothing.
            _hook = null;
            _enabled = false;
            return;
        }

        lock (_gate)
        {
            _spent.Clear();
            _colourTriggered.Clear();
            _colourHeld.Clear();
            _colourStopping.Clear();
        }

        _poll.Change(PollInterval, PollInterval);
    }

    private void Detach()
    {
        if (_hook is { } hook)
        {
            hook.KeyPressed -= OnKeyPressed;
            hook.KeyReleased -= OnKeyReleased;
            hook.MousePressed -= OnMousePressed;
            hook.MouseReleased -= OnMouseReleased;
            hook.MouseWheel -= OnMouseWheel;
            _hook = null;
        }

        _poll.Change(Timeout.Infinite, Timeout.Infinite);
        StopAll();
    }

    private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        if (!Listening)
        {
            return;
        }

        // Bound keys carry their side, so the right Shift and the left one are told apart.
        var key = KeyNames.Name(e.Data.KeyCode, sided: true);
        if (key.Length == 0)
        {
            return;
        }

        lock (_keys)
        {
            _heldKeys.Add(key);
        }

        Fire(key, pressed: true);
    }

    private void OnKeyReleased(object? sender, KeyboardHookEventArgs e)
    {
        if (!Listening)
        {
            return;
        }

        var key = KeyNames.Name(e.Data.KeyCode, sided: true);
        if (key.Length == 0)
        {
            return;
        }

        lock (_keys)
        {
            _heldKeys.Remove(key);
        }

        Fire(key, pressed: false);
    }

    /// <summary>True while a key press should be looked at.</summary>
    private bool Listening => _enabled && !_disposed && !MacroTriggerGate.IsOpen;

    /// <summary>A mouse button answers a macro the same way a key does.</summary>
    private void OnMousePressed(object? sender, MouseHookEventArgs e)
    {
        if (!Listening)
        {
            return;
        }

        var button = KeyNames.MouseName(e.Data.Button);
        if (button.Length == 0)
        {
            return;
        }

        lock (_keys)
        {
            _heldKeys.Add(button);
        }

        Fire(button, pressed: true);
    }

    private void OnMouseReleased(object? sender, MouseHookEventArgs e)
    {
        if (!Listening)
        {
            return;
        }

        var button = KeyNames.MouseName(e.Data.Button);
        if (button.Length == 0)
        {
            return;
        }

        lock (_keys)
        {
            _heldKeys.Remove(button);
        }

        Fire(button, pressed: false);
    }

    /// <summary>
    /// A wheel notch has no "down" state, so it counts as a press that is let go again straight
    /// away. A trigger bound to a wheel fires once per notch, the way a key press does.
    /// </summary>
    private void OnMouseWheel(object? sender, MouseWheelHookEventArgs e)
    {
        if (!Listening)
        {
            return;
        }

        var notch = KeyNames.WheelName(e.Data.Direction, e.Data.Rotation);
        Fire(notch, pressed: true);
        Fire(notch, pressed: false);
    }

    private void Fire(string key, bool pressed)
    {
        // A key Viktor is sending itself is its own output, not the user asking for something.
        if (ViktorInputGate.RecentlySent(key))
        {
            return;
        }

        foreach (var macro in Snapshot())
        {
            if (!macro.IsEnabled || macro.TriggerMode != MacroTrigger.KeystrokesButtonInputs)
            {
                continue;
            }

            if (!Binds(macro.BindKey, key))
            {
                continue;
            }

            if (pressed)
            {
                Pressed(macro);
            }
            else
            {
                Released(macro);
            }
        }
    }

    // ---------------------------------------------------------------- triggers

    /// <summary>
    /// Whether a macro's binding answers to a key. A binding may name a combination, in which
    /// case the modifiers have to be down for it to count.
    /// </summary>
    private bool Binds(string binding, string key)
    {
        lock (_keys)
        {
            return Binds(binding, key, _heldKeys);
        }
    }

    /// <summary>
    /// Whether a binding answers to a key that was just pressed, with the named modifiers down.
    /// The words the binding is asked about are plain names, so this is where the two sides of a
    /// modifier and the older side-agnostic spelling are sorted out.
    /// </summary>
    internal static bool Binds(string binding, string key, IEnumerable<string> held)
    {
        var parts = binding.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || !Same(parts[^1], key))
        {
            return false;
        }

        for (var index = 0; index < parts.Length - 1; index++)
        {
            if (!held.Any(heldKey => Same(parts[index], heldKey)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Compares two key names. The key picker spells the top row the way the interface library
    /// does (<c>D7</c>) while the hook calls the same key <c>7</c>, and a binding says which
    /// shift it means (<c>右Shift</c>) unless it was written before the sides were told apart.
    /// </summary>
    internal static bool Same(string? first, string? second)
    {
        var left = Alias(first);
        var right = Alias(second);

        if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // A binding of just "Shift" is the older, side-agnostic spelling: it answers to whichever
        // shift is pressed, so the macros written before the sides existed keep working.
        return StandsFor(left, right) || StandsFor(right, left);
    }

    /// <summary>Whether a bare modifier name means the side the other name spells out.</summary>
    private static bool StandsFor(string bare, string sided)
        => Sides.TryGetValue(bare, out var sides)
           && sides.Contains(sided, StringComparer.OrdinalIgnoreCase);

    /// <summary>The halves a bare modifier name stands for.</summary>
    private static readonly Dictionary<string, string[]> Sides = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ctrl"] = ["左ctrl", "右ctrl"],
        ["control"] = ["左ctrl", "右ctrl"],
        ["shift"] = ["左shift", "右shift"],
        ["alt"] = ["左alt", "右alt"],
        ["win"] = ["左win", "右win"],
        ["windows"] = ["左win", "右win"],
        ["meta"] = ["左win", "右win"],
        ["super"] = ["左win", "右win"],
    };

    /// <summary>Trims a name to the spelling the comparisons use.</summary>
    private static string Alias(string? name)
    {
        var text = (name ?? string.Empty).Trim();

        // The key picker spells the top row the way the interface library does (D7), while the
        // hook calls the same key 7.
        if (text.Length == 2 && (text[0] is 'D' or 'd') && char.IsAsciiDigit(text[1]))
        {
            return text[1..];
        }

        // The sided names are written with Chinese words in the interface and with English words
        // in hand-written macros; both have to compare as the same key.
        return text.ToLowerInvariant() switch
        {
            "leftctrl" or "leftcontrol" or "lctrl" => "左ctrl",
            "rightctrl" or "rightcontrol" or "rctrl" => "右ctrl",
            "leftshift" or "lshift" => "左shift",
            "rightshift" or "rshift" => "右shift",
            "leftalt" or "lalt" => "左alt",
            "rightalt" or "ralt" => "右alt",
            "leftwin" or "leftmeta" or "lwin" => "左win",
            "rightwin" or "rightmeta" or "rwin" => "右win",
            _ => text.ToLowerInvariant(),
        };
    }

    private void Pressed(MacroItem macro)
    {
        switch (macro.LoopMode)
        {
            case MacroLoop.Press:
                Start(macro, repeating: false);
                break;

            case MacroLoop.Hold:
                Start(macro, repeating: true);
                break;

            case MacroLoop.Toggle:
                if (IsRunning(macro))
                {
                    Stop(macro);
                }
                else
                {
                    Start(macro, repeating: true);
                }

                break;
        }
    }

    private void Released(MacroItem macro)
    {
        switch (macro.LoopMode)
        {
            case MacroLoop.Hold:
                Stop(macro);
                break;

            case MacroLoop.Release:
                Start(macro, repeating: false);
                break;
        }
    }

    /// <summary>Looks at every watched pixel and treats a change of state as a press or a release.</summary>
    private void Poll()
    {
        if (!Listening)
        {
            return;
        }

        var macros = Snapshot();
        foreach (var macro in macros)
        {
            if (!macro.IsEnabled || macro.TriggerMode != MacroTrigger.ColorPixelChanges)
            {
                continue;
            }

            bool observed;
            try
            {
                observed = ColourMatches(macro);
            }
            catch (Exception)
            {
                continue;   // reading the screen can fail outside a session
            }

            bool matched;
            lock (_gate)
            {
                matched = observed;
                if (matched == _colourTriggered.Contains(macro))
                {
                    continue;
                }

                if (matched)
                {
                    _colourTriggered.Add(macro);
                }
                else
                {
                    _colourTriggered.Remove(macro);
                }
            }

            if (matched)
            {
                // The macro's own mouse moves change the pixel under the cursor; they must not
                // look like the colour coming back and restart the macro over and over.
                if (IsRunning(macro))
                {
                    continue;
                }

                Pressed(macro);

                lock (_gate)
                {
                    if (_running.ContainsKey(macro)
                        && macro.LoopMode is MacroLoop.Toggle or MacroLoop.Hold)
                    {
                        _colourHeld.Add(macro);
                    }
                }
            }
            else
            {
                bool held;
                lock (_gate)
                {
                    held = _colourHeld.Remove(macro);
                    if (held)
                    {
                        // Let the pass that is running finish before the macro stops, rather
                        // than cutting it off in the middle of its steps.
                        _colourStopping.Add(macro);
                    }
                }

                // A colour that stops matching stops the macro it started; anything else keeps
                // the loop mode's own release behaviour (a "once on release" macro, say).
                if (held)
                {
                    continue;
                }

                Released(macro);
            }
        }

        PollTimers(macros);
        PollFiles(macros);
        PollProcesses(macros);
        PollWindows(macros);
        PollIdle(macros);
        PruneWatches(macros);
    }

    /// <summary>True when a running macro has been asked to stop once its current pass ends.</summary>
    private bool StoppingAfterPass(MacroItem macro)
    {
        lock (_gate)
        {
            return _colourStopping.Contains(macro);
        }
    }

    /// <summary>Runs the macros a timer trigger is waiting for.</summary>
    private void PollTimers(IReadOnlyList<MacroItem> macros)
    {
        var now = DateTime.Now;

        foreach (var macro in macros)
        {
            if (!macro.IsEnabled || macro.TriggerMode != MacroTrigger.Timer)
            {
                continue;
            }

            // A schedule that has had its one turn waits until the macro is re-armed.
            if (macro.TriggerOnce && IsSpent(macro))
            {
                continue;
            }

            var fire = false;
            lock (_gate)
            {
                if (!_scheduleDue.TryGetValue(macro, out var due))
                {
                    // Counting starts when the system is switched on, so turning it on does not
                    // set off every interval macro in the list at once.
                    if (NextDue(macro, now) is { } first)
                    {
                        _scheduleDue[macro] = first;
                    }
                }
                else if (now >= due)
                {
                    fire = true;

                    // The next run counts from the one that has just come round, so the schedule
                    // does not drift. When the machine was away past several of them, the wait
                    // starts again from now rather than firing them off in a burst.
                    var next = NextDue(macro, due);
                    if (next is not { } armed || armed <= now)
                    {
                        next = NextDue(macro, now);
                    }

                    if (next is { } moment)
                    {
                        _scheduleDue[macro] = moment;
                    }
                    else
                    {
                        _scheduleDue.Remove(macro);
                    }
                }
            }

            if (fire)
            {
                // One pass for each moment the schedule comes round. A timer says how often to
                // start, so "while holding" and "until pressed again" have nothing to hold on to;
                // a macro that wants to keep going writes its own loop.
                Start(macro, repeating: false);
            }
        }
    }

    /// <summary>When this macro's schedule next wants to run, or null when it is not filled in.</summary>
    private static DateTime? NextDue(MacroItem macro, DateTime from)
        => MacroSchedule.TryNextDue(macro, from, out var due) ? due : null;

    /// <summary>Runs the macros whose watched file or folder has changed.</summary>
    private void PollFiles(IReadOnlyList<MacroItem> macros)
    {
        var now = DateTime.Now;

        foreach (var macro in macros)
        {
            if (!macro.IsEnabled || macro.TriggerMode != MacroTrigger.FileChanges)
            {
                continue;
            }

            if (Watch(macro) is not { } watch)
            {
                continue;
            }

            watch.Ensure(now);

            // A watch that has had its one turn waits until the macro is re-armed.
            if (macro.TriggerOnce && IsSpent(macro))
            {
                continue;
            }

            if (watch.Take(now))
            {
                // One pass for each change, the way a timer does it: there is nothing to hold on
                // to, so a macro that wants to keep going writes its own loop.
                Start(macro, repeating: false);
            }
        }
    }

    /// <summary>
    /// The watch belonging to a macro: built the first time the macro is seen, and built again
    /// when its path or its settings change.
    /// </summary>
    private FileWatch? Watch(MacroItem macro)
    {
        string path;
        try
        {
            path = _devices.Files.Resolve(macro.WatchPath.Trim());
        }
        catch (Exception)
        {
            return null;    // no files on this machine, so there is nothing to watch
        }

        if (path.Length == 0)
        {
            return null;
        }

        var signature = FileWatch.SignatureOf(macro, path);
        lock (_gate)
        {
            if (_watches.TryGetValue(macro, out var existing))
            {
                if (existing.Signature == signature)
                {
                    return existing;
                }

                existing.Dispose();
                _watches.Remove(macro);
            }

            var watch = FileWatch.Create(macro, path);
            _watches[macro] = watch;
            return watch;
        }
    }

    /// <summary>
    /// Runs the macros waiting for a program to start or to finish. The processes with the wanted
    /// name are counted afresh on every poll, so a difference from the count before is what
    /// happened since — a program that was already running is not something that has just started.
    /// </summary>
    private void PollProcesses(IReadOnlyList<MacroItem> macros)
    {
        foreach (var macro in macros)
        {
            if (!macro.IsEnabled || macro.TriggerMode != MacroTrigger.Process)
            {
                continue;
            }

            var name = macro.ProcessName.Trim();
            if (name.Length == 0)
            {
                continue;
            }

            HashSet<int> running;
            try
            {
                running = [.. _devices.Processes.Find(name)];
            }
            catch (Exception)
            {
                continue;   // another program's process list can be refused; the trigger waits
            }

            bool started;
            bool stopped;
            lock (_gate)
            {
                if (!_programsRunning.TryGetValue(macro, out var before))
                {
                    // Counting starts when the system is switched on, so switching it on does not
                    // set off every macro whose program happens to be open already.
                    _programsRunning[macro] = running;
                    continue;
                }

                started = running.Except(before).Any();
                stopped = before.Except(running).Any();
                _programsRunning[macro] = running;
            }

            // A "once" macro waits to be re-armed rather than watching for ever.
            if (macro.TriggerOnce && IsSpent(macro))
            {
                continue;
            }

            var wanted = macro.ProcessChange switch
            {
                ProcessChangeKind.Started => started,
                ProcessChangeKind.Stopped => stopped,
                _ => started || stopped,
            };

            if (wanted)
            {
                Start(macro, repeating: false);
            }
        }
    }

    /// <summary>
    /// Runs the macros waiting for a window to appear or to go away. A macro is answered by
    /// whether a window matching it is there at all, so a window whose title comes to match what
    /// the macro is waiting for counts as appearing, which is what "wait for the page to load"
    /// means in practice.
    /// </summary>
    private void PollWindows(IReadOnlyList<MacroItem> macros)
    {
        var now = DateTime.Now;
        if (now < _windowsDueAt)
        {
            return;
        }

        _windowsDueAt = now + WindowPollInterval;

        foreach (var macro in macros)
        {
            if (!macro.IsEnabled || macro.TriggerMode != MacroTrigger.Window)
            {
                continue;
            }

            var value = macro.WindowValue.Trim();
            if (value.Length == 0)
            {
                continue;
            }

            bool there;
            try
            {
                there = _devices.Windows.Find(value, macro.WindowLookup) is not null;
            }
            catch (Exception)
            {
                continue;   // the window list can be refused; the trigger simply waits
            }

            bool before;
            lock (_gate)
            {
                if (!_windowsThere.TryGetValue(macro, out before))
                {
                    // What is open when the system is switched on is not something that has just
                    // appeared.
                    _windowsThere[macro] = there;
                    continue;
                }

                _windowsThere[macro] = there;
            }

            if (before == there)
            {
                continue;
            }

            // A "once" macro waits to be re-armed rather than watching for ever.
            if (macro.TriggerOnce && IsSpent(macro))
            {
                continue;
            }

            var wanted = macro.WindowChange switch
            {
                WindowChangeKind.Appeared => there,
                WindowChangeKind.Disappeared => !there,
                _ => true,
            };

            if (wanted)
            {
                Start(macro, repeating: false);
            }
        }
    }

    /// <summary>
    /// Runs the macros waiting for the machine to be left alone. One run per quiet spell: once it
    /// has run, the macro waits for input to arrive and the machine to fall quiet again, so a
    /// machine left alone overnight does not set the same macro off all night.
    /// </summary>
    /// <remarks>
    /// Input anywhere counts, including what a macro sends itself: a macro that moves the mouse
    /// counts as having used the machine.
    /// </remarks>
    private void PollIdle(IReadOnlyList<MacroItem> macros)
    {
        var idle = IdleWatch.Since();

        foreach (var macro in macros)
        {
            if (!macro.IsEnabled || macro.TriggerMode != MacroTrigger.Idle)
            {
                continue;
            }

            if (idle < TimeSpan.FromSeconds(Math.Max(1, macro.IdleSeconds)))
            {
                lock (_gate)
                {
                    _idleDone.Remove(macro);
                }

                continue;
            }

            bool first;
            lock (_gate)
            {
                first = _idleDone.Add(macro);
            }

            if (first && !(macro.TriggerOnce && IsSpent(macro)))
            {
                Start(macro, repeating: false);
            }
        }
    }

    /// <summary>True when a "trigger once" macro has already had its turn.</summary>
    private bool IsSpent(MacroItem macro)
    {
        lock (_gate)
        {
            return _spent.Contains(macro);
        }
    }

    /// <summary>Forgets what was watched for macros that are gone, switched off or changed.</summary>
    private void PruneWatches(IReadOnlyList<MacroItem> macros)
    {
        lock (_gate)
        {
            if (_colourTriggered.Count == 0 && _colourHeld.Count == 0 && _colourStopping.Count == 0
                && _scheduleDue.Count == 0 && _watches.Count == 0 && _programsRunning.Count == 0
                && _windowsThere.Count == 0 && _idleDone.Count == 0)
            {
                return;
            }

            var tracked = new HashSet<MacroItem>(_colourTriggered);
            tracked.UnionWith(_colourHeld);
            tracked.UnionWith(_colourStopping);
            tracked.UnionWith(_scheduleDue.Keys);
            tracked.UnionWith(_programsRunning.Keys);
            tracked.UnionWith(_windowsThere.Keys);
            tracked.UnionWith(_idleDone);

            foreach (var item in tracked)
            {
                if (!macros.Contains(item) || !item.IsEnabled)
                {
                    _colourTriggered.Remove(item);
                    _colourHeld.Remove(item);
                    _colourStopping.Remove(item);
                    _scheduleDue.Remove(item);
                    _programsRunning.Remove(item);
                    _windowsThere.Remove(item);
                    _idleDone.Remove(item);
                    continue;
                }

                // A macro moved to another kind of trigger stands down like one that was removed,
                // so moving it back starts its watch over.
                if (item.TriggerMode != MacroTrigger.ColorPixelChanges)
                {
                    _colourTriggered.Remove(item);
                    _colourHeld.Remove(item);
                    _colourStopping.Remove(item);
                }

                if (item.TriggerMode != MacroTrigger.Timer)
                {
                    _scheduleDue.Remove(item);
                }

                if (item.TriggerMode != MacroTrigger.Process)
                {
                    _programsRunning.Remove(item);
                }

                if (item.TriggerMode != MacroTrigger.Window)
                {
                    _windowsThere.Remove(item);
                }

                if (item.TriggerMode != MacroTrigger.Idle)
                {
                    _idleDone.Remove(item);
                }
            }

            // A watch holds a listener of its own, so one that is no longer wanted has to be let
            // go of rather than only forgotten.
            foreach (var (item, watch) in _watches.ToList())
            {
                if (!macros.Contains(item) || !item.IsEnabled
                    || item.TriggerMode != MacroTrigger.FileChanges)
                {
                    watch.Dispose();
                    _watches.Remove(item);
                }
            }
        }
    }

    private bool ColourMatches(MacroItem macro)
    {
        var x = macro.ColorPositionX;
        var y = macro.ColorPositionY;

        if (macro.UseCursorPosition)
        {
            var cursor = _devices.Input.Cursor;
            x = cursor.X;
            y = cursor.Y;
        }

        var pixel = _devices.Screen.PixelAt(x, y);
        var wanted = PixelColor.Parse(macro.HexColor);
        var close = pixel.Matches(wanted, macro.ColorTolerance);

        return macro.ColorMatch == ColorMatchCondition.ColorMatches ? close : !close;
    }

    // ---------------------------------------------------------------- running

    private void Start(MacroItem macro, bool repeating)
    {
        CancellationTokenSource cancellation;
        lock (_gate)
        {
            if (!_enabled || _disposed || macro.Steps.Count == 0 || _running.ContainsKey(macro))
            {
                return;
            }

            if (macro.TriggerOnce && _spent.Contains(macro))
            {
                return;
            }

            if (macro.TriggerOnce)
            {
                _spent.Add(macro);
            }

            cancellation = new CancellationTokenSource();
            _running[macro] = cancellation;
        }

        StatusChanged?.Invoke();
        _ = Task.Run(() => RunAsync(macro, repeating, cancellation));
    }

    private async Task RunAsync(MacroItem macro, bool repeating, CancellationTokenSource cancellation)
    {
        var token = cancellation.Token;
        try
        {
            var steps = macro.Steps.ToExecutable();
            var library = Library();

            // Read once for the whole stay: a macro that repeats for an hour has no reason to
            // keep asking the settings file, and a change made while it runs can wait.
            var failureScreenshot = LocalSettings.LoadFailureScreenshot();

            // No debugger is behind a run a trigger started, but a step whose failure rule is
            // "ask me" still has to reach whoever can put the question on screen.
            var host = new AskRunHost(Ask);

            while (!token.IsCancellationRequested)
            {
                var runner = new MacroRunner(MacroVariables.Seed(), host, _devices, macro.DelayScale, library)
                {
                    FailureScreenshot = failureScreenshot,
                };
                var result = await runner.RunAsync(steps, token);
                if (result.Status != RunStatus.Completed || !repeating || StoppingAfterPass(macro))
                {
                    break;
                }

                await Task.Delay(RepeatGapMs, token);
            }
        }
        catch (OperationCanceledException)
        {
            // Stopping is the normal way for one of these to end.
        }
        catch (Exception)
        {
            // A macro that goes wrong must not take the application down with it.
        }
        finally
        {
            lock (_gate)
            {
                _running.Remove(macro);
                _colourHeld.Remove(macro);
                _colourStopping.Remove(macro);
            }

            cancellation.Dispose();
            StatusChanged?.Invoke();
        }
    }

    /// <summary>The project's macros, in the shape a "run another macro" step calls them by.</summary>
    private IMacroLibrary Library()
        => new ProjectMacroLibrary(Snapshot()
            .Where(macro => macro.Name.Trim().Length > 0)
            .Select(macro => (macro.Name.Trim(), macro.Steps.ToExecutable())));

    private IReadOnlyList<MacroItem> Snapshot()
    {
        try
        {
            // A copy, because the list is the interface's and it is read from other threads.
            return [.. _macros()];
        }
        catch (Exception)
        {
            return [];
        }
    }
}
