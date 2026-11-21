using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Input;
using Avalonia.Threading;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

// The main window as the trigger host's surface: the menu, wheel, panels and the run's questions
// are this window's own drawing and state, and the host's hooks land here through these members.
public partial class MainWindow : Runtime.ITriggerHostSurface
{
    bool Runtime.ITriggerHostSurface.MenuOpen => Volatile.Read(ref _menuOpen) == 1;

    bool Runtime.ITriggerHostSurface.MenuSearchFocused => Volatile.Read(ref _menuSearchFocused) == 1;

    bool Runtime.ITriggerHostSurface.MenuPinned => Volatile.Read(ref _menuPinned) == 1;

    bool Runtime.ITriggerHostSurface.WindowTextFieldFocused => WindowTextFieldFocused;

    void Runtime.ITriggerHostSurface.CloseMenuFromLauncher() => _menu?.CloseFromLauncher();

    void Runtime.ITriggerHostSurface.OpenMenu() => _ = OpenMacroMenuAsync();

    void Runtime.ITriggerHostSurface.MenuHandleKey(string name) => _menu?.HandleKey(name);

    void Runtime.ITriggerHostSurface.MenuMouseDown(MouseHookEvent e) => OnMenuMouseDown(e);

    void Runtime.ITriggerHostSurface.WheelMouseDown(MouseHookEvent e) => OnWheelMouseDown(e);

    void Runtime.ITriggerHostSurface.WheelMouseMoved(MouseMovedHookEvent e) => OnWheelMouseMoved(e);

    void Runtime.ITriggerHostSurface.WheelMouseUp(MouseHookEvent e) => OnWheelMouseUp(e);

    void Runtime.ITriggerHostSurface.WheelKeyDown(KeyHookEvent e) => OnWheelKeyDown(e);

    void Runtime.ITriggerHostSurface.WheelScroll(WheelHookEvent e) => OnWheelScroll(e);

    void Runtime.ITriggerHostSurface.ToggleInterfaceGroup(string groupKey)
        => ToggleInterfaceGroupFromHotkey(groupKey);

    void Runtime.ITriggerHostSurface.RefreshForSystemToggle()
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        _host?.PublishPlan();
        RefreshSurface(viewModel);
        RefreshWheelGate(viewModel);
        RefreshRunning(viewModel);
    }

    Task<StepErrorChoice> Runtime.ITriggerHostSurface.AskAboutFailedStep(
        string step, string reason, string detail, string comment)
        => AskAboutFailedStep(step, reason, detail, comment);

    Task<AskAnswer> Runtime.ITriggerHostSurface.AskUser(AskQuestion question)
        => AskWhatTheMacroWantsToKnow(question);

    Task<bool> Runtime.ITriggerHostSurface.ConfirmIrreversibleStep(ExecutableStep step)
        => ConfirmIrreversibleStep(step);
}

