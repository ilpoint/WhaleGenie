using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

/// <summary>
/// Runs a macro and shows what it does, one step at a time if asked. It is also where the variables
/// are managed: a macro that reads the wrong variable is usually the reason a run went wrong, and
/// being able to look at — or fix — the shared ones without leaving the run is the point of putting
/// the two together.
/// </summary>
public partial class RunWindow : Window
{
    /// <summary>The frame around the window the run is reading right now.</summary>
    private readonly ScreenFrames _frames = new();

    /// <summary>The macros the list holds, which is what the variables in use are read off.</summary>
    private readonly IReadOnlyList<MacroItem> _listed;

    public RunWindow()
        : this([])
    {
    }

    /// <summary>
    /// Opens the window on a set of steps. A device layer can be handed in so the run can be
    /// driven without touching the real keyboard, mouse or screen, and the macros of the list can be
    /// handed in so the variables half can say which of them uses what. No steps and no layer is the
    /// window opened from the button on the main window: variables only, with nothing to run.
    /// </summary>
    public RunWindow(IEnumerable<MacroStep> steps, IDeviceLayer? devices = null, double delayScale = 1,
        IMacroLibrary? macros = null, IReadOnlyList<MacroItem>? listed = null)
    {
        InitializeComponent();

        _listed = listed ?? [];
        var viewModel = new RunViewModel(steps, devices, delayScale, macros, _listed);
        DataContext = viewModel;
        Title = Strings.Get("Run.Title");

        viewModel.CloseRequested += Close;
        viewModel.LookRequested += look => LookWindow.Show(look, this);
        viewModel.WindowRead += place =>
        {
            if (place is { } found)
            {
                _frames.Show([found]);
            }
            else
            {
                _frames.Clear();
            }
        };
        Closing += (_, _) => viewModel.StopCommand.Execute(null);
        Closed += (_, _) =>
        {
            viewModel.ReleaseDevices();
            viewModel.ReleaseLooks();
            _frames.Dispose();
        };
        MacroTriggerGate.Enter();
        Closed += (_, _) => MacroTriggerGate.Exit();

        var closeButton = this.FindControl<Button>("CloseButton");
        if (closeButton is not null)
        {
            closeButton.Click += (_, _) => Close();
        }

        // A run keeps going while its window is out of the way, so this one is put away on its own
        // and brought back from the taskbar when the log is wanted again.
        var minimizeButton = this.FindControl<Button>("MinimizeButton");
        if (minimizeButton is not null)
        {
            minimizeButton.Click += (_, _) => WindowState = WindowState.Minimized;
        }

        // Keep the newest log line in view, so a long run stays readable without scrolling.
        var logScroll = this.FindControl<ScrollViewer>("LogScroll");
        if (logScroll is not null)
        {
            viewModel.Lines.CollectionChanged += (_, _) => logScroll.ScrollToEnd();
        }

        // Reading the clipboard cannot be done while the rows are built, so the row that reports it
        // is filled in once the window is up.
        _ = LoadClipboardAsync();
    }

    /// <summary>The form this window is driving.</summary>
    private RunViewModel? Form => DataContext as RunViewModel;

    /// <summary>
    /// Points the window at a macro and starts debugging it, which is what the card's own menu asks
    /// for: one of these windows at a time, so debugging a second macro is this window with the
    /// other macro's steps in it rather than a second window over the same list.
    /// </summary>
    internal Task DebugAsync(MacroItem macro) => Form?.DebugAsync(macro) ?? Task.CompletedTask;

    /// <summary>Fills in the clipboard text, which cannot be read while the rows are built.</summary>
    private async Task LoadClipboardAsync()
    {
        if (Form is not { } viewModel)
        {
            return;
        }

        try
        {
            var text = await Clipboard!.TryGetTextAsync();
            viewModel.SetClipboard(text ?? string.Empty);
        }
        catch
        {
            // The clipboard may be locked by another process, or not be there at all; the row that
            // reports it simply stays empty.
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
