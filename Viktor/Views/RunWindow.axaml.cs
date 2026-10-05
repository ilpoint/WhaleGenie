using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Viktor.Core.Devices;
using Viktor.Core.Execution;
using Viktor.Execution;
using Viktor.Localization;
using Viktor.Models;
using Viktor.ViewModels;

namespace Viktor.Views;

/// <summary>Runs a macro and shows what it does, one step at a time if asked.</summary>
public partial class RunWindow : Window
{
    public RunWindow()
        : this([])
    {
    }

    /// <summary>
    /// Opens the window on a set of steps. A device layer can be handed in so the run can be
    /// driven without touching the real keyboard, mouse or screen.
    /// </summary>
    public RunWindow(IEnumerable<MacroStep> steps, IDeviceLayer? devices = null, double delayScale = 1,
        IMacroLibrary? macros = null)
    {
        InitializeComponent();

        var viewModel = new RunViewModel(steps, devices, delayScale, macros);
        DataContext = viewModel;
        Title = Strings.Get("Run.Title");

        viewModel.CloseRequested += Close;
        Closing += (_, _) => viewModel.StopCommand.Execute(null);
        Closed += (_, _) => viewModel.ReleaseDevices();
        MacroTriggerGate.Enter();
        Closed += (_, _) => MacroTriggerGate.Exit();

        var closeButton = this.FindControl<Button>("CloseButton");
        if (closeButton is not null)
        {
            closeButton.Click += (_, _) => Close();
        }

        // Keep the newest log line in view, so a long run stays readable without scrolling.
        var logScroll = this.FindControl<ScrollViewer>("LogScroll");
        if (logScroll is not null)
        {
            viewModel.Lines.CollectionChanged += (_, _) => logScroll.ScrollToEnd();
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            && e.GetPosition(this).Y <= 38)
        {
            BeginMoveDrag(e);
        }
    }
}
