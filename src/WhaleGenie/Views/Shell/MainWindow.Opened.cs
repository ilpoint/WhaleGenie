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

        viewModel.Variables.GlobalsChanged += OnGlobalsChanged;

        // The one device layer the program holds: a trigger run and a window that is open both use
        // it, so the text recognition models are loaded once rather than once per run.
        Volatile.Write(ref _systemRunning, viewModel.IsRunning ? 1 : 0);
        _triggers = Runtime.Triggers;

        // The hooks, the hotkey plan and the sounds live in the trigger host; this window is the
        // surface it draws the floating work on (menu, wheel, panels, the run's questions).
        _host = new TriggerHost(viewModel, Runtime, _devices, this);
        _host.Attach();

        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.IsRunning))
            {
                Volatile.Write(ref _systemRunning, viewModel.IsRunning ? 1 : 0);
            }
        };

        // The wheel has its own state and its own file. Nothing here asks the trigger service to
        // yield a key: the middle button keeps the untouched menu path above.
        _wheelState = WheelMenuStore.Load();
        _wheel = new WheelGesture(
            () => Volatile.Read(ref _wheelState).Options.Launcher,
            () => Volatile.Read(ref _wheelState).Options.RepeatKey,
            WheelCanOpen);
        WheelMenuStore.Changed += OnWheelStateChanged;

        var windowEvents = _devices.WindowEvents;
        windowEvents.ForegroundChanged += OnSurfaceForegroundChanged;
        windowEvents.BoundsChanged += OnSurfaceBoundsChanged;
        viewModel.Triggers.Changed += OnProjectTriggersChanged;
        viewModel.Changed += OnProjectChanged;
        viewModel.Macros.CollectionChanged += OnMacrosChanged;

        RefreshWheelGate(viewModel);
        RefreshRunning(viewModel);
        WatchUnsavedProject(viewModel);
        Closed += (_, _) =>
        {
            _host?.Dispose();
            _host = null;
            WheelMenuStore.Changed -= OnWheelStateChanged;
            CloseWheel();
            windowEvents.ForegroundChanged -= OnSurfaceForegroundChanged;
            windowEvents.BoundsChanged -= OnSurfaceBoundsChanged;
            viewModel.Triggers.Changed -= OnProjectTriggersChanged;
            viewModel.Changed -= OnProjectChanged;
            viewModel.Macros.CollectionChanged -= OnMacrosChanged;
            viewModel.Variables.GlobalsChanged -= OnGlobalsChanged;
            HideSurface();
            StopTriggers();
            _recovery?.Stop();
        };
    }

    private void OnGlobalsChanged()
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.MarkGlobalsChanged();

            // A global is written into the package, and it moves nothing the list itself raises a
            // change for, so the draft is told to follow now rather than on its own clock.
            _recovery?.Follow();
        }
    }

    private void OnProjectTriggersChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            RefreshWheelGate(viewModel);
            _host?.PublishPlan();
            RefreshSurface(viewModel);
        }
    }

    private void OnProjectChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            RefreshWheelGate(viewModel);
            _host?.PublishPlan();
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
            _host?.PublishPlan();
        }
    }
}
