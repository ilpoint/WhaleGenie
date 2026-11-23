using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Models;

namespace WhaleGenie.Runtime;

/// <summary>
/// The native floating work the trigger host asks a window to draw: the menu, the wheel and the
/// interface panels. A window owns those surfaces; the host only forwards the hooks and asks for
/// the refresh that follows the system switch.
/// </summary>
public interface ITriggerSurfaceHost
{
    bool MenuOpen { get; }
    bool MenuSearchFocused { get; }
    bool MenuPinned { get; }

    void CloseMenuFromLauncher();
    void OpenMenu();
    void MenuHandleKey(string name);
    void MenuMouseDown(MouseHookEvent e);
    void WheelMouseDown(MouseHookEvent e);
    void WheelMouseMoved(MouseMovedHookEvent e);
    void WheelMouseUp(MouseHookEvent e);
    void WheelKeyDown(KeyHookEvent e);
    void WheelScroll(WheelHookEvent e);
    void ToggleInterfaceGroup(string groupKey);
    void RefreshForSystemToggle();
}

/// <summary>
/// The questions a triggered run asks. They are windows the interface owns and may have to show
/// while the main shell is hidden, so they are a separate surface from the menu and the wheel.
/// </summary>
public interface IRunQuestionHost
{
    Task<StepErrorChoice> AskAboutFailedStep(string step, string reason, string detail, string comment);
    Task<AskAnswer> AskUser(AskQuestion question);
    Task<bool> ConfirmIrreversibleStep(ExecutableStep step);
}

/// <summary>
/// Whether the person is typing into a text field of this program. The hook thread asks before it
/// lets a global switch key through, so the answer must be cheap and must not read a control from
/// that thread; the window copies its focus state into this probe.
/// </summary>
public interface ITriggerFocusProbe
{
    bool IsTextInputFocused { get; }
}

/// <summary>
/// The part of the program that listens to the machine between the windows: the system hotkey,
/// the macro arm/disarm keys, the plan those keys match against, and the sounds that confirm a
/// switch was heard. It lives beside a window rather than inside one — the shell in front and a
/// hidden main window behind it are two surfaces of the same host.
/// </summary>
/// <remarks>
/// The hooks are held for as long as the host is attached, which is for as long as the project is
/// open. Everything the hotkeys can ask for lands either in this class (system switch, macro
/// arm/disarm, sounds) or on the surface (menu navigation, wheel, interface groups, the run's
/// questions), so a new front never has to rewire the hooks itself.
/// </remarks>
public sealed class TriggerHost : IDisposable
{
    private readonly ProjectWorkspace _workspace;

    private readonly SystemState _system;

    private readonly TriggerRuntime _runtime;

    private readonly IDeviceLayer _devices;

    private readonly ITriggerSurfaceHost _surface;

    private readonly IRunQuestionHost _questions;

    private readonly ITriggerFocusProbe _focus;

    private readonly Action<UiSound> _playSound;

    private readonly WhaleGenie.Localization.AppSettings _settings;

    private readonly HashSet<string> _globalHeld = new(StringComparer.OrdinalIgnoreCase);

    private GlobalHotkeyPlan _plan = GlobalHotkeyPlan.Empty;

    private IInputHook? _hook;

    private bool _attached;

    /// <param name="playSound">
    /// Where the switch sounds go. The program hands in AppSound, which reads the sound settings; a
    /// check hands in a recorder so it can assert which sound was asked for.
    /// </param>
    internal TriggerHost(ProjectWorkspace workspace, SystemState system, TriggerRuntime runtime,
        IDeviceLayer devices, ITriggerSurfaceHost surface, IRunQuestionHost questions,
        ITriggerFocusProbe focus, Action<UiSound>? playSound = null,
        WhaleGenie.Localization.AppSettings? settings = null)
    {
        _settings = settings ?? WhaleGenie.Localization.AppSettings.Shared;
        _workspace = workspace;
        _system = system;
        _runtime = runtime;
        _devices = devices;
        _surface = surface;
        _questions = questions;
        _focus = focus;
        _playSound = playSound ?? (sound => AppSound.Play(_settings, sound));
    }

    /// <summary>The plan the hotkeys match against, for callers that read it while it is live.</summary>
    public GlobalHotkeyPlan Plan => Volatile.Read(ref _plan);

    /// <summary>
    /// Puts the hooks on the machine, arms the trigger service the way the switch says, and starts
    /// following the project and the switch. Attaching twice is a bug.
    /// </summary>
    public void Attach()
    {
        if (_attached)
        {
            return;
        }

        _attached = true;
        _runtime.Triggers.Ask = _questions.AskAboutFailedStep;
        _runtime.Triggers.AskUser = _questions.AskUser;
        _runtime.Triggers.ConfirmIrreversible = _questions.ConfirmIrreversibleStep;
        _runtime.Triggers.StatusChanged += OnTriggerStatusChanged;
        _runtime.Triggers.IsEnabled = _system.IsEnabled;
        _runtime.Triggers.MenuRequested += OnMenuRequested;

        _system.Changed += OnSystemChanged;
        _workspace.Changed += OnWorkspaceChanged;
        _workspace.Macros.CollectionChanged += OnMacrosChanged;
        _workspace.Triggers.Changed += OnTriggersChanged;

        AttachHooks();
        PublishPlan();
    }

    public void Dispose()
    {
        if (!_attached)
        {
            return;
        }

        _attached = false;
        _system.Changed -= OnSystemChanged;
        _workspace.Changed -= OnWorkspaceChanged;
        _workspace.Macros.CollectionChanged -= OnMacrosChanged;
        _workspace.Triggers.Changed -= OnTriggersChanged;
        _runtime.Triggers.StatusChanged -= OnTriggerStatusChanged;
        _runtime.Triggers.MenuRequested -= OnMenuRequested;

        // The service outlives the window, so the callbacks that pointed into it must not.
        _runtime.Triggers.Ask = null;
        _runtime.Triggers.AskUser = null;
        _runtime.Triggers.ConfirmIrreversible = null;
        _runtime.Triggers.IsEnabled = false;
        _runtime.Triggers.StopAll();

        DetachHooks();
        Volatile.Write(ref _plan, GlobalHotkeyPlan.Empty);
    }

    /// <summary>
    /// Gives the trigger service the project as it stands, and rewrites the hotkey plan: a plan
    /// made before a rename would otherwise switch whatever now answers to the old name.
    /// </summary>
    public void PublishPlan()
    {
        var menuLauncher = _settings.LoadMenuLauncher();
        _runtime.Publish(_workspace.Triggers, [.. _workspace.Macros], menuLauncher);
        Volatile.Write(ref _plan,
            GlobalHotkeyPlan.From(_workspace, _system,
                _settings.LoadSystemHotkey(), menuLauncher));
    }

    /// <summary>
    /// Marks the macros the trigger is running, which colours the dots on their cards and keeps
    /// the web list in the same state.
    /// </summary>
    private void OnTriggerStatusChanged()
        => Dispatcher.UIThread.Post(() =>
        {
            foreach (var macro in _workspace.Macros)
            {
                macro.IsRunning = _runtime.Triggers.IsRunning(macro.Name);
            }
        });

    private void OnWorkspaceChanged(object? sender, EventArgs e) => PublishPlan();

    private void OnMacrosChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        => PublishPlan();

    private void OnTriggersChanged(object? sender, EventArgs e) => PublishPlan();

    /// <summary>
    /// The system switch: the person flipped it (the page, the hotkey or the tray all land on the
    /// same object), so the trigger service follows and the sound says it was heard.
    /// </summary>
    private void OnSystemChanged()
    {
        _runtime.Triggers.IsEnabled = _system.IsEnabled;
        if (!_system.IsEnabled)
        {
            _surface.CloseMenuFromLauncher();
        }

        PublishPlan();
        _playSound(_system.IsEnabled ? UiSound.SystemOn : UiSound.SystemOff);
        _surface.RefreshForSystemToggle();
    }

    private void AttachHooks()
    {
        try
        {
            var hook = _devices.Hook;
            hook.KeyDown += OnSystemHotkeyDown;
            hook.KeyUp += OnSystemHotkeyUp;
            hook.MouseDown += OnSurfaceMouseDown;
            hook.MouseDown += OnSurfaceWheelMouseDown;
            hook.MouseUp += OnSurfaceWheelMouseUp;
            hook.Moved += OnSurfaceWheelMouseMoved;
            hook.KeyDown += OnSurfaceWheelKeyDown;
            hook.Wheel += OnSurfaceWheelScroll;
            _hook = hook;
        }
        catch (Exception)
        {
            // Without a hook the switch on the window still works; a machine whose hooks refuse
            // to mount must not stop the program from starting.
            _hook = null;
        }
    }

    private void DetachHooks()
    {
        if (_hook is not { } hook)
        {
            return;
        }

        hook.KeyDown -= OnSystemHotkeyDown;
        hook.KeyUp -= OnSystemHotkeyUp;
        hook.MouseDown -= OnSurfaceMouseDown;
        hook.MouseDown -= OnSurfaceWheelMouseDown;
        hook.MouseUp -= OnSurfaceWheelMouseUp;
        hook.Moved -= OnSurfaceWheelMouseMoved;
        hook.KeyDown -= OnSurfaceWheelKeyDown;
        hook.Wheel -= OnSurfaceWheelScroll;
        _globalHeld.Clear();
        _hook = null;
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

            var plan = Plan;
            if (_surface.MenuOpen)
            {
                if (plan.MenuLauncher.Length > 0
                    && TriggerKeys.Binds(plan.MenuLauncher, name, _globalHeld))
                {
                    e.Suppress = true;
                    Dispatcher.UIThread.Post(_surface.CloseMenuFromLauncher);
                    return;
                }

                if (_surface.MenuPinned && !_surface.MenuSearchFocused
                    && name is "Esc" or "Escape" or "Esc键")
                {
                    e.Suppress = true;
                    Dispatcher.UIThread.Post(() => _surface.MenuHandleKey(name));
                    return;
                }

                if (_surface.MenuSearchFocused || _surface.MenuPinned)
                {
                    return;
                }

                if (name is "Left" or "Right" or "Up" or "Down"
                    or "左" or "右" or "上" or "下"
                    or "Enter" or "Return" or "回车" or "Esc" or "Escape")
                {
                    e.Suppress = true;
                    Dispatcher.UIThread.Post(() => _surface.MenuHandleKey(name));
                    return;
                }
            }

            if (_surface.MenuOpen
                && plan.SystemHotkey.Length > 0
                && TriggerKeys.Binds(plan.SystemHotkey, name, _globalHeld))
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
                Dispatcher.UIThread.Post(() => _surface.ToggleInterfaceGroup(match.GroupKey));
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

    /// <summary>
    /// Flips the system switch from a hotkey. The keyboard-focus check is the surface's: a field
    /// on the window having the keyboard means the keys were meant for it.
    /// </summary>
    private void ToggleSystemFromHotkey()
    {
        if (_focus.IsTextInputFocused)
        {
            return;
        }

        _system.Set(!_system.IsEnabled);
    }

    /// <summary>
    /// The same switch, reached from a macro's own arm/disarm key. The name is looked up again
    /// here rather than carried as a macro: a plan made before a rename would otherwise switch
    /// whatever now answers to the old name, and doing nothing is the better answer for a name
    /// that is gone.
    /// </summary>
    private void ToggleMacroFromHotkey(string name)
    {
        var macro = _workspace.Macros.FirstOrDefault(candidate =>
            string.Equals(candidate.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));
        if (macro is null)
        {
            return;
        }

        _workspace.ToggleMacro(macro);

        if (macro.IsEnabled)
        {
            _runtime.Triggers.Rearm(macro.Name);
        }
        else
        {
            _runtime.Triggers.Stop(macro.Name);
        }

        _playSound(macro.IsEnabled ? UiSound.MacroOn : UiSound.MacroOff);
    }

    private void OnSurfaceMouseDown(MouseHookEvent e)
    {
        try
        {
            _surface.MenuMouseDown(e);
        }
        catch (Exception error)
        {
            AppDiagnostics.Record("hook.menu-mouse", error);
        }
    }

    private void OnSurfaceWheelMouseDown(MouseHookEvent e)
    {
        try
        {
            _surface.WheelMouseDown(e);
        }
        catch (Exception error)
        {
            AppDiagnostics.Record("hook.wheel-mouse-down", error);
        }
    }

    private void OnSurfaceWheelMouseUp(MouseHookEvent e)
    {
        try
        {
            _surface.WheelMouseUp(e);
        }
        catch (Exception error)
        {
            AppDiagnostics.Record("hook.wheel-mouse-up", error);
        }
    }

    private void OnSurfaceWheelMouseMoved(MouseMovedHookEvent e)
    {
        try
        {
            _surface.WheelMouseMoved(e);
        }
        catch (Exception error)
        {
            AppDiagnostics.Record("hook.wheel-moved", error);
        }
    }

    private void OnSurfaceWheelKeyDown(KeyHookEvent e)
    {
        try
        {
            _surface.WheelKeyDown(e);
        }
        catch (Exception error)
        {
            AppDiagnostics.Record("hook.wheel-key", error);
        }
    }

    private void OnSurfaceWheelScroll(WheelHookEvent e)
    {
        try
        {
            _surface.WheelScroll(e);
        }
        catch (Exception error)
        {
            AppDiagnostics.Record("hook.wheel-scroll", error);
        }
    }

    /// <summary>The menu launcher wants the floating menu opened.</summary>
    private void OnMenuRequested()
        => Dispatcher.UIThread.Post(_surface.OpenMenu);
}
