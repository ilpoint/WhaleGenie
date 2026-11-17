using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Models;

namespace WhaleGenie.Runtime;

/// <summary>
/// Starts the macros a project's trigger entries are waiting for, and stops them again. The
/// interface publishes a plan; the watches in <see cref="TriggerHub"/> read that plan and ask this
/// class to start, stop and report a macro.
/// </summary>
/// <remarks>
/// The main window's system switch turns the whole thing on and off. Everything that touches the
/// machine — the hook, the timers and the window notices — is attached on the switch and released
/// on it, so a program that is not watching holds no handle and fires no timer.
/// </remarks>
public sealed partial class MacroTriggerService : IDisposable, ITriggerRunner
{
    private readonly IDeviceLayer _devices;
    private readonly IWindowEvents _windowEvents;
    private readonly VariableSession _variables;
    private readonly TriggerHub _hub;
    private readonly IInputHook? _hookSource;
    private readonly Timer _poll;
    private readonly Timer _programs;
    private readonly Timer _pad;
    private readonly Dictionary<string, RunSlot> _running = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    private IInputHook? _hook;
    private int _pollBusy;
    private int _programsBusy;
    private int _padBusy;
    private int _windowPass;
    private int _windowAgain;
    private volatile bool _enabled;
    private volatile bool _disposed;

    /// <summary>
    /// Raised when a macro starts or stops, so the window can show which ones are running. It is
    /// raised on whatever thread noticed; a listener that owns interface objects has to marshal.
    /// </summary>
    public event Action? StatusChanged;

    /// <summary>Raised on the hook thread when a menu launcher asks for the project menu.</summary>
    public event Action? MenuRequested;

    /// <param name="devices">
    /// The devices to watch and to run with. The program hands over the one layer it holds, so a
    /// triggered run uses the same devices — and the same loaded models — as a run started by hand;
    /// a check hands over a layer of its own, which is where the hook and the window notices come
    /// from too.
    /// </param>
    /// <param name="hook">
    /// The keyboard and the mouse, as the bound keys and buttons are answered from. The whole
    /// program shares the one hook the machine allows, so only a check that wants to drive the
    /// events itself hands in one of its own; with none, the layer's own hook is used.
    /// </param>
    /// <param name="windowEvents">
    /// Where the word that a window changed comes from, or null for the layer's own. A check that is
    /// about something else hands in a machine with nothing to say, which leaves the window list to
    /// the clock exactly as an older Windows does.
    /// </param>
    public MacroTriggerService(IDeviceLayer devices, VariableSession variables,
        IWindowEvents? windowEvents = null, IInputHook? hook = null)
    {
        _devices = devices;
        _variables = variables;
        _windowEvents = windowEvents ?? devices.WindowEvents;
        _hookSource = hook;
        _hub = new TriggerHub(devices, _windowEvents, this);

        _poll = new Timer(_ => RunGuarded("trigger.poll",
            () => Skippable(ref _pollBusy, () => Tick(Look))), null, Timeout.Infinite, Timeout.Infinite);
        _programs = new Timer(_ => RunGuarded("trigger.programs",
            () => Skippable(ref _programsBusy, () => Tick(LookAtPrograms))),
            null, Timeout.Infinite, Timeout.Infinite);
        _pad = new Timer(_ => RunGuarded("trigger.pad",
            () => Skippable(ref _padBusy, () => Tick(LookAtGamepad))), null, Timeout.Infinite, Timeout.Infinite);
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

    /// <summary>True while a key press should be looked at.</summary>
    private bool Listening => _enabled && !_disposed;

    /// <summary>
    /// Hands over the project as it stands now. The watches read this rather than the project, so a
    /// run can only see what was published before it started.
    /// </summary>
    public void Publish(TriggerPlan plan)
    {
        if (!_disposed)
        {
            _hub.Publish(plan);
        }
    }

    /// <summary>
    /// What every armed trigger is waiting for right now, when it was last looked at and when it
    /// will be looked at again. It is read by the run window, which is where a person goes when a
    /// macro they were expecting did not run.
    /// </summary>
    public IReadOnlyList<TriggerWatch> Watches() => _hub.Report(DateTime.Now);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Detach();
        _hub.Dispose();
        _poll.Dispose();
        _programs.Dispose();
        _pad.Dispose();
    }

    /// <summary>
    /// A clock tick only counts when the system is on and no run has the input door open. The guards
    /// live here rather than in the reads themselves: a check asks for one pass by calling a read
    /// directly, and must get it whether or not the system happens to be switched on.
    /// </summary>
    private void Tick(Action read)
    {
        if (!Listening || MacroTriggerGate.IsOpen)
        {
            return;
        }

        read();
    }

    /// <summary>Runs one piece of work unless the same piece is still running from the last tick.</summary>
    private static void Skippable(ref int busy, Action work)
    {
        if (Interlocked.Exchange(ref busy, 1) == 1)
        {
            return;
        }

        try
        {
            work();
        }
        finally
        {
            Interlocked.Exchange(ref busy, 0);
        }
    }

    internal static void RunGuarded(string stage, Action work)
    {
        try
        {
            work();
        }
        catch (Exception error)
        {
            AppDiagnostics.Record(stage, error);
        }
    }

    /// <summary>One run of a macro, and the way to ask it to stop.</summary>
    private sealed record RunSlot(MacroRef Macro, CancellationTokenSource Cancellation);
}
