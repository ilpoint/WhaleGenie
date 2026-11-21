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
using WhaleGenie.Projects;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;


namespace WhaleGenie.Views;

// What happens once the window is up, and the project changes it answers.
public partial class MainWindow : AppWindow
{
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

        // The one device layer the program holds: a trigger run and a window that is open both use
        // it, so the text recognition models are loaded once rather than once per run.
        _triggers = Runtime.Triggers;

        // A window built without a container (a check, or a caller that only has a view model)
        // shares the switch the view model draws; the production root hands in the same object.
        _system = viewModel.SystemSwitch;
        _interaction = new ProjectInteraction(viewModel.Workspace, () => this, ReportInteractionAsync);

        // The menu, wheel and panels are the host's; the trigger host forwards their hooks there.
        _nativeSurface = new NativeSurfaceHost(viewModel.Workspace, Runtime, _system, _devices,
            () => this, () => _host?.Plan ?? GlobalHotkeyPlan.Empty, this);
        _host = new TriggerHost(viewModel.Workspace, _system, Runtime, _devices,
            this, _questions, new WindowFocusProbe());
        _host.Attach();

        viewModel.Triggers.Changed += OnProjectTriggersChanged;
        viewModel.Changed += OnProjectChanged;
        viewModel.Macros.CollectionChanged += OnMacrosChanged;

        _nativeSurface.RefreshWheelGate();
        RefreshRunning(viewModel);
        WatchSession(viewModel);
        Closed += (_, _) =>
        {
            _host?.Dispose();
            _host = null;
            _nativeSurface?.Dispose();
            _nativeSurface = null;
            _interaction = null;
            viewModel.Triggers.Changed -= OnProjectTriggersChanged;
            viewModel.Changed -= OnProjectChanged;
            viewModel.Macros.CollectionChanged -= OnMacrosChanged;
            viewModel.Variables.GlobalsChanged -= OnGlobalsChanged;
            StopTriggers();
            _session?.Stop();
        };
    }

    private void OnGlobalsChanged()
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.MarkGlobalsChanged();

            // A global is written into the package, and it moves nothing the list itself raises a
            // change for, so the draft is told to follow now rather than on its own clock.
            _session?.Follow();
        }
    }

    private void OnProjectTriggersChanged(object? sender, EventArgs e)
    {
        _nativeSurface?.RefreshWheelGate();
        _nativeSurface?.RefreshPanels();
    }

    private void OnProjectChanged(object? sender, EventArgs e)
        => _nativeSurface?.RefreshWheelGate();

    private void OnMacrosChanged(object? sender,
        System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        // Opening a package replaces the macros and the wheel without raising a change of the
        // project itself, so the gate learns about the new wheel here as well.
        _nativeSurface?.RefreshWheelGate();
    }
}
