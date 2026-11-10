using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

public partial class MainWindow : AppWindow
{
    /// <summary>Runs the macros the list is waiting for; null until the window is open.</summary>
    private MacroTriggerService? _triggers;

    /// <summary>
    /// A plain copy of the system switch for the hook thread. Avalonia's DataContext and the
    /// view model may only be read on the interface thread: reading them from a hook callback
    /// throws InvalidOperationException, which used to leave the wheel gesture permanently idle.
    /// </summary>
    private int _systemRunning;

    /// <summary>
    /// The macro editor on screen, or null while there is none. One at a time, because two of them
    /// on one macro would hand back two versions of it and the last one closed would win.
    /// </summary>
    private MacroEditorWindow? _editor;

    /// <summary>
    /// The settings window on screen, or null while there is none. It stands beside the main window
    /// rather than over it: the wheel page is edited while the thing it edits is on the desk, and a
    /// modal window would be put away by every picker that has to look at the desk.
    /// </summary>
    private SettingsWindow? _settings;

    /// <summary>
    /// The macro an open editor currently replaces in the project. Save commits a fresh snapshot,
    /// so this moves forward with every successful save and keeps close from applying it twice.
    /// </summary>
    private sealed class EditorCommitState(MacroItem? target, MacroView newView)
    {
        public MacroItem? Target { get; set; } = target;

        /// <summary>
        /// The view the macro was created from. A macro made from the menu or the wheel view is
        /// placed there once it has a name, which is the only moment a position can point at it.
        /// </summary>
        public MacroView NewView { get; } = newView;

        public HashSet<MacroItem> Lifted { get; } = [];
    }

    /// <summary>
    /// The run-and-debug window on screen, or null while there is none. One at a time for the same
    /// reason: it debugs one macro, and asking for another puts those steps in the window that is
    /// already open rather than laying a second one over the same list.
    /// </summary>
    private RunWindow? _runDebug;

    /// <summary>
    /// The run-and-debug window on screen, or null while there is none. A property so that a check
    /// can see which window the button opened without going through the desktop's own list.
    /// </summary>
    internal RunWindow? RunDebug => _runDebug;

    /// <summary>Whether the pin on the menu bar is holding the window above the other windows.</summary>
    private bool _alwaysOnTop;

    /// <summary>The shared hook the Home key is answered through; null until the window is open.</summary>
    private IInputHook? _hotkeyHook;

    private readonly HashSet<string> _globalHeld = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _hiddenInterfaceGroups = new(StringComparer.Ordinal);
    private TriggerRuntime? _runtime;
    private GlobalHotkeyPlan _globalHotkeyPlan = GlobalHotkeyPlan.Empty;

    /// <summary>Set by the tray once the answer to the leaving question is "go".</summary>
    private bool _allowClose;

    /// <summary>Whether the question about unsaved work is on screen, so only one is asked.</summary>
    private bool _askingToSave;

    /// <summary>Writes the unsaved project out as it changes; null until the window is watched.</summary>
    private RecoveryWriter? _recovery;

    /// <summary>
    /// Whether this session put the project into the snapshot. What clears it is what wrote it:
    /// the list comes up empty on every start, so clearing "because it is not dirty" at startup
    /// would throw away the last run's work before the user was even asked about it.
    /// </summary>
    private bool _projectInRecovery;

    /// <summary>
    /// Whether this run has already put the question about work the last one left behind. The
    /// window is announced again every time it comes back from the notification area, which it does
    /// whenever the macro editor closes, and what the snapshot holds by then is the work this run is
    /// already showing rather than the leftovers the question is about.
    /// </summary>
    private bool _recoveryOffered;

    /// <summary>
    /// The icon in the notification area, or null when this machine has none. It is what tells a
    /// close that leaves the program apart from a close that only puts the window out of the way,
    /// which is the difference between asking about unsaved work and saying nothing at all.
    /// </summary>
    internal AppTray? Tray { get; set; }

    /// <summary>
    /// Puts the question about the unsaved project to the user and reports what they chose. It is
    /// a property so that a check can answer without a dialog standing between it and the decision;
    /// nothing else replaces it.
    /// </summary>
    internal Func<Task<ConfirmChoice>> AskToSaveProject { get; set; }

    /// <summary>
    /// Offers back the work a stopped run left behind and reports what the user chose. A property
    /// for the same reason as <see cref="AskToSaveProject"/>: a check can answer without a dialog.
    /// </summary>
    internal Func<RecoveryContents, Task<ConfirmChoice>> AskToRecover { get; set; }

    public MainWindow()
        : this(null)
    {
    }

    /// <summary>
    /// The triggers, built when something first needs them. A window that is only read for its
    /// bindings should not put a key hook on the machine; the program hands in the one the container
    /// built, so opening the window never builds a second.
    /// </summary>
    private TriggerRuntime Runtime => _runtime ??= new TriggerRuntime(SharedDevices.Current);

    /// <summary>
    /// The window over the run's own triggers. The program hands in the one the container built, so
    /// the service that starts a run is the same one the window shows the state of.
    /// </summary>
    internal MainWindow(TriggerRuntime? runtime)
    {
        _runtime = runtime;
        InitializeComponent();

        // 标题栏归库，但这一扇的内容伸进标题栏：那条 40px 的行里放着置顶钮，整条都可以拖动。
        // 系统按钮由库画在它上面，行尾留出了它们的宽度。
        TitleBar.ExtendsContentIntoTitleBar = true;
        TitleBar.Height = 40;

        AskToSaveProject = () => ConfirmDialog.ShowAsync(this,
            Strings.Get("Main.UnsavedTitle"),
            Strings.Get("Main.UnsavedMessage"),
            Strings.Get("Main.UnsavedSave"),
            Strings.Get("Main.UnsavedDiscard"));

        AskToRecover = contents => ConfirmDialog.ShowAsync(this,
            Strings.Get("Recover.Title"),
            RecoveryMessage(contents),
            Strings.Get("Recover.Restore"),
            Strings.Get("Recover.Discard"),
            height: 280);

        var addMacroButton = this.FindControl<Button>("AddMacroButton");
        if (addMacroButton is not null)
        {
            addMacroButton.Click += (_, _) => OpenEditor(null, null, draft: false,
                (DataContext as MainViewModel)?.SelectedView);
        }

        // Clicking a macro card arms or disarms it; the card buttons opt out below.
        var macroList = this.FindControl<ItemsControl>("MacroList");
        if (macroList is not null)
        {
            macroList.AddHandler(PointerPressedEvent, OnMacroCardPressed, RoutingStrategies.Bubble);
        }

        var settingsButton = this.FindControl<Button>("SettingsButton");
        if (settingsButton is not null)
        {
            settingsButton.Click += OnOpenSettings;
        }

        var runDebugButton = this.FindControl<Button>("RunDebugButton");
        if (runDebugButton is not null)
        {
            runDebugButton.Click += async (_, _) => await ShowRunDebugAsync();
        }

        var alwaysOnTopButton = this.FindControl<Button>("AlwaysOnTopButton");
        if (alwaysOnTopButton is not null)
        {
            alwaysOnTopButton.Click += (_, _) => ToggleAlwaysOnTop(alwaysOnTopButton);
        }
    }

    /// <summary>Keeps the main window above the other windows until the pin is pressed again.</summary>
    private void ToggleAlwaysOnTop(Button button)
    {
        _alwaysOnTop = !_alwaysOnTop;
        Topmost = _alwaysOnTop;

        // Two ways to read the state: the icon changes (`Pin` when free, `PinOff` when pinned) and the
        // button takes the accent while it is on. The hint says what the next press does.
        button.Classes.Set("pinned", _alwaysOnTop);
        if (button.Content is IconView icon)
        {
            icon.Data = _alwaysOnTop ? AppIcons.PinOff : AppIcons.Pin;
        }

        ToolTip.SetTip(button, Strings.Get(_alwaysOnTop ? "Main.AlwaysOnTopCancel" : "Main.AlwaysOnTop"));
    }

    /// <summary>True when a close really stops the program rather than hiding the window.</summary>
    private bool Leaving => Tray is null || Tray.IsLeaving;

    /// <summary>
    /// Applies the size this machine last chose, limited to the desktop that is here now. Position
    /// is deliberately not restored: a monitor may have gone, and a window must not open off-screen.
    /// </summary>
    private void RestoreWindowSize()
    {
        if (LocalSettings.LoadMainWindowSize() is not { } size)
        {
            return;
        }

        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        var scale = screen is { Scaling: > 0 } ? screen.Scaling : 1;
        var widest = screen is null ? size.Width : screen.WorkingArea.Width / scale;
        var tallest = screen is null ? size.Height : screen.WorkingArea.Height / scale;

        Width = Math.Clamp(size.Width, MinWidth, Math.Max(MinWidth, widest));
        Height = Math.Clamp(size.Height, MinHeight, Math.Max(MinHeight, tallest));
    }

    /// <summary>Remembers the ordinary size; a maximized or minimized size is not a choice.</summary>
    private void RememberWindowSize()
    {
        if (WindowState == WindowState.Normal)
        {
            LocalSettings.StoreMainWindowSize(Width, Height);
        }
    }

    /// <summary>
    /// Asks about the project before the program stops. A plain close is not the moment for this:
    /// the window only goes out of the way, and everything in it is still there to be saved later.
    /// </summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        RememberWindowSize();

        if (!_allowClose
            && Leaving
            && DataContext is MainViewModel { HasUnsavedChanges: true })
        {
            // The answer is given before the base call, because the base call is what raises the
            // Closing event and the way out reads the answer there. A handler that ran first and
            // found nothing would take this window for one that had already gone, and would stop
            // the program on top of the very question it is asking.
            e.Cancel = true;

            // And the question itself goes up one turn later: the dialog is a window of its own,
            // and opening one while this one is being closed is not a position to be in.
            Dispatcher.UIThread.Post(() => _ = PromptToSaveProjectAsync());
        }

        base.OnClosing(e);
    }

    /// <summary>
    /// Puts the unsaved project to the user, the same question the macro editor asks about one
    /// macro. Only "save" and "discard" let the program stop; dismissing the question means the
    /// user is staying, and the way out the tray started is called off with it.
    /// </summary>
    private async Task PromptToSaveProjectAsync()
    {
        if (_askingToSave || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        _askingToSave = true;
        try
        {
            // The window is usually away in the notification area, and a question nobody can see
            // is no question at all: it comes back so the answer can be given.
            if (!IsVisible)
            {
                Show();
                WindowState = WindowState.Normal;
                Activate();
            }

            var choice = await AskToSaveProject();

            if (choice == ConfirmChoice.Primary)
            {
                // A save that was called off — the file picker dismissed — is an answer as well:
                // the project is still unsaved, so the program stays.
                if (!await SaveProjectAsync(viewModel))
                {
                    Tray?.CalledOff();
                    return;
                }
            }
            else if (choice != ConfirmChoice.Secondary)
            {
                Tray?.CalledOff();
                return;
            }

            // The program is going with nothing carried over: the project was just written out,
            // or the user said the unsaved work is not wanted, so the snapshot has no business
            // surviving to be offered back on the next run.
            RecoveryStore.ClearProject();

            _allowClose = true;
            Close();
        }
        finally
        {
            _askingToSave = false;
        }
    }

    /// <summary>
    /// Keeps the recovery snapshot in step with the macro list: written while there is something
    /// to lose, dropped the moment there is not. A run that ends without notice — a crash, or the
    /// machine going down — therefore leaves the work somewhere the next run can find it.
    /// </summary>
    private void WatchUnsavedProject(MainViewModel viewModel)
    {
        _recovery = new RecoveryWriter(() => WriteUnsavedProject(viewModel));

        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(MainViewModel.IsDirty)
                or nameof(MainViewModel.HasUnsavedChanges)
                or nameof(MainViewModel.CurrentPath))
            {
                SyncUnsavedProject(viewModel);
            }
        };

        // A change that arrives while the list is already unsaved reaches nothing above: the dirty
        // flag does not move for it, so the snapshot would be left holding the state it had before.
        viewModel.Changed += (_, _) => KeepUnsavedProject(viewModel);

        // The armed state of a macro is written into the package, so a change to it counts, and
        // it moves nothing that would raise a property change on the list itself.
        viewModel.Macros.CollectionChanged += (_, _) => KeepUnsavedProject(viewModel);
        SyncUnsavedProject(viewModel);
    }

    /// <summary>Writes the project out when it first becomes unsaved, and lets the snapshot go
    /// when there is nothing left to lose.</summary>
    private void SyncUnsavedProject(MainViewModel viewModel)
    {
        if (!viewModel.HasUnsavedChanges)
        {
            _recovery?.Stop();

            // Only take back what this session put there. A snapshot left by the last run is not
            // this window's to drop: the list is empty on every start, and clearing it here would
            // do that before the recovered work had even been offered.
            if (_projectInRecovery)
            {
                _projectInRecovery = false;
                RecoveryStore.ClearProject();
            }

            return;
        }

        _projectInRecovery = true;
        _recovery?.Now();
    }

    /// <summary>
    /// Keeps a change that arrived while the list was already unsaved from going unwritten. The
    /// write itself waits for the changes to pause; what makes the work unsaved was written already.
    /// </summary>
    private void KeepUnsavedProject(MainViewModel viewModel)
    {
        if (!viewModel.HasUnsavedChanges)
        {
            return;
        }

        _projectInRecovery = true;
        _recovery?.Changed();
    }

    private static void WriteUnsavedProject(MainViewModel viewModel)
    {
        if (!viewModel.HasUnsavedChanges)
        {
            return;
        }

        RecoveryStore.SaveProject([.. viewModel.Macros], viewModel.Triggers,
            [.. VariableCatalog.Globals], viewModel.CurrentPath);
    }

    /// <summary>
    /// Starts watching for the keys the macros are bound to. The system switch on the main window
    /// turns the watching on and off, so this only has to follow it.
    /// </summary>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        RestoreWindowSize();

        if (_triggers is not null || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        VariableCatalog.GlobalsChanged += OnGlobalsChanged;

        // The one device layer the program holds: a trigger run and a window that is open both use
        // it, so the text recognition models are loaded once rather than once per run.
        PublishRuntimeState(viewModel);
        Volatile.Write(ref _systemRunning, viewModel.IsRunning ? 1 : 0);
        _triggers = Runtime.Triggers;
        _triggers.Ask = AskAboutFailedStep;
        _triggers.AskUser = AskWhatTheMacroWantsToKnow;
        _triggers.ConfirmIrreversible = ConfirmIrreversibleStep;
        _triggers.StatusChanged += OnTriggerStatusChanged;
        _triggers.IsEnabled = viewModel.IsRunning;

        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.IsRunning))
            {
                Volatile.Write(ref _systemRunning, viewModel.IsRunning ? 1 : 0);
            }

            if (args.PropertyName == nameof(MainViewModel.IsRunning) && _triggers is not null)
            {
                _triggers.IsEnabled = viewModel.IsRunning;
                if (!viewModel.IsRunning)
                {
                    _menu?.CloseFromLauncher();
                }
                PublishRuntimeState(viewModel);
                RefreshSurface(viewModel);

                // 系统开关常常是盲按热键换的：听得到的确认，比事后去窗口里看那行字有用。
                AppSound.Play(viewModel.IsRunning ? UiSound.SystemOn : UiSound.SystemOff);
            }

        };

        _triggers.MenuRequested += OnMenuRequested;

        // The wheel has its own state and its own file. Nothing here asks the trigger service to
        // yield a key: the middle button keeps the untouched menu path above.
        _wheelState = WheelMenuStore.Load();
        _wheel = new WheelGesture(
            () => Volatile.Read(ref _wheelState).Options.Launcher,
            () => Volatile.Read(ref _wheelState).Options.RepeatKey,
            WheelCanOpen);
        WheelMenuStore.Changed += OnWheelStateChanged;

        var windowEvents = SharedDevices.Current.WindowEvents;
        windowEvents.ForegroundChanged += OnSurfaceForegroundChanged;
        windowEvents.BoundsChanged += OnSurfaceBoundsChanged;
        viewModel.Triggers.Changed += OnProjectTriggersChanged;
        viewModel.Changed += OnProjectChanged;
        viewModel.Macros.CollectionChanged += OnMacrosChanged;

        RefreshWheelGate(viewModel);
        RefreshRunning(viewModel);
        AttachSystemHotkey();
        WatchUnsavedProject(viewModel);
        Closed += (_, _) =>
        {
            DetachSystemHotkey();
            _triggers.MenuRequested -= OnMenuRequested;
            WheelMenuStore.Changed -= OnWheelStateChanged;
            CloseWheel();
            windowEvents.ForegroundChanged -= OnSurfaceForegroundChanged;
            windowEvents.BoundsChanged -= OnSurfaceBoundsChanged;
            viewModel.Triggers.Changed -= OnProjectTriggersChanged;
            viewModel.Changed -= OnProjectChanged;
            viewModel.Macros.CollectionChanged -= OnMacrosChanged;
            VariableCatalog.GlobalsChanged -= OnGlobalsChanged;
            HideSurface();
            StopTriggers();
            _recovery?.Stop();
        };
    }

    private void OnGlobalsChanged()
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.MarkSharedVariablesChanged();
            WriteUnsavedProject(viewModel);
        }
    }

    private void OnProjectTriggersChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            RefreshWheelGate(viewModel);
            PublishRuntimeState(viewModel);
            RefreshSurface(viewModel);
        }
    }

    private void OnProjectChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            RefreshWheelGate(viewModel);
            PublishRuntimeState(viewModel);
        }
    }

    private void OnMacrosChanged(object? sender,
        System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            // Opening a package replaces the macros and the wheel without raising a change of the
            // project itself, so the gate learns about the new wheel here as well.
            RefreshWheelGate(viewModel);
            PublishRuntimeState(viewModel);
        }
    }

    private void PublishRuntimeState(MainViewModel viewModel)
    {
        Runtime.Publish(viewModel.Triggers, [.. viewModel.Macros]);
        Volatile.Write(ref _globalHotkeyPlan, GlobalHotkeyPlan.From(viewModel));
    }

    /// <summary>
    /// Answers the Home key from anywhere, so the system switch works without bringing the window
    /// forward. The hook is the one the trigger service already keeps alive for the process.
    /// </summary>
    private void AttachSystemHotkey()
    {
        try
        {
            var hook = SharedDevices.Current.Hook;
            hook.KeyDown += OnSystemHotkeyDown;
            hook.KeyUp += OnSystemHotkeyUp;
            hook.MouseDown += OnMenuMouseDown;
            hook.MouseDown += OnWheelMouseDown;
            hook.MouseUp += OnWheelMouseUp;
            hook.Moved += OnWheelMouseMoved;
            hook.KeyDown += OnWheelKeyDown;
            hook.Wheel += OnWheelScroll;
            _hotkeyHook = hook;
        }
        catch (Exception)
        {
            // Without a hook the button on the window still works.
            _hotkeyHook = null;
        }
    }

    private void DetachSystemHotkey()
    {
        if (_hotkeyHook is { } hook)
        {
            hook.KeyDown -= OnSystemHotkeyDown;
            hook.KeyUp -= OnSystemHotkeyUp;
            hook.MouseDown -= OnMenuMouseDown;
            hook.MouseDown -= OnWheelMouseDown;
            hook.MouseUp -= OnWheelMouseUp;
            hook.Moved -= OnWheelMouseMoved;
            hook.KeyDown -= OnWheelKeyDown;
            hook.Wheel -= OnWheelScroll;
            _globalHeld.Clear();
            _hotkeyHook = null;
        }
    }

    private void OnSystemHotkeyDown(KeyHookEvent e)
    {
        var name = string.Empty;
        try
        {
            // The editor and the other dialogs own the keyboard while they are open.
            name = KeyNames.Name(e.Key, sided: true);
            if (name.Length == 0)
            {
                return;
            }

            // A key WhaleGenie sent itself is its own output, not the user asking for a switch.
            if (WhaleGenieInputGate.RecentlySent(name))
            {
                return;
            }

            var plan = Volatile.Read(ref _globalHotkeyPlan);
            if (Volatile.Read(ref _menuOpen) == 1)
            {
                if (plan.MenuLauncher.Length > 0
                    && MacroTriggerService.Binds(plan.MenuLauncher, name, _globalHeld))
                {
                    e.Suppress = true;
                    Dispatcher.UIThread.Post(() => _menu?.CloseFromLauncher());
                    return;
                }

                if (Volatile.Read(ref _menuSearchFocused) == 1)
                {
                    return;
                }

                if (_menu?.IsPinned == true)
                {
                    return;
                }

                if (name is "Left" or "Right" or "Up" or "Down"
                    or "左" or "右" or "上" or "下"
                    or "Enter" or "Return" or "回车" or "Esc" or "Escape")
                {
                    e.Suppress = true;
                    Dispatcher.UIThread.Post(() => _menu?.HandleKey(name));
                    return;
                }
            }

            if (Volatile.Read(ref _menuOpen) == 1
                && plan.SystemHotkey.Length > 0
                && MacroTriggerService.Binds(plan.SystemHotkey, name, _globalHeld))
            {
                e.Suppress = true;
                Dispatcher.UIThread.Post(ToggleSystemFromHotkey);
                return;
            }

            if (MacroTriggerGate.IsOpen)
            {
                return;
            }

            var match = plan.Match(name, _globalHeld);
            if (match.Action == GlobalHotkeyAction.ToggleSystem)
            {
                Dispatcher.UIThread.Post(ToggleSystemFromHotkey);
            }
            else if (match.Action == GlobalHotkeyAction.ToggleMacro)
            {
                // An arm/disarm key is a switch, not something the person meant to type into
                // whatever window is in front, so it is held back the way the system switch is.
                e.Suppress = true;
                Dispatcher.UIThread.Post(() => ToggleMacroFromHotkey(match.MacroName));
            }
            else if (match.Action == GlobalHotkeyAction.ToggleInterfaceGroup)
            {
                Dispatcher.UIThread.Post(() => ToggleInterfaceGroupFromHotkey(match.GroupKey));
            }
        }
        catch (Exception error)
        {
            AppDiagnostics.Record("hook.global-shortcut", error);
        }
        finally
        {
            if (name.Length > 0)
            {
                _globalHeld.Add(name);
            }
        }
    }

    private void OnSystemHotkeyUp(KeyHookEvent e)
    {
        try
        {
            var name = KeyNames.Name(e.Key, sided: true);
            if (name.Length > 0)
            {
                _globalHeld.Remove(name);
            }
        }
        catch (Exception error)
        {
            AppDiagnostics.Record("hook.global-shortcut-up", error);
        }
    }

    /// <summary>Flips the system switch, unless a field on the window has the keyboard.</summary>
    private void ToggleSystemFromHotkey()
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (IsActive && FocusManager?.GetFocusedElement() is TextBox)
        {
            return;
        }

        viewModel.IsRunning = !viewModel.IsRunning;
    }

    private void ToggleInterfaceGroupFromHotkey(string groupKey)
    {
        if (!_hiddenInterfaceGroups.Add(groupKey))
        {
            _hiddenInterfaceGroups.Remove(groupKey);
        }

        RefreshSurface(DataContext as MainViewModel);
    }

    /// <summary>Whether the pressed key should answer a macro, rather than the app being edited.</summary>
    private void OnTriggerStatusChanged()
        => Dispatcher.UIThread.Post(() => RefreshRunning(DataContext as MainViewModel));

    /// <summary>Marks the macros the trigger is running, which colours the dots on their cards.</summary>
    private void RefreshRunning(MainViewModel? viewModel)
    {
        if (_triggers is null || viewModel is null)
        {
            return;
        }

        foreach (var macro in viewModel.Macros)
        {
            macro.IsRunning = _triggers.IsRunning(macro);
        }
    }

    private void StopTriggers()
    {
        if (_triggers is null)
        {
            return;
        }

        _triggers.StatusChanged -= OnTriggerStatusChanged;

        // The container owns the service, so this only stops it: the window that shows what a
        // trigger did is going away, and nothing should keep triggering behind it.
        _triggers.IsEnabled = false;
        _triggers.StopAll();
        _triggers = null;
    }

    /// <summary>
    /// Opens the run-and-debug window, or brings the one already open to the front. Asked for
    /// without a macro it shows the variables and nothing to run, and offers to pick one and start
    /// it; the card's own button and menu hand in the macro they are on instead.
    /// </summary>
    private async Task ShowRunDebugAsync(MacroItem? macro = null)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (_runDebug is { } open)
        {
            // The window is put away rather than gone: it may have been minimized to the taskbar
            // or hidden by the scope that took the screen away for a moment. Bring it back before
            // anything else, or the run it is showing goes on with nobody watching.
            open.BringBack();
            if (macro is not null)
            {
                await open.DebugAsync(macro);
            }

            return;
        }

        // The shared variables are written into the package with the macros, so creating or editing
        // one is a change to the project like any other — and nothing on the macro list moves when
        // it happens, so this is the one place that can notice it. The window is not modal, so when
        // it is closed is when to look.
        var before = MainViewModel.SharedVariables();

        // The layer the whole program shares, so a debug run reads with the same loaded models the
        // rest of the program uses rather than loading a second copy of them.
        var window = new RunWindow([], SharedDevices.Current, listed: [.. viewModel.Macros],
            triggers: _triggers);
        _runDebug = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_runDebug, window))
            {
                _runDebug = null;
            }

            if (MainViewModel.SharedVariablesChangedSince(before))
            {
                viewModel.MarkSharedVariablesChanged();
            }
        };

        // The run window stands beside the list rather than under it: the list goes into the
        // notification area whenever it is minimized, and an owned window would be taken down
        // with it — a run being watched would lose the window watching it, and the button that
        // opens it would find a window that is not on the screen.
        window.ShowAsPeer(this);

        if (macro is not null)
        {
            await window.DebugAsync(macro);
        }
    }

    // -------------------------------------------------------------- macro package

    // -------------------------------------------------------------- recovery

    /// <summary>
    /// Offers back whatever the last run left unsaved. The list comes up empty, so this is the one
    /// moment the snapshot can be read: the work it holds is not in any package yet. It is asked
    /// once for the whole run — when the real application starts, and directly by a check.
    /// </summary>
    internal async Task OfferRecoveryAsync()
    {
        if (_recoveryOffered || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        _recoveryOffered = true;

        if (RecoveryStore.Load() is not { } contents)
        {
            return;
        }

        switch (await AskToRecover(contents))
        {
            case ConfirmChoice.Primary:
                Recover(viewModel, contents);
                break;
            case ConfirmChoice.Secondary:
                // The work is not wanted, so it goes for good rather than being asked about
                // again on the next run.
                RecoveryStore.Clear();
                break;
            default:
                // Put off, not answered: the snapshot stays, so the next run asks again.
                break;
        }
    }

    private void Recover(MainViewModel viewModel, RecoveryContents contents)
    {
        viewModel.RestoreFrom(contents);
        RefreshRunning(viewModel);

        // An editor that was open is opened again on the macro it was holding, so that work comes
        // back too and not only the list behind it.
        if (contents.Editor is { } editor)
        {
            var target = editor.Replaces is { Length: > 0 } name
                ? viewModel.Macros.FirstOrDefault(macro =>
                    string.Equals(macro.Name, name, StringComparison.OrdinalIgnoreCase))
                : null;

            OpenEditor(editor.Macro, target, draft: true);
        }
    }

    /// <summary>Says what the snapshot holds, one place at a time, for the question.</summary>
    private static string RecoveryMessage(RecoveryContents contents)
    {
        var found = new List<string>();
        if (contents.Macros.Count > 0 || contents.PackagePath is not null)
        {
            found.Add(Strings.Format("Recover.Project", contents.Macros.Count));
        }

        if (contents.Editor is not null)
        {
            found.Add(Strings.Get("Recover.Editor"));
        }

        return Strings.Format("Recover.Message", string.Join(Environment.NewLine, found));
    }

    private static FilePickerFileType PackageFileType
        => new(Strings.Get("Package.Filter")) { Patterns = [$"*{MacroPackage.Extension}"] };

    private async void OnOpenProjectClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var path = await PickPackageAsync(Strings.Get("Main.OpenProjectTitle"));
        if (path is null)
        {
            return;
        }

        try
        {
            viewModel.OpenPackage(path);
            LocalSettings.StoreLastProject(path);
        }
        catch (Exception error)
        {
            await ReportAsync(Strings.Get("Package.OpenFailed"), path, error);
        }
    }

    /// <summary>
    /// Imports from a package: the window shows what is inside it, and what was ticked decides
    /// where it goes. Macros join this project; submacros ticked on their own go to the public
    /// library, because that is where something a macro calls belongs.
    /// </summary>
    private async void OnImportClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var path = await PickPackageAsync(Strings.Get("Main.ImportTitle"));
        if (path is null)
        {
            return;
        }

        PackageContents contents;
        try
        {
            // Pictures land in this project's folder: the file being imported is not where the
            // macros will be working from. A name already used there is compared by content, so an
            // arriving picture never replaces one another macro looks for.
            contents = MacroPackage.Load(path, ImageAssets.FolderFor(viewModel.CurrentPath));
        }
        catch (Exception error)
        {
            await ReportAsync(Strings.Get("Package.OpenFailed"), path, error);
            return;
        }

        var nodes = await MacroChooseWindow.ShowAsync(this, MacroChooseKind.Import, contents.Macros,
            contents.Triggers, contents.Globals, fileName: Path.GetFileName(path));
        if (nodes is null || nodes.Count == 0)
        {
            return;
        }

        var chosen = nodes.Select(node => node.Macro).ToList();

        try
        {
            if (nodes.All(node => node.IsSub))
            {
                var added = MacroImport.IntoLibrary(chosen, AppPaths.PublicMacroLibrary);
                await ConfirmDialog.ShowAsync(this, Strings.Get("Import.ReportTitle"),
                    Strings.Format("Import.LibraryAdded",
                        string.Join("、", added.Select(entry => entry.Macro.Name))),
                    Strings.Get("Common.Ok"), null, false);
                return;
            }

            // What to do about a variable both sides declare is a question, not a default: the wrong
            // answer is a macro that reads somebody else's number without saying so.
            var decisions = MacroImport.Decisions(chosen, contents.Globals);
            if (!await GlobalConflictWindow.ShowAsync(this, decisions, chosen))
            {
                return;
            }

            var report = MacroImport.IntoProject(viewModel, contents, chosen, decisions);
            await ConfirmDialog.ShowAsync(this, Strings.Get("Import.ReportTitle"), ReportText(report),
                Strings.Get("Common.Ok"), null, false, height: 320);
        }
        catch (Exception error)
        {
            await ReportAsync(Strings.Get("Package.OpenFailed"), path, error);
        }
    }

    /// <summary>
    /// What the import did, in the order a person checks it: what arrived, what had to be renamed,
    /// what happened to the variables, and what now needs their attention.
    /// </summary>
    private static string ReportText(ImportReport report)
    {
        var lines = new List<string>();

        if (report.Added.Count > 0)
        {
            lines.Add(Strings.Format("Import.ReportMacros",
                string.Join("、", report.Added.Select(macro => macro.Name))));
        }

        if (report.RenamedMacros.Count > 0)
        {
            lines.Add(Strings.Format("Import.ReportRenamed",
                string.Join("、", report.RenamedMacros.Select(pair => $"{pair.Was} → {pair.Now}"))));
        }

        var globals = new List<string>();
        if (report.GlobalsAdded.Count > 0)
        {
            globals.Add(Strings.Format("Import.GlobalAdded", string.Join("、", report.GlobalsAdded)));
        }

        if (report.GlobalsRenamed.Count > 0)
        {
            globals.Add(Strings.Format("Import.GlobalRenamed", string.Join("、", report.GlobalsRenamed)));
        }

        if (report.GlobalsOverwritten.Count > 0)
        {
            globals.Add(Strings.Format("Import.GlobalOverwritten",
                string.Join("、", report.GlobalsOverwritten)));
        }

        if (report.GlobalsKept.Count > 0)
        {
            globals.Add(Strings.Format("Import.GlobalKept", string.Join("、", report.GlobalsKept)));
        }

        if (globals.Count > 0)
        {
            lines.Add(Strings.Format("Import.ReportGlobals", string.Join("；", globals)));
        }

        if (report.DuplicateTriggers.Count > 0)
        {
            lines.Add(Strings.Format("Import.ReportDuplicates",
                string.Join("；", report.DuplicateTriggers)));
        }

        if (report.ScriptsToCheck.Count > 0)
        {
            lines.Add(Strings.Format("Import.ReportScripts", string.Join("；", report.ScriptsToCheck)));
        }

        return lines.Count == 0 ? Strings.Get("Import.ReportNothing") : string.Join("\n", lines);
    }

    /// <summary>
    /// Writes one macro out as a file of its own. The pictures it looks for travel inside the file,
    /// so the person it is handed to does not have to be told what to copy as well.
    /// </summary>
    /// <summary>
    /// Exports from the file menu: the same window as the card's own menu, with nothing ticked, for
    /// a person who wants to pick several macros rather than start from one of them.
    /// </summary>
    private async void OnExportMacrosClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            await ExportMacrosAsync(viewModel, []);
        }
    }

    /// <summary>
    /// Writes the macro a card was right-clicked on. The window opens with it ticked, because that
    /// is what the click meant; the rest of the list is there for a change of mind.
    /// </summary>
    private async void OnExportMacroClicked(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;

        if (sender is Control { DataContext: MacroItem macro }
            && DataContext is MainViewModel viewModel)
        {
            await ExportMacrosAsync(viewModel, [macro]);
        }
    }

    /// <summary>
    /// Writes the chosen macros into a package: the macros with the submacros inside them, the
    /// global variables they read, the trigger entries that start them and the pictures they look
    /// for. A project's own macros are copied first, because writing a package rewrites the trigger
    /// table of what it is handed.
    /// </summary>
    private async Task ExportMacrosAsync(MainViewModel viewModel, IReadOnlyList<MacroItem> preselected)
    {
        var nodes = await MacroChooseWindow.ShowAsync(this, MacroChooseKind.Export, viewModel.Macros,
            viewModel.Triggers, preselected: preselected);
        if (nodes is null || nodes.Count == 0)
        {
            return;
        }

        var selection = nodes.Select(node => node.Macro).ToList();

        var path = await PackagePicker.AskWhereAsync(this, Strings.Get("Main.ExportMacroTitle"),
            selection[0].Name);
        if (path is null)
        {
            return;
        }

        try
        {
            // An export is written somewhere else, while the pictures stay beside the project: the
            // folder comes along so the package carries them rather than pointing at this machine.
            MacroPackage.Save(path, MacroBundle.Copies(selection),
                MacroBundle.Take(viewModel.Triggers, selection),
                MacroBundle.UsedGlobals(selection), AppVersion.Text,
                ImageAssets.FolderFor(viewModel.CurrentPath));
        }
        catch (Exception error)
        {
            await ReportAsync(Strings.Get("Main.ExportMacroTitle"), path, error);
        }
    }

    private async void OnSaveProjectClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (viewModel.CurrentPath is { Length: > 0 } current)
        {
            Save(viewModel, current);
            return;
        }

        await SaveAsAsync(viewModel);
    }

    private async void OnSaveProjectAsClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            await SaveAsAsync(viewModel);
        }
    }

    private async void OnReloadCurrentClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { CurrentPath: { Length: > 0 } path } viewModel)
        {
            return;
        }

        try
        {
            viewModel.OpenPackage(path);
            LocalSettings.StoreLastProject(path);
        }
        catch (Exception error)
        {
            await ReportAsync(Strings.Get("Package.OpenFailed"), path, error);
        }
    }

    private void OnOpenLocationClicked(object? sender, RoutedEventArgs e)
        => OpenFolder((DataContext as MainViewModel)?.CurrentFolder);

    private void OnOpenDirectoryClicked(object? sender, RoutedEventArgs e)
        => OpenFolder((DataContext as MainViewModel)?.CurrentFolder
            ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));

    /// <summary>The explicit way out, for people who never found the notification-area icon.</summary>
    private void OnExitClicked(object? sender, RoutedEventArgs e)
    {
        if (Tray is { } tray)
        {
            tray.RequestExit();
            return;
        }

        Close();
    }

    private async void OnCloseAllClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { HasMacros: true } viewModel)
        {
            return;
        }

        var choice = await ConfirmDialog.ShowAsync(this, Strings.Get("Main.CloseAllTitle"),
            Strings.Get("Main.CloseAllMessage"), Strings.Get("Main.CloseAll"));

        if (choice == ConfirmChoice.Primary)
        {
            _triggers?.StopAll();
            viewModel.ClearMacros();
            viewModel.CurrentPath = null;
            RefreshRunning(viewModel);
        }
    }

    private async Task<string?> PickPackageAsync(string title)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [PackageFileType],
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    /// <summary>
    /// Writes the project out, asking where it goes when it has no file yet. False when the save
    /// was called off or failed, which is the answer that keeps a program from stopping on a
    /// project it did not manage to write.
    /// </summary>
    private async Task<bool> SaveProjectAsync(MainViewModel viewModel)
        => viewModel.CurrentPath is { Length: > 0 } current
            ? Save(viewModel, current)
            : await SaveAsAsync(viewModel);

    private async Task<bool> SaveAsAsync(MainViewModel viewModel)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Strings.Get("Main.SaveProjectTitle"),
            SuggestedFileName = viewModel.CurrentPath is { Length: > 0 } current
                ? Path.GetFileName(current)
                : $"macros{MacroPackage.Extension}",
            DefaultExtension = MacroPackage.Extension.TrimStart('.'),
            FileTypeChoices = [PackageFileType],
            ShowOverwritePrompt = true,
        });

        if (file?.TryGetLocalPath() is { Length: > 0 } path)
        {
            return Save(viewModel, path);
        }

        return false;
    }

    private bool Save(MainViewModel viewModel, string path)
    {
        try
        {
            viewModel.SavePackage(path);
            LocalSettings.StoreLastProject(path);
            return true;
        }
        catch (Exception error)
        {
            _ = ReportAsync(Strings.Get("Package.SaveFailed"), path, error);
            return false;
        }
    }

    /// <summary>Reports a package error without leaving the main window.</summary>
    private async Task ReportAsync(string header, string path, Exception error)
    {
        await ConfirmDialog.ShowAsync(this, header,
            $"{Path.GetFileName(path)}\n{error.Message}", Strings.Get("Common.Ok"), null, false);
    }

    /// <summary>
    /// Puts a failed step of a triggered macro to the user, the same question the debugger asks.
    /// A trigger runs its macro on a thread of its own, so the question has to be handed to the
    /// interface thread; an answer of "stop" is what a closed box or a cancelled run gives, which
    /// is the one choice that cannot make things worse on its own.
    /// </summary>
    private async Task<StepErrorChoice> AskAboutFailedStep(string step, string reason, string detail,
        string comment)
        => await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var choice = await ConfirmDialog.ShowAsync(this, Strings.Get("Run.AskTitle"),
                FailedStepPrompt.Question(step, reason, detail, comment),
                Strings.Get("Run.AskRetry"), Strings.Get("Run.AskSkip"), true, Strings.Get("Run.AskStop"));

            return choice switch
            {
                ConfirmChoice.Primary => StepErrorChoice.Retry,
                ConfirmChoice.Secondary => StepErrorChoice.Skip,
                _ => StepErrorChoice.Stop,
            };
        });

    /// <summary>
    /// Puts "this cannot be undone" to the user for a run a trigger started. A closed box or a
    /// cancelled run answers no, which is the one answer that cannot make things worse on its own.
    /// </summary>
    private async Task<bool> ConfirmIrreversibleStep(ExecutableStep step)
        => await Dispatcher.UIThread.InvokeAsync(async () =>
            await ConfirmDialog.ShowAsync(this, Strings.Get("Run.CannotBeUndoneTitle"),
                IrreversiblePrompt.Question(step.Type, step.Meta.Comment),
                Strings.Get("Common.Ok"), null, true, Strings.Get("Common.Cancel"),
                tone: ConfirmTone.Danger) is ConfirmChoice.Primary);

    /// <summary>
    /// Puts a macro's own question on screen for a run a trigger started, where there is no run
    /// window to show it in. Closing either dialog is the same as answering no: a question left
    /// unanswered should not read as a yes.
    /// </summary>
    private async Task<AskAnswer> AskWhatTheMacroWantsToKnow(AskQuestion question)
        => await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (question.Kind is AskKind.Text)
            {
                return await TextPromptDialog.ShowAsync(this, Strings.Get("Main.Title"), question);
            }

            var choice = await ConfirmDialog.ShowAsync(this, Strings.Get("Main.Title"),
                question.Message, Strings.Get("Run.AnswerOk"), null, true,
                Strings.Get("Run.AnswerCancel"));

            return choice is ConfirmChoice.Primary ? new AskAnswer(true, string.Empty) : AskAnswer.Cancel;
        });

    private static void OpenFolder(string? folder)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            return;
        }

        Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
    }

    private void OnSettingsClicked(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        OnOpenSettings(sender, e);
    }

    /// <summary>Shows what the whole release list says, which is the change log.</summary>
    private void OnWhatsNewClicked(object? sender, RoutedEventArgs e)
        => WebPage.Open(ProjectLinks.Releases);

    /// <summary>
    /// Opens where a newer version would be. WhaleGenie does not check by itself: asking GitHub on
    /// every launch is a network call nobody asked for, and opening the page leaves the choice —
    /// and the download — with the user.
    /// </summary>
    private void OnUpdatesClicked(object? sender, RoutedEventArgs e)
        => WebPage.Open(ProjectLinks.LatestRelease);

    private void OnFeedbackClicked(object? sender, RoutedEventArgs e)
        => WebPage.Open(ProjectLinks.Feedback);

    /// <summary>
    /// Opens the manual. "Get started" says what the item is about, and the manual is where the
    /// program explains itself: the copy that came with the program when there is one, the page in
    /// the repository otherwise.
    /// </summary>
    private void OnGetStartedClicked(object? sender, RoutedEventArgs e)
        => WebPage.Open(Manual.Where());

    /// <summary>
    /// Gives the flyout its window's data context. A flyout's content is not part of the window's
    /// visual tree, so the bindings that name the MCP view model need this one hand-off. The button
    /// only opens the flyout; the switch inside it owns the start and stop.
    /// </summary>
    private void OnMcpClicked(object? sender, RoutedEventArgs e)
    {
        if (this.FindControl<Button>("McpButton")?.Flyout is Flyout { Content: Control content })
        {
            content.DataContext = DataContext;
        }
    }

    /// <summary>
    /// Copies the block a client's configuration file wants: this program's path and the flag that
    /// makes it speak MCP. The client starts the process itself, so there is no address to paste.
    /// </summary>
    private async void OnCopyMcpConfig(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (viewModel.Mcp.Executable.Length == 0
            || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            viewModel.Mcp.ShowNoProgram();
            return;
        }

        await clipboard.SetTextAsync(viewModel.Mcp.Config);
        viewModel.Mcp.ShowCopied();
    }

    /// <summary>
    /// Opens the window that says what the program is, shows the two donation codes, and offers the
    /// project page and the list of third-party work. Both the "?" button and "About" open it.
    /// </summary>
    private async void OnAboutClicked(object? sender, RoutedEventArgs e)
        => await new AboutWindow().ShowDialogOver(this);

    private async void OnOpenSettings(object? sender, RoutedEventArgs e)
        => await OpenSettingsAsync(SettingsSubPage.General);

    /// <summary>
    /// Opens the settings window beside the main window, on the page asked for, and answers when it
    /// closes. One at a time: a second request brings the open window forward instead of laying
    /// another copy of the same settings over it.
    /// </summary>
    /// <remarks>
    /// It is shown as a peer rather than as a dialog because the pages it holds reach outside it: a
    /// picker has to put this program's windows away and take a window from the desk, and the wheel
    /// has to be tried while its page is open. A modal settings window cannot be put away that way
    /// without breaking the modal chain its owner is disabled by, which is what left "add rule"
    /// doing nothing at all.
    /// </remarks>
    private Task OpenSettingsAsync(SettingsSubPage page)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return Task.CompletedTask;
        }

        if (_settings is { } open)
        {
            open.Activate();
            return Task.CompletedTask;
        }

        var dialog = new SettingsWindow(viewModel, viewModel.CurrentPath, page);
        var closed = new TaskCompletionSource();
        _settings = dialog;
        dialog.Closed += (_, _) =>
        {
            if (ReferenceEquals(_settings, dialog))
            {
                _settings = null;
            }

            // Everything the settings pages change is read back through these three: the runtime
            // snapshot the triggers run from, the words on the hotkey hints, and the order of the
            // list, which the settings can change without touching a single macro.
            PublishRuntimeState(viewModel);
            viewModel.RefreshHotkeyHints();
            viewModel.RefreshVisibleMacros();
            closed.TrySetResult();
        };
        dialog.ShowAsPeer(this);
        return closed.Task;
    }

    private Task OpenMenuSettingsAsync(WindowInfo? foreground) => ShowMenuSettingsAsync(foreground);

    private async Task ShowMenuSettingsAsync(WindowInfo? foreground)
    {
        // The settings window is not modal any more, so this waits for it to close by its own
        // Closed event rather than by the modal loop that used to end here.
        await OpenSettingsAsync(SettingsSubPage.MenuLauncher);
        if (foreground is not null)
        {
            try
            {
                SharedDevices.Current.Windows.Activate(foreground.Handle);
            }
            catch
            {
                // The target may have closed while settings were open.
            }
        }
    }

    /// <summary>
    /// Opens the macro editor on a macro: one from the list, a new one (<paramref name="editing"/> is
    /// null), or a draft recovered from a run that stopped without notice. What that macro replaces
    /// when it is saved is <paramref name="existing"/>, which is null for a new one. It answers the
    /// editor back so that a check can write in it; nothing else reads that.
    /// </summary>
    internal MacroEditorWindow? OpenEditor(MacroItem? editing, MacroItem? existing, bool draft,
        MacroView? newView = null)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return null;
        }

        // Two editors on one macro would each hand back their own version and the last one closed
        // would win, so the editor already open is the one wanted; it is brought to the front.
        if (_editor is { } open)
        {
            open.Activate();
            return open;
        }

        // The package path is what a picture taken from the screen is stored beside, and the device
        // layer is the program's own, so the trials and the runs started from here use the one layer
        // whose text recognition models are already loaded.
        var editor = new MacroEditorWindow(editing, [.. viewModel.Macros], viewModel.CurrentPath,
            SharedDevices.Current, viewModel.Triggers, callableOnly: false, newView);

        // Recovered work is unsaved by definition, so closing that editor has to ask about it rather
        // than let it go without a word.
        if (draft)
        {
            editor.MarkUnsaved();
        }

        var commit = new EditorCommitState(existing, newView ?? MacroView.All);
        _editor = editor;
        editor.Saved += macro => CommitEditorSnapshot(editor, macro, commit, viewModel);
        editor.Closed += (_, _) => OnEditorClosed(editor, commit, viewModel);

        // The editor stands beside the list rather than replacing it, so both stay on screen and
        // either can be minimized without taking the other down.
        editor.ShowAsPeer(this);
        return editor;
    }

    /// <summary>
    /// Takes what the editor ended with: the macro joins the list, or the one that was edited
    /// replaces it. Then the list comes back, unless the user is on their way out of the program.
    /// </summary>
    private void OnEditorClosed(MacroEditorWindow editor, EditorCommitState commit,
        MainViewModel viewModel)
    {
        _editor = null;

        if (editor.SteppedAside && !(Tray?.IsLeaving ?? false))
        {
            Tray?.ComeBack();
        }

        if (editor.Result is not { } macro)
        {
            return;
        }

        CommitEditorSnapshot(editor, macro, commit, viewModel);
    }

    private void CommitEditorSnapshot(MacroEditorWindow editor, MacroItem macro,
        EditorCommitState commit, MainViewModel viewModel)
    {
        // A stretch of steps lifted into a macro of its own during the session is real work: it
        // joins the project together with the macro that calls it, because the call is only in the
        // list that is arriving now.
        foreach (var lifted in editor.LiftedMacros)
        {
            if (!commit.Lifted.Add(lifted))
            {
                continue;
            }

            viewModel.AddMacro(lifted);
        }

        if (commit.Target is null)
        {
            ApplyEditorTriggerSettings(viewModel, macro);
            viewModel.AddMacro(macro);
            PlaceNewMacro(commit.NewView, macro, viewModel);
            commit.Target = macro;
            WriteUnsavedProject(viewModel);
            return;
        }

        // Saving an unchanged macro is not a project change. This also makes the close after a
        // successful save a no-op rather than a second insertion or replacement.
        if (Same(commit.Target, macro))
        {
            return;
        }

        // The edited macro replaces the one the trigger may be running.
        _triggers?.Stop(commit.Target);
        ApplyEditorTriggerSettings(viewModel, macro);
        viewModel.ReplaceMacro(commit.Target, macro);
        commit.Target = macro;
        RefreshRunning(viewModel);

        // The editor has just let its draft go, so the list is the only copy of this macro: it
        // goes into the snapshot now rather than waiting for the next turn of the timer.
        WriteUnsavedProject(viewModel);
    }

    /// <summary>
    /// Puts a macro that was created from the menu or the wheel view where that view keeps things.
    /// It happens after the save because a position names the macro, and a macro is named by the
    /// editor. The key, window and string views already brought their own entry with them.
    /// </summary>
    internal static void PlaceNewMacro(MacroView view, MacroItem macro, MainViewModel viewModel)
    {
        switch (view)
        {
            case MacroView.Menu when viewModel.Triggers.FirstFreeMenuCell() is { } cell:
                viewModel.Triggers.Entries.Add(new TriggerEntry
                {
                    Kind = TriggerKind.Menu,
                    MacroName = macro.Name,
                    MenuPageId = cell.PageId,
                    MenuRow = cell.Row,
                    MenuColumn = cell.Column,
                    IsEnabled = true,
                });
                break;
            case MacroView.Wheel when viewModel.Triggers.Wheel.Global.FirstEmptySlot() is { } slot:
                slot.Macro = macro.Name;
                viewModel.Triggers.NotifyChanged();
                break;
        }
    }

    /// <summary>
    /// Whether the editor brought back the very same macro. The editor builds the macro again as it
    /// hands it over, so this is a question about what the macro says rather than about which object
    /// it is; two macros that read the same are written to the package the same way.
    /// </summary>
    private static bool Same(MacroItem one, MacroItem other)
        => string.Equals(one.ToJson().ToJsonString(), other.ToJson().ToJsonString(),
            StringComparison.Ordinal)
           && string.Equals(TriggerJson(one), TriggerJson(other), StringComparison.Ordinal)
           && string.Equals(EditorSettingsJson(one), EditorSettingsJson(other), StringComparison.Ordinal);

    private static string TriggerJson(MacroItem macro)
    {
        var entries = new JsonArray();
        foreach (var entry in macro.Triggers)
        {
            entries.Add(entry.ToJson());
        }

        return entries.ToJsonString();
    }

    private static string EditorSettingsJson(MacroItem macro)
        => new JsonObject
        {
            ["layouts"] = new JsonArray(
                [.. macro.InterfaceLayouts.Select(layout => layout.ToJson())]),
        }.ToJsonString();

    private static void ApplyEditorTriggerSettings(MainViewModel viewModel, MacroItem macro)
    {
        foreach (var layout in macro.InterfaceLayouts)
        {
            viewModel.Triggers.StoreInterfaceHotkey(layout.Window, layout.ToggleHotkey);
        }
    }

    /// <summary>
    /// A card's right-click menu carries no data of its own: its four items are written out in XAML
    /// and used to inherit the card's macro because the menu sat in the card's logical tree. Under a
    /// theme that presents the menu inside a popup, that tree stops at the menu, every item arrives
    /// with a null DataContext, and the four handlers read that as "no macro" and quietly do nothing.
    /// Stamping it here keeps their one rule — the sender's DataContext is the macro — true.
    /// </summary>
    internal static void StampMenu(ContextMenu menu, object? data)
    {
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            item.DataContext = data;
        }
    }

    private void OnCardMenuOpening(object? sender, CancelEventArgs e)
    {
        if (sender is ContextMenu menu)
        {
            StampMenu(menu, menu.DataContext);
        }
    }

    private void OnEditMacroClicked(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;

        if (sender is Control { DataContext: MacroItem macro })
        {
            OpenEditor(macro, macro, draft: false);
        }
    }

    /// <summary>
    /// Debugs a macro, which is what the card's own button and the menu a right click opens both
    /// ask for. The window keeps the whole list, which is what its variables half lists.
    /// </summary>
    private async void OnDebugMacroClicked(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;

        if (sender is Control { DataContext: MacroItem macro })
        {
            await ShowRunDebugAsync(macro);
        }
    }

    private async void OnDeleteMacroClicked(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;

        if (sender is not Control { DataContext: MacroItem macro }
            || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var choice = await ConfirmDialog.ShowAsync(this, Strings.Get("Editor.DeleteTitle"),
            Strings.Format("Editor.DeleteMessage", macro.Name), Strings.Get("Editor.Delete"));

        if (choice == ConfirmChoice.Primary)
        {
            _triggers?.Stop(macro);
            viewModel.RemoveMacro(macro);
            RefreshRunning(viewModel);
        }
    }

    /// <summary>Clicking anywhere on a macro card toggles whether that macro is armed.</summary>
    private void OnMacroCardPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (e.Source is Visual source && source.FindAncestorOfType<Button>(true) is not null)
        {
            return;   // the card's own Edit and Delete buttons
        }

        if (e.Source is Visual clicked
            && clicked.FindAncestorOfType<Border>(true) is { DataContext: MacroItem macro }
            && DataContext is MainViewModel viewModel)
        {
            ToggleMacro(macro, viewModel);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Arms or disarms a macro, which the card answers to with a left click and its menu with
    /// "Enable" / "Disable". Both come through here: what a switch does must not depend on which of
    /// the two ways somebody reached it.
    /// </summary>
    private void ToggleMacro(MacroItem macro, MainViewModel viewModel)
    {
        viewModel.ToggleMacro(macro);

        // Arming a macro again gives a "trigger once" macro a fresh chance to run.
        if (macro.IsEnabled)
        {
            _triggers?.Rearm(macro);
        }
        else
        {
            _triggers?.Stop(macro);
        }

        RefreshRunning(viewModel);

        // 圆点就在眼前，声音不是用来看状态的：它说的是"这一下点到了"，两者成对才好分辨。
        AppSound.Play(macro.IsEnabled ? UiSound.MacroOn : UiSound.MacroOff);
    }

    /// <summary>
    /// The same switch, reached from a macro's own arm/disarm key. The name is looked up again here
    /// rather than carried as a macro: a plan made before a rename would otherwise switch whatever
    /// now answers to the old name, and doing nothing is the better answer for a name that is gone.
    /// </summary>
    private void ToggleMacroFromHotkey(string name)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var macro = viewModel.Macros.FirstOrDefault(candidate =>
            string.Equals(candidate.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));
        if (macro is not null)
        {
            ToggleMacro(macro, viewModel);
        }
    }

    /// <summary>The same switch, chosen from the card's right-click menu.</summary>
    private void OnToggleMacroClicked(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;

        if (sender is Control { DataContext: MacroItem macro }
            && DataContext is MainViewModel viewModel)
        {
            ToggleMacro(macro, viewModel);
        }
    }

    private static NativePoint? GetCursorPosition()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        return GetCursorPos(out var point) ? point : null;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }
}
