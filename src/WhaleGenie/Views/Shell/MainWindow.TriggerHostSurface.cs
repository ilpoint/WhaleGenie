using System.Threading;
using Avalonia.Input;
using Avalonia.Threading;
using WhaleGenie.Core.Devices;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

// The main window as the trigger host's native surface: the menu, wheel and panels are this
// window's own drawing and state, and the host's hooks land here through these members. The
// questions of a triggered run live in RunQuestionHost, which does not need this window.
public partial class MainWindow : Runtime.ITriggerSurfaceHost, Runtime.ITriggerFocusProbe
{
    bool Runtime.ITriggerSurfaceHost.MenuOpen => Volatile.Read(ref _menuOpen) == 1;

    bool Runtime.ITriggerSurfaceHost.MenuSearchFocused => Volatile.Read(ref _menuSearchFocused) == 1;

    bool Runtime.ITriggerSurfaceHost.MenuPinned => Volatile.Read(ref _menuPinned) == 1;

    bool Runtime.ITriggerFocusProbe.IsTextInputFocused => WindowTextFieldFocused;

    void Runtime.ITriggerSurfaceHost.CloseMenuFromLauncher() => _menu?.CloseFromLauncher();

    void Runtime.ITriggerSurfaceHost.OpenMenu() => _ = OpenMacroMenuAsync();

    void Runtime.ITriggerSurfaceHost.MenuHandleKey(string name) => _menu?.HandleKey(name);

    void Runtime.ITriggerSurfaceHost.MenuMouseDown(MouseHookEvent e) => OnMenuMouseDown(e);

    void Runtime.ITriggerSurfaceHost.WheelMouseDown(MouseHookEvent e) => OnWheelMouseDown(e);

    void Runtime.ITriggerSurfaceHost.WheelMouseMoved(MouseMovedHookEvent e) => OnWheelMouseMoved(e);

    void Runtime.ITriggerSurfaceHost.WheelMouseUp(MouseHookEvent e) => OnWheelMouseUp(e);

    void Runtime.ITriggerSurfaceHost.WheelKeyDown(KeyHookEvent e) => OnWheelKeyDown(e);

    void Runtime.ITriggerSurfaceHost.WheelScroll(WheelHookEvent e) => OnWheelScroll(e);

    void Runtime.ITriggerSurfaceHost.ToggleInterfaceGroup(string groupKey)
        => ToggleInterfaceGroupFromHotkey(groupKey);

    void Runtime.ITriggerSurfaceHost.RefreshForSystemToggle()
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        RefreshSurface(viewModel);
        RefreshWheelGate(viewModel);
        RefreshRunning(viewModel);
    }
}

