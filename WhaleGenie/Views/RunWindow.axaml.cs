using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
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

/// <summary>
/// Runs a macro and shows what it does, one step at a time if asked. Its variable page is the
/// runtime view: definitions are edited in the macro editor's variable center.
/// </summary>
public partial class RunWindow : AppWindow
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

    /// <summary>
    /// The check that a step row's menu actually arrived. It stands while the menu is being opened
    /// and is cleared once the answer is known, which is also what keeps a second right-click from
    /// opening a second menu on top of the first.
    /// </summary>
    private DispatcherTimer? _menuWatch;

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
        MacroTriggerService? triggers = null, string macroName = "",
        MacroCallable? callable = null)
    {
        InitializeComponent();

        _listed = listed ?? [];
        var viewModel = new RunViewModel(steps, devices ?? SharedDevices.Current, delayScale, macros,
            _listed, triggers, macroName, callable);
        DataContext = viewModel;
        VariableCatalog.GlobalsChanged += OnVariablesChanged;
        SharedVariables.Current.Changed += OnVariableChanged;
        _watchesTriggers = triggers is not null;
        Title = Strings.Get("Run.Title");

        // 标题栏归库，内容伸进标题栏：那一行里放鲸鱼、标题与置顶钮，整条都能拖（`AppWindow` 负责拖）。
        TitleBar.ExtendsContentIntoTitleBar = true;
        TitleBar.Height = 40;

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
            VariableCatalog.GlobalsChanged -= OnVariablesChanged;
            SharedVariables.Current.Changed -= OnVariableChanged;
            viewModel.ReleaseLooks();
            _frames.Dispose();
        };
        MacroTriggerGate.Enter();
        Closed += (_, _) => MacroTriggerGate.Exit();

        // A macro that drives another program is watched with that program in front, so this window
        // is pinned like the editor's: the run stays in sight while the work happens behind it.
        var pinButton = this.FindControl<Button>("PinButton");
        if (pinButton is not null)
        {
            pinButton.Click += (_, _) =>
            {
                Topmost = !Topmost;
                UpdateTitleBarButtons();
            };
        }

        UpdateTitleBarButtons();

        // A run keeps going while its window is out of the way, so this one is put away on its own
        // and brought back from the taskbar when the log is wanted again.
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

    private void OnVariablesChanged() =>
        Dispatcher.UIThread.Post(() => Form?.Rebuild());

    private void OnVariableChanged(string _) =>
        Dispatcher.UIThread.Post(() => Form?.Rebuild());

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
            Button { DataContext: RunGlobalVariable variable } => variable.CurrentValue,
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
    /// <summary>Keeps the pin and the maximize button saying what pressing them will do.</summary>
    private void UpdateTitleBarButtons()
    {
        if (this.FindControl<Button>("PinButton") is { } pin)
        {
            pin.Classes.Set("pinned", Topmost);
            ToolTip.SetTip(pin, Strings.Get(Topmost ? "Common.Unpin" : "Common.Pin"));
        }

        if (this.FindControl<IconView>("PinIcon") is { } pinIcon)
        {
            pinIcon.Data = Topmost ? AppIcons.PinOff : AppIcons.Pin;
        }

        var maximized = WindowState == WindowState.Maximized;

        if (this.FindControl<IconView>("MaximizeIcon") is { } maximizeIcon)
        {
            maximizeIcon.Data = maximized ? AppIcons.Restore : AppIcons.Maximize;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == WindowStateProperty)
        {
            UpdateTitleBarButtons();
        }
    }

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
    /// A context menu is lifted out of the row's logical tree when it is shown, so its items do not
    /// inherit the row. Stamping the row here keeps both their visibility bindings and the click
    /// handler looking at the same step.
    /// </summary>
    /// <remarks>
    /// The menu is opened here rather than left to the framework: it has to be opened exactly once,
    /// with the row already stamped into it, and a second open on top of the first is a menu that
    /// closes as it appears. What the framework would have done with this event is done instead, and
    /// the event is marked handled so it does not also do it.
    /// </remarks>
    private void OnStepMenuRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is Control { ContextMenu: { } menu } row)
        {
            e.Handled = true;
            OpenStepMenu(row, menu);
        }
    }

    /// <summary>
    /// The same open, reached from the release rather than from the request: a machine that does not
    /// raise the request still gets its menu, and the two paths cannot open it twice.
    /// </summary>
    private void OnStepMenuReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Right
            || sender is not Control { ContextMenu: { } menu } row
            || menu.IsOpen)
        {
            return;
        }

        OpenStepMenu(row, menu);
    }

    /// <summary>
    /// Opens the row's menu at the pointer, and notices when it does not arrive.
    /// </summary>
    /// <remarks>
    /// A context menu is a popup, which is a window of the platform's own making. On the machines
    /// this went wrong on, it reports itself open and shows nothing; nobody watching the row can tell
    /// that from a menu that is there. So the open is watched for a moment, and an answer that never
    /// arrives is recorded and answered with the panel below instead — the same four things, in a
    /// window this program draws itself.
    /// </remarks>
    private void OpenStepMenu(Control row, ContextMenu menu)
    {
        if (_menuWatch is not null || menu.IsOpen)
        {
            return;
        }

        var step = row.DataContext as RunStepViewModel;
        menu.Tag = step;
        menu.PlacementTarget = row;
        menu.Placement = PlacementMode.Pointer;
        StampStepMenu(menu);

        AppDiagnostics.Note("menu.request", $"run:{step?.Id ?? "?"}");

        var opened = false;
        void OnOpened(object? sender, EventArgs e) => opened = true;
        void OnClosed(object? sender, EventArgs e) => LetGo();

        menu.Opened += OnOpened;
        menu.Closed += OnClosed;

        var watch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        watch.Tick += (_, _) =>
        {
            watch.Stop();
            if (_menuWatch != watch)
            {
                return;
            }

            _menuWatch = null;
            menu.Opened -= OnOpened;

            // The menu is where it should be: nothing more to do, and the panel is never made.
            if (opened)
            {
                AppDiagnostics.Note("menu.opened", $"run:{step?.Id ?? "?"}");
                return;
            }

            AppDiagnostics.Note("menu.notopened", $"run:{step?.Id ?? "?"}");
        };

        _menuWatch = watch;
        watch.Start();

        try
        {
            menu.Open(row);
        }
        catch (Exception error)
        {
            AppDiagnostics.Record("run.stepmenu.open", error);
            LetGo();
        }

        // A menu that has been closed — however it was closed — is no longer being opened, and the
        // next right-click has to be able to open one again straight away.
        void LetGo()
        {
            if (_menuWatch is { } running)
            {
                running.Stop();
                _menuWatch = null;
            }

            menu.Opened -= OnOpened;
            menu.Closed -= OnClosed;
        }
    }

    private void OnStepMenuOpening(object? sender, CancelEventArgs e)
    {
        if (sender is ContextMenu menu)
        {
            StampStepMenu(menu);
        }
    }

    private void OnStepMenuOpened(object? sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu menu)
        {
            StampStepMenu(menu);
        }
    }

    private static void StampStepMenu(ContextMenu menu)
    {
        var row = menu.Tag ?? menu.DataContext ?? (menu.PlacementTarget as Control)?.DataContext;
        menu.Tag = row;
        menu.DataContext = row;
        MainWindow.StampMenu(menu, row);
    }

    /// <summary>
    /// A step row's own menu, which is where the breakpoint's three things are asked for. Writing the
    /// condition is the same dialog the editor writes one in, held to the conditions: what a breakpoint
    /// waits for and what an <c>if</c> waits for are the same question, and only the place it is asked
    /// from is different.
    /// </summary>
    private async void OnBreakpointMenuClicked(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;

        if (sender is not MenuItem { Tag: string action } item)
        {
            return;
        }

        var row = item.DataContext as RunStepViewModel
                  ?? item.CommandParameter as RunStepViewModel;
        if (row is null)
        {
            AppDiagnostics.Record("run.stepmenu.nodata",
                new InvalidOperationException($"Breakpoint menu item '{action}' has no step."));
            return;
        }

        await ApplyBreakpointAsync(row, action);
    }

    /// <summary>
    /// One of the four answers, whether it came from the popup or from the panel that stands in for
    /// it: the two paths differ in how the question is put, never in what it does.
    /// </summary>
    private async Task ApplyBreakpointAsync(RunStepViewModel row, string action)
    {
        if (Form is not { } viewModel)
        {
            return;
        }

        switch (action)
        {
            case "toggle":
                viewModel.ToggleBreakpointCommand.Execute(row);
                break;
            case "condition":
                // A small panel of its own, not the action editor: a condition is a question about
                // the run, and asking somebody to build an `if` step to write it was the reason
                // nobody used this.
                var panel = await BreakpointWindow.EditAsync(this, viewModel.BreakpointRuleOf(row.Id),
                    viewModel.Choices);
                if (panel is null || panel.How is BreakpointViewModel.Ending.Cancel)
                {
                    break;
                }

                if (panel.How is BreakpointViewModel.Ending.Clear || panel.Rule is null)
                {
                    viewModel.ClearBreakpointRule(row);
                    break;
                }

                viewModel.SetBreakpointRule(row, panel.Rule);
                break;
            case "clear":
                viewModel.ClearBreakpointRule(row);
                break;
        }
    }

    /// <summary>
    /// Points the window at a macro and starts debugging it, which is what the card's own menu asks
    /// for: one of these windows at a time, so debugging a second macro is this window with the
    /// other macro's steps in it rather than a second window over the same list.
    /// </summary>
    internal Task DebugAsync(MacroItem macro) => Form?.DebugAsync(macro) ?? Task.CompletedTask;


}
