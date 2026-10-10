using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Devices.Platform;
using WhaleGenie.Core.Execution;
using WhaleGenie.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

public partial class AddActionWindow : Window
{
    /// <summary>Whether the dialog has been dismissed, which can happen while a page is open.</summary>
    private bool _closed;

    /// <summary>The clock that takes a "ran it" line away again, while one is running.</summary>
    private DispatcherTimer? _runTimer;

    /// <summary>
    /// The keyboard drawn on screen, kept while the dialog is up. A combination is built a key at a
    /// time, so the window stays open and takes the next cap instead of being opened again for it.
    /// </summary>
    private VirtualKeyboardWindow? _keyPad;

    /// <summary>Where the keyboard is writing at the moment: the field or the row that asked for it.</summary>
    private Action<string>? _keyTarget;

    /// <summary>The step the form was opened on for editing, or null while writing a new one.</summary>
    private MacroStep? _editingStep;

    private IDeviceLayer? _devices;

    public AddActionWindow()
        : this(null, null, null, null, null, null)
    {
    }

    /// <summary>
    /// The devices the test button sends through — the real ones unless a check has put its own
    /// here. Made the first time the button is used, so opening the dialog costs nothing.
    /// </summary>
    internal IDeviceLayer Devices
    {
        get => _devices ??= new WindowsDeviceLayer();
        set => _devices = value;
    }

    /// <summary>
    /// The macros a run started here may call, handed in by the editor. A step that calls another
    /// macro is run the same way it will be in a run, rather than failing for want of a library the
    /// dialog was never given.
    /// </summary>
    internal IMacroLibrary? Macros { get; set; }

    /// <summary>
    /// True while the window stays up from one step to the next, which is how the editor opens it:
    /// a macro is a run of steps, and being taken back to the list to open the window again for
    /// each one is the thing this saves.
    /// </summary>
    internal bool StaysOpen
    {
        get => Form?.StaysOpen ?? false;
        set
        {
            if (Form is { } form)
            {
                form.StaysOpen = value;
            }
        }
    }

    /// <summary>
    /// Where a step written here goes. The editor takes it because only the editor knows what is
    /// picked in the list and what that place accepts; it answers null once the step is in the
    /// list, or what to say when the place would not take it.
    /// </summary>
    internal Func<MacroStep?, MacroStep, string?>? PlaceStep { get; set; }

    /// <summary>
    /// A name for the next step, minted by the editor. The window outlives one step, and two steps
    /// of a macro are told apart by their names, so a name held from when the window opened would
    /// be handed out twice.
    /// </summary>
    internal Func<string>? MintStepId { get; set; }

    /// <summary>The form this window is driving.</summary>
    private AddActionViewModel? Form => DataContext as AddActionViewModel;

    /// <summary>
    /// Opens the dialog, optionally preloaded with a step being edited and optionally
    /// restricted to a catalogue subset, such as the condition actions.
    /// </summary>
    public AddActionWindow(MacroStep? existing, IReadOnlyList<ActionDefinition>? actions = null,
        IReadOnlyList<VariableChoice>? variables = null, IReadOnlyList<string>? macros = null,
        string? presetKey = null, string? assetFolder = null,
        IReadOnlyList<ActionParameterOption>? steps = null, string? newStepId = null)
    {
        InitializeComponent();

        var viewModel = new AddActionViewModel(actions, variables, macros, steps, newStepId);
        viewModel.AssetFolder = assetFolder ?? string.Empty;

        // Picking a position has to know where the window a step is anchored to sits right now,
        // so what the pointer is over is stored the way the engine will read it back. The same goes
        // for a step anchored to a control, which needs the control's own rectangle.
        viewModel.Windows = new WindowsWindowDevice();
        viewModel.Ui = new FlaUiDevice();

        if (existing is not null)
        {
            _editingStep = existing;
            viewModel.LoadFrom(existing);
        }
        else if (!string.IsNullOrEmpty(presetKey))
        {
            viewModel.SelectAction(presetKey);
        }

        DataContext = viewModel;
        viewModel.CloseRequested += OnFormFinished;
        viewModel.NestedAddRequested += OnNestedAddRequested;
        viewModel.NestedEditRequested += OnNestedEditRequested;
        Title = viewModel.Header;

        var minimizeButton = this.FindControl<Button>("MinimizeButton");
        if (minimizeButton is not null)
        {
            minimizeButton.Click += (_, _) => WindowState = WindowState.Minimized;
        }

        var closeButton = this.FindControl<Button>("CloseButton");
        if (closeButton is not null)
        {
            closeButton.Click += (_, _) => Close(null);
        }

        // Tunnelling so Escape cancels before any focused editor consumes it.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);

        // A variable picker shows nothing until a key is pressed, so open its candidate
        // list as soon as the field is clicked or receives focus.
        AddHandler(GotFocusEvent, OnVariablePickerFocused, RoutingStrategies.Bubble);
        AddHandler(PointerPressedEvent, OnVariablePickerPressed, RoutingStrategies.Tunnel);
    }

    private void OnVariablePickerFocused(object? sender, RoutedEventArgs e) => OpenCandidates(e.Source);

    private void OnVariablePickerPressed(object? sender, PointerPressedEventArgs e) => OpenCandidates(e.Source);

    /// <summary>
    /// Opens at a size the desktop can hold. The four pages are read comfortably in a thousand
    /// pixels of width, but a 1366-wide laptop has less than that to give once the window is kept
    /// clear of its edges — so the size wanted is trimmed to the room there is rather than the
    /// dialog hanging off the screen.
    /// </summary>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        var screen = (Owner is { } owner ? Screens.ScreenFromWindow(owner) : null) ?? Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var scale = screen.Scaling > 0 ? screen.Scaling : 1;
        var (width, height) = DialogSize.Fit(
            screen.WorkingArea.Width / scale, screen.WorkingArea.Height / scale);
        Width = width;
        Height = height;
    }

    /// <summary>Opens the dropdown of a variable picker that has something to offer.</summary>
    private static void OpenCandidates(object? source)
    {
        if (source is not Visual visual
            || visual.FindAncestorOfType<AutoCompleteBox>(true) is not { } picker
            || picker.ItemsSource is not { } items
            || !items.Cast<object>().Any())
        {
            return;
        }

        picker.IsDropDownOpen = true;
    }

    /// <summary>
    /// The form was finished with: it either holds a step to put somewhere, or nothing when the
    /// user cleared it. A window opened for one step closes and hands the step back; the editor's
    /// window stays up, so it is told where the step goes and starts again on the next one.
    /// </summary>
    private void OnFormFinished(MacroStep? step)
    {
        if (!StaysOpen)
        {
            Close(step);
            return;
        }

        if (step is not null && PlaceStep is { } place && place(_editingStep, step) is { } refusal)
        {
            // The place picked in the list does not take this kind of step, so nothing was added.
            // The form goes back to a fresh step — offering what that place does take, which the
            // editor has already handed over — and the line under it says why.
            BeginStep(null);
            Form?.Say(refusal, warning: true);
            return;
        }

        BeginStep(null);
    }

    /// <summary>
    /// Starts the form again on a fresh step of the action it is already showing, under a name the
    /// editor mints. This is what "that one is done" looks like in a window that does not close.
    /// </summary>
    internal void BeginStep(string? presetKey)
    {
        _editingStep = null;
        ResetKeyPad();
        Form?.Reset(MintStepId?.Invoke());

        if (!string.IsNullOrEmpty(presetKey))
        {
            Form?.SelectAction(presetKey);
        }
    }

    /// <summary>Points the form at a step that is already in the list, so it can be changed.</summary>
    internal void EditStep(MacroStep step)
    {
        _editingStep = step;
        ResetKeyPad();
        Form?.LoadFrom(step);
    }

    /// <summary>
    /// Takes what the editor knows about the place a step would go now: which actions that place
    /// takes, and the names its fields may offer. Both change while the window is up, because the
    /// list behind it is still being worked on.
    /// </summary>
    internal void Retarget(IReadOnlyList<ActionDefinition>? actions,
        IReadOnlyList<VariableChoice>? variables, IReadOnlyList<string>? macros,
        IReadOnlyList<ActionParameterOption>? steps, string? assetFolder)
        => Form?.Retarget(actions, variables, macros, steps, assetFolder);

    /// <summary>
    /// The form has moved on to another step, so the keyboard is no longer writing into anything
    /// that is on screen: the fields it was pointed at are gone with the form it belonged to.
    /// </summary>
    private void ResetKeyPad()
    {
        _keyTarget = null;
        _keyPad?.Close();
    }

    /// <summary>Opens a picker for a nested list and appends whatever the user builds.</summary>
    private async void OnNestedAddRequested(StepListEditorViewModel list)
    {
        var variables = (DataContext as AddActionViewModel)?.CollectVariables();
        var assets = (DataContext as AddActionViewModel)?.AssetFolder;
        var steps = (DataContext as AddActionViewModel)?.StepChoices;
        var dialog = new AddActionWindow(null, list.Catalog, variables, null, null, assets, steps,
            FreeStepId(steps));
        var step = await dialog.ShowDialogOver<MacroStep?>(this);

        if (step is not null)
        {
            list.AddStep(step);
        }
    }

    /// <summary>
    /// A name for a step that is being added from inside another dialog. The editor settles the
    /// whole tree when the outer step joins the list, so a name drawn from the ones already in the
    /// macro is one it will keep — which is what the variables this step creates are named after.
    /// </summary>
    private static string FreeStepId(IReadOnlyList<ActionParameterOption>? steps)
        => StepIds.Next(new HashSet<string>(
            (steps ?? []).Select(option => option.Value), StringComparer.OrdinalIgnoreCase));

    /// <summary>Opens a picker for an existing nested step and swaps in the result.</summary>
    private async void OnNestedEditRequested(MacroStep step)
    {
        var variables = (DataContext as AddActionViewModel)?.CollectVariables();
        var assets = (DataContext as AddActionViewModel)?.AssetFolder;
        var steps = (DataContext as AddActionViewModel)?.StepChoices;
        var dialog = new AddActionWindow(step, null, variables, null, null, assets, steps);
        var edited = await dialog.ShowDialogOver<MacroStep?>(this);

        if (edited is null || DataContext is not AddActionViewModel viewModel)
        {
            return;
        }

        foreach (var parameter in viewModel.Parameters)
        {
            if (parameter.List is { } list && list.Steps.Contains(step))
            {
                list.ReplaceStep(step, edited);
                return;
            }
        }
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        // Alt + X takes the pointer's place, for the actions that aim at a screen position.
        if (e.Key == Key.X && e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            e.Handled = true;
            CaptureCursorPosition();
            return;
        }

        if (e.Key is not Key.Escape || DataContext is not AddActionViewModel viewModel)
        {
            return;
        }

        viewModel.CancelCommand.Execute(null);
        e.Handled = true;
    }

    private void CaptureCursorPosition()
    {
        if (DataContext is AddActionViewModel viewModel)
        {
            var point = WindowsScreenDevice.CursorPosition();
            viewModel.ApplyCursorPosition(point.X, point.Y);
        }
    }

    /// <summary>Opens the region picker and writes the rectangle it returns into the action.</summary>
    private async void OnPickRegion(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AddActionViewModel viewModel)
        {
            return;
        }

        var region = await RegionPickerWindow.PickAsync(this);
        if (region is { } picked)
        {
            viewModel.ApplyRegion(picked.X, picked.Y, picked.Width, picked.Height);
        }
    }

    /// <summary>Adds an empty place to look at to a step's list of them.</summary>
    private void OnAddRegionRow(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: StepParameterViewModel parameter })
        {
            parameter.AddRegion();
        }
    }

    /// <summary>Adds another press to a key run.</summary>
    private void OnAddKeyRow(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: StepParameterViewModel parameter })
        {
            parameter.AddKeyRow();
        }
    }

    /// <summary>Adds an empty row to the pictures a search looks for.</summary>
    private void OnAddImageRow(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: StepParameterViewModel parameter })
        {
            parameter.AddPicture();
        }
    }

    /// <summary>Chooses a picture file for one row of a search's list of them.</summary>
    private async void OnBrowseImageRow(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: ImageRowViewModel row })
        {
            return;
        }

        if (await PickPictureAsync() is { Length: > 0 } path)
        {
            row.Text = path;
        }
    }

    /// <summary>
    /// Drags a rectangle on the screen and keeps it as a picture inside the macro project, which is
    /// how a reference picture is normally made.
    /// </summary>
    private async void OnCaptureImageRow(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: ImageRowViewModel row }
            || DataContext is not AddActionViewModel viewModel)
        {
            return;
        }

        if (await RegionPickerWindow.PickAsync(this) is not { } region)
        {
            return;
        }

        try
        {
            row.Text = ImageAssets.Capture(new WindowsScreenDevice(),
                region.X, region.Y, region.Width, region.Height, viewModel.AssetFolder);
        }
        catch (Exception)
        {
            await ConfirmDialog.ShowAsync(this, Strings.Get("Add.CaptureImage"),
                Strings.Get("Add.ImageFailed"), Strings.Get("Common.Ok"), showCancel: false);
        }
    }

    /// <summary>
    /// Points the keyboard drawn on screen at one press of a key run. The keys already there stay,
    /// because a combination of several keys is built a key at a time.
    /// </summary>
    private void OnPickKeyRow(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: KeyRowViewModel row })
        {
            OpenKeyPad(Strings.Format("Add.KeyRunPress", row.Number), row.AddKey);
        }
    }

    /// <summary>
    /// Points the keyboard at the field whose button was clicked: one key is set outright, a
    /// combination is joined to what is already there.
    /// </summary>
    private void OpenKeyPadFor(StepParameterViewModel parameter)
        => OpenKeyPad(parameter.Definition.LocalLabel, key =>
        {
            if (parameter.IsKeys)
            {
                parameter.AddKey(key);
            }
            else
            {
                parameter.Text = key;
            }
        });

    /// <summary>
    /// Shows the keyboard drawn on screen, or points the one already up at something else. A key is
    /// a name the macro has to spell exactly, and a combination is built a key at a time, so the
    /// window takes one cap after another instead of being opened again for each of them.
    /// </summary>
    private void OpenKeyPad(string filling, Action<string> write)
    {
        _keyTarget = write;

        if (_keyPad is null)
        {
            _keyPad = new VirtualKeyboardWindow();
            _keyPad.KeyChosen += OnKeyChosen;
            _keyPad.Closed += (_, _) => ForgetKeyPad();
        }

        _keyPad.Filling = filling;
        if (_keyPad.IsVisible)
        {
            _keyPad.Activate();
        }
        else
        {
            _keyPad.ShowOver(this);
        }
    }

    /// <summary>A cap was clicked: the key goes into whatever the keyboard was pointed at.</summary>
    private void OnKeyChosen(string key) => _keyTarget?.Invoke(key);

    /// <summary>
    /// The keyboard is gone. What it was writing into goes with it, so a later click cannot land in
    /// a field the user has since left.
    /// </summary>
    private void ForgetKeyPad()
    {
        _keyPad = null;
        _keyTarget = null;
    }

    /// <summary>
    /// Drags a rectangle out on the screen and writes it into the row that asked for it, which is
    /// how a region is given without the four numbers being read off a ruler.
    /// </summary>
    private async void OnPickRegionRow(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: RegionRowViewModel row })
        {
            return;
        }

        if (await RegionPickerWindow.PickAsync(this) is { } picked)
        {
            row.Put(picked.X, picked.Y, picked.Width, picked.Height);
        }
    }

    /// <summary>Switches a number between the spinner and an expression such as $match.x.</summary>
    private void OnToggleNumberFormula(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: StepParameterViewModel parameter })
        {
            parameter.ToggleFormula();
        }
    }

    /// <summary>
    /// Opens the expression builder on the field that asked for it and keeps what it returns.
    /// Writing a formula is the same whether the field holds an expression or a number turned
    /// into one, so both reach it through here.
    /// </summary>
    private async void OnOpenExpressionBuilder(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: StepParameterViewModel parameter })
        {
            return;
        }

        if (await ExpressionBuilderWindow.ShowFor(this, parameter.Text ?? string.Empty,
                parameter.Variables) is { } built)
        {
            parameter.Text = built;
        }
    }

    /// <summary>
    /// Opens the variable picker on the field that asked for it. A field that can name a variable
    /// should never need the name typed out of memory, and the name goes in where the caret is: a
    /// field is often a piece of text with a variable beside it rather than only a variable.
    /// </summary>
    private async void OnPickVariable(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: StepParameterViewModel parameter } button
            || DataContext is not AddActionViewModel viewModel)
        {
            return;
        }

        if (await VariablePickerWindow.ShowFor(this, viewModel.CollectVariables())
            is not { Length: > 0 } picked)
        {
            return;
        }

        // The caret belongs to the box beside the button, which is the row's own field: the box
        // holds one value per row, so the one on this row is the one that was being filled in.
        var caret = button.FindAncestorOfType<Grid>(true)?.GetVisualDescendants()
            .OfType<AutoCompleteBox>().FirstOrDefault()?.CaretIndex;
        parameter.InsertVariable(picked, caret ?? (parameter.Text ?? string.Empty).Length);
    }

    /// <summary>Folds one group of the action picker open or shut.</summary>
    private void OnToggleActionGroup(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ActionGroupViewModel group }
            && DataContext is AddActionViewModel viewModel)
        {
            viewModel.ToggleGroup(group);
        }
    }

    /// <summary>Chooses the action whose card was clicked.</summary>
    private void OnPickAction(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ActionCardViewModel card }
            && DataContext is AddActionViewModel viewModel)
        {
            viewModel.SelectAction(card.Key);
        }
    }

    /// <summary>Opens the page whose tab was clicked.</summary>
    private void OnSelectPage(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ActionPageViewModel page }
            && DataContext is AddActionViewModel viewModel)
        {
            viewModel.Select(page);
        }
    }

    /// <summary>Opens the screen magnifier for a colour parameter and keeps what it reads.</summary>
    private async void OnPickParameterColor(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: StepParameterViewModel parameter })
        {
            return;
        }

        var colour = await ColorPickerWindow.PickAsync(this);
        if (colour is { } picked)
        {
            parameter.Text = picked.ToHex();
        }
    }

    /// <summary>
    /// Opens the keyboard drawn on screen and keeps the key whose cap was clicked. A key is a name
    /// the macro has to spell exactly, so it is taken off a picture of a keyboard rather than out
    /// of a list of names.
    /// </summary>
    private void OnOpenKeyPad(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: StepParameterViewModel parameter })
        {
            OpenKeyPadFor(parameter);
        }
    }

    /// <summary>
    /// Opens the controller drawn on screen and keeps the control that was clicked. Which question
    /// the pad answers — a button, a stick or a trigger — follows from the parameter it was opened
    /// from, because that is what the step is asking for.
    /// </summary>
    private async void OnOpenGamepadPad(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: StepParameterViewModel parameter })
        {
            return;
        }

        var pick = parameter.Definition.Name switch
        {
            "stick" => GamepadPick.Stick,
            "trigger" => GamepadPick.Trigger,
            _ => GamepadPick.Button,
        };

        if (await VirtualGamepadWindow.PickAsync(this, pick) is { Length: > 0 } value)
        {
            parameter.Choose(value);
        }
    }

    /// <summary>
    /// Tries the step for real, once, which is the same question the element picker's test asks:
    /// is this the thing I meant? A key name can be spelled another way and a controller control
    /// can be the wrong one of a pair, so the answer is to send it and let the user watch — in the
    /// window they meant it for, which is why the dialog steps aside first, the way picking does.
    /// </summary>
    private async void OnTestStep(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: StepParameterViewModel parameter }
            || DataContext is not AddActionViewModel viewModel)
        {
            return;
        }

        if (parameter.CurrentText.Trim().Length == 0)
        {
            await ReportTestAsync(Strings.Get("Add.TestFieldEmpty"));
            return;
        }

        // The step as it would be saved: what is tried is what was written, settings and all.
        var step = new[] { viewModel.BuildStep() }.ToExecutable()[0];

        // Out of the way while it is tried, so the key or the control lands on the window that was
        // meant, which is whatever the user had in mind behind this dialog.
        var previous = WindowState;
        WindowState = WindowState.Minimized;

        RunResult outcome;
        try
        {
            // Off the thread that draws the window: a device call can wait on hardware, and the
            // dialog has to stay able to come back when it is done.
            outcome = await Task.Run(() => MacroRunner.TryAsync(step, Devices));
        }
        finally
        {
            // The dialog can be dismissed while the trial runs, and a window on its way out has
            // nothing left to come back to.
            if (!_closed)
            {
                WindowState = previous == WindowState.Maximized
                    ? WindowState.Maximized
                    : WindowState.Normal;
                Activate();
            }
        }

        if (outcome.Status is not RunStatus.Completed && !_closed)
        {
            await ReportTestAsync(TrialFailure(outcome));
        }
    }

    /// <summary>Why a step could not be tried, in the interface's own words.</summary>
    private static string TrialFailure(RunResult outcome)
        => outcome.Detail.Length == 0
            ? Strings.Get(outcome.Key)
            : Strings.Format(outcome.Key, outcome.Detail);

    /// <summary>
    /// Runs the step being written, once, without closing the dialog or going into a run. What it
    /// does follows from what the step is: a step that looks at the screen only looks and shows what
    /// turned up — trying <c>vision.clickImage</c> for real would click on whatever the user has on
    /// screen — and every other step is sent for real, the same way the field's own test sends it.
    /// </summary>
    private async void OnRunStep(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AddActionViewModel viewModel || viewModel.IsRunning || !viewModel.CanRun)
        {
            return;
        }

        // The step as it would be saved, so what is run is what was written.
        var step = new[] { viewModel.BuildStep() }.ToExecutable()[0];

        // Some steps put the machine out of use. A debug button is one click, and one click should
        // not sign the user out or shut their machine down without being asked.
        if (PutsTheMachineOut(viewModel)
            && await ConfirmDialog.ShowAsync(this, Strings.Get("Add.RunPowerTitle"),
                Strings.Get("Add.RunPowerMessage"), Strings.Get("Common.Ok")) is not ConfirmChoice.Primary)
        {
            return;
        }

        if (MacroRunner.CanLook(step.Type))
        {
            await RunLookingAsync(step);
            return;
        }

        await RunForRealAsync(viewModel, step);
    }

    /// <summary>
    /// Looks at the screen the way the step would and shows what turned up, without doing anything
    /// about it: no click, no key, no variable written.
    /// </summary>
    private async Task RunLookingAsync(ExecutableStep step)
    {
        // Off the thread that draws the window: reading the screen and matching a picture can take
        // a moment, and the dialog has to stay able to come back when it is done.
        var outcome = await Task.Run(() => MacroRunner.LookOnce(step, Devices));
        if (outcome.Look is null)
        {
            await ReportTestAsync(TrialFailure(
                new RunResult(RunStatus.Failed, outcome.Key, outcome.Detail, 0)));
            return;
        }

        LookWindow.Show(outcome.Look, this);
    }

    /// <summary>
    /// Sends the step for real, the way a run would. Input steps are sent with the dialog out of the
    /// way, because a key or a click lands on whatever is in front, and the window in front of the
    /// window the user meant would be this dialog.
    /// </summary>
    private async Task RunForRealAsync(AddActionViewModel viewModel, ExecutableStep step)
    {
        var movesTheWindow = viewModel.SelectedDefinition?.Category is ActionCategory.Input;
        var previous = WindowState;
        if (movesTheWindow)
        {
            WindowState = WindowState.Minimized;
        }

        viewModel.IsRunning = true;
        RunResult outcome;
        try
        {
            // Off the thread that draws the window: a device call can wait on hardware, and the
            // dialog has to stay able to come back when it is done.
            outcome = await Task.Run(() => MacroRunner.TryAsync(step, Devices,
                variables: MacroVariables.Seed(), macros: Macros));
        }
        finally
        {
            viewModel.IsRunning = false;

            // The dialog can be dismissed while the run is going, and a window on its way out has
            // nothing left to come back to.
            if (movesTheWindow && !_closed)
            {
                WindowState = previous == WindowState.Maximized
                    ? WindowState.Maximized
                    : WindowState.Normal;
                Activate();
            }
        }

        if (outcome.Status is not RunStatus.Completed)
        {
            if (!_closed)
            {
                await ReportTestAsync(TrialFailure(outcome));
            }

            return;
        }

        Say(Strings.Format("Add.RanStep", viewModel.SelectedActionName));
    }

    /// <summary>
    /// Whether running this step signs the user out or stops the machine. A power step that only
    /// locks the screen or turns the monitor off costs nothing, so it is not asked about.
    /// </summary>
    private static bool PutsTheMachineOut(AddActionViewModel viewModel)
        => viewModel.SelectedDefinition?.Key is "system.power"
            && viewModel.Parameters
                .FirstOrDefault(parameter => parameter.Definition.Name == "what")
                ?.CurrentText.Trim() is "signOut" or "sleep" or "hibernate" or "restart" or "shutDown";

    /// <summary>Says what a run did, under the form, until the next thing the user does.</summary>
    private void Say(string message)
    {
        if (Form is not { } viewModel)
        {
            return;
        }

        viewModel.Say(message);

        _runTimer?.Stop();
        _runTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _runTimer.Tick += (_, _) =>
        {
            _runTimer?.Stop();
            _runTimer = null;
            viewModel.Say(string.Empty);
        };
        _runTimer.Start();
    }

    /// <summary>
    /// Picks where this step's click should land, on the picture it looks for: clicking the middle
    /// of a found picture is wrong whenever the thing to hit sits beside it, and working the offset
    /// out in the head is the kind of arithmetic a person gets wrong quietly.
    /// </summary>
    private async void OnPickOffset(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AddActionViewModel viewModel)
        {
            return;
        }

        var field = viewModel.Parameters
            .FirstOrDefault(parameter => parameter.Definition.Name == "image");

        // The picture may be held in a variable the run has not made yet, and there is nothing to
        // point at before one is chosen.
        var path = field is null ? null : ImageAssets.Resolve(field.PictureText, field.AssetFolder);
        if (path is null)
        {
            await ReportTestAsync(Strings.Get("Add.PickOffsetNeedsImage"));
            return;
        }

        ImageFrame? picture;
        try
        {
            picture = await Task.Run(() => Devices.Vision.Load(path));
        }
        catch (Exception error)
        {
            await ReportTestAsync(error.Message);
            return;
        }

        if (picture is null || picture.IsEmpty)
        {
            await ReportTestAsync(Strings.Format("Add.PickOffsetNeedsImage"));
            return;
        }

        var offset = await OffsetPickerWindow.PickAsync(this, picture, Strings.Get("Offset.Title"));
        if (offset is { } point)
        {
            viewModel.ApplyOffset(point.X, point.Y);
        }
    }

    /// <summary>
    /// Puts the step's name on the clipboard. It is a name people write down — into a condition, a
    /// message, a note to whoever reads the macro next — and typing four characters off a screen is
    /// where they get it wrong.
    /// </summary>
    private async void OnCopyStepId(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AddActionViewModel { HasStepId: true } viewModel)
        {
            return;
        }

        if (Clipboard is { } clipboard)
        {
            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.CreateText(viewModel.StepId));
            await clipboard.SetDataAsync(transfer);
        }
    }

    /// <summary>
    /// Picks the file or folder a path field is about, with the dialog that suits which way the
    /// path is going: reading, writing, or a folder. What comes back is stored the way the engine
    /// will read it — relative when it is inside the macros folder, in full otherwise.
    /// </summary>
    private async void OnBrowsePath(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: StepParameterViewModel parameter })
        {
            return;
        }

        var filters = Filter(parameter.Definition.PathFilter);
        var picked = parameter.Definition.PathIntent switch
        {
            PathIntent.Write => await PickToWrite(parameter, filters),
            PathIntent.Folder => await PickFolder(),
            _ => await PickToRead(parameter, filters),
        };

        if (picked is { Length: > 0 })
        {
            parameter.Text = MacroPaths.ForMacro(picked);
        }
    }

    /// <summary>The kinds of file a field is about, as dialog filters, or none for any file.</summary>
    private static IReadOnlyList<FilePickerFileType> Filter(string patterns)
    {
        if (patterns.Length == 0)
        {
            return [];
        }

        var wanted = patterns.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return
        [
            new FilePickerFileType(string.Join(" / ", wanted)) { Patterns = wanted },
            new FilePickerFileType(Strings.Get("Add.AllFiles")) { Patterns = ["*.*"] },
        ];
    }

    private async Task<string> PickToRead(StepParameterViewModel parameter,
        IReadOnlyList<FilePickerFileType> filters)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Strings.Format("Add.BrowseOpenTitle", parameter.Definition.LocalLabel),
            AllowMultiple = false,
            FileTypeFilter = filters,
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() ?? string.Empty : string.Empty;
    }

    private async Task<string> PickToWrite(StepParameterViewModel parameter,
        IReadOnlyList<FilePickerFileType> filters)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Strings.Format("Add.BrowseSaveTitle", parameter.Definition.LocalLabel),
            SuggestedFileName = parameter.Text.Trim(),
            FileTypeChoices = filters,
        });

        return file?.TryGetLocalPath() ?? string.Empty;
    }

    private async Task<string> PickFolder()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Strings.Get("Add.BrowseFolderTitle"),
            AllowMultiple = false,
        });

        return folders.Count > 0 ? folders[0].TryGetLocalPath() ?? string.Empty : string.Empty;
    }

    /// <summary>
    /// Gives a result variable a name nothing else is using, by putting a number after the one it
    /// has. It is what the line under such a field offers, so noticing a clash and doing something
    /// about it is one click rather than a rewrite of the name.
    /// </summary>
    private void OnMakeNameUnique(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: StepParameterViewModel parameter })
        {
            parameter.MakeNameUnique();
        }
    }

    /// <summary>Chooses a picture file for an image parameter.</summary>
    private async void OnBrowseImage(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: StepParameterViewModel parameter })
        {
            return;
        }

        if (await PickPictureAsync() is { Length: > 0 } path)
        {
            parameter.Text = path;
        }
    }

    /// <summary>Asks for one picture file, whichever field wanted one.</summary>
    private async Task<string> PickPictureAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Strings.Get("Add.BrowseImageTitle"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(Strings.Get("Add.ImageFilter"))
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif"],
                },
            ],
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() ?? string.Empty : string.Empty;
    }

    /// <summary>
    /// Drags a rectangle on the screen and keeps it as a picture inside the macro project, which
    /// is how a reference image is normally made.
    /// </summary>
    private async void OnCaptureImage(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: StepParameterViewModel parameter }
            || DataContext is not AddActionViewModel viewModel)
        {
            return;
        }

        if (await RegionPickerWindow.PickAsync(this) is not { } region)
        {
            return;
        }

        try
        {
            parameter.Text = ImageAssets.Capture(new WindowsScreenDevice(),
                region.X, region.Y, region.Width, region.Height, viewModel.AssetFolder);
        }
        catch (Exception)
        {
            await ConfirmDialog.ShowAsync(this, Strings.Get("Add.CaptureImage"),
                Strings.Get("Add.ImageFailed"), Strings.Get("Common.Ok"), showCancel: false);
        }
    }

    /// <summary>Forgets the picture an image parameter points at.</summary>
    private void OnClearImage(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: StepParameterViewModel parameter })
        {
            parameter.Text = string.Empty;
        }
    }

    /// <summary>Picks one of the windows that are open and writes its title into the field.</summary>
    private async void OnPickWindow(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: StepParameterViewModel parameter })
        {
            return;
        }

        // The picker takes the part this step compares its text with, so what it hands back can be
        // written straight into the field.
        var match = (DataContext as AddActionViewModel)?.WindowMatchFor() ?? WindowMatch.Title;
        if (await WindowPickerWindow.PickAsync(this, match) is { Length: > 0 } picked)
        {
            parameter.Text = picked;
        }
    }

    /// <summary>
    /// Takes the control the pointer is put on through UI Automation and writes the selector it
    /// reads. The window that control sits in fills the action's window filter when that is still
    /// empty, which is what stops the selector from matching the same kind of control somewhere
    /// else.
    /// </summary>
    private async void OnPickElement(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: StepParameterViewModel parameter }
            || DataContext is not AddActionViewModel viewModel)
        {
            return;
        }

        if (await ElementPickerWindow.PickAsync(this) is not { } picked)
        {
            return;
        }

        parameter.Text = picked.Selector;

        // The window filter is filled in for whichever field this action keeps it in, so a step
        // that is anchored to a control gets the same narrowing as one that looks the control up.
        foreach (var name in new[] { "window", "anchorWindow" })
        {
            if (viewModel.Parameters.FirstOrDefault(item => item.Definition.Name == name) is { } filter
                && string.IsNullOrWhiteSpace(filter.Text))
            {
                filter.Text = picked.Window;
            }
        }
    }

    /// <summary>
    /// Opens the page a browser step is about and writes the selector of the element clicked on
    /// it. The address comes from the step's own address field, so "go to this page, then click
    /// that" is picked off the page the macro will actually be looking at.
    /// </summary>
    private async void OnPickBrowserElement(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: StepParameterViewModel parameter }
            || DataContext is not AddActionViewModel viewModel)
        {
            return;
        }

        var url = viewModel.Parameters.FirstOrDefault(item => item.Definition.Name == "url")?.Text
            ?? string.Empty;

        try
        {
            if (await BrowserPicker.PickAsync(url) is { Length: > 0 } selector)
            {
                parameter.Text = selector;
            }
        }
        catch (Exception)
        {
            // The dialog can be dismissed while the page is open, and closing it takes the browser
            // down with it — there is nowhere left to show a message about that.
            if (!_closed)
            {
                await ConfirmDialog.ShowAsync(this, Strings.Get("Add.PickBrowserElement"),
                    Strings.Get("Add.BrowserPickFailed"), Strings.Get("Common.Ok"), showCancel: false);
            }
        }
    }

    /// <summary>
    /// Flashes the control the selector names, so a pick can be checked without running the step.
    /// The dialog steps aside first, the same way picking does: the control being looked at is
    /// usually behind the window asking about it.
    /// </summary>
    private async void OnTestElement(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: StepParameterViewModel parameter }
            || DataContext is not AddActionViewModel viewModel)
        {
            return;
        }

        if (Selector(parameter) is not { Length: > 0 } selector)
        {
            await ReportTestAsync(Strings.Get("Add.TestElementEmpty"));
            return;
        }

        // A control is measured from the same window field the picker fills in, so which one that
        // is follows the field being tested rather than the action.
        var filter = ParameterText(viewModel,
            parameter.Definition.Name == "anchorSelector" ? "anchorWindow" : "window");

        // Out of the way while the frame is up, the same way picking does it: the control is
        // usually behind the very window asking about it. The dialog waits the frame out, so it
        // never comes back on top of the thing it just said to look at.
        var previous = WindowState;
        WindowState = WindowState.Minimized;

        ElementFlashWindow? flash = null;
        try
        {
            if (ElementFlashWindow.Locate(viewModel.Ui, selector, filter) is { } found)
            {
                flash = ElementFlashWindow.Show(found);
                if (flash is not null)
                {
                    await flash.Finished;
                }
            }
        }
        finally
        {
            // The dialog can be dismissed while the frame is up, and a window on its way out has
            // nothing left to come back to.
            if (!_closed)
            {
                WindowState = previous == WindowState.Maximized
                    ? WindowState.Maximized
                    : WindowState.Normal;
                Activate();
            }
        }

        if (flash is null && !_closed)
        {
            await ReportTestAsync(Strings.Get("Add.TestElementMissing"));
        }
    }

    /// <summary>
    /// Outlines the element a page selector names on the page it belongs to, which is the same
    /// question the desktop picker answers next to it.
    /// </summary>
    private async void OnTestBrowserElement(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: StepParameterViewModel parameter })
        {
            return;
        }

        if (Selector(parameter) is not { Length: > 0 } selector)
        {
            await ReportTestAsync(Strings.Get("Add.TestElementEmpty"));
            return;
        }

        var found = false;
        try
        {
            found = await BrowserPicker.HighlightAsync(selector);
        }
        catch (Exception)
        {
            // The dialog can be dismissed while the page is being asked, and a window that is on
            // its way out is not worth reporting to.
        }

        if (!found && !_closed)
        {
            await ReportTestAsync(Strings.Get("Add.TestElementMissing"));
        }
    }

    /// <summary>The selector a field holds, or null when nothing has been picked or written yet.</summary>
    private static string? Selector(StepParameterViewModel parameter)
    {
        var text = (parameter.Text ?? string.Empty).Trim();
        return text.Length == 0 ? null : text;
    }

    /// <summary>The text of a parameter of this action, or an empty string when it has none.</summary>
    private static string ParameterText(AddActionViewModel viewModel, string name)
        => viewModel.Parameters.FirstOrDefault(item => item.Definition.Name == name)?.Text ?? string.Empty;

    /// <summary>Says what the test found, without leaving the dialog.</summary>
    private async Task ReportTestAsync(string message)
        => await ConfirmDialog.ShowAsync(this, Strings.Get("Add.TestElement"), message,
            Strings.Get("Common.Ok"), showCancel: false);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            && e.GetPosition(this).Y <= 38)
        {
            BeginMoveDrag(e);
        }
    }

    /// <summary>Noted so a pick that outlives the dialog does not try to report back to it.</summary>
    protected override void OnClosed(EventArgs e)
    {
        _closed = true;

        // The devices a trial used go with the dialog, so a controller it put on the machine does
        // not outlive the window that asked for it.
        (_devices as IDisposable)?.Dispose();
        _devices = null;

        base.OnClosed(e);
    }
}
