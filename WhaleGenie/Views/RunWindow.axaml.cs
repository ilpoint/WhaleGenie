using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Storage;
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

    /// <summary>Reads what the armed triggers are waiting for again, while the window is up.</summary>
    private readonly DispatcherTimer _triggers = new();

    /// <summary>
    /// Whether there is a trigger service to read. A window opened on its own has none, and a clock
    /// that wakes up to read nothing is a clock nobody needs.
    /// </summary>
    private readonly bool _watchesTriggers;

    /// <summary>The macros the list holds, which is what the variables in use are read off.</summary>
    private readonly IReadOnlyList<MacroItem> _listed;

    /// <summary>The list the steps are shown in, kept so the run can be followed down it.</summary>
    private readonly ScrollViewer? _stepScroll;

    public RunWindow()
        : this([], SharedDevices.Current)
    {
    }

    /// <summary>
    /// Opens the window on a set of steps. A device layer can be handed in so the run can be
    /// driven without touching the real keyboard, mouse or screen, and the macros of the list can be
    /// handed in so the variables half can say which of them uses what. No steps and no layer is the
    /// window opened from the button on the main window: variables only, with nothing to run. The
    /// name the steps belong to is handed in where there is one, so a run says which macro it is.
    /// </summary>
    public RunWindow(IEnumerable<MacroStep> steps, IDeviceLayer? devices = null, double delayScale = 1,
        IMacroLibrary? macros = null, IReadOnlyList<MacroItem>? listed = null,
        MacroTriggerService? triggers = null, string macroName = "")
    {
        InitializeComponent();

        _listed = listed ?? [];
        var viewModel = new RunViewModel(steps, devices ?? SharedDevices.Current, delayScale, macros,
            _listed, triggers, macroName);
        DataContext = viewModel;
        _watchesTriggers = triggers is not null;
        Title = Strings.Get("Run.Title");

        viewModel.CloseRequested += Close;
        // The machine comes along so the window that shows what a step saw can put the writing it
        // read on the clipboard: that copy is the whole point of listing it, and the picture belongs
        // to this run rather than to a step being written, so there is no step to hand over.
        viewModel.LookRequested += look => LookWindow.Show(look, this, viewModel.Devices);
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

        // The steps list follows the run for the same reason: a row highlighted below the fold says
        // nothing about where the run is.
        _stepScroll = this.FindControl<ScrollViewer>("StepScroll");
        viewModel.CurrentChanged += row => Follow(row);
    }

    /// <summary>
    /// Brings the row the run is standing in front of into view, with a row of room on each side of
    /// it. The row's place is only known once the layout has run — a step that has just finished has
    /// written its result into the row, which makes it taller — so this waits for that pass rather
    /// than measuring against the list as it was before the step ran.
    /// </summary>
    private void Follow(RunStepViewModel? row)
    {
        if (_stepScroll is not { } scroll || row is null || Form is not { } viewModel)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (this.FindControl<ItemsControl>("StepList") is not { } list
                || list.ContainerFromIndex(viewModel.Steps.IndexOf(row)) is not { } container
                || container.TranslatePoint(default, scroll) is not { } place)
            {
                return;
            }

            // The place comes back in the space the list is shown in, which the scroll offset has
            // already moved; where the row sits down the whole list is what the arithmetic wants.
            var top = place.Y + scroll.Offset.Y;
            scroll.Offset = new Vector(0, RunScroll.Where(
                scroll.Offset.Y, top, container.Bounds.Height, scroll.Viewport.Height));
        }, DispatcherPriority.Loaded);
    }

    /// <summary>The form this window is driving.</summary>
    private RunViewModel? Form => DataContext as RunViewModel;

    /// <summary>
    /// Takes one row's value away. What lands on the clipboard is the value beside the button that
    /// was pressed rather than whichever row happens to be selected: a run is read back row by row,
    /// and a value that has to be found first is not one anybody can use.
    /// </summary>
    private void OnCopyValueClicked(object? sender, RoutedEventArgs e)
    {
        var value = sender switch
        {
            // The watch panel and the variables page show the same thing in two shapes: the live
            // value the run has left behind, and the definition the page describes a variable with.
            Button { DataContext: WatchedVariable watched } => watched.Value,
            Button { DataContext: VariableDefinition variable } => variable.CurrentValue,
            _ => null,
        };

        if (value is { Length: > 0 })
        {
            Copy(value);
        }
    }

    /// <summary>
    /// The whole log, times and all. Reading a run back is what a log is for, and when each line was
    /// written is half of what it says, so a copy carries both rather than the words alone.
    /// </summary>
    private void OnCopyLogClicked(object? sender, RoutedEventArgs e)
    {
        if (Form is { Lines.Count: > 0 } viewModel)
        {
            Copy(string.Join(Environment.NewLine,
                viewModel.Lines.Select(line => $"{line.Time}  {line.Text}")));
        }
    }

    /// <summary>
    /// Puts text where the rest of the window's clipboard work goes: through the device layer, so
    /// the clipboard this writes to is the one the run's own steps read and write.
    /// </summary>
    private void Copy(string text)
    {
        if (Form is { } viewModel && text.Length > 0)
        {
            viewModel.Devices.Clipboard.WriteText(text);
        }
    }

    /// <summary>
    /// The three keys a debugger is driven with, doing exactly what the three buttons on the
    /// tool bar do: F10 does one step, Shift+F5 stops, and F5 is run or carry on. F5 stands for two
    /// buttons at once, so which of them it is follows from where the run is — carrying on is what
    /// it means once something has stopped it.
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || Form is not { } viewModel)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.F10:
                Act(viewModel.StepCommand);
                break;
            case Key.F5 when e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                Act(viewModel.StopCommand);
                break;
            case Key.F5 when !Act(viewModel.ContinueCommand):
                Act(viewModel.RunCommand);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    /// <summary>Does what one of the run buttons would do, if it would do anything at all.</summary>
    private static bool Act(System.Windows.Input.ICommand command)
    {
        if (!command.CanExecute(null))
        {
            return false;
        }

        command.Execute(null);
        return true;
    }

    /// <summary>
    /// The line that says whose turn it is at the input door is only right while the door is
    /// watched, so the window listens while it is open and stops when it closes: the event is the
    /// whole program's, and a closed window listening to it would be a window kept alive for
    /// nothing.
    /// </summary>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        InputGate.Changed += OnInputGateChanged;
        BrowserGate.Changed += OnBrowserGateChanged;

        // What the triggers are waiting for changes on its own, so the lines are read again while
        // the window is up rather than once when it opens. A second is often enough to watch a
        // window being waited for.
        if (_watchesTriggers)
        {
            Form?.RefreshTriggers();
            _triggers.Interval = TimeSpan.FromSeconds(1);
            _triggers.Tick += OnTriggerTick;
            _triggers.Start();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        InputGate.Changed -= OnInputGateChanged;
        BrowserGate.Changed -= OnBrowserGateChanged;
        _triggers.Stop();
        _triggers.Tick -= OnTriggerTick;
        base.OnClosed(e);
    }

    private void OnInputGateChanged()
        => Dispatcher.UIThread.Post(() => Form?.RefreshInputOwner());

    private void OnBrowserGateChanged()
        => Dispatcher.UIThread.Post(() => Form?.RefreshBrowserOwner());

    private void OnTriggerTick(object? sender, EventArgs e) => Form?.RefreshTriggers();

    /// <summary>
    /// A step row's own menu, which is where the breakpoint's three things are asked for. Writing the
    /// condition is the same dialog the editor writes one in, held to the conditions: what a breakpoint
    /// waits for and what an <c>if</c> waits for are the same question, and only the place it is asked
    /// from is different.
    /// </summary>
    private async void OnBreakpointMenuClicked(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;

        if (sender is not MenuItem { DataContext: RunStepViewModel row } item || Form is not { } viewModel)
        {
            return;
        }

        switch (item.Tag as string)
        {
            case "toggle":
                viewModel.ToggleBreakpointCommand.Execute(row);
                break;
            case "condition":
                var dialog = new AddActionWindow(
                    viewModel.BreakpointCondition(row.Id), ActionCatalog.Conditions, viewModel.Choices,
                    null, null, ImageAssets.FolderFor(null))
                {
                    // The condition is tried out on the devices the run is on: a picture taken for
                    // its own sake then lands in the program's own folder, which is the one place
                    // the window knows of.
                    Devices = viewModel.Devices,
                };
                if (await dialog.ShowDialogOver<MacroStep?>(this) is { } condition)
                {
                    viewModel.ConditionBreakpoint(row, condition);
                }

                break;
            case "clear":
                viewModel.ClearBreakpointCondition(row);
                break;
        }
    }

    /// <summary>
    /// Points the window at a macro and starts debugging it, which is what the card's own menu asks
    /// for: one of these windows at a time, so debugging a second macro is this window with the
    /// other macro's steps in it rather than a second window over the same list.
    /// </summary>
    internal Task DebugAsync(MacroItem macro) => Form?.DebugAsync(macro) ?? Task.CompletedTask;

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
