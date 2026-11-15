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

/// <summary>
/// Watches the keyboard, the controller, the watched pixels and the clock for what the macros in
/// the list are waiting for, and runs them the way their trigger settings ask. The main window's
/// system switch turns the whole thing on and off.
/// </summary>
public sealed partial class MacroTriggerService : IDisposable
{
    /// <summary>How often a watched pixel is looked at, and the clock is asked the time.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(60);

    /// <summary>
    /// How often the open windows are looked at. A title comes free with the listing, but a
    /// program name or a window class costs a look-up per window, so the window list is not read
    /// as often as a pixel: half a second late is soon enough to notice a window opening.
    /// </summary>
    /// <remarks>
    /// This is the fallback, not the way a window is usually noticed: while the machine is willing
    /// to say when a window changes, the list is read there and then, and this interval only says
    /// how often the reading happens anyway. See <see cref="IWindowEvents"/>.
    /// </remarks>
    private static readonly TimeSpan WindowPollInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// How often the programs are counted. Asking for the programs of one name costs a single
    /// listing, while reading every program on the machine costs a handle each, so counting this
    /// often costs what the older, slower reading cost once every sixty milliseconds. It is worth
    /// that: a program that starts and quits again inside the wait between two counts is a program
    /// a trigger never notices, and the difference between sixty milliseconds and thirty is the
    /// difference between missing such a program and catching it.
    /// </summary>
    private static readonly TimeSpan ProgramPollInterval = TimeSpan.FromMilliseconds(30);

    /// <summary>
    /// How often the controller is read. A controller button is read rather than reported — Windows
    /// has no word for one going down — so a press that starts and ends between two readings is a
    /// press nothing saw. This is the pace the programs are counted at, for the same reason: it is
    /// short enough for the quickest press a hand makes, and one reading is a fraction of the cost of
    /// one look at a pixel.
    /// </summary>
    private static readonly TimeSpan PadPollInterval = TimeSpan.FromMilliseconds(30);

    /// <summary>A breather between the passes of a macro that repeats, so it cannot spin.</summary>
    private const int RepeatGapMs = 10;

    private readonly Func<IReadOnlyList<MacroItem>> _macros;
    private readonly Func<ProjectTriggers> _triggerSource;
    private readonly ProjectTriggers _legacyTriggers;
    private Func<TriggerRuntimeSnapshot>? _runtimeSource;
    private bool _legacyMode;
    private readonly IDeviceLayer _devices;
    private readonly IWindowEvents _windowEvents;

    /// <summary>The table every run this service starts shares with the window.</summary>
    private readonly VariableSession _variables;

    /// <summary>The controller in the person's hands, read on its own timer.</summary>
    private readonly IGamepadReading _pads;

    /// <summary>What that controller was holding at the last reading, so a press can be told from a hold.</summary>
    private readonly GamepadButtonWatch _padHeld = new();

    private readonly object _gate = new();
    private readonly object _windowGate = new();
    private readonly object _keys = new();
    private readonly Dictionary<MacroItem, CancellationTokenSource> _running = [];
    private readonly HashSet<TriggerEntry> _colourTriggered = [];
    private readonly HashSet<MacroItem> _colourHeld = [];
    private readonly HashSet<MacroItem> _colourStopping = [];
    private readonly HashSet<TriggerEntry> _spent = [];
    private readonly Dictionary<TriggerEntry, DateTime> _scheduleDue = [];
    private readonly Dictionary<TriggerEntry, FileWatch> _watches = [];
    private readonly ProgramWatches _programsRunning = new();
    private readonly WindowWatches _windows = new();
    private readonly HashSet<TriggerEntry> _idleDone = [];
    private readonly HashSet<string> _heldKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Timer _poll;
    private readonly Timer _programs;
    private readonly Timer _padTimer;

    /// <summary>The hook handed in for this service, or null to share the one the program keeps.</summary>
    private readonly IInputHook? _hookSource;
    private readonly HotstringRecognizer _hotstrings = new();

    private IInputHook? _hook;
    private int _windowPass;
    private int _windowAgain;
    private DateTime _windowsDueAt;
    private DateTime _windowsCheckedAt;
    private DateTime _programsCheckedAt;
    private DateTime _programsDueAt;
    private DateTime _polledAt;
    private bool _enabled;
    private bool _disposed;

    /// <summary>
    /// Raised when a macro starts or stops, so the window can show which ones are running. It is
    /// raised on whatever thread noticed, which is never the interface thread.
    /// </summary>
    public event Action? StatusChanged;

    /// <summary>Raised on the hook thread when a menu launcher asks for the project menu.</summary>
    public event Action? MenuRequested;
}
