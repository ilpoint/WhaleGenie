using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;


namespace WhaleGenie.Runtime;

// Starting and stopping: the switches, what is running, the watches it reports and the hooks a run may ask.
public sealed partial class MacroTriggerService
{
    /// <param name="devices">
    /// The devices to watch and to run with. The program hands over the one layer it holds, so a
    /// triggered run uses the same devices — and the same loaded models — as a run started by hand;
    /// a check hands over a layer of its own, which is also where the hook and the word about
    /// windows changing come from.
    /// </param>
    /// <param name="hook">
    /// The keyboard and the mouse, as the bound keys and buttons are answered from. The whole
    /// program shares the one hook the machine allows, so only a check that wants to drive the
    /// events itself hands in one of its own; with none, the layer's own hook is used.
    /// </param>
    /// <param name="windowEvents">
    /// Where the word that a window changed comes from, or null for the layer's own. A check that is
    /// about something else hands in a machine with nothing to say, which leaves the window list to
    /// the timer exactly as a Windows too old to say anything does.
    /// </param>
    public MacroTriggerService(Func<IReadOnlyList<MacroItem>> macros, IDeviceLayer devices,
        IWindowEvents? windowEvents = null, IInputHook? hook = null)
    {
        _macros = macros;
        _legacyTriggers = ProjectTriggers.FromMacros(Snapshot());
        _legacyMode = true;
        _triggerSource = () => _legacyTriggers;
        _devices = devices;
        _windowEvents = windowEvents ?? devices.WindowEvents;
        _pads = devices.GamepadReading;
        _hookSource = hook;
        _poll = new Timer(_ => RunGuarded("trigger.poll", Poll), null, Timeout.Infinite, Timeout.Infinite);
        _programs = new Timer(_ => RunGuarded("trigger.programs", Programs), null,
            Timeout.Infinite, Timeout.Infinite);
        _padTimer = new Timer(_ => RunGuarded("trigger.gamepad", Pad), null,
            Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>The same service over the project-wide trigger entries.</summary>
    public MacroTriggerService(Func<IReadOnlyList<MacroItem>> macros,
        Func<ProjectTriggers> triggers, IDeviceLayer devices,
        IWindowEvents? windowEvents = null, IInputHook? hook = null)
        : this(macros, devices, windowEvents, hook)
    {
        _legacyMode = false;
        _triggerSource = triggers;
    }

    /// <summary>The running program's service, reading only immutable UI-published snapshots.</summary>
    public MacroTriggerService(Func<TriggerRuntimeSnapshot> triggers, IDeviceLayer devices,
        IWindowEvents? windowEvents = null, IInputHook? hook = null)
        : this(() => [], devices, windowEvents, hook)
    {
        _runtimeSource = triggers;
        _macros = () => triggers().Macros;
    }

    /// <summary>
    /// Asked when a step of a triggered macro has a failure rule of "ask me". Set by the window
    /// that can ask; without it a run that wants to ask stops, which says more than guessing.
    /// </summary>
    public Func<string, string, string, string, Task<StepErrorChoice>>? Ask { get; set; }

    /// <summary>
    /// Asked when a step of a triggered macro puts the macro's own question to the person. Set by
    /// the window that can ask; without it the question reads as unanswered, which is what a macro
    /// running with nobody at the machine should see.
    /// </summary>
    public Func<AskQuestion, Task<AskAnswer>>? AskUser { get; set; }

    /// <summary>
    /// Asked before a step of a triggered macro that cannot be undone is carried out. Set by the
    /// window that can ask; without it the step does not run, which is what a macro started by a
    /// schedule with nobody at the machine has to do.
    /// </summary>
    public Func<ExecutableStep, Task<bool>>? ConfirmIrreversible { get; set; }

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

    /// <summary>
    /// What every armed trigger is waiting for right now, when it was last looked at and when it
    /// will be looked at again. It is read by the run window, which is where a person goes when a
    /// macro they were expecting did not run.
    /// </summary>
    public IReadOnlyList<TriggerWatch> Watches()
    {
        var rows = new List<TriggerWatch>();

        lock (_windowGate)
        {
            lock (_gate)
            {
                foreach (var entry in TriggerSnapshot().Entries)
                {
                    if (!entry.IsEnabled || MacroFor(entry) is not { IsEnabled: true } macro)
                    {
                        continue;
                    }

                    var looking = entry.Summary;
                    switch (entry.Kind)
                    {
                        case TriggerKind.Window:
                            rows.Add(new TriggerWatch(macro.Name, looking,
                                _windows.For(entry),
                                _windowsCheckedAt, _windowsDueAt, _windowEvents.Available));
                            break;

                        case TriggerKind.Process:
                            rows.Add(new TriggerWatch(macro.Name, looking,
                                _programsRunning.For(entry),
                                _programsCheckedAt, _programsDueAt, false));
                            break;

                        case TriggerKind.Color:
                            rows.Add(new TriggerWatch(macro.Name, looking,
                                _colourTriggered.Contains(entry), _polledAt,
                                _polledAt + PollInterval, false));
                            break;

                        case TriggerKind.Timer:
                            rows.Add(new TriggerWatch(macro.Name, looking, null, _polledAt,
                                _scheduleDue.TryGetValue(entry, out var due) ? due : default, false));
                            break;
                    }
                }
            }
        }

        return rows;
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
            _windows.Clear();
            _idleDone.Clear();
            _hotstrings.Reset();
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
            _colourHeld.Remove(macro);
            _colourStopping.Remove(macro);
            foreach (var entry in TriggerSnapshot().ForMacro(macro.Name))
            {
                _spent.Remove(entry);
                _colourTriggered.Remove(entry);
                _scheduleDue.Remove(entry);
                _programsRunning.Forget(entry);
                _windows.Forget(entry);
                _idleDone.Remove(entry);
            }
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
        _programs.Dispose();
        _padTimer.Dispose();
    }
}
