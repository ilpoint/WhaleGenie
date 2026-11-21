using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Runtime;

/// <summary>
/// The surface the trigger host draws its floating work on: the menu, the wheel, the interface
/// panels and the questions a run asks are windows the shell owns, so the host asks rather than
/// shows them itself. The main window (and anything else that carries a project) implements this;
/// a check passes a stand-in.
/// </summary>
public interface ITriggerHostSurface
{
    bool MenuOpen { get; }
    bool MenuSearchFocused { get; }
    bool MenuPinned { get; }
    bool WindowTextFieldFocused { get; }

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

    Task<StepErrorChoice> AskAboutFailedStep(string step, string reason, string detail, string comment);
    Task<AskAnswer> AskUser(AskQuestion question);
    Task<bool> ConfirmIrreversibleStep(ExecutableStep step);
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
    private readonly MainViewModel _viewModel;

    private readonly TriggerRuntime _runtime;

    private readonly IDeviceLayer _devices;

    private readonly ITriggerHostSurface _surface;

    private readonly HashSet<string> _globalHeld = new(StringComparer.OrdinalIgnoreCase);

    private GlobalHotkeyPlan _plan = GlobalHotkeyPlan.Empty;

    private IInputHook? _hook;

    private bool _attached;

    internal TriggerHost(MainViewModel viewModel, TriggerRuntime runtime, IDeviceLayer devices,
        ITriggerHostSurface surface)
    {
        _viewModel = viewModel;
        _runtime = runtime;
        _devices = devices;
        _surface = surface;
    }

    /// <summary>The plan the hotkeys match against, for callers that read it while it is live.</summary>
    public GlobalHotkeyPlan Plan => Volatile.Read(ref _plan);

    /// <summary>
    /// Puts the hooks on the machine, arms the trigger service the way the setting says, and
    /// starts following the project and the system switch. Attaching twice is a bug.
    /// </summary>
    public void Attach()
    {
        if (_attached)
        {
            return;
        }

        _attached = true;
        PublishPlan();
        _runtime.Triggers.Ask = _surface.AskAboutFailedStep;
        _runtime.Triggers.AskUser = _surface.AskUser;
        _runtime.Triggers.ConfirmIrreversible = _surface.ConfirmIrreversibleStep;
        _runtime.Triggers.StatusChanged += OnTriggerStatusChanged;
        _runtime.Triggers.IsEnabled = _viewModel.IsRunning;
        _runtime.Triggers.MenuRequested += OnMenuRequested;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

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
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _runtime.Triggers.StatusChanged -= OnTriggerStatusChanged;
        _runtime.Triggers.MenuRequested -= OnMenuRequested;
        DetachHooks();
    }

    /// <summary>
    /// Gives the trigger service the project as it stands, and rewrites the hotkey plan: a plan
    /// made before a rename would otherwise switch whatever now answers to the old name.
    /// </summary>
    public void PublishPlan()
    {
        var menuLauncher = Localization.LocalSettings.LoadMenuLauncher();
        _runtime.Publish(_viewModel.Triggers, [.. _viewModel.Macros], menuLauncher);
        Volatile.Write(ref _plan,
            GlobalHotkeyPlan.From(_viewModel, Localization.LocalSettings.LoadSystemHotkey(), menuLauncher));
    }

    /// <summary>
    /// Marks the macros the trigger is running, which colours the dots on their cards and keeps
    /// the web list in the same state.
    /// </summary>
    private void OnTriggerStatusChanged()
        => Dispatcher.UIThread.Post(() =>
        {
            foreach (var macro in _viewModel.Macros)
            {
                macro.IsRunning = _runtime.Triggers.IsRunning(macro.Name);
            }
        });

    /// <summary>
    /// The system switch: the person flipped it (the page, the hotkey or the tray all land on the
    /// same property), so the trigger service follows and the sound says it was heard.
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.IsRunning))
        {
            return;
        }

        _runtime.Triggers.IsEnabled = _viewModel.IsRunning;
        if (!_viewModel.IsRunning)
        {
            _surface.CloseMenuFromLauncher();
        }

        AppSound.Play(_viewModel.IsRunning ? UiSound.SystemOn : UiSound.SystemOff);
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
        if (_surface.WindowTextFieldFocused)
        {
            return;
        }

        _viewModel.IsRunning = !_viewModel.IsRunning;
    }

    /// <summary>
    /// The same switch, reached from a macro's own arm/disarm key. The name is looked up again
    /// here rather than carried as a macro: a plan made before a rename would otherwise switch
    /// whatever now answers to the old name, and doing nothing is the better answer for a name
    /// that is gone.
    /// </summary>
    private void ToggleMacroFromHotkey(string name)
    {
        var macro = _viewModel.Macros.FirstOrDefault(candidate =>
            string.Equals(candidate.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));
        if (macro is null)
        {
            return;
        }

        _viewModel.ToggleMacro(macro);

        if (macro.IsEnabled)
        {
            _runtime.Triggers.Rearm(macro.Name);
        }
        else
        {
            _runtime.Triggers.Stop(macro.Name);
        }

        AppSound.Play(macro.IsEnabled ? UiSound.MacroOn : UiSound.MacroOff);
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
