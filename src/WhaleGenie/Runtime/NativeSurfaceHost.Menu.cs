using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using WhaleGenie.Core.Devices;
using WhaleGenie.Models;

namespace WhaleGenie.Runtime;

/// <summary>
/// The things a native surface needs from the window that owns it but must not learn about: opening
/// the editor, the run window and the settings, and hiding or refreshing the panels that live beside
/// the menu. A check can hand in a stand-in instead of a window.
/// </summary>
public interface INativeSurfaceActions
{
    void OpenEditor(MacroItem macro);

    void OpenRun(MacroItem macro);

    void OpenSettings(WindowInfo? foreground);
}

/// <summary>
/// The floating work that belongs beside a window rather than inside it: the macro menu, the wheel
/// and the interface panels. The menu itself is the Lit overlay; this class owns its open state so
/// the trigger host can tell whether the launcher should close it or a hotkey should pass through.
/// </summary>
internal sealed partial class NativeSurfaceHost : ITriggerSurfaceHost, IDisposable
{
    private readonly ProjectWorkspace _workspace;
    private readonly TriggerRuntime _runtime;
    private readonly SystemState _system;
    private readonly IDeviceLayer _devices;
    private readonly Func<Avalonia.Controls.Window?> _owner;
    private readonly Func<GlobalHotkeyPlan> _plan;
    private readonly INativeSurfaceActions _actions;
    private readonly IMenuOverlay _menu;
    private readonly IWheelOverlay _wheelOverlay;

    private int _menuOpen;
    private int _menuSearchFocused;

    internal NativeSurfaceHost(ProjectWorkspace workspace, TriggerRuntime runtime, SystemState system,
        IDeviceLayer devices, Func<Avalonia.Controls.Window?> owner, Func<GlobalHotkeyPlan> plan,
        INativeSurfaceActions actions, IMenuOverlay menu, IWheelOverlay wheelOverlay)
    {
        _workspace = workspace;
        _runtime = runtime;
        _system = system;
        _devices = devices;
        _owner = owner;
        _plan = plan;
        _actions = actions;
        _menu = menu;
        _wheelOverlay = wheelOverlay;
        _menu.Hidden += OnMenuHidden;
        _menu.FocusChanged += OnMenuFocusChanged;
        _wheelOverlay.TimedOut += OnWheelTimedOut;
        InitializeWheel();
        InitializePanels();
    }

    public bool MenuOpen => Volatile.Read(ref _menuOpen) == 1;

    public bool MenuSearchFocused => Volatile.Read(ref _menuSearchFocused) == 1;

    public bool MenuPinned => _menu.Pinned;

    /// <summary>
    /// The system switch was flipped: the wheel gate and the panels both read it, and neither has a
    /// reason to wait for the next project change to notice.
    /// </summary>
    public void RefreshForSystemToggle()
    {
        RefreshWheelGate();
        RefreshPanels();
    }

    public void OpenMenu() => Dispatcher.UIThread.Post(() => _ = OpenMenuAsync());

    /// <summary>
    /// Shows the menu over the program in front. The page reads the project itself; this only keeps
    /// the trigger host's state: whether the launcher now closes the menu, and whether a hotkey may
    /// pass through to the page's search field.
    /// </summary>
    private Task OpenMenuAsync()
    {
        var foreground = Foreground();
        if (foreground is not null && _workspace.Triggers.IsMenuBlocked(foreground, _devices.Windows))
        {
            return Task.CompletedTask;
        }

        HidePanels();
        Volatile.Write(ref _menuOpen, 1);
        Volatile.Write(ref _menuSearchFocused, 0);
        MacroTriggerGate.Enter();
        _menu.Show();
        return Task.CompletedTask;
    }

    public void CloseMenuFromLauncher() => Dispatcher.UIThread.Post(_menu.Hide);

    /// <summary>The page owns its keys; the hook never needs to replay them.</summary>
    public void MenuHandleKey(string name)
    {
    }

    /// <summary>The page owns its clicks; this hook exists for the interface only.</summary>
    public void MenuMouseDown(MouseHookEvent e)
    {
    }

    /// <summary>The overlay watches the foreground itself and honours its own pin.</summary>
    public void CloseMenuForForegroundChange()
    {
    }

    private void OnMenuHidden()
    {
        MacroTriggerGate.Exit();
        Volatile.Write(ref _menuOpen, 0);
        Volatile.Write(ref _menuSearchFocused, 0);
    }

    private void OnMenuFocusChanged(bool focused)
        => Volatile.Write(ref _menuSearchFocused, focused ? 1 : 0);

    private void OnWheelTimedOut()
    {
        _wheel?.Timeout();
        CloseWheel();
    }

    private WindowInfo? Foreground()
    {
        try
        {
            return _devices.Windows.Foreground();
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        DisposePanels();
        DisposeWheel();
        _menu.Hidden -= OnMenuHidden;
        _menu.FocusChanged -= OnMenuFocusChanged;
        _wheelOverlay.TimedOut -= OnWheelTimedOut;
        Volatile.Write(ref _menuOpen, 0);
        MacroTriggerGate.Exit();
    }
}