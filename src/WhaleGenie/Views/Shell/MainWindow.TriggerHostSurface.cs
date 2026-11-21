using WhaleGenie.Core.Devices;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

// The main window as the trigger host's native surface: the menu now lives in NativeSurfaceHost,
// the wheel and panels are still this window's own drawing and state. The questions of a triggered
// run live in RunQuestionHost, which does not need this window.
public partial class MainWindow : Runtime.ITriggerSurfaceHost, Runtime.ITriggerFocusProbe,
    Runtime.INativeSurfaceActions
{
    bool Runtime.ITriggerSurfaceHost.MenuOpen => _nativeSurface?.MenuOpen == true;

    bool Runtime.ITriggerSurfaceHost.MenuSearchFocused => _nativeSurface?.MenuSearchFocused == true;

    bool Runtime.ITriggerSurfaceHost.MenuPinned => _nativeSurface?.MenuPinned == true;

    bool Runtime.ITriggerFocusProbe.IsTextInputFocused => WindowTextFieldFocused;

    void Runtime.ITriggerSurfaceHost.CloseMenuFromLauncher() => _nativeSurface?.CloseMenuFromLauncher();

    void Runtime.ITriggerSurfaceHost.OpenMenu() => _nativeSurface?.OpenMenu();

    void Runtime.ITriggerSurfaceHost.MenuHandleKey(string name) => _nativeSurface?.MenuHandleKey(name);

    void Runtime.ITriggerSurfaceHost.MenuMouseDown(MouseHookEvent e) => _nativeSurface?.MenuMouseDown(e);

    void Runtime.ITriggerSurfaceHost.WheelMouseDown(MouseHookEvent e)
        => _nativeSurface?.WheelMouseDown(e);

    void Runtime.ITriggerSurfaceHost.WheelMouseMoved(MouseMovedHookEvent e)
        => _nativeSurface?.WheelMouseMoved(e);

    void Runtime.ITriggerSurfaceHost.WheelMouseUp(MouseHookEvent e)
        => _nativeSurface?.WheelMouseUp(e);

    void Runtime.ITriggerSurfaceHost.WheelKeyDown(KeyHookEvent e)
        => _nativeSurface?.WheelKeyDown(e);

    void Runtime.ITriggerSurfaceHost.WheelScroll(WheelHookEvent e)
        => _nativeSurface?.WheelScroll(e);

    void Runtime.ITriggerSurfaceHost.ToggleInterfaceGroup(string groupKey)
        => _nativeSurface?.ToggleInterfaceGroup(groupKey);

    void Runtime.ITriggerSurfaceHost.RefreshForSystemToggle()
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        _nativeSurface?.RefreshPanels();
        _nativeSurface?.RefreshWheelGate();
        RefreshRunning(viewModel);
    }

    void Runtime.INativeSurfaceActions.OpenEditor(MacroItem macro)
        => OpenEditor(macro, macro, draft: false);

    void Runtime.INativeSurfaceActions.OpenRun(MacroItem macro)
        => _ = ShowRunDebugAsync(macro);

    void Runtime.INativeSurfaceActions.OpenSettings(WindowInfo? foreground)
        => _ = OpenMenuSettingsAsync(foreground);

}

